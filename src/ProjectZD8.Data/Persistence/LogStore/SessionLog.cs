using System.Buffers.Binary;
using System.Globalization;
using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>1セッションの追記専用ジャーナル（生ログと取得証拠）です。</summary>
/// <remarks>
/// <para>
/// 保存先は <c>&lt;logs&gt;/&lt;sessionID&gt;/g&lt;世代&gt;/seg-NNNNNN.log</c> です。生ログの除去や復元は新しい世代ディレクトリーを
/// 作ってから切り替えるので、途中で落ちても古い世代か新しい世代のどちらかが丸ごと残ります。
/// </para>
/// <para>
/// 追記はメモリ上の状態（索引と証拠状態）を更新し、枠付きバイト列を <see cref="SessionLogWriter"/> のキューへ入れて戻ります。
/// 開くときは最後のセグメントを走査し、長さかCRCが合わない末尾（書きかけのレコード）を切り詰めます。
/// きれいに閉じたときは索引ファイル（<c>index.bin</c>）を書き、次回は走査を省きます。
/// </para>
/// <para>状態の変更は呼出元（ファサード）のロックで直列化されている前提です。読取は呼出元のロックの外から呼べます。</para>
/// </remarks>
internal sealed class SessionLog
{
    /// <summary>索引ファイル名です。</summary>
    private const string SidecarFileName = "index.bin";

    /// <summary>メモリ状態とスナップショットを守るロックです。</summary>
    private readonly object _stateGate = new();
    /// <summary>実行中の圧縮の数を守るロックです（書込みスレッドから使うため状態のロックと分けます）。</summary>
    private readonly object _compressionGate = new();
    /// <summary>ファイルの読取と、削除・置換・圧縮後の名前変更を排他するロックです。</summary>
    private readonly ReaderWriterLockSlim _fileLock = new(LockRecursionPolicy.NoRecursion);
    /// <summary>セッションのディレクトリーです。</summary>
    private readonly string _sessionDirectory;
    /// <summary>ストアの設定です。</summary>
    private readonly LogStoreOptions _options;
    /// <summary>背景書込みです。</summary>
    private readonly SessionLogWriter _writer;
    /// <summary>現在の世代番号です。</summary>
    private int _generation;
    /// <summary>現在の世代ディレクトリーです。</summary>
    private string _generationDirectory;
    /// <summary>生ログの疎な索引です。</summary>
    private SessionLogIndex _index;
    /// <summary>取得証拠の状態です。索引ファイルから開いた場合は必要になるまで作りません。</summary>
    private SessionLogEvidenceState? _evidence;
    /// <summary>次の追記位置です。</summary>
    private SessionLogPosition _cursor;
    /// <summary>最後にキューへ入れた通し番号です。</summary>
    private long _enqueuedTicket;
    /// <summary>索引ファイルが残っているかどうかです（変更前に消します）。</summary>
    private bool _sidecarPresent;
    /// <summary>実行中の圧縮の数です。</summary>
    private int _pendingCompressions;
    /// <summary>削除済みかどうかです。</summary>
    private bool _isDeleted;
    /// <summary>書込みスレッドが開いているファイルです（書込みスレッドだけが使います）。</summary>
    private FileStream? _writerStream;
    /// <summary>書込みスレッドが開いているセグメント番号です。</summary>
    private int _writerSegment = -1;
    /// <summary>背景書込みの失敗です。</summary>
    private Exception? _fault;

    /// <summary>開いた状態を指定して生成します。</summary>
    /// <remarks>責務: 復元済みの世代・索引・証拠状態・追記位置を保持します。</remarks>
    /// <param name="sessionDirectory">セッションのディレクトリー。</param>
    /// <param name="options">ストアの設定。</param>
    /// <param name="writer">背景書込み。</param>
    /// <param name="generation">世代番号。</param>
    /// <param name="index">索引。</param>
    /// <param name="evidence">証拠状態。</param>
    /// <param name="cursor">次の追記位置。</param>
    /// <param name="sidecarPresent">索引ファイルが残っているか。</param>
    private SessionLog(
        string sessionDirectory,
        LogStoreOptions options,
        SessionLogWriter writer,
        int generation,
        SessionLogIndex index,
        SessionLogEvidenceState? evidence,
        SessionLogPosition cursor,
        bool sidecarPresent)
    {
        _sessionDirectory = sessionDirectory;
        _options = options;
        _writer = writer;
        _generation = generation;
        _generationDirectory = GenerationDirectory(sessionDirectory, generation);
        _index = index;
        _evidence = evidence;
        _cursor = cursor;
        _sidecarPresent = sidecarPresent;
    }

    /// <summary>書込みスレッドが書き終えた通し番号です（書込みのロック内で読み書きします）。</summary>
    internal long WrittenTicket { get; set; }

    /// <summary>ディスクへ確定した通し番号です（書込みのロック内で読み書きします）。</summary>
    internal long DurableTicket { get; set; }

    /// <summary>書込みスレッドが現在のグループで書いた最後の通し番号です。</summary>
    internal long PendingWrittenTicket { get; set; }

    /// <summary>背景書込みの失敗です。</summary>
    internal Exception? Fault
    {
        get => Volatile.Read(ref _fault);
        set => Volatile.Write(ref _fault, value);
    }

    /// <summary>計測中のセッションとして、メモリから追い出さない印です。</summary>
    internal bool IsPinned { get; set; }

    /// <summary>最後に使った時刻（追い出し順の判定用）です。</summary>
    internal long LastUsed { get; set; }

    /// <summary>書込み待ちの項目があるかどうかです。</summary>
    internal bool HasPendingWrites
    {
        get
        {
            lock (_stateGate)
            {
                return WrittenTicket < _enqueuedTicket;
            }
        }
    }

    /// <summary>生ログの件数です。</summary>
    internal long Count
    {
        get
        {
            lock (_stateGate)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>生ログの応答データの合計バイト数です。</summary>
    internal long ByteCount
    {
        get
        {
            lock (_stateGate)
            {
                return _index.ByteCount;
            }
        }
    }

    /// <summary>次に採番するsequenceです。</summary>
    internal long NextSequence
    {
        get
        {
            lock (_stateGate)
            {
                return _index.NextSequence;
            }
        }
    }

    /// <summary>取得証拠の状態です。索引ファイルから開いた場合はここでジャーナルを走査します。</summary>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">証拠が壊れている場合は <c>Unavailable</c>。</exception>
    internal SessionLogEvidenceState Evidence
    {
        get
        {
            while (true)
            {
                string directory;
                SessionLogPosition cursor;
                long ticket;
                lock (_stateGate)
                {
                    if (_evidence is not null)
                    {
                        if (_evidence.IsCorrupt)
                        {
                            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                        }
                        return _evidence;
                    }
                    directory = _generationDirectory;
                    cursor = _cursor;
                    ticket = _enqueuedTicket;
                }
                _writer.WaitWritten(this, ticket);
                var evidence = new SessionLogEvidenceState();
                _fileLock.EnterReadLock();
                try
                {
                    foreach (var segment in SegmentFileFormat.ListSegments(directory))
                    {
                        if (segment > cursor.Segment)
                        {
                            break;
                        }
                        var end = segment == cursor.Segment ? cursor.Offset : long.MaxValue;
                        using var reader = new SegmentFrameReader(SegmentFileFormat.OpenRead(directory, segment), SegmentFileFormat.HeaderSize, end);
                        while (reader.TryReadNext(out _, out var payload) == SegmentFrameReadStatus.Frame)
                        {
                            evidence.Apply(SessionLogRecordCodec.Decode(payload));
                        }
                    }
                }
                catch (Exception)
                {
                    throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                finally
                {
                    _fileLock.ExitReadLock();
                }
                lock (_stateGate)
                {
                    if (_enqueuedTicket == ticket && string.Equals(directory, _generationDirectory, StringComparison.Ordinal))
                    {
                        _evidence = evidence;
                    }
                }
            }
        }
    }

    /// <summary>セッションのログを開き、壊れた末尾を回復します。</summary>
    /// <remarks>
    /// 責務: 最新の世代を選び、古い世代と作りかけの世代を消し、索引ファイルか走査で状態を復元します。
    /// 走査では長さかCRCが合わない最初のレコードで止め、非圧縮セグメントならそこで切り詰め、後ろのセグメントを消します。
    /// </remarks>
    /// <param name="sessionDirectory">セッションのディレクトリー。</param>
    /// <param name="options">ストアの設定。</param>
    /// <param name="writer">背景書込み。</param>
    /// <returns>開いたログ。</returns>
    internal static SessionLog Open(string sessionDirectory, LogStoreOptions options, SessionLogWriter writer)
    {
        Directory.CreateDirectory(sessionDirectory);
        var generations = new List<int>();
        foreach (var path in Directory.EnumerateDirectories(sessionDirectory))
        {
            var name = Path.GetFileName(path);
            if (name.Length == 7 && name[0] == 'g'
                && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                generations.Add(number);
            }
            else
            {
                Directory.Delete(path, recursive: true);
            }
        }
        generations.Sort();
        var generation = generations.Count == 0 ? 1 : generations[^1];
        foreach (var stale in generations.Where(number => number != generation))
        {
            Directory.Delete(GenerationDirectory(sessionDirectory, stale), recursive: true);
        }
        var directory = GenerationDirectory(sessionDirectory, generation);
        Directory.CreateDirectory(directory);
        foreach (var temporary in Directory.EnumerateFiles(directory, "*.tmp"))
        {
            File.Delete(temporary);
        }
        RemoveDuplicateSegments(directory);
        if (TryLoadSidecar(directory, options, out var loadedIndex, out var loadedCursor))
        {
            return new SessionLog(sessionDirectory, options, writer, generation, loadedIndex, null, loadedCursor, sidecarPresent: true);
        }
        var (index, evidence, cursor) = Scan(directory, options);
        return new SessionLog(sessionDirectory, options, writer, generation, index, evidence, cursor, sidecarPresent: false);
    }

    /// <summary>生ログを次のsequenceで追記します。</summary>
    /// <remarks>責務: 1件の未デコード応答を採番してRawレコードとして追記します。</remarks>
    /// <param name="observation">追記する未デコード応答。</param>
    /// <returns>採番済みの生ログ。</returns>
    internal ConnectionSessionRawLogEntry AppendRaw(OBDRawResponseObservation observation)
    {
        lock (_stateGate)
        {
            var entry = NextEntry(observation);
            AppendLocked(new SessionLogRecord.Raw(entry));
            return entry;
        }
    }

    /// <summary>次のsequenceで採番した生ログを作ります（追記はしません）。</summary>
    /// <remarks>責務: 正応答Rawとrequest終端を1レコードにまとめる前に、採番結果を決めます。</remarks>
    /// <param name="observation">未デコード応答。</param>
    /// <returns>次のsequenceを持つ生ログ。</returns>
    internal ConnectionSessionRawLogEntry PeekNextEntry(OBDRawResponseObservation observation)
    {
        lock (_stateGate)
        {
            return NextEntry(observation);
        }
    }

    /// <summary>1件のレコードを状態へ適用して追記します。</summary>
    /// <remarks>責務: 証拠状態と索引を更新し、枠付きバイト列を背景書込みへ渡します。</remarks>
    /// <param name="record">追記するレコード。</param>
    internal void Append(SessionLogRecord record)
    {
        lock (_stateGate)
        {
            AppendLocked(record);
        }
    }

    /// <summary>これまでの追記がディスクへ確定するまで待ちます。</summary>
    /// <remarks>責務: SQLiteの確定より前に、ログ側の変更を確定させます。</remarks>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">書込みが失敗している場合は <c>Unavailable</c>。</exception>
    internal void WaitDurable()
    {
        long ticket;
        lock (_stateGate)
        {
            ticket = _enqueuedTicket;
        }
        try
        {
            _writer.WaitDurable(this, ticket);
        }
        catch (IOException)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
    }

    /// <summary>全生ログをsequence順で返します。</summary>
    /// <remarks>責務: 現在の世代の生ログをすべて読みます。</remarks>
    /// <returns>sequence昇順の生ログ。</returns>
    internal List<ConnectionSessionRawLogEntry> ReadAll() => ReadSequenceRange(long.MinValue, long.MaxValue, int.MaxValue);

    /// <summary>sequence範囲の生ログを返します。</summary>
    /// <remarks>責務: 疎な索引で開始ブロックを引き、範囲内の生ログだけを最大件数まで読みます。</remarks>
    /// <param name="fromInclusive">範囲の開始（含む）。</param>
    /// <param name="toExclusive">範囲の終了（含まない）。</param>
    /// <param name="limit">最大件数。</param>
    /// <returns>sequence昇順の生ログ。</returns>
    internal List<ConnectionSessionRawLogEntry> ReadSequenceRange(long fromInclusive, long toExclusive, int limit)
    {
        var results = new List<ConnectionSessionRawLogEntry>();
        if (limit <= 0 || fromInclusive >= toExclusive)
        {
            return results;
        }
        ReadSnapshot(index => index.StartPosition(Math.Max(fromInclusive, 0)), (snapshot, planned) =>
        {
            if (planned is not { } start)
            {
                return;
            }
            Scan(snapshot, start, null, entry =>
            {
                if (entry.Sequence >= toExclusive)
                {
                    return false;
                }
                if (entry.Sequence >= fromInclusive)
                {
                    results.Add(entry);
                }
                return results.Count < limit;
            });
        });
        return results;
    }

    /// <summary>観測日時の範囲の生ログを返します。</summary>
    /// <remarks>責務: 索引の最小・最大が重なるブロックだけを読み、範囲内の生ログをsequence順で返します。</remarks>
    /// <param name="from">範囲の開始（含む）。</param>
    /// <param name="to">範囲の終了（含む）。</param>
    /// <returns>sequence昇順の生ログ。</returns>
    internal List<ConnectionSessionRawLogEntry> ReadTimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        var results = new List<ConnectionSessionRawLogEntry>();
        var fromTicks = from.UtcTicks;
        var toTicks = to.UtcTicks;
        if (fromTicks > toTicks)
        {
            return results;
        }
        ReadSnapshot(index => index.TimeRanges(fromTicks, toTicks), (snapshot, ranges) =>
        {
            foreach (var (start, end) in ranges)
            {
                Scan(snapshot, start, end, entry =>
                {
                    var ticks = entry.ObservedAt.UtcTicks;
                    if (ticks >= fromTicks && ticks <= toTicks)
                    {
                        results.Add(entry);
                    }
                    return true;
                });
            }
        });
        return results;
    }

    /// <summary>指定sequenceの生ログを返します。</summary>
    /// <remarks>責務: 1件の生ログを索引経由で読みます。</remarks>
    /// <param name="sequence">sequence。</param>
    /// <returns>生ログ。ない場合は <see langword="null"/>。</returns>
    internal ConnectionSessionRawLogEntry? ReadEntry(long sequence) =>
        sequence == long.MaxValue ? null : ReadSequenceRange(sequence, sequence + 1, 1).FirstOrDefault();

    /// <summary>生ログを置き換えた新しい世代を用意します（まだ切り替えません）。</summary>
    /// <remarks>
    /// 責務: 現在の取得証拠の写しと新しい生ログだけを持つ世代を書き、読み戻して検証します。
    /// 呼出元はSQLiteの確定後に <see cref="PreparedGeneration.Commit"/>、失敗時に <see cref="PreparedGeneration.Rollback"/> を呼びます。
    /// </remarks>
    /// <param name="entries">新しい世代の生ログ。空なら生ログを除去します。</param>
    /// <returns>切替前の新しい世代。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">読み戻しが一致しない場合は <c>IntegrityConflict</c>。</exception>
    internal PreparedGeneration PrepareReplacement(IReadOnlyList<ConnectionSessionRawLogEntry> entries)
    {
        var batches = Evidence.Batches.ToArray();
        WaitDurable();
        _writer.Close(this);
        WaitForCompressions();
        var nextGeneration = _generation + 1;
        var temporary = GenerationDirectory(_sessionDirectory, nextGeneration) + ".tmp";
        if (Directory.Exists(temporary))
        {
            Directory.Delete(temporary, recursive: true);
        }
        Directory.CreateDirectory(temporary);
        var index = new SessionLogIndex(_options.IndexStride);
        var evidence = new SessionLogEvidenceState();
        var segment = 0;
        long offset = SegmentFileFormat.HeaderSize;
        FileStream? stream = null;
        try
        {
            void WriteRecord(SessionLogRecord record)
            {
                var frame = SessionLogRecordCodec.EncodeFrame(record);
                if (stream is null || (offset + frame.Length > _options.SegmentMaxBytes && offset > SegmentFileFormat.HeaderSize))
                {
                    if (stream is not null)
                    {
                        stream.Flush(flushToDisk: true);
                        stream.Dispose();
                        segment += 1;
                        offset = SegmentFileFormat.HeaderSize;
                    }
                    stream = new FileStream(SegmentFileFormat.PlainPath(temporary, segment), FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
                    SegmentFileFormat.WriteHeader(stream, segment);
                }
                Track(index, evidence, record, new SessionLogPosition(segment, offset));
                stream.Write(frame);
                offset += frame.Length;
            }
            foreach (var batch in batches)
            {
                WriteRecord(new SessionLogRecord.BatchSnapshot(batch));
            }
            foreach (var entry in entries)
            {
                WriteRecord(new SessionLogRecord.Raw(entry));
            }
            stream?.Flush(flushToDisk: true);
        }
        finally
        {
            stream?.Dispose();
        }
        var (verifyIndex, _, verifyCursor) = Scan(temporary, _options);
        var readBack = ReadAllFrom(temporary, verifyIndex, verifyCursor);
        if (readBack.Count != entries.Count || !readBack.SequenceEqual(entries))
        {
            Directory.Delete(temporary, recursive: true);
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
        }
        var activated = GenerationDirectory(_sessionDirectory, nextGeneration);
        Directory.Move(temporary, activated);
        var cursor = stream is null ? new SessionLogPosition(0, SegmentFileFormat.HeaderSize) : new SessionLogPosition(segment, offset);
        return new PreparedGeneration(this, nextGeneration, index, evidence, cursor);
    }

    /// <summary>書込みを確定して閉じ、索引ファイルを書きます。</summary>
    /// <remarks>責務: 次回の起動で走査を省けるよう、確定済みの状態を索引ファイルへ残します。</remarks>
    internal void Close()
    {
        lock (_stateGate)
        {
            if (_isDeleted)
            {
                return;
            }
        }
        try
        {
            WaitDurable();
        }
        catch (ConnectionSessionAcquisitionRepositoryException)
        {
            _writer.Close(this);
            return;
        }
        _writer.Close(this);
        WaitForCompressions();
        lock (_stateGate)
        {
            if (Fault is null && !_isDeleted && !_sidecarPresent)
            {
                WriteSidecar();
            }
        }
    }

    /// <summary>セッションのログをすべて削除します。</summary>
    /// <remarks>責務: 書込みを閉じ、読取が終わるのを待ってからセッションのディレクトリーを消します。</remarks>
    internal void Delete()
    {
        try
        {
            WaitDurable();
        }
        catch (ConnectionSessionAcquisitionRepositoryException)
        {
        }
        _writer.Close(this);
        WaitForCompressions();
        _fileLock.EnterWriteLock();
        try
        {
            lock (_stateGate)
            {
                _isDeleted = true;
            }
            if (Directory.Exists(_sessionDirectory))
            {
                Directory.Delete(_sessionDirectory, recursive: true);
            }
        }
        finally
        {
            _fileLock.ExitWriteLock();
        }
    }

    /// <summary>枠付きバイト列を指定位置へ書きます（書込みスレッド専用）。</summary>
    /// <remarks>責務: 必要ならセグメントを切り替え、指定オフセットへ1件の枠を書きます。</remarks>
    /// <param name="segment">セグメント番号。</param>
    /// <param name="offset">オフセット。</param>
    /// <param name="frame">枠付きバイト列。</param>
    internal void WriteFrame(int segment, long offset, byte[] frame)
    {
        if (_writerStream is null || _writerSegment != segment)
        {
            if (_writerStream is not null)
            {
                var sealedSegment = _writerSegment;
                _writerStream.Flush(flushToDisk: true);
                _writerStream.Dispose();
                _writerStream = null;
                ScheduleCompression(sealedSegment);
            }
            var path = SegmentFileFormat.PlainPath(_generationDirectory, segment);
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            if (stream.Length < SegmentFileFormat.HeaderSize)
            {
                stream.SetLength(0);
                SegmentFileFormat.WriteHeader(stream, segment);
            }
            _writerStream = stream;
            _writerSegment = segment;
        }
        if (_writerStream.Position != offset)
        {
            _writerStream.Position = offset;
        }
        _writerStream.Write(frame);
    }

    /// <summary>書込みファイルをOSへ、必要ならディスクまで書き出します（書込みスレッド専用）。</summary>
    /// <remarks>責務: グループの終わりに1回だけ書き出し、確定方針に従ってfsyncします。</remarks>
    /// <param name="durable">ディスクまで確定する場合は <see langword="true"/>。</param>
    internal void FlushWriterStream(bool durable) => _writerStream?.Flush(flushToDisk: durable);

    /// <summary>書込みファイルを閉じます（書込みスレッド専用）。</summary>
    /// <remarks>責務: 確定してからファイルを閉じます。圧縮は行いません（次の追記で続きを書けるように）。</remarks>
    internal void CloseWriterStream()
    {
        if (_writerStream is null)
        {
            return;
        }
        _writerStream.Flush(flushToDisk: true);
        _writerStream.Dispose();
        _writerStream = null;
        _writerSegment = -1;
    }

    /// <summary>新しい世代へ切り替えます。</summary>
    /// <remarks>責務: 用意済みの世代の状態を採用し、古い世代のディレクトリーを消します。</remarks>
    /// <param name="prepared">用意済みの世代。</param>
    internal void CommitGeneration(PreparedGeneration prepared)
    {
        _fileLock.EnterWriteLock();
        try
        {
            lock (_stateGate)
            {
                var previous = _generationDirectory;
                _generation = prepared.Generation;
                _generationDirectory = GenerationDirectory(_sessionDirectory, prepared.Generation);
                _index = prepared.Index;
                _evidence = prepared.Evidence;
                _cursor = prepared.Cursor;
                _sidecarPresent = false;
                if (Directory.Exists(previous))
                {
                    Directory.Delete(previous, recursive: true);
                }
            }
        }
        finally
        {
            _fileLock.ExitWriteLock();
        }
    }

    /// <summary>用意済みの世代を捨てます。</summary>
    /// <remarks>責務: 切り替えずに新しい世代のディレクトリーを消し、現在の世代を残します。</remarks>
    /// <param name="prepared">用意済みの世代。</param>
    internal void RollbackGeneration(PreparedGeneration prepared)
    {
        var directory = GenerationDirectory(_sessionDirectory, prepared.Generation);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>世代ディレクトリーのパスを返します。</summary>
    /// <remarks>責務: セッションディレクトリーと世代番号からパスを組み立てます。</remarks>
    /// <param name="sessionDirectory">セッションのディレクトリー。</param>
    /// <param name="generation">世代番号。</param>
    /// <returns>世代ディレクトリー。</returns>
    private static string GenerationDirectory(string sessionDirectory, int generation) =>
        Path.Combine(sessionDirectory, "g" + generation.ToString("D6", CultureInfo.InvariantCulture));

    /// <summary>次のsequenceを持つ生ログを作ります。</summary>
    /// <remarks>責務: 未デコード応答へ索引の次のsequenceを割り当てます。</remarks>
    /// <param name="observation">未デコード応答。</param>
    /// <returns>生ログ。</returns>
    private ConnectionSessionRawLogEntry NextEntry(OBDRawResponseObservation observation) =>
        new(
            _index.NextSequence,
            observation.ObservedAt,
            observation.BatchElapsedNanoseconds,
            observation.Request.Service,
            observation.Request.Pid,
            observation.Payload);

    /// <summary>ロック内で1件のレコードを追記します。</summary>
    /// <remarks>責務: 追記位置を決め、状態を更新し、背景書込みへ渡します。</remarks>
    /// <param name="record">追記するレコード。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">削除済み、書込み失敗、キュー満杯の場合は <c>Unavailable</c>。</exception>
    private void AppendLocked(SessionLogRecord record)
    {
        if (_isDeleted || Fault is not null || _evidence is { IsCorrupt: true })
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
        if (_sidecarPresent)
        {
            File.Delete(Path.Combine(_generationDirectory, SidecarFileName));
            _sidecarPresent = false;
        }
        var frame = SessionLogRecordCodec.EncodeFrame(record);
        if (_cursor.Offset + frame.Length > _options.SegmentMaxBytes && _cursor.Offset > SegmentFileFormat.HeaderSize)
        {
            _cursor = new SessionLogPosition(_cursor.Segment + 1, SegmentFileFormat.HeaderSize);
        }
        var position = _cursor;
        var ticket = _enqueuedTicket + 1;
        _writer.Enqueue(this, position.Segment, position.Offset, frame, ticket);
        _enqueuedTicket = ticket;
        _cursor = position with { Offset = position.Offset + frame.Length };
        Track(_index, _evidence, record, position);
    }

    /// <summary>レコードを索引と証拠状態へ反映します。</summary>
    /// <remarks>責務: Rawを含むレコードを索引へ、証拠レコードを証拠状態へ反映します。</remarks>
    /// <param name="index">反映先の索引。</param>
    /// <param name="evidence">反映先の証拠状態（未作成なら <see langword="null"/>）。</param>
    /// <param name="record">反映するレコード。</param>
    /// <param name="position">レコードの位置。</param>
    private static void Track(SessionLogIndex index, SessionLogEvidenceState? evidence, SessionLogRecord record, SessionLogPosition position)
    {
        switch (record)
        {
            case SessionLogRecord.Raw raw:
                index.Add(raw.Entry.Sequence, raw.Entry.ObservedAt.UtcTicks, raw.Entry.Payload.Count, position);
                break;
            case SessionLogRecord.Responded responded:
                index.Add(responded.Entry.Sequence, responded.Entry.ObservedAt.UtcTicks, responded.Entry.Payload.Count, position);
                break;
        }
        evidence?.Apply(record);
    }

    /// <summary>読取用のスナップショットを取り、ファイル読取のロック内で処理を実行します。</summary>
    /// <remarks>
    /// 責務: 索引・追記位置・世代を固定し、キューの分がファイルへ渡るのを待ってから読みます。
    /// 読取中に世代が切り替わった場合は取り直します。削除済みなら何も読みません。
    /// </remarks>
    /// <typeparam name="TPlan">ロック内で索引から求める読取計画の型。</typeparam>
    /// <param name="plan">ロック内で索引から読取位置を求める処理。</param>
    /// <param name="read">スナップショットと読取計画を受け取る読取処理。</param>
    private void ReadSnapshot<TPlan>(Func<SessionLogIndex, TPlan> plan, Action<ReadState, TPlan> read)
    {
        while (true)
        {
            ReadState snapshot;
            TPlan planned;
            long ticket;
            lock (_stateGate)
            {
                if (_isDeleted)
                {
                    return;
                }
                snapshot = new ReadState(_generation, _generationDirectory, _cursor);
                planned = plan(_index);
                ticket = _enqueuedTicket;
            }
            _writer.WaitWritten(this, ticket);
            _fileLock.EnterReadLock();
            try
            {
                lock (_stateGate)
                {
                    if (_isDeleted)
                    {
                        return;
                    }
                    if (_generation != snapshot.Generation)
                    {
                        continue;
                    }
                }
                read(snapshot, planned);
                return;
            }
            finally
            {
                _fileLock.ExitReadLock();
            }
        }
    }

    /// <summary>位置から順にRawを含むレコードを読み、生ログを渡します。</summary>
    /// <remarks>責務: スナップショットの追記位置まで、セグメントをまたいで生ログを順に読みます。</remarks>
    /// <param name="snapshot">読取スナップショット。</param>
    /// <param name="start">開始位置。</param>
    /// <param name="end">終了位置（含まない）。<see langword="null"/>ならスナップショットの終わりまで。</param>
    /// <param name="visit">生ログを受け取り、続けるなら <see langword="true"/> を返す処理。</param>
    private static void Scan(ReadState snapshot, SessionLogPosition start, SessionLogPosition? end, Func<ConnectionSessionRawLogEntry, bool> visit)
    {
        var stop = end is { } explicitEnd && explicitEnd.CompareTo(snapshot.Cursor) < 0 ? explicitEnd : snapshot.Cursor;
        for (var segment = start.Segment; segment <= stop.Segment; segment++)
        {
            var from = segment == start.Segment ? start.Offset : SegmentFileFormat.HeaderSize;
            var to = segment == stop.Segment ? stop.Offset : long.MaxValue;
            if (from >= to)
            {
                continue;
            }
            Stream stream;
            try
            {
                stream = SegmentFileFormat.OpenRead(snapshot.Directory, segment);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            using var reader = new SegmentFrameReader(stream, from, to);
            while (reader.TryReadNext(out _, out var payload) == SegmentFrameReadStatus.Frame)
            {
                if (SessionLogRecordCodec.TryDecodeEntry(payload, out var entry) && !visit(entry))
                {
                    return;
                }
            }
        }
    }

    /// <summary>指定ディレクトリーの全生ログを読みます。</summary>
    /// <remarks>責務: 切替前の世代を読み戻して検証するために使います。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="index">世代の索引。</param>
    /// <param name="cursor">世代の終わり。</param>
    /// <returns>sequence順の生ログ。</returns>
    private static List<ConnectionSessionRawLogEntry> ReadAllFrom(string directory, SessionLogIndex index, SessionLogPosition cursor)
    {
        var results = new List<ConnectionSessionRawLogEntry>();
        if (index.StartPosition(0) is { } start)
        {
            Scan(new ReadState(0, directory, cursor), start, null, entry =>
            {
                results.Add(entry);
                return true;
            });
        }
        return results;
    }

    /// <summary>世代ディレクトリーを走査して状態を復元し、壊れた末尾を切り詰めます。</summary>
    /// <remarks>責務: 全セグメントのレコードを検証しながら索引と証拠状態を作ります。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="options">ストアの設定。</param>
    /// <returns>索引、証拠状態、次の追記位置。</returns>
    private static (SessionLogIndex Index, SessionLogEvidenceState Evidence, SessionLogPosition Cursor) Scan(string directory, LogStoreOptions options)
    {
        var index = new SessionLogIndex(options.IndexStride);
        var evidence = new SessionLogEvidenceState();
        var segments = SegmentFileFormat.ListSegments(directory);
        var cursor = new SessionLogPosition(0, SegmentFileFormat.HeaderSize);
        for (var position = 0; position < segments.Count; position++)
        {
            var segment = segments[position];
            var plainPath = SegmentFileFormat.PlainPath(directory, segment);
            var isPlain = File.Exists(plainPath);
            long validEnd;
            bool corrupt;
            try
            {
                (validEnd, corrupt) = ScanSegment(directory, segment, index, evidence);
            }
            catch (Exception) when (!isPlain)
            {
                (validEnd, corrupt) = (SegmentFileFormat.HeaderSize, true);
            }
            if (isPlain)
            {
                if (corrupt || new FileInfo(plainPath).Length != validEnd)
                {
                    using var file = new FileStream(plainPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    if (validEnd <= SegmentFileFormat.HeaderSize && !HasValidHeader(file, segment))
                    {
                        file.SetLength(0);
                        SegmentFileFormat.WriteHeader(file, segment);
                        validEnd = SegmentFileFormat.HeaderSize;
                    }
                    file.SetLength(validEnd);
                    file.Flush(flushToDisk: true);
                }
                cursor = new SessionLogPosition(segment, validEnd);
            }
            else
            {
                cursor = new SessionLogPosition(segment + 1, SegmentFileFormat.HeaderSize);
            }
            if (corrupt)
            {
                foreach (var later in segments.Skip(position + 1))
                {
                    File.Delete(SegmentFileFormat.PlainPath(directory, later));
                    File.Delete(SegmentFileFormat.CompressedPath(directory, later));
                }
                break;
            }
        }
        return (index, evidence, cursor);
    }

    /// <summary>1セグメントを走査します。</summary>
    /// <remarks>責務: ヘッダーを確かめ、正しいレコードを索引と証拠状態へ反映して、正しい末尾の位置を返します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="segment">セグメント番号。</param>
    /// <param name="index">反映先の索引。</param>
    /// <param name="evidence">反映先の証拠状態。</param>
    /// <returns>(正しい末尾の位置, 壊れたレコードがあったか)。</returns>
    private static (long ValidEnd, bool Corrupt) ScanSegment(string directory, int segment, SessionLogIndex index, SessionLogEvidenceState evidence)
    {
        using var stream = SegmentFileFormat.OpenRead(directory, segment);
        if (!HasValidHeader(stream, segment))
        {
            return (SegmentFileFormat.HeaderSize, true);
        }
        using var reader = new SegmentFrameReader(stream, SegmentFileFormat.HeaderSize, long.MaxValue);
        while (true)
        {
            var status = reader.TryReadNext(out var offset, out var payload);
            if (status == SegmentFrameReadStatus.End)
            {
                return (reader.NextOffset, false);
            }
            if (status == SegmentFrameReadStatus.Corrupt)
            {
                return (offset, true);
            }
            SessionLogRecord record;
            try
            {
                record = SessionLogRecordCodec.Decode(payload);
            }
            catch (InvalidDataException)
            {
                return (offset, true);
            }
            Track(index, evidence, record, new SessionLogPosition(segment, offset));
        }
    }

    /// <summary>ストリーム先頭のヘッダーが正しいかを返します。</summary>
    /// <remarks>責務: 先頭16バイトを読んでマジックとセグメント番号を確かめます。</remarks>
    /// <param name="stream">セグメントの論理バイト列。</param>
    /// <param name="segment">期待するセグメント番号。</param>
    /// <returns>正しいヘッダーなら <see langword="true"/>。</returns>
    private static bool HasValidHeader(Stream stream, int segment)
    {
        if (stream.Length < SegmentFileFormat.HeaderSize)
        {
            return false;
        }
        Span<byte> header = stackalloc byte[SegmentFileFormat.HeaderSize];
        stream.Position = 0;
        stream.ReadExactly(header);
        return SegmentFileFormat.IsValidHeader(header, segment);
    }

    /// <summary>非圧縮と圧縮の両方があるセグメントの重複を片付けます。</summary>
    /// <remarks>責務: 圧縮の名前変更の途中で落ちた場合に、確定済みの圧縮ファイルを残して非圧縮を消します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    private static void RemoveDuplicateSegments(string directory)
    {
        foreach (var segment in SegmentFileFormat.ListSegments(directory))
        {
            var plain = SegmentFileFormat.PlainPath(directory, segment);
            var compressed = SegmentFileFormat.CompressedPath(directory, segment);
            if (File.Exists(plain) && File.Exists(compressed))
            {
                File.Delete(plain);
            }
        }
    }

    /// <summary>索引ファイルを読み、現在のセグメントと一致すれば採用します。</summary>
    /// <remarks>責務: CRCとセグメントの論理長を確かめてから、索引と追記位置を復元します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="options">ストアの設定。</param>
    /// <param name="index">復元した索引。</param>
    /// <param name="cursor">次の追記位置。</param>
    /// <returns>採用できた場合は <see langword="true"/>。</returns>
    private static bool TryLoadSidecar(string directory, LogStoreOptions options, out SessionLogIndex index, out SessionLogPosition cursor)
    {
        index = null!;
        cursor = default;
        var path = Path.Combine(directory, SidecarFileName);
        if (!File.Exists(path))
        {
            return false;
        }
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 8 || Crc32.Compute(data.AsSpan(0, data.Length - 4)) != BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4)))
            {
                File.Delete(path);
                return false;
            }
            var segmentCount = BinaryPrimitives.ReadInt32LittleEndian(data);
            var actual = SegmentFileFormat.ListSegments(directory);
            if (segmentCount != actual.Count)
            {
                File.Delete(path);
                return false;
            }
            var offset = 4;
            var last = new SessionLogPosition(0, SegmentFileFormat.HeaderSize);
            for (var item = 0; item < segmentCount; item++)
            {
                var segment = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
                var length = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset + 4));
                offset += 12;
                if (actual[item] != segment || SegmentFileFormat.LogicalLength(directory, segment) != length)
                {
                    File.Delete(path);
                    return false;
                }
                last = File.Exists(SegmentFileFormat.PlainPath(directory, segment))
                    ? new SessionLogPosition(segment, length)
                    : new SessionLogPosition(segment + 1, SegmentFileFormat.HeaderSize);
            }
            if (SessionLogIndex.ReadFrom(data.AsSpan(offset, data.Length - 4 - offset)) is not { } loaded || loaded.Stride != Math.Max(1, options.IndexStride))
            {
                File.Delete(path);
                return false;
            }
            index = loaded;
            cursor = last;
            return true;
        }
        catch (Exception)
        {
            File.Delete(path);
            return false;
        }
    }

    /// <summary>現在の状態を索引ファイルへ書きます。</summary>
    /// <remarks>責務: セグメントの論理長と索引をCRC付きで一時ファイルへ書き、確定名へ置き換えます。</remarks>
    private void WriteSidecar()
    {
        var segments = SegmentFileFormat.ListSegments(_generationDirectory);
        using var buffer = new MemoryStream();
        Span<byte> item = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(item, segments.Count);
        buffer.Write(item[..4]);
        foreach (var segment in segments)
        {
            BinaryPrimitives.WriteInt32LittleEndian(item, segment);
            BinaryPrimitives.WriteInt64LittleEndian(item[4..], SegmentFileFormat.LogicalLength(_generationDirectory, segment));
            buffer.Write(item);
        }
        _index.WriteTo(buffer);
        var body = buffer.ToArray();
        var data = new byte[body.Length + 4];
        body.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(body.Length), Crc32.Compute(body));
        var path = Path.Combine(_generationDirectory, SidecarFileName);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
        _sidecarPresent = true;
    }

    /// <summary>確定済みセグメントの圧縮を背景で始めます（書込みスレッドから呼ばれます）。</summary>
    /// <remarks>責務: 設定で有効なら、切替後のセグメントをスレッドプールで圧縮して置き換えます。</remarks>
    /// <param name="segment">圧縮するセグメント番号。</param>
    private void ScheduleCompression(int segment)
    {
        if (!_options.CompressSealedSegments)
        {
            return;
        }
        if (Volatile.Read(ref _isDeleted))
        {
            return;
        }
        var directory = Volatile.Read(ref _generationDirectory);
        lock (_compressionGate)
        {
            _pendingCompressions += 1;
        }
        ThreadPool.UnsafeQueueUserWorkItem(_ => Compress(directory, segment), null);
    }

    /// <summary>1セグメントを圧縮して置き換えます。</summary>
    /// <remarks>責務: 一時ファイルへ圧縮し、読取がない間に圧縮ファイルへ名前を変えて非圧縮を消します。失敗時は非圧縮のまま残します。</remarks>
    /// <param name="directory">世代ディレクトリー。</param>
    /// <param name="segment">セグメント番号。</param>
    private void Compress(string directory, int segment)
    {
        try
        {
            var temporary = SegmentFileFormat.WriteCompressedTemporary(directory, segment);
            _fileLock.EnterWriteLock();
            try
            {
                bool current;
                lock (_stateGate)
                {
                    current = !_isDeleted && string.Equals(directory, _generationDirectory, StringComparison.Ordinal);
                }
                if (current)
                {
                    File.Move(temporary, SegmentFileFormat.CompressedPath(directory, segment), overwrite: true);
                    File.Delete(SegmentFileFormat.PlainPath(directory, segment));
                }
                else
                {
                    File.Delete(temporary);
                }
            }
            finally
            {
                _fileLock.ExitWriteLock();
            }
        }
        catch (Exception)
        {
            // 圧縮は任意の最適化です。失敗しても非圧縮のセグメントが正しく残ります。
        }
        finally
        {
            lock (_compressionGate)
            {
                _pendingCompressions -= 1;
                Monitor.PulseAll(_compressionGate);
            }
        }
    }

    /// <summary>実行中の圧縮が終わるまで待ちます。</summary>
    /// <remarks>責務: 削除や世代切替の前に、圧縮がファイルを使い終えるのを待ちます。</remarks>
    private void WaitForCompressions()
    {
        lock (_compressionGate)
        {
            while (_pendingCompressions > 0)
            {
                Monitor.Wait(_compressionGate);
            }
        }
    }

    /// <summary>読取時に固定する状態です。</summary>
    /// <param name="Generation">世代番号。</param>
    /// <param name="Directory">世代ディレクトリー。</param>
    /// <param name="Cursor">読取の終わり。</param>
    private sealed record ReadState(int Generation, string Directory, SessionLogPosition Cursor);
}

/// <summary>切替前の新しい世代です。</summary>
/// <param name="Log">対象のログ。</param>
/// <param name="Generation">新しい世代番号。</param>
/// <param name="Index">新しい世代の索引。</param>
/// <param name="Evidence">新しい世代の証拠状態。</param>
/// <param name="Cursor">新しい世代の次の追記位置。</param>
internal sealed record PreparedGeneration(
    SessionLog Log,
    int Generation,
    SessionLogIndex Index,
    SessionLogEvidenceState Evidence,
    SessionLogPosition Cursor)
{
    /// <summary>新しい世代へ切り替えます。</summary>
    /// <remarks>責務: SQLiteの確定後に新しい世代を採用します。</remarks>
    public void Commit() => Log.CommitGeneration(this);

    /// <summary>新しい世代を捨てます。</summary>
    /// <remarks>責務: SQLiteの確定に失敗したとき、元の世代へ戻します。</remarks>
    public void Rollback() => Log.RollbackGeneration(this);
}
