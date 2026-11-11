using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 不具合レポートに書く、アプリの外から分かる情報です。
/// </summary>
/// <param name="App">アプリと環境（バージョン・OS・CPU・表示言語など）。</param>
/// <param name="Channel">チャンネル。</param>
/// <param name="RunId">今の実行 ID。</param>
/// <param name="VerboseLogging">「詳細ログ」が入っているか。</param>
/// <param name="DefaultAdapterKind">既定アダプターの種類（<c>BluetoothLowEnergy</c> など）。選んでいなければ null。</param>
/// <param name="SessionCount">接続セッションの数。分からなければ null。</param>
public sealed record DiagnosticReportInfo(
    DiagnosticAppInfo App,
    DiagnosticsChannel Channel,
    string RunId,
    bool VerboseLogging,
    string? DefaultAdapterKind,
    int? SessionCount);

/// <summary>
/// 書き出した不具合レポートの結果です。
/// </summary>
/// <param name="LogFileCount">zip に入れた診断ログのファイルの数。</param>
/// <param name="ErrorCount"><c>summary.json</c> に載せた Error の数（最大 10）。</param>
public sealed record DiagnosticReportResult(int LogFileCount, int ErrorCount);

/// <summary>
/// 不具合レポート（zip）を書き出します（docs/LOGGING_STRATEGY.md の 6 の 2）。
/// </summary>
/// <remarks>
/// <para>
/// 中身は、診断ログ（<c>diagnostics\*.jsonl</c>、直近 7 日）と <c>summary.json</c> だけです。
/// 入れるのは診断ログのファイル名（<see cref="DiagnosticFileName"/>）に合うものに限るので、生ログ・SQLite・写真・
/// <c>appsettings.Local.json</c> などは、フォルダーに置いてあっても入りません。
/// </para>
/// <para>書込み中のファイルも読めるよう、共有を許して開きます。</para>
/// </remarks>
public static class DiagnosticReport
{
    /// <summary>レポートに入れる診断ログの期間です。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>概要のファイル名です。</summary>
    public const string SummaryEntryName = "summary.json";

    /// <summary>zip の中の診断ログのフォルダー名です。</summary>
    public const string LogsFolderName = "diagnostics";

    /// <summary>概要に載せる直近の Error の数です。</summary>
    public const int RecentErrorLimit = 10;

    /// <summary>書き出しの前に、書き終えるまで待つ上限です。</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

    /// <summary>概要の JSON の書式（日本語をそのまま、読みやすく）。</summary>
    private static readonly JsonWriterOptions SummaryWriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 保存ダイアログの既定のファイル名を返します。
    /// </summary>
    /// <param name="now">今の時刻。</param>
    /// <param name="runId">実行 ID。</param>
    /// <returns><c>RevTorqueInsight-report-20261111-7f3a.zip</c> の形。</returns>
    public static string SuggestedFileName(DateTimeOffset now, string runId)
    {
        var run = runId.Length > DiagnosticReportNumber.RunDigits ? runId[..DiagnosticReportNumber.RunDigits] : runId;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"RevTorqueInsight-report-{now.UtcDateTime:yyyyMMdd}-{run.ToLowerInvariant()}.zip");
    }

    /// <summary>
    /// 今の実行の診断ログから不具合レポートを書き出します。
    /// </summary>
    /// <remarks>責務: 書き出す前に <see cref="DiagnosticSession.Flush"/> で受け取ったイベントを書き終えてから、<see cref="Write(string, string, DiagnosticReportInfo, DateTimeOffset)"/> を呼びます。</remarks>
    /// <param name="session">今の実行の診断ログ。</param>
    /// <param name="zipPath">書き出す zip のパス（あれば上書き）。</param>
    /// <param name="defaultAdapterKind">既定アダプターの種類。なければ null。</param>
    /// <param name="sessionCount">接続セッションの数。分からなければ null。</param>
    /// <returns>書き出した結果。</returns>
    public static DiagnosticReportResult Write(
        DiagnosticSession session,
        string zipPath,
        string? defaultAdapterKind,
        int? sessionCount)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Flush(FlushTimeout);
        return Write(
            session.Directory,
            zipPath,
            new DiagnosticReportInfo(session.AppInfo, session.Channel, session.RunId, session.VerboseLogging, defaultAdapterKind, sessionCount),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 診断ログのフォルダーから不具合レポートを書き出します。
    /// </summary>
    /// <remarks>
    /// 責務: 直近 7 日に書いた診断ログを zip の <c>diagnostics\</c> に入れ、環境と直近の Error 10 件の <c>summary.json</c> を添えます。
    /// 書き出せなければ途中の zip を消して例外を投げます。
    /// </remarks>
    /// <param name="diagnosticsDirectory">診断ログのフォルダー。</param>
    /// <param name="zipPath">書き出す zip のパス（あれば上書き）。</param>
    /// <param name="info">環境の情報。</param>
    /// <param name="now">今の時刻。</param>
    /// <returns>書き出した結果。</returns>
    public static DiagnosticReportResult Write(string diagnosticsDirectory, string zipPath, DiagnosticReportInfo info, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(diagnosticsDirectory);
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        ArgumentNullException.ThrowIfNull(info);

        var threshold = now.UtcDateTime - Window;
        var files = DiagnosticLogFolder.Enumerate(diagnosticsDirectory)
            .Where(file => file.LastWriteTimeUtc >= threshold)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var errors = RecentErrors(files);

        try
        {
            using (var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    CopyLog(archive, file);
                }

                var summary = archive.CreateEntry(SummaryEntryName, CompressionLevel.Optimal);
                using var summaryStream = summary.Open();
                WriteSummary(summaryStream, info, now, files, errors);
            }
        }
        catch
        {
            TryDelete(zipPath);
            throw;
        }

        return new DiagnosticReportResult(files.Count, errors.Count);
    }

    /// <summary>診断ログを 1 つ、zip へ写します。書込み中でも読めるよう共有を許して開きます。</summary>
    /// <param name="archive">zip。</param>
    /// <param name="file">診断ログのファイル。</param>
    private static void CopyLog(ZipArchive archive, FileInfo file)
    {
        var entry = archive.CreateEntry($"{LogsFolderName}/{file.Name}", CompressionLevel.Optimal);
        using var target = entry.Open();
        using var source = OpenShared(file.FullName);
        source.CopyTo(target);
    }

    /// <summary>概要の JSON を書きます。</summary>
    /// <param name="stream">書き先。</param>
    /// <param name="info">環境の情報。</param>
    /// <param name="now">今の時刻。</param>
    /// <param name="files">入れた診断ログ。</param>
    /// <param name="errors">直近の Error。</param>
    private static void WriteSummary(Stream stream, DiagnosticReportInfo info, DateTimeOffset now, List<FileInfo> files, List<RecentError> errors)
    {
        using var writer = new Utf8JsonWriter(stream, SummaryWriterOptions);
        writer.WriteStartObject();
        writer.WriteString("generatedAt", now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("appVersion", info.App.Version);
        writer.WriteString("channel", info.Channel.ToRawValue());
        writer.WriteString("os", info.App.OsBuild);
        writer.WriteString("cpu", info.App.Cpu);
        writer.WriteString("language", info.App.UiLanguage);
        writer.WriteBoolean("localMode", info.App.IsLocalMode);
        writer.WriteBoolean("packaged", info.App.IsPackaged);
        writer.WriteBoolean("verboseLogging", info.VerboseLogging);
        writer.WriteString("runId", info.RunId);
        if (info.DefaultAdapterKind is { } adapter)
        {
            writer.WriteString("defaultAdapterKind", adapter);
        }
        else
        {
            writer.WriteNull("defaultAdapterKind");
        }

        if (info.SessionCount is { } count)
        {
            writer.WriteNumber("sessionCount", count);
        }
        else
        {
            writer.WriteNull("sessionCount");
        }

        writer.WriteStartArray("logFiles");
        foreach (var file in files)
        {
            writer.WriteStringValue(file.Name);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("recentErrors");
        foreach (var error in errors)
        {
            writer.WriteStartObject();
            writer.WriteString("evt", error.EventName);
            writer.WriteString("ts", error.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("report", DiagnosticReportNumber.Format(error.RunId, error.Sequence));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>入れる診断ログから、直近の Error を新しい順に取ります。</summary>
    /// <param name="files">入れる診断ログ。</param>
    /// <returns>直近の Error（最大 <see cref="RecentErrorLimit"/> 件）。</returns>
    private static List<RecentError> RecentErrors(List<FileInfo> files)
    {
        var found = new Dictionary<(string Run, long Sequence), RecentError>();
        foreach (var file in files)
        {
            try
            {
                using var stream = OpenShared(file.FullName);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    if (TryReadError(line) is { } error)
                    {
                        found[(error.RunId, error.Sequence)] = error;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 読めないファイルの Error は概要に載せない（ファイル自体は zip に入れる）。
            }
        }

        return found.Values
            .OrderByDescending(error => error.Timestamp)
            .ThenByDescending(error => error.Sequence)
            .Take(RecentErrorLimit)
            .ToList();
    }

    /// <summary>1 行が Error か Fatal なら読みます。</summary>
    /// <param name="line">JSONL の 1 行。</param>
    /// <returns>Error の情報。Error でない・読めない行（書込み中の途中の行など）は null。</returns>
    private static RecentError? TryReadError(string line)
    {
        // JSON を読む前に、Error の行だけに絞る（量の多い行を読み飛ばす）。
        if (!line.Contains("\"lvl\":\"error\"", StringComparison.Ordinal)
            && !line.Contains("\"lvl\":\"fatal\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("ring", out var ring) && ring.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("lvl", out var level)
                || level.GetString() is not ("error" or "fatal")
                || !root.TryGetProperty("evt", out var evt)
                || !root.TryGetProperty("run", out var run)
                || !root.TryGetProperty("seq", out var sequence)
                || !root.TryGetProperty("ts", out var ts)
                || evt.GetString() is not { } eventName
                || run.GetString() is not { Length: > 0 } runId
                || !sequence.TryGetInt64(out var sequenceNumber)
                || !DateTimeOffset.TryParse(
                    ts.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                return null;
            }

            return new RecentError(eventName, timestamp, runId, sequenceNumber);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>書込み中のファイルも読めるように開きます。</summary>
    /// <param name="path">ファイル。</param>
    /// <returns>読み取り用のストリーム。</returns>
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>ファイルを消します（消せなくても例外にしない）。</summary>
    /// <param name="path">ファイル。</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 途中の zip が残っても害はない。
        }
    }

    /// <summary>概要に載せる Error の 1 件です。</summary>
    /// <param name="EventName">イベント名。</param>
    /// <param name="Timestamp">時刻。</param>
    /// <param name="RunId">実行 ID。</param>
    /// <param name="Sequence">通し番号。</param>
    private sealed record RecentError(string EventName, DateTimeOffset Timestamp, string RunId, long Sequence);
}
