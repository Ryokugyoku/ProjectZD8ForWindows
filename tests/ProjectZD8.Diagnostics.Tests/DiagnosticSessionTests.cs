using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics.Tests.Support;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>起動と終了の記録、前回の異常終了の検出、クラッシュの記録を確かめます。</summary>
[Collection(GlobalDiagnosticLogCollection.Name)]
public sealed class DiagnosticSessionTests : IDisposable
{
    /// <summary>一時フォルダー（データのフォルダー）です。</summary>
    private readonly TempDirectory _data = new();

    /// <summary>診断ログのフォルダーです。</summary>
    private string DiagnosticsDirectory => Path.Combine(_data.Path, "diagnostics");

    /// <summary>一時フォルダーを消します。</summary>
    public void Dispose() => _data.Dispose();

    /// <summary>最初の行は app.start（環境の情報付き）、正常終了で app.exit を書く。</summary>
    [Fact]
    public void StartAndStop_WriteStartAndExit()
    {
        using (var session = Start(DiagnosticsChannel.Stable, "aaaaaa01"))
        {
            Assert.Same(session, DiagnosticLog.Session);
            Assert.False(session.PreviousRunCrashed);
            Assert.Null(session.PreviousRun);
            Assert.Equal(_data.Path, Redact.DataDirectory);
        }

        Assert.Null(DiagnosticLog.Session);
        Assert.Same(NullDiagnosticSink.Instance, DiagnosticLog.Sink);
        var lines = Jsonl.ReadApp(DiagnosticsDirectory);
        Assert.Equal([DiagnosticEventNames.AppStart, DiagnosticEventNames.AppExit], lines.Select(line => line.Evt()));
        var props = lines[0].GetProperty("props");
        Assert.Equal("1.2.3.4", props.GetProperty("version").GetString());
        Assert.Equal("stable", props.GetProperty("channel").GetString());
        Assert.Equal("10.0.26200.0", props.GetProperty("os").GetString());
        Assert.Equal("X64/X64 x8", props.GetProperty("cpu").GetString());
        Assert.Equal("ja-JP", props.GetProperty("lang").GetString());
        Assert.True(props.GetProperty("localMode").GetBoolean());
        Assert.False(props.GetProperty("packaged").GetBoolean());
        Assert.Equal("aaaaaa01", lines[0].GetProperty("run").GetString());
        Assert.Equal(1, lines[0].Seq());
    }

    /// <summary>前回に app.exit がなければ app.previous-run.crashed を書き、プロパティで知らせる。正常終了なら知らせない。</summary>
    [Fact]
    public void Start_DetectsPreviousRunWithoutExit()
    {
        // 1 回目：止めずに終わる（クラッシュの代わり）。出力先だけ戻す。
        var crashed = Start(DiagnosticsChannel.Beta, "aaaaaa01");
        crashed.AbandonWithoutExit();

        using (var second = Start(DiagnosticsChannel.Beta, "bbbbbb02"))
        {
            Assert.True(second.PreviousRunCrashed);
            Assert.Equal("aaaaaa", second.PreviousRun!.RunPrefix);
        }

        var secondLines = Jsonl.Read(Assert.Single(Directory.GetFiles(DiagnosticsDirectory, "app-*-bbbbbb.jsonl")));
        Assert.Equal(
            [DiagnosticEventNames.AppStart, DiagnosticEventNames.PreviousRunCrashed, DiagnosticEventNames.AppExit],
            secondLines.Select(line => line.Evt()));
        Assert.Equal("aaaaaa", secondLines[1].GetProperty("props").GetProperty("previousRun").GetString());

        using (var third = Start(DiagnosticsChannel.Beta, "cccccc03"))
        {
            Assert.False(third.PreviousRunCrashed);
            Assert.Equal("bbbbbb", third.PreviousRun!.RunPrefix);
        }
    }

    /// <summary>クラッシュの報告は crash-*.jsonl に直前の出来事と例外を書き、同じ例外は二重に書かない。</summary>
    [Fact]
    public void ReportCrash_WritesCrashFileWithRecentEvents()
    {
        using var session = Start(DiagnosticsChannel.Stable, "dddddd04");
        var logger = DiagnosticLog.For(DiagnosticCategories.Acquisition);
        SampleLog.BatchSaved(logger, 12, 3.5);
        var exception = new InvalidOperationException("落ちる");

        DiagnosticLog.ReportCrash(exception, DiagnosticCrashSource.AppDomain);
        DiagnosticLog.ReportCrash(exception, DiagnosticCrashSource.Xaml);

        var crashFile = Assert.Single(Directory.GetFiles(DiagnosticsDirectory, "crash-*-dddddd.jsonl"));
        var lines = Jsonl.Read(crashFile);
        Assert.Equal([DiagnosticEventNames.AppStart, "acq.batch.saved", DiagnosticEventNames.CrashUnhandled], lines.Select(line => line.Evt()));
        var crash = lines[^1];
        Assert.Equal("fatal", crash.GetProperty("lvl").GetString());
        Assert.Equal("appdomain", crash.GetProperty("props").GetProperty("source").GetString());
        Assert.Equal("System.InvalidOperationException", crash.GetProperty("ex").GetProperty("type").GetString());
    }

    /// <summary>待たれなかったタスクの例外は Error として記録する（プロセスは落ちない）。</summary>
    [Fact]
    public void ReportCrash_UnobservedTaskIsError()
    {
        using var session = Start(DiagnosticsChannel.Dev, "eeeeee05");

        DiagnosticLog.ReportCrash(new AggregateException(new TimeoutException("待たれなかった")), DiagnosticCrashSource.UnobservedTask);

        var crash = Jsonl.Read(Assert.Single(Directory.GetFiles(DiagnosticsDirectory, "crash-*-eeeeee.jsonl")))[^1];
        Assert.Equal(DiagnosticEventNames.UnobservedTask, crash.Evt());
        Assert.Equal("error", crash.GetProperty("lvl").GetString());
        Assert.Equal("System.TimeoutException", crash.GetProperty("ex").GetProperty("inner").GetProperty("type").GetString());
    }

    /// <summary>「詳細ログ」スイッチで Stable が Beta と同じ量になる。環境変数の指定があればそちらが勝つ。</summary>
    [Fact]
    public void SetVerboseLogging_SwitchesPolicyUnlessOverridden()
    {
        using (var session = Start(DiagnosticsChannel.Stable, "ffffff06"))
        {
            Assert.True(session.Sink.Policy.HasBuffer);
            Assert.False(DiagnosticLog.Sink.IsEnabled("acq", LogLevel.Trace));
            session.SetVerboseLogging(true);
            Assert.Equal(DiagnosticLevelSource.VerboseSwitch, session.LevelSource);
            Assert.False(session.Sink.Policy.HasBuffer);
            Assert.True(DiagnosticLog.Sink.IsEnabled("obd.wire", LogLevel.Trace));
            session.SetVerboseLogging(false);
            Assert.Equal(DiagnosticLevelSource.Channel, session.LevelSource);
            Assert.True(session.Sink.Policy.HasBuffer);
        }

        using (var overridden = Start(DiagnosticsChannel.Stable, "ffffff07", environmentLevel: "error"))
        {
            overridden.SetVerboseLogging(true);
            Assert.Equal(DiagnosticLevelSource.Environment, overridden.LevelSource);
            Assert.Equal(LogLevel.Error, overridden.Sink.Policy.FileLevel);
        }
    }

    /// <summary>診断ログを始めます。</summary>
    /// <param name="channel">チャンネル。</param>
    /// <param name="runId">実行 ID。</param>
    /// <param name="environmentLevel">環境変数の値。</param>
    /// <returns>診断ログ。</returns>
    private DiagnosticSession Start(DiagnosticsChannel channel, string runId, string? environmentLevel = null) =>
        DiagnosticSession.Start(new DiagnosticSessionOptions
        {
            DataDirectory = _data.Path,
            Channel = channel,
            RunId = runId,
            EnvironmentLevel = environmentLevel,
            App = new DiagnosticAppInfo("1.2.3.4", "10.0.26200.0", "X64/X64 x8", "ja-JP", IsLocalMode: true, IsPackaged: false),
        });
}
