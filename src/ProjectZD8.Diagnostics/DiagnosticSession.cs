using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// <see cref="DiagnosticSession.Start"/> の設定です。
/// </summary>
public sealed class DiagnosticSessionOptions
{
    /// <summary>診断ログのフォルダー名です（データのフォルダーの下。生ログの <c>logs</c> とは別）。</summary>
    public const string DirectoryName = "diagnostics";

    /// <summary>データのフォルダー（パッケージの LocalState）です。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>ビルド時に埋め込んだチャンネルです。</summary>
    public required DiagnosticsChannel Channel { get; init; }

    /// <summary><c>app.start</c> に書く情報です。</summary>
    public required DiagnosticAppInfo App { get; init; }

    /// <summary>環境変数 <c>PROJECTZD8_DIAGNOSTICS_LEVEL</c> の値です。</summary>
    public string? EnvironmentLevel { get; init; }

    /// <summary><c>appsettings.Local.json</c> のパスです（<c>Diagnostics:Level</c> を読む）。</summary>
    public string? SettingsFilePath { get; init; }

    /// <summary>設定画面の「詳細ログ」スイッチが入っているかです（UI は後で作る。今は差し込み口だけ）。</summary>
    public bool VerboseLogging { get; init; }

    /// <summary>実行 ID です。省略すると乱数で作ります（テスト用）。</summary>
    public string? RunId { get; init; }

    /// <summary>時計です（テストで差し替える）。</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>1 つのファイルの上限です。</summary>
    public long MaxFileBytes { get; init; } = JsonlDiagnosticSinkOptions.DefaultMaxFileBytes;

    /// <summary>キューの件数の上限です。</summary>
    public int QueueCapacity { get; init; } = JsonlDiagnosticSinkOptions.DefaultQueueCapacity;
}

/// <summary>
/// 1 回の実行の診断ログです。出力先を差し込み、<c>app.start</c> / <c>app.exit</c> と前回の異常終了を記録します。
/// </summary>
/// <remarks>
/// アプリは組み立ての最初で <see cref="Start"/> を呼び、正常に終わるときに <see cref="Stop"/> を呼びます。
/// 起動時に前回の実行に <c>app.exit</c> がなければ <c>app.previous-run.crashed</c> を書き、
/// <see cref="PreviousRunCrashed"/> を true にします（シェルの InfoBar が読む）。
/// </remarks>
public sealed class DiagnosticSession : IDisposable
{
    /// <summary>終了のときに書き終えるまで待つ上限です。</summary>
    private static readonly TimeSpan StopFlushTimeout = TimeSpan.FromSeconds(2);

    /// <summary>出力先です。</summary>
    private readonly JsonlDiagnosticSink _sink;

    /// <summary>出力先の差し込みを戻すものです。</summary>
    private readonly IDisposable _installation;

    /// <summary>設定です。</summary>
    private readonly DiagnosticSessionOptions _options;

    /// <summary>始めた時刻です。</summary>
    private readonly DateTimeOffset _startedAt;

    /// <summary>止めたか。</summary>
    private int _stopped;

    /// <summary>作ります。</summary>
    /// <param name="options">設定。</param>
    /// <param name="sink">出力先。</param>
    /// <param name="resolution">最低レベルの決まり。</param>
    /// <param name="previousRun">前回の実行の様子。</param>
    private DiagnosticSession(
        DiagnosticSessionOptions options,
        JsonlDiagnosticSink sink,
        DiagnosticLevelResolution resolution,
        DiagnosticPreviousRun? previousRun)
    {
        _options = options;
        _sink = sink;
        _startedAt = options.Clock.GetUtcNow();
        LevelSource = resolution.Source;
        PreviousRun = previousRun;
        VerboseLogging = options.VerboseLogging;
        _installation = DiagnosticLog.Install(sink);
    }

    /// <summary>実行 ID（16 進 8 桁）です。報告用の番号（<c>7F3A-1842</c>）の先頭 4 桁に使います。</summary>
    public string RunId => _sink.RunId;

    /// <summary>起動時に集めたアプリと環境の情報です（不具合レポートの <c>summary.json</c> に使う）。</summary>
    public DiagnosticAppInfo AppInfo => _options.App;

    /// <summary>
    /// 利用者に見せる報告用の番号（<c>7F3A-1842</c>）です。実行 ID の先頭 4 桁と、直近の Error の <c>seq</c> を並べます。
    /// この実行でまだ Error がなければ null。
    /// </summary>
    public string? ReportNumber => _sink.LastErrorSequence is > 0 and var sequence
        ? DiagnosticReportNumber.Format(RunId, sequence)
        : null;

    /// <summary>チャンネルです。</summary>
    public DiagnosticsChannel Channel => _options.Channel;

    /// <summary>診断ログのフォルダーです。</summary>
    public string Directory => _sink.Directory;

    /// <summary>今の最低レベルの出どころです。</summary>
    public DiagnosticLevelSource LevelSource { get; private set; }

    /// <summary>「詳細ログ」スイッチが入っているかです。</summary>
    public bool VerboseLogging { get; private set; }

    /// <summary>前回の実行の様子です。初めての実行なら null。</summary>
    public DiagnosticPreviousRun? PreviousRun { get; }

    /// <summary>前回の実行が正常に終わらなかったか（<c>app.exit</c> がなかったか）です。</summary>
    public bool PreviousRunCrashed => PreviousRun?.Crashed == true;

    /// <summary>出力先です（テスト・確認用）。</summary>
    internal JsonlDiagnosticSink Sink => _sink;

    /// <summary>
    /// 診断ログを始めます。
    /// </summary>
    /// <remarks>
    /// 責務: 前回の異常終了を調べ、上限を超えた古いファイルを消し、最低レベルを決めて出力先を差し込み、
    /// <c>app.start</c>（と必要なら <c>app.previous-run.crashed</c>）を書きます。
    /// </remarks>
    /// <param name="options">設定。</param>
    /// <returns>始めた診断ログ。</returns>
    public static DiagnosticSession Start(DiagnosticSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = Path.Combine(options.DataDirectory, DiagnosticSessionOptions.DirectoryName);
        System.IO.Directory.CreateDirectory(directory);
        var runId = options.RunId ?? NewRunId();
        var runPrefix = runId[..DiagnosticFileName.RunPrefixLength];

        var previousRun = DiagnosticFileMaintenance.DetectPreviousRun(directory, runPrefix);
        DiagnosticFileMaintenance.Clean(
            directory,
            DiagnosticRetention.ForChannel(options.Channel),
            options.Clock.GetUtcNow().UtcDateTime,
            runPrefix);

        var resolution = Resolve(options, options.VerboseLogging);
        var sink = new JsonlDiagnosticSink(new JsonlDiagnosticSinkOptions
        {
            Directory = directory,
            RunId = runId,
            Policy = resolution.Policy,
            Clock = options.Clock,
            MaxFileBytes = options.MaxFileBytes,
            QueueCapacity = options.QueueCapacity,
        });
        Redact.DataDirectory = options.DataDirectory;
        var session = new DiagnosticSession(options, sink, resolution, previousRun);
        DiagnosticLog.Session = session;
        session.WriteStart();
        return session;
    }

    /// <summary>
    /// 「詳細ログ」スイッチを切り替えます（設定画面のスイッチの差し込み口）。
    /// </summary>
    /// <remarks>
    /// 責務: 環境変数・<c>appsettings.Local.json</c> の指定がなければ、Stable を Beta と同じ量に切り替えます。
    /// 7 日で自動で切る処理は、スイッチを持つ側（設定画面）が行います。
    /// </remarks>
    /// <param name="enabled">入れるなら true。</param>
    public void SetVerboseLogging(bool enabled)
    {
        var resolution = Resolve(_options, enabled);
        VerboseLogging = enabled;
        LevelSource = resolution.Source;
        _sink.UpdatePolicy(resolution.Policy);
        _sink.Emit(new DiagnosticEvent(
            LogLevel.Information,
            DiagnosticCategories.Diagnostics,
            "diag.level.changed",
            "診断ログの詳細さを変えました",
            [new("verbose", enabled), new("source", resolution.Source.ToString())]));
    }

    /// <summary>
    /// 受け取ったイベントを書き終えるまで待ちます。
    /// </summary>
    /// <param name="timeout">待つ上限。</param>
    /// <returns>書き終えたら true。</returns>
    public bool Flush(TimeSpan timeout) => _sink.Flush(timeout);

    /// <summary>
    /// 正常終了を記録して止めます（<c>app.exit</c>）。2 回目以降は何もしません。
    /// </summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var uptime = _options.Clock.GetUtcNow() - _startedAt;
        _sink.Emit(new DiagnosticEvent(
            LogLevel.Information,
            DiagnosticCategories.App,
            DiagnosticEventNames.AppExit,
            "アプリを終了します",
            [new("uptimeMs", (long)uptime.TotalMilliseconds)]));
        _sink.Flush(StopFlushTimeout);
        _installation.Dispose();
        _sink.Dispose();
        if (ReferenceEquals(DiagnosticLog.Session, this))
        {
            DiagnosticLog.Session = null;
        }
    }

    /// <summary>
    /// 止めます（<see cref="Stop"/> と同じ）。
    /// </summary>
    public void Dispose() => Stop();

    /// <summary>
    /// <c>app.exit</c> を書かずに止めます（テスト用：異常終了の代わり）。
    /// </summary>
    internal void AbandonWithoutExit()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _sink.Flush(StopFlushTimeout);
        _installation.Dispose();
        _sink.Dispose();
        if (ReferenceEquals(DiagnosticLog.Session, this))
        {
            DiagnosticLog.Session = null;
        }
    }

    /// <summary>新しい実行 ID（16 進 8 桁）を作ります。</summary>
    /// <returns>実行 ID。</returns>
    internal static string NewRunId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    /// <summary>設定と「詳細ログ」スイッチから最低レベルを決めます。</summary>
    /// <param name="options">設定。</param>
    /// <param name="verbose">「詳細ログ」スイッチ。</param>
    /// <returns>決まり。</returns>
    private static DiagnosticLevelResolution Resolve(DiagnosticSessionOptions options, bool verbose) =>
        DiagnosticLevelResolver.Resolve(
            options.Channel,
            options.EnvironmentLevel,
            DiagnosticLevelResolver.ReadSettingsFileLevel(options.SettingsFilePath),
            verbose);

    /// <summary><c>app.start</c> と、前回が異常終了なら <c>app.previous-run.crashed</c> を書きます。</summary>
    private void WriteStart()
    {
        var app = _options.App;
        _sink.Emit(new DiagnosticEvent(
            LogLevel.Information,
            DiagnosticCategories.App,
            DiagnosticEventNames.AppStart,
            "アプリを起動しました",
            [
                new("version", app.Version),
                new("channel", _options.Channel.ToRawValue()),
                new("os", app.OsBuild),
                new("cpu", app.Cpu),
                new("lang", app.UiLanguage),
                new("localMode", app.IsLocalMode),
                new("packaged", app.IsPackaged),
                new("runtime", RuntimeInformation.FrameworkDescription),
                new("levelSource", LevelSource.ToString()),
            ]));

        if (PreviousRun is { Crashed: true } previous)
        {
            _sink.Emit(new DiagnosticEvent(
                LogLevel.Warning,
                DiagnosticCategories.App,
                DiagnosticEventNames.PreviousRunCrashed,
                "前回の実行は正常に終了しませんでした",
                [
                    new("previousRun", previous.RunPrefix),
                    new("lastWrite", previous.LastWriteTimeUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
                ]));
        }
    }
}
