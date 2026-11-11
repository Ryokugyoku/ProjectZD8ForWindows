using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// <see cref="JsonlDiagnosticSink"/> の設定です。
/// </summary>
public sealed class JsonlDiagnosticSinkOptions
{
    /// <summary>1 つのファイルの上限の既定（10 MB）です。超えたら連番の次のファイルへ移ります。</summary>
    public const long DefaultMaxFileBytes = 10L * 1024 * 1024;

    /// <summary>キューの件数の上限の既定です。</summary>
    public const int DefaultQueueCapacity = 8192;

    /// <summary>診断ログのフォルダー（<c>&lt;データ&gt;\diagnostics</c>）です。</summary>
    public required string Directory { get; init; }

    /// <summary>実行 ID（16 進 8 桁）です。</summary>
    public required string RunId { get; init; }

    /// <summary>最低レベルの決まりです。</summary>
    public required DiagnosticLevelPolicy Policy { get; init; }

    /// <summary>時計です（テストで差し替える）。</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>1 つのファイルの上限です。</summary>
    public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;

    /// <summary>キューの件数の上限です。Trace・Debug はこの 4 分の 3 で捨て始めます。</summary>
    public int QueueCapacity { get; init; } = DefaultQueueCapacity;
}

/// <summary>
/// JSON Lines のファイルへ書く出力先です（docs/LOGGING_STRATEGY.md の 3・8）。
/// </summary>
/// <remarks>
/// <para>
/// 呼び出し側はロックの中で番号を付けてキューへ入れるだけで、ファイルへは背景のスレッド 1 本が書きます（呼び出し側を待たせない）。
/// キューがあふれたら、Trace・Debug は上限の 4 分の 3 で、Info 以上は上限で捨て、捨てた件数を <c>diag.dropped</c> に書きます。
/// </para>
/// <para>
/// リングバッファを持つ決まり（Stable）では、ファイルへ書かないイベントもリングバッファに入れ、
/// Error 以上が来たらまだ書いていない分を <c>"ring":true</c> を付けてその前に書きます。
/// </para>
/// </remarks>
public sealed class JsonlDiagnosticSink : IDiagnosticSink, IDisposable
{
    /// <summary>クラッシュのときに通常のログを書き終えるまで待つ上限です。</summary>
    private static readonly TimeSpan CrashFlushTimeout = TimeSpan.FromSeconds(2);

    /// <summary>キューと番号を守るロックです。</summary>
    private readonly object _gate = new();

    /// <summary>設定です。</summary>
    private readonly JsonlDiagnosticSinkOptions _options;

    /// <summary>Trace・Debug を捨て始める件数です。</summary>
    private readonly int _lowPriorityLimit;

    /// <summary>実行を始めた日（ファイル名に使う）です。</summary>
    private readonly DateOnly _date;

    /// <summary>書込みのスレッドです。</summary>
    private readonly Thread _writer;


    /// <summary>1 行を作るもの（書込みのスレッドだけが使う）。</summary>
    private readonly DiagnosticJsonLine _line = new();

    /// <summary>最低レベルの決まりです。</summary>
    private DiagnosticLevelPolicy _policy;

    /// <summary>リングバッファです。持たない決まりなら null。</summary>
    private DiagnosticRingBuffer? _ring;

    /// <summary>書く前のイベントです。</summary>
    private List<PendingLine> _queue = [];

    /// <summary>書込みのスレッドが使い回すリストです。</summary>
    private List<PendingLine> _spare = [];

    /// <summary>最後に付けた通し番号です。</summary>
    private long _sequence;

    /// <summary>捨てた Trace・Debug の件数（まだ書いていない分）です。</summary>
    private long _droppedLow;

    /// <summary>捨てた Info 以上の件数（まだ書いていない分）です。</summary>
    private long _droppedHigh;

    /// <summary>捨てた件数の合計です（テスト・確認用）。</summary>
    private long _droppedTotal;

    /// <summary>書込みのスレッドが 1 回分を書いている最中か。</summary>
    private bool _writing;

    /// <summary>書込みのスレッドが待っているか。</summary>
    private bool _writerWaiting;

    /// <summary>止めるか。</summary>
    private bool _stopping;

    /// <summary>書込みを止めているか（テスト用：キューのあふれを作る）。</summary>
    private bool _paused;

    /// <summary>今のファイルです（書込みのスレッドだけが使う）。</summary>
    private FileStream? _stream;

    /// <summary>今のファイルの連番です。</summary>
    private int _fileIndex;

    /// <summary>今のファイルの大きさです。</summary>
    private long _fileLength;

    /// <summary>
    /// 出力先を作り、書込みのスレッドを始めます。
    /// </summary>
    /// <param name="options">設定。</param>
    public JsonlDiagnosticSink(JsonlDiagnosticSinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.RunId.Length < DiagnosticFileName.RunPrefixLength)
        {
            throw new ArgumentException("実行 ID は 6 桁以上にしてください。", nameof(options));
        }

        _options = options;
        _policy = options.Policy;
        _ring = options.Policy.HasBuffer ? new DiagnosticRingBuffer(options.Policy.BufferCapacity) : null;
        _lowPriorityLimit = Math.Max(1, options.QueueCapacity * 3 / 4);
        _date = DateOnly.FromDateTime(options.Clock.GetUtcNow().UtcDateTime);
        System.IO.Directory.CreateDirectory(options.Directory);
        _writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "ProjectZD8 診断ログの書込み",
            Priority = ThreadPriority.BelowNormal,
        };
        _writer.Start();
    }

    /// <summary>実行 ID です。</summary>
    public string RunId => _options.RunId;

    /// <summary>実行 ID の先頭 6 桁（ファイル名に使う）です。</summary>
    public string RunPrefix => _options.RunId[..DiagnosticFileName.RunPrefixLength];

    /// <summary>診断ログのフォルダーです。</summary>
    public string Directory => _options.Directory;

    /// <summary>今の最低レベルの決まりです。</summary>
    public DiagnosticLevelPolicy Policy => Volatile.Read(ref _policy);

    /// <summary>捨てた件数の合計です。</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedTotal);

    /// <summary>今の通常のログのファイルのパスです。</summary>
    public string CurrentFilePath => Path.Combine(_options.Directory, FileName(DiagnosticFileKind.App, _fileIndex));

    /// <summary>クラッシュのログのファイルのパスです。</summary>
    public string CrashFilePath => Path.Combine(_options.Directory, FileName(DiagnosticFileKind.Crash, 0));

    /// <inheritdoc />
    public bool IsEnabled(string category, LogLevel level) =>
        level != LogLevel.None && Volatile.Read(ref _policy).IsEnabled(category, level);

    /// <inheritdoc />
    public void Emit(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        var policy = Volatile.Read(ref _policy);
        var write = policy.ShouldWrite(diagnosticEvent);
        var buffer = policy.ShouldBuffer(diagnosticEvent);
        if (!write && !buffer)
        {
            return;
        }

        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            diagnosticEvent.Stamp(_options.Clock.GetUtcNow(), _options.RunId, ++_sequence);
            AcceptLocked(diagnosticEvent, write, buffer);
        }
    }

    /// <inheritdoc />
    public bool Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_queue.Count > 0 || _writing || _droppedLow + _droppedHigh > 0)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !_writer.IsAlive)
                {
                    return false;
                }

                Monitor.PulseAll(_gate);
                Monitor.Wait(_gate, remaining);
            }

            return true;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 責務: リングバッファの中身（全部）とクラッシュのイベントを、呼び出したスレッドで <c>crash-*.jsonl</c> に書いてディスクまで落とします。
    /// そのあと通常のログにも同じイベント（と、まだ書いていないリングバッファの分）を入れ、最大 2 秒待って書き終えます。
    /// </remarks>
    public void WriteCrash(DiagnosticEvent crash)
    {
        ArgumentNullException.ThrowIfNull(crash);
        List<DiagnosticEvent> context;
        lock (_gate)
        {
            crash.Stamp(_options.Clock.GetUtcNow(), _options.RunId, ++_sequence);
            context = _ring?.Snapshot() ?? [];
            if (!_stopping)
            {
                AcceptLocked(crash, write: true, buffer: _ring is not null);
            }
        }

        try
        {
            using var stream = new FileStream(CrashFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var line = new DiagnosticJsonLine();
            foreach (var previous in context)
            {
                stream.Write(line.Format(previous, fromBuffer: true).Span);
            }

            stream.Write(line.Format(crash, fromBuffer: false).Span);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 書けなければあきらめる（クラッシュの最中に別の例外を起こさない）。
        }

        Flush(CrashFlushTimeout);
    }

    /// <summary>
    /// 最低レベルの決まりを入れ替えます（「詳細ログ」スイッチなど）。
    /// </summary>
    /// <remarks>責務: 決まりを入れ替え、リングバッファを決まりに合わせて作り直すか外します。</remarks>
    /// <param name="policy">新しい決まり。</param>
    public void UpdatePolicy(DiagnosticLevelPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        lock (_gate)
        {
            Volatile.Write(ref _policy, policy);
            if (!policy.HasBuffer)
            {
                _ring = null;
            }
            else if (_ring is null || _ring.Capacity != policy.BufferCapacity)
            {
                _ring = new DiagnosticRingBuffer(policy.BufferCapacity);
            }
        }
    }

    /// <summary>
    /// 書込みのスレッドを止め、残りを書いてファイルを閉じます。
    /// </summary>
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

        _writer.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>書込みを止めます（テスト用：キューのあふれを作る）。</summary>
    internal void PauseWriter()
    {
        lock (_gate)
        {
            _paused = true;
        }
    }

    /// <summary>書込みを再開します（テスト用）。</summary>
    internal void ResumeWriter()
    {
        lock (_gate)
        {
            _paused = false;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// 番号を付けたイベントを、リングバッファとキューへ振り分けます（ロックの中で呼ぶ）。
    /// </summary>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <param name="write">ファイルへ書くか。</param>
    /// <param name="buffer">リングバッファに持つか。</param>
    private void AcceptLocked(DiagnosticEvent diagnosticEvent, bool write, bool buffer)
    {
        if (buffer && _ring is not null)
        {
            _ring.Add(diagnosticEvent, write);
            if (diagnosticEvent.Level >= LogLevel.Error)
            {
                foreach (var previous in _ring.TakeUnwritten())
                {
                    if (!ReferenceEquals(previous, diagnosticEvent))
                    {
                        EnqueueLocked(new PendingLine(previous, FromBuffer: true), highPriority: true);
                    }
                }
            }
        }

        if (write)
        {
            EnqueueLocked(new PendingLine(diagnosticEvent, FromBuffer: false), diagnosticEvent.Level >= LogLevel.Information);
        }
    }

    /// <summary>キューへ入れます。あふれていれば捨てて数えます（ロックの中で呼ぶ）。</summary>
    /// <param name="line">書く行。</param>
    /// <param name="highPriority">Info 以上（とリングバッファの吐き出し）なら true。</param>
    private void EnqueueLocked(PendingLine line, bool highPriority)
    {
        var limit = highPriority ? _options.QueueCapacity : _lowPriorityLimit;
        if (_queue.Count >= limit)
        {
            if (highPriority)
            {
                _droppedHigh++;
            }
            else
            {
                _droppedLow++;
            }

            _droppedTotal++;
            return;
        }

        _queue.Add(line);
        if (_writerWaiting)
        {
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>書込みのスレッドの本体です。</summary>
    private void WriterLoop()
    {
        try
        {
            while (true)
            {
                List<PendingLine> batch;
                DiagnosticEvent? dropped = null;
                lock (_gate)
                {
                    while ((_queue.Count == 0 && _droppedLow + _droppedHigh == 0 && !_stopping) || (_paused && !_stopping))
                    {
                        _writerWaiting = true;
                        Monitor.Wait(_gate);
                        _writerWaiting = false;
                    }

                    if (_queue.Count == 0 && _droppedLow + _droppedHigh == 0 && _stopping)
                    {
                        break;
                    }

                    batch = _queue;
                    _queue = _spare;
                    if (_droppedLow + _droppedHigh > 0)
                    {
                        dropped = new DiagnosticEvent(
                            LogLevel.Warning,
                            DiagnosticCategories.Diagnostics,
                            DiagnosticEventNames.Dropped,
                            "診断ログのキューがあふれたため、イベントを捨てました",
                            [new("low", _droppedLow), new("high", _droppedHigh)]);
                        dropped.Stamp(_options.Clock.GetUtcNow(), _options.RunId, ++_sequence);
                        _droppedLow = 0;
                        _droppedHigh = 0;
                    }

                    _writing = true;
                }

                try
                {
                    WriteBatch(batch, dropped);
                }
                catch (Exception)
                {
                    // 書込みのスレッドは止めない（次の回でファイルを開き直す）。
                    CloseStream();
                }

                batch.Clear();
                _spare = batch;
                lock (_gate)
                {
                    _writing = false;
                    Monitor.PulseAll(_gate);
                }
            }
        }
        finally
        {
            CloseStream();
            lock (_gate)
            {
                _writing = false;
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>1 回分の行を書き、OS へ渡します。</summary>
    /// <param name="batch">書く行。</param>
    /// <param name="dropped">捨てた件数のイベント。なければ null。</param>
    private void WriteBatch(List<PendingLine> batch, DiagnosticEvent? dropped)
    {
        foreach (var pending in batch)
        {
            WriteLine(_line.Format(pending.Event, pending.FromBuffer));
        }

        if (dropped is not null)
        {
            WriteLine(_line.Format(dropped, fromBuffer: false));
        }

        _stream?.Flush();
    }

    /// <summary>1 行を書きます。上限を超えるなら次のファイルへ移ります。</summary>
    /// <param name="bytes">1 行。</param>
    private void WriteLine(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (_stream is not null && _fileLength > 0 && _fileLength + bytes.Length > _options.MaxFileBytes)
            {
                CloseStream();
                _fileIndex++;
            }

            if (_stream is null)
            {
                OpenStream();
            }

            _stream!.Write(bytes.Span);
            _fileLength += bytes.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            CloseStream();
        }
    }

    /// <summary>今の連番のファイルを開きます（すでに上限を超えていれば次の連番へ）。</summary>
    private void OpenStream()
    {
        while (true)
        {
            var path = Path.Combine(_options.Directory, FileName(DiagnosticFileKind.App, _fileIndex));
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            if (stream.Length > 0 && stream.Length >= _options.MaxFileBytes)
            {
                stream.Dispose();
                _fileIndex++;
                continue;
            }

            _stream = stream;
            _fileLength = stream.Length;
            return;
        }
    }

    /// <summary>今のファイルを閉じます。</summary>
    private void CloseStream()
    {
        try
        {
            _stream?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        _stream = null;
        _fileLength = 0;
    }

    /// <summary>ファイル名を返します。</summary>
    /// <param name="kind">種類。</param>
    /// <param name="index">連番。</param>
    /// <returns>ファイル名。</returns>
    private string FileName(DiagnosticFileKind kind, int index) =>
        new DiagnosticFileName(kind, _date, RunPrefix, index).ToString();

    /// <summary>書く前の 1 行です。</summary>
    /// <param name="Event">イベント。</param>
    /// <param name="FromBuffer">リングバッファから吐き出した行か。</param>
    private readonly record struct PendingLine(DiagnosticEvent Event, bool FromBuffer);
}
