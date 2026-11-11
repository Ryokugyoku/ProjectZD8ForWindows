using ProjectZD8.Diagnostics.Tests.Support;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>ファイル名・古いファイルの掃除・前回の異常終了の検出を確かめます。</summary>
public sealed class DiagnosticFileMaintenanceTests : IDisposable
{
    /// <summary>今の時刻です。</summary>
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>一時フォルダーです。</summary>
    private readonly TempDirectory _directory = new();

    /// <summary>一時フォルダーを消します。</summary>
    public void Dispose() => _directory.Dispose();

    /// <summary>ファイル名は文書の形で、読み戻せる。</summary>
    [Fact]
    public void FileName_FormatsAndParses()
    {
        var date = new DateOnly(2026, 11, 11);
        Assert.Equal("app-20261111-7f3a9c.jsonl", new DiagnosticFileName(DiagnosticFileKind.App, date, "7f3a9c", 0).ToString());
        Assert.Equal("app-20261111-7f3a9c.1.jsonl", new DiagnosticFileName(DiagnosticFileKind.App, date, "7f3a9c", 1).ToString());
        Assert.Equal("crash-20261111-7f3a9c.jsonl", new DiagnosticFileName(DiagnosticFileKind.Crash, date, "7f3a9c", 0).ToString());

        Assert.True(DiagnosticFileName.TryParse("app-20261111-7f3a9c.12.jsonl", out var name));
        Assert.Equal(new DiagnosticFileName(DiagnosticFileKind.App, date, "7f3a9c", 12), name);
        Assert.False(DiagnosticFileName.TryParse("raw.jsonl", out _));
        Assert.False(DiagnosticFileName.TryParse("app-2026111-7f3a9c.jsonl", out _));
    }

    /// <summary>日数を超えたファイルを消し、残りは新しい順に合計サイズの上限まで残す。今の実行と関係のないファイルは消さない。</summary>
    [Fact]
    public void Clean_DeletesByAgeThenBySize()
    {
        var expired = Write("app-20261001-aaaaaa.jsonl", 10, Now.AddDays(-31));
        var expiredCrash = Write("crash-20261001-aaaaaa.jsonl", 10, Now.AddDays(-31));
        var oldest = Write("app-20261105-bbbbbb.jsonl", 400, Now.AddDays(-6));
        var middle = Write("app-20261108-cccccc.jsonl", 400, Now.AddDays(-3));
        var newest = Write("app-20261110-dddddd.jsonl", 400, Now.AddDays(-1));
        var current = Write("app-20261111-eeeeee.jsonl", 5000, Now.AddDays(-40));
        var unrelated = Write("notes.txt", 10, Now.AddDays(-100));

        var deleted = DiagnosticFileMaintenance.Clean(_directory.Path, new DiagnosticRetention(1000, TimeSpan.FromDays(30)), Now, "eeeeee");

        Assert.Equal(3, deleted);
        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(expiredCrash));
        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(middle));
        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(unrelated));
    }

    /// <summary>前回の系列の最後のファイルに app.exit があれば正常終了。</summary>
    [Fact]
    public void DetectPreviousRun_FindsExitInLastRotatedFile()
    {
        Write("app-20261110-aaaaaa.jsonl", """{"evt":"app.start"}""" + "\n", Now.AddDays(-1).AddMinutes(-5));
        Write("app-20261110-aaaaaa.1.jsonl", """{"evt":"acq.x"}""" + "\n" + """{"ts":"…","lvl":"info","cat":"app","evt":"app.exit"}""" + "\n", Now.AddDays(-1));
        Write("app-20261101-ffffff.jsonl", """{"evt":"app.start"}""" + "\n", Now.AddDays(-10));

        var previous = DiagnosticFileMaintenance.DetectPreviousRun(_directory.Path, "eeeeee");

        Assert.NotNull(previous);
        Assert.Equal("aaaaaa", previous.RunPrefix);
        Assert.False(previous.Crashed);
    }

    /// <summary>前回の最後のファイルに app.exit がなければ異常終了。</summary>
    [Fact]
    public void DetectPreviousRun_ReportsCrashWithoutExit()
    {
        Write("app-20261110-aaaaaa.jsonl", """{"evt":"app.exit"}""" + "\n", Now.AddDays(-2));
        Write("app-20261111-bbbbbb.jsonl", """{"evt":"app.start"}""" + "\n", Now.AddHours(-1));

        var previous = DiagnosticFileMaintenance.DetectPreviousRun(_directory.Path, "eeeeee");

        Assert.NotNull(previous);
        Assert.Equal("bbbbbb", previous.RunPrefix);
        Assert.True(previous.Crashed);
    }

    /// <summary>前回がなければ null。今の実行のファイルは前回として扱わない。</summary>
    [Fact]
    public void DetectPreviousRun_ReturnsNullWithoutHistory()
    {
        Assert.Null(DiagnosticFileMaintenance.DetectPreviousRun(_directory.Path, "eeeeee"));
        Write("app-20261111-eeeeee.jsonl", "{}\n", Now);
        Assert.Null(DiagnosticFileMaintenance.DetectPreviousRun(_directory.Path, "eeeeee"));
        Assert.Null(DiagnosticFileMaintenance.DetectPreviousRun(Path.Combine(_directory.Path, "none"), "eeeeee"));
    }

    /// <summary>決まった大きさのファイルを書き、更新時刻を設定します。</summary>
    /// <param name="name">ファイル名。</param>
    /// <param name="size">大きさ。</param>
    /// <param name="lastWrite">更新時刻。</param>
    /// <returns>パス。</returns>
    private string Write(string name, int size, DateTime lastWrite) => Write(name, new string('x', size), lastWrite);

    /// <summary>中身を書き、更新時刻を設定します。</summary>
    /// <param name="name">ファイル名。</param>
    /// <param name="content">中身。</param>
    /// <param name="lastWrite">更新時刻。</param>
    /// <returns>パス。</returns>
    private string Write(string name, string content, DateTime lastWrite)
    {
        var path = Path.Combine(_directory.Path, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }
}
