using System.Diagnostics;
using ProjectZD8.Domain.Errors;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>全セッションログのファイル書込みを1本の背景スレッドでまとめて行います。</summary>
/// <remarks>
/// 計測側（追記を呼ぶスレッド）はメモリ上の状態を更新して枠付きバイト列をキューへ入れるだけで戻り、
/// ファイル書込みと確定（fsync）を待ちません。背景スレッドはキューにたまった分をまとめて書き、
/// グループごとに1回だけ確定します（group commit）。キューはバイト数で上限があり、満杯のときだけ追記が待ちます。
/// </remarks>
internal sealed class SessionLogWriter : IDisposable
{
    /// <summary>キューと進捗を守るロックです。</summary>
    private readonly object _gate = new();
    /// <summary>書込み待ちの項目です。</summary>
    private readonly Queue<WriteItem> _queue = new();
    /// <summary>確定待ち（書いたがfsyncしていない）のログです。</summary>
    private readonly HashSet<SessionLog> _dirty = [];
    /// <summary>書込み設定です。</summary>
    private readonly LogStoreOptions _options;
    /// <summary>書込みスレッドです。</summary>
    private readonly Thread _thread;
    /// <summary>キュー内の枠のバイト数です。</summary>
    private long _queuedBytes;
    /// <summary>停止要求があるかどうかです。</summary>
    private bool _stopping;
    /// <summary>最後に周期確定した時刻です。</summary>
    private long _lastPeriodicFlush = Stopwatch.GetTimestamp();

    /// <summary>設定を指定して書込みスレッドを開始します。</summary>
    /// <remarks>責務: 背景の書込みスレッドを1本起動します。</remarks>
    /// <param name="options">書込み設定。</param>
    internal SessionLogWriter(LogStoreOptions options)
    {
        _options = options;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "ProjectZD8 LogStore writer",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    /// <summary>枠付きレコードを書込みキューへ入れます。</summary>
    /// <remarks>責務: 書込み位置が決まった1件の枠を背景書込みへ渡します。キュー満杯時だけ上限時間まで待ちます。</remarks>
    /// <param name="log">書込み先のログ。</param>
    /// <param name="segment">書込み先のセグメント番号。</param>
    /// <param name="offset">書込み先のオフセット。</param>
    /// <param name="frame">枠付きバイト列。</param>
    /// <param name="ticket">ログ内の通し番号。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">キューが上限時間内に空かない場合は <c>Unavailable</c>。</exception>
    internal void Enqueue(SessionLog log, int segment, long offset, byte[] frame, long ticket)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_queuedBytes + frame.Length > _options.QueueCapacityBytes && _queuedBytes > 0)
            {
                var deadline = Stopwatch.GetTimestamp() + (long)(_options.EnqueueTimeout.TotalSeconds * Stopwatch.Frequency);
                while (_queuedBytes + frame.Length > _options.QueueCapacityBytes && _queuedBytes > 0)
                {
                    var remaining = deadline - Stopwatch.GetTimestamp();
                    if (remaining <= 0)
                    {
                        throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                    }
                    Monitor.Wait(_gate, TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
                }
            }
            _queue.Enqueue(new WriteItem(log, WriteItemKind.Frame, segment, offset, frame, ticket));
            _queuedBytes += frame.Length;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>指定番号までの書込みがOSへ渡るまで待ちます。</summary>
    /// <remarks>責務: 読取の前に、キューにある分がファイルから読める状態になるのを待ちます。</remarks>
    /// <param name="log">対象のログ。</param>
    /// <param name="ticket">待つ通し番号。</param>
    /// <exception cref="IOException">書込みが失敗している場合。</exception>
    internal void WaitWritten(SessionLog log, long ticket)
    {
        lock (_gate)
        {
            while (log.WrittenTicket < ticket && log.Fault is null)
            {
                Monitor.Wait(_gate);
            }
            ThrowIfFaulted(log);
        }
    }

    /// <summary>指定番号までの書込みがディスクへ確定するまで待ちます。</summary>
    /// <remarks>責務: セッション終了などの境界で、確定方針にかかわらず必ずfsyncさせて待ちます。</remarks>
    /// <param name="log">対象のログ。</param>
    /// <param name="ticket">待つ通し番号。</param>
    /// <exception cref="IOException">書込みが失敗している場合。</exception>
    internal void WaitDurable(SessionLog log, long ticket)
    {
        lock (_gate)
        {
            if (log.DurableTicket >= ticket)
            {
                ThrowIfFaulted(log);
                return;
            }
            _queue.Enqueue(new WriteItem(log, WriteItemKind.Barrier, 0, 0, null, ticket));
            Monitor.PulseAll(_gate);
            while (log.DurableTicket < ticket && log.Fault is null)
            {
                Monitor.Wait(_gate);
            }
            ThrowIfFaulted(log);
        }
    }

    /// <summary>ログの書込みファイルを閉じるまで待ちます。</summary>
    /// <remarks>責務: すべての書込みを確定してから、書込みスレッドが持つファイルを閉じさせます。</remarks>
    /// <param name="log">対象のログ。</param>
    internal void Close(SessionLog log)
    {
        lock (_gate)
        {
            var done = false;
            _queue.Enqueue(new WriteItem(log, WriteItemKind.Close, 0, 0, null, 0, () => done = true));
            Monitor.PulseAll(_gate);
            while (!done && !_stopping)
            {
                Monitor.Wait(_gate);
            }
        }
    }

    /// <summary>書込みスレッドの本体です。</summary>
    /// <remarks>責務: キューをまとめて取り出して書き、ログごとに確定して進捗を通知します。</remarks>
    private void Run()
    {
        var batch = new List<WriteItem>();
        var touched = new HashSet<SessionLog>();
        var forced = new HashSet<SessionLog>();
        while (true)
        {
            long bytes = 0;
            lock (_gate)
            {
                while (_queue.Count == 0 && !_stopping)
                {
                    if (_options.FlushPolicy == LogStoreFlushPolicy.Periodic && _dirty.Count > 0)
                    {
                        if (!Monitor.Wait(_gate, _options.FlushInterval))
                        {
                            break;
                        }
                    }
                    else
                    {
                        Monitor.Wait(_gate);
                    }
                }
                if (_queue.Count == 0 && _stopping)
                {
                    break;
                }
                while (_queue.Count > 0)
                {
                    var item = _queue.Dequeue();
                    batch.Add(item);
                    bytes += item.Frame?.Length ?? 0;
                }
            }
            var completions = new List<Action>();
            foreach (var item in batch)
            {
                switch (item.Kind)
                {
                    case WriteItemKind.Frame:
                        if (item.Log.Fault is null)
                        {
                            try
                            {
                                item.Log.WriteFrame(item.Segment, item.Offset, item.Frame!);
                            }
                            catch (Exception error)
                            {
                                item.Log.Fault = error;
                            }
                        }
                        item.Log.PendingWrittenTicket = item.Ticket;
                        touched.Add(item.Log);
                        break;
                    case WriteItemKind.Barrier:
                        touched.Add(item.Log);
                        forced.Add(item.Log);
                        break;
                    case WriteItemKind.Close:
                        touched.Add(item.Log);
                        forced.Add(item.Log);
                        completions.Add(() =>
                        {
                            try
                            {
                                item.Log.CloseWriterStream();
                            }
                            catch (Exception error)
                            {
                                item.Log.Fault ??= error;
                            }
                            item.Completion!();
                        });
                        break;
                }
            }
            var periodicDue = _options.FlushPolicy == LogStoreFlushPolicy.Periodic
                && Stopwatch.GetElapsedTime(_lastPeriodicFlush) >= _options.FlushInterval;
            lock (_gate)
            {
                foreach (var log in _dirty)
                {
                    touched.Add(log);
                }
            }
            if (periodicDue)
            {
                _lastPeriodicFlush = Stopwatch.GetTimestamp();
            }
            var durableLogs = new List<SessionLog>();
            foreach (var log in touched)
            {
                var durable = _options.FlushPolicy == LogStoreFlushPolicy.EveryGroup || forced.Contains(log) || periodicDue;
                try
                {
                    log.FlushWriterStream(durable);
                }
                catch (Exception error)
                {
                    log.Fault ??= error;
                }
                if (durable)
                {
                    durableLogs.Add(log);
                }
            }
            foreach (var completion in completions)
            {
                completion();
            }
            lock (_gate)
            {
                foreach (var log in touched)
                {
                    log.WrittenTicket = log.PendingWrittenTicket;
                    if (durableLogs.Contains(log))
                    {
                        log.DurableTicket = log.WrittenTicket;
                        _dirty.Remove(log);
                    }
                    else if (log.DurableTicket < log.WrittenTicket)
                    {
                        _dirty.Add(log);
                    }
                }
                foreach (var item in batch)
                {
                    if (item.Kind == WriteItemKind.Barrier && item.Log.DurableTicket < item.Ticket && item.Log.Fault is null)
                    {
                        item.Log.DurableTicket = Math.Max(item.Log.DurableTicket, Math.Min(item.Ticket, item.Log.WrittenTicket));
                    }
                }
                _queuedBytes -= bytes;
                Monitor.PulseAll(_gate);
            }
            batch.Clear();
            touched.Clear();
            forced.Clear();
        }
    }

    /// <summary>ログの書込み失敗を例外として送出します。</summary>
    /// <remarks>責務: 背景スレッドで起きた入出力エラーを待っていた呼出元へ伝えます。</remarks>
    /// <param name="log">確かめるログ。</param>
    /// <exception cref="IOException">書込みが失敗している場合。</exception>
    private static void ThrowIfFaulted(SessionLog log)
    {
        if (log.Fault is { } fault)
        {
            throw new IOException("Session log write failed", fault);
        }
    }

    /// <summary>残りを書き終えてから書込みスレッドを止めます。</summary>
    /// <remarks>責務: キューを空にしてからスレッドを終了させます。</remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }
            _stopping = true;
            Monitor.PulseAll(_gate);
        }
        _thread.Join();
    }

    /// <summary>書込み項目の種類です。</summary>
    private enum WriteItemKind
    {
        /// <summary>枠付きレコードの書込みです。</summary>
        Frame,

        /// <summary>確定（fsync）の要求です。</summary>
        Barrier,

        /// <summary>書込みファイルを閉じる要求です。</summary>
        Close,
    }

    /// <summary>書込みキューの1項目です。</summary>
    /// <param name="Log">対象のログ。</param>
    /// <param name="Kind">項目の種類。</param>
    /// <param name="Segment">書込み先のセグメント番号。</param>
    /// <param name="Offset">書込み先のオフセット。</param>
    /// <param name="Frame">枠付きバイト列。</param>
    /// <param name="Ticket">ログ内の通し番号。</param>
    /// <param name="Completion">完了時の通知。</param>
    private sealed record WriteItem(
        SessionLog Log,
        WriteItemKind Kind,
        int Segment,
        long Offset,
        byte[]? Frame,
        long Ticket,
        Action? Completion = null);
}
