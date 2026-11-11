using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics.Tests.Support;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>不具合レポート（zip）の中身、入れてはいけないファイル、報告用の番号を確かめます。</summary>
[Collection(GlobalDiagnosticLogCollection.Name)]
public sealed class DiagnosticReportTests : IDisposable
{
    /// <summary>テストで使う「今」です。</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>一時フォルダー（データのフォルダー）です。</summary>
    private readonly TempDirectory _data = new();

    /// <summary>診断ログのフォルダーです。</summary>
    private string DiagnosticsDirectory => Path.Combine(_data.Path, "diagnostics");

    /// <summary>一時フォルダーを消します。</summary>
    public void Dispose() => _data.Dispose();

    /// <summary>報告用の番号は、実行 ID の先頭 4 桁（大文字）と seq をつなぐ。</summary>
    [Fact]
    public void ReportNumber_Format()
    {
        Assert.Equal("7F3A-1842", DiagnosticReportNumber.Format("7f3a9c2e", 1842));
        Assert.Equal("AB-3", DiagnosticReportNumber.Format("ab", 3));
    }

    /// <summary>Error がまだない実行は番号なし。Error が出たら直近の Error の seq で番号が付く。</summary>
    [Fact]
    public void Session_ReportNumber_FollowsLatestError()
    {
        using var session = DiagnosticSession.Start(Options("7f3a9c2e"));
        Assert.Null(session.ReportNumber);

        var logger = DiagnosticLog.For("app");
        logger.LogInformation("info");
        logger.LogError(new InvalidOperationException("boom"), "first");
        session.Flush(TimeSpan.FromSeconds(5));
        var first = session.ReportNumber;
        Assert.NotNull(first);
        Assert.StartsWith("7F3A-", first, StringComparison.Ordinal);

        logger.LogWarning("warn");
        Assert.Equal(first, session.ReportNumber);
        logger.LogError(new InvalidOperationException("boom 2"), "second");
        session.Flush(TimeSpan.FromSeconds(5));
        Assert.NotEqual(first, session.ReportNumber);

        var lines = Jsonl.ReadApp(DiagnosticsDirectory).Where(line => line.GetProperty("lvl").GetString() == "error").ToList();
        Assert.Equal($"7F3A-{lines[^1].Seq()}", session.ReportNumber);
    }

    /// <summary>zip には直近 7 日の診断ログと summary.json だけが入る。古いログ・生ログ・SQLite・写真・appsettings.Local.json は入らない。</summary>
    [Fact]
    public void Write_IncludesRecentLogsAndSummaryOnly()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        var recent = WriteLog("app-20261110-aaaaaa.jsonl", Now.AddDays(-1), Line("2026-11-10T01:00:00.0000000Z", "info", "app.start", "aaaaaaaa", 1));
        var crash = WriteLog("crash-20261110-aaaaaa.jsonl", Now.AddDays(-1), Line("2026-11-10T02:00:00.0000000Z", "fatal", "app.crash.unhandled", "aaaaaaaa", 5));
        WriteLog("app-20261101-bbbbbb.jsonl", Now.AddDays(-10), Line("2026-11-01T01:00:00.0000000Z", "error", "old.error", "bbbbbbbb", 9));
        foreach (var forbidden in new[] { "appsettings.Local.json", "data.sqlite", "photo.jpg", "raw.bin", "notes.txt", "verbose-logging.json" })
        {
            File.WriteAllText(Path.Combine(DiagnosticsDirectory, forbidden), "secret");
        }

        var logs = Path.Combine(_data.Path, "logs", "session-1");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "raw.log"), "raw");
        File.WriteAllText(Path.Combine(_data.Path, "appsettings.Local.json"), "secret");

        var zipPath = Path.Combine(_data.Path, "report.zip");
        var result = DiagnosticReport.Write(DiagnosticsDirectory, zipPath, Info(), Now);

        Assert.Equal(2, result.LogFileCount);
        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ["diagnostics/app-20261110-aaaaaa.jsonl", "diagnostics/crash-20261110-aaaaaa.jsonl", "summary.json"],
            names);
        Assert.Equal(File.ReadAllText(recent), ReadEntry(archive, "diagnostics/app-20261110-aaaaaa.jsonl"));
        Assert.Equal(File.ReadAllText(crash), ReadEntry(archive, "diagnostics/crash-20261110-aaaaaa.jsonl"));
    }

    /// <summary>summary.json に環境と、直近の Error 10 件（新しい順、evt・ts・報告用番号）が入る。</summary>
    [Fact]
    public void Write_SummaryHasEnvironmentAndTenLatestErrors()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        var lines = new List<string> { Line("2026-11-11T00:00:00.0000000Z", "info", "app.start", "7f3a9c2e", 1) };
        for (var index = 0; index < 12; index++)
        {
            lines.Add(Line($"2026-11-11T00:{index + 1:00}:00.0000000Z", "error", $"obd.connect.failed{index}", "7f3a9c2e", 10 + index));
        }

        lines.Add(Line("2026-11-11T01:00:00.0000000Z", "warn", "acq.slow", "7f3a9c2e", 99));
        // リングバッファから吐き出した行と、書込み途中の行は Error として数えない。
        lines.Add(Line("2026-11-11T02:00:00.0000000Z", "error", "ring.copy", "7f3a9c2e", 100, ring: true));
        lines.Add("{\"ts\":\"2026-11-11T03:00:00.0000000Z\",\"lvl\":\"error\",\"evt\":\"half");
        WriteLog("app-20261111-7f3a9c.jsonl", Now, lines.ToArray());
        // クラッシュのファイルにある同じ Error は二重に数えない。
        WriteLog("crash-20261111-7f3a9c.jsonl", Now, Line("2026-11-11T00:12:00.0000000Z", "error", "obd.connect.failed11", "7f3a9c2e", 21));

        var zipPath = Path.Combine(_data.Path, "report.zip");
        var result = DiagnosticReport.Write(DiagnosticsDirectory, zipPath, Info(), Now);

        Assert.Equal(10, result.ErrorCount);
        using var archive = ZipFile.OpenRead(zipPath);
        using var summary = JsonDocument.Parse(ReadEntry(archive, "summary.json"));
        var root = summary.RootElement;
        Assert.Equal("1.2.3.4", root.GetProperty("appVersion").GetString());
        Assert.Equal("beta", root.GetProperty("channel").GetString());
        Assert.Equal("10.0.26200.0", root.GetProperty("os").GetString());
        Assert.Equal("X64/X64 x8", root.GetProperty("cpu").GetString());
        Assert.Equal("ja-JP", root.GetProperty("language").GetString());
        Assert.Equal("BluetoothLowEnergy", root.GetProperty("defaultAdapterKind").GetString());
        Assert.Equal(7, root.GetProperty("sessionCount").GetInt32());
        var errors = root.GetProperty("recentErrors").EnumerateArray().ToList();
        Assert.Equal(10, errors.Count);
        Assert.Equal("obd.connect.failed11", errors[0].GetProperty("evt").GetString());
        Assert.Equal("7F3A-21", errors[0].GetProperty("report").GetString());
        Assert.Equal("2026-11-11T00:12:00.0000000Z", errors[0].GetProperty("ts").GetString());
        Assert.Equal("obd.connect.failed2", errors[^1].GetProperty("evt").GetString());
        Assert.DoesNotContain(errors, error => error.GetProperty("evt").GetString() is "ring.copy" or "acq.slow");
    }

    /// <summary>未取得の項目は null、診断ログが 1 つもなくても zip は作れる。</summary>
    [Fact]
    public void Write_WithoutLogsStillWritesSummary()
    {
        var zipPath = Path.Combine(_data.Path, "report.zip");
        var result = DiagnosticReport.Write(
            DiagnosticsDirectory,
            zipPath,
            Info() with { DefaultAdapterKind = null, SessionCount = null },
            Now);

        Assert.Equal(0, result.LogFileCount);
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Equal(["summary.json"], archive.Entries.Select(entry => entry.FullName));
        using var summary = JsonDocument.Parse(ReadEntry(archive, "summary.json"));
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("defaultAdapterKind").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("sessionCount").ValueKind);
        Assert.Empty(summary.RootElement.GetProperty("recentErrors").EnumerateArray());
    }

    /// <summary>書込み中（開いたまま）のファイルも読めて、zip に入る。</summary>
    [Fact]
    public void Write_ReadsFilesThatAreOpenForWriting()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        var path = Path.Combine(DiagnosticsDirectory, "app-20261111-cccccc.jsonl");
        using var writing = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var line = Line("2026-11-11T00:00:00.0000000Z", "error", "store.sqlite.open.failed", "cccccccc", 4) + "\n";
        writing.Write(Encoding.UTF8.GetBytes(line));
        writing.Flush();

        var zipPath = Path.Combine(_data.Path, "report.zip");
        var result = DiagnosticReport.Write(DiagnosticsDirectory, zipPath, Info(), Now);

        Assert.Equal(1, result.LogFileCount);
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Equal(line, ReadEntry(archive, "diagnostics/app-20261111-cccccc.jsonl"));
    }

    /// <summary>今の実行から書き出すと、書き出す前に Flush されるので、直前の Error も zip に入る。</summary>
    [Fact]
    public void Write_FromSession_FlushesBeforeReading()
    {
        using var session = DiagnosticSession.Start(Options("7f3a9c2e"));
        DiagnosticLog.For("app").LogError(new InvalidOperationException("boom"), "just before export");

        var zipPath = Path.Combine(_data.Path, "report.zip");
        var result = DiagnosticReport.Write(session, zipPath, "Serial", 3);

        Assert.Equal(1, result.ErrorCount);
        using var archive = ZipFile.OpenRead(zipPath);
        using var summary = JsonDocument.Parse(ReadEntry(archive, "summary.json"));
        var error = summary.RootElement.GetProperty("recentErrors")[0];
        Assert.Equal(session.ReportNumber, error.GetProperty("report").GetString());
        Assert.Equal("Serial", summary.RootElement.GetProperty("defaultAdapterKind").GetString());
        Assert.Equal("1.2.3.4", summary.RootElement.GetProperty("appVersion").GetString());
    }

    /// <summary>保存ダイアログの既定のファイル名。</summary>
    [Fact]
    public void SuggestedFileName_HasDateAndRun()
    {
        Assert.Equal("RevTorqueInsight-report-20261111-7f3a.zip", DiagnosticReport.SuggestedFileName(Now, "7F3A9C2E"));
    }

    /// <summary>テスト用の診断ログの 1 行を作ります。</summary>
    /// <param name="ts">時刻。</param>
    /// <param name="level">レベル。</param>
    /// <param name="evt">イベント名。</param>
    /// <param name="run">実行 ID。</param>
    /// <param name="seq">通し番号。</param>
    /// <param name="ring">リングバッファから吐き出した行か。</param>
    /// <returns>1 行の JSON。</returns>
    private static string Line(string ts, string level, string evt, string run, long seq, bool ring = false) =>
        $"{{\"ts\":\"{ts}\",\"lvl\":\"{level}\",\"cat\":\"app\",\"evt\":\"{evt}\",\"run\":\"{run}\",\"seq\":{seq},\"msg\":\"テスト\"{(ring ? ",\"ring\":true" : string.Empty)}}}";

    /// <summary>診断ログのファイルを書き、最終書込みの時刻を決めます。</summary>
    /// <param name="name">ファイル名。</param>
    /// <param name="lastWrite">最終書込みの時刻。</param>
    /// <param name="lines">行。</param>
    /// <returns>ファイルのパス。</returns>
    private string WriteLog(string name, DateTimeOffset lastWrite, params string[] lines)
    {
        var path = Path.Combine(DiagnosticsDirectory, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
        return path;
    }

    /// <summary>zip の項目を文字列で読みます。</summary>
    /// <param name="archive">zip。</param>
    /// <param name="name">項目の名前。</param>
    /// <returns>中身。</returns>
    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>環境の情報です。</summary>
    /// <returns>情報。</returns>
    private static DiagnosticReportInfo Info() => new(
        new DiagnosticAppInfo("1.2.3.4", "10.0.26200.0", "X64/X64 x8", "ja-JP", IsLocalMode: true, IsPackaged: false),
        DiagnosticsChannel.Beta,
        "7f3a9c2e",
        VerboseLogging: false,
        DefaultAdapterKind: "BluetoothLowEnergy",
        SessionCount: 7);

    /// <summary>診断ログを始める設定です。</summary>
    /// <param name="runId">実行 ID。</param>
    /// <returns>設定。</returns>
    private DiagnosticSessionOptions Options(string runId) => new()
    {
        DataDirectory = _data.Path,
        Channel = DiagnosticsChannel.Stable,
        RunId = runId,
        App = new DiagnosticAppInfo("1.2.3.4", "10.0.26200.0", "X64/X64 x8", "ja-JP", IsLocalMode: true, IsPackaged: false),
    };
}
