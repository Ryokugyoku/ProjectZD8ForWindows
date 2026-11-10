using System.Diagnostics;
using System.Globalization;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>セッションごとの追記専用ジャーナル（生ログ・取得batch・PID要求証拠）を管理する非リレーショナルなストアです。</summary>
/// <remarks>
/// <para>
/// 保存先は組み立てが渡すディレクトリーの下の <c>&lt;sessionID&gt;/</c> です。書込みは1本の背景スレッドがまとめて行い、
/// 計測側は確定（fsync）を待ちません。詳しい形式は <see cref="SessionLog"/> を参照してください。
/// </para>
/// <para>
/// Application層からは見えません。接続セッションの保存一式（<c>SqliteConnectionSessionRepository</c>）がDomainの
/// リポジトリーインターフェースの裏で使うので、保存方式を後で差し替えられます。
/// </para>
/// </remarks>
public sealed class ConnectionSessionLogStore : IDisposable
{
    /// <summary>開いているログの表を守るロックです。</summary>
    private readonly object _gate = new();
    /// <summary>開いているログです。</summary>
    private readonly Dictionary<Guid, SessionLog> _logs = [];
    /// <summary>ストアのルートディレクトリーです。</summary>
    private readonly string _directory;
    /// <summary>ストアの設定です。</summary>
    private readonly LogStoreOptions _options;
    /// <summary>背景書込みです。</summary>
    private readonly SessionLogWriter _writer;
    /// <summary>破棄済みかどうかです。</summary>
    private bool _isDisposed;

    /// <summary>ルートディレクトリーと設定を指定して生成します。</summary>
    /// <remarks>責務: ルートディレクトリーを作成し、背景書込みを開始します。</remarks>
    /// <param name="directory">ストアのルートディレクトリー。</param>
    /// <param name="options">ストアの設定。</param>
    private ConnectionSessionLogStore(string directory, LogStoreOptions options)
    {
        _directory = directory;
        _options = options;
        Directory.CreateDirectory(directory);
        _writer = new SessionLogWriter(options);
    }

    /// <summary>ストアのルートディレクトリーです。</summary>
    public string DirectoryPath => _directory;

    /// <summary>ストアの設定です。</summary>
    public LogStoreOptions Options => _options;

    /// <summary>指定ディレクトリーのストアを開きます。</summary>
    /// <remarks>責務: 組み立てやテストが渡したディレクトリーをストアの保存先として開きます。</remarks>
    /// <param name="directory">ストアのルートディレクトリー。</param>
    /// <param name="options">ストアの設定。省略時は既定。</param>
    /// <returns>開いたストア。</returns>
    public static ConnectionSessionLogStore Open(string directory, LogStoreOptions? options = null) =>
        new(directory, options ?? LogStoreOptions.Default);

    /// <summary>セッションのログを開きます。なければ作ります。</summary>
    /// <remarks>責務: 1件のセッションのログを開いた状態で返し、最近使った順を更新します。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>開いたログ。</returns>
    internal SessionLog Get(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_logs.TryGetValue(sessionID.RawValue, out var log))
            {
                log = SessionLog.Open(SessionDirectory(sessionID.RawValue), _options, _writer);
                _logs[sessionID.RawValue] = log;
            }
            log.LastUsed = Stopwatch.GetTimestamp();
            return log;
        }
    }

    /// <summary>ログがあれば開いて返します。</summary>
    /// <remarks>責務: 読取専用の照会で、ログがないセッションのディレクトリーを作らないようにします。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>開いたログ。ない場合は <see langword="null"/>。</returns>
    internal SessionLog? Find(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_logs.ContainsKey(sessionID.RawValue) && !Directory.Exists(SessionDirectory(sessionID.RawValue)))
            {
                return null;
            }
        }
        return Get(sessionID);
    }

    /// <summary>セッションのログを削除します。</summary>
    /// <remarks>責務: 開いていれば閉じてから、セッションのディレクトリーを消します。</remarks>
    /// <param name="sessionID">セッションID。</param>
    internal void Delete(ConnectionSessionID sessionID)
    {
        SessionLog? log;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _logs.Remove(sessionID.RawValue, out log);
        }
        if (log is not null)
        {
            log.Delete();
            return;
        }
        var directory = SessionDirectory(sessionID.RawValue);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>ログを持つセッションのIDを返します。</summary>
    /// <remarks>責務: 起動時の孤立ログ片付けのため、ディレクトリーがあるセッションを列挙します。</remarks>
    /// <returns>セッションID。</returns>
    internal IReadOnlyList<ConnectionSessionID> StoredSessionIDs()
    {
        var identifiers = new List<ConnectionSessionID>();
        foreach (var path in Directory.EnumerateDirectories(_directory))
        {
            if (Guid.TryParseExact(Path.GetFileName(path), "D", out var identifier))
            {
                identifiers.Add(new ConnectionSessionID(identifier));
            }
        }
        return identifiers;
    }

    /// <summary>使っていないログを閉じてメモリを空けます。</summary>
    /// <remarks>責務: 計測中でなく書込み待ちもないログを、最近使っていない順に設定数まで閉じます。</remarks>
    internal void TrimIdle()
    {
        List<SessionLog> closing;
        lock (_gate)
        {
            if (_isDisposed || _logs.Count <= Math.Max(0, _options.MaximumIdleSessionLogs))
            {
                return;
            }
            var idle = _logs
                .Where(pair => !pair.Value.IsPinned && !pair.Value.HasPendingWrites)
                .OrderBy(pair => pair.Value.LastUsed)
                .ToList();
            var excess = idle.Count - Math.Max(0, _options.MaximumIdleSessionLogs);
            closing = [];
            foreach (var pair in idle.Take(Math.Max(0, excess)))
            {
                _logs.Remove(pair.Key);
                closing.Add(pair.Value);
            }
        }
        foreach (var log in closing)
        {
            log.Close();
        }
    }

    /// <summary>索引ファイルを書かずにストアを捨てます（テストでプロセスの異常終了を再現するため）。</summary>
    /// <remarks>責務: キューの分だけ書いてファイルを閉じ、背景書込みを止めます。索引ファイルは書きません。</remarks>
    internal void Abandon()
    {
        List<SessionLog> logs;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            logs = _logs.Values.ToList();
            _logs.Clear();
        }
        foreach (var log in logs)
        {
            _writer.Close(log);
        }
        _writer.Dispose();
    }

    /// <summary>セッションのディレクトリーを返します。</summary>
    /// <remarks>責務: セッションIDを小文字UUIDのディレクトリー名へ変換します。</remarks>
    /// <param name="identifier">セッションID。</param>
    /// <returns>ディレクトリーのパス。</returns>
    private string SessionDirectory(Guid identifier) =>
        Path.Combine(_directory, identifier.ToString("D", CultureInfo.InvariantCulture));

    /// <summary>すべてのログを確定して閉じ、背景書込みを止めます。</summary>
    /// <remarks>責務: 開いている全ログの索引ファイルを書き、書込みスレッドを終了させます。</remarks>
    public void Dispose()
    {
        List<SessionLog> logs;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            logs = _logs.Values.ToList();
            _logs.Clear();
        }
        foreach (var log in logs)
        {
            log.Close();
        }
        _writer.Dispose();
    }
}
