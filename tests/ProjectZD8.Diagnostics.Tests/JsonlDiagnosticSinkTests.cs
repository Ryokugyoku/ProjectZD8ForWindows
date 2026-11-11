using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics.Tests.Support;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>JSONL の出力先（絞り込み・リングバッファ・ローテーション・あふれ・同時書込み・クラッシュ）を確かめます。</summary>
public sealed class JsonlDiagnosticSinkTests : IDisposable
{
    /// <summary>待つ上限です。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>一時フォルダーです。</summary>
    private readonly TempDirectory _directory = new();

    /// <summary>一時フォルダーを消します。</summary>
    public void Dispose() => _directory.Dispose();

    /// <summary>決まりに合うイベントだけを書き、通し番号を 1 から付ける。</summary>
    [Fact]
    public void Emit_WritesOnlyEventsAllowedByPolicy()
    {
        using (var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Beta)))
        {
            sink.Emit(Events.Make(LogLevel.Trace, "acq"));
            sink.Emit(Events.Make(LogLevel.Debug, "acq"));
            sink.Emit(Events.Make(LogLevel.Trace, "obd.wire"));
            Assert.True(sink.Flush(Timeout));
        }

        var lines = Jsonl.ReadApp(_directory.Path);
        Assert.Equal(["acq.test.debug", "obd.wire.test.trace"], lines.Select(line => line.Evt()));
        Assert.Equal([1L, 2L], lines.Select(line => line.Seq()));
        Assert.All(lines, line => Assert.Equal("7f3a9c2e", line.GetProperty("run").GetString()));
        Assert.Single(Directory.GetFiles(_directory.Path, "app-20261111-7f3a9c.jsonl"));
    }

    /// <summary>Stable では Error が来たら、まだ書いていないリングバッファの分を番号の順にその前へ書く。同じ行は二重に書かない。</summary>
    [Fact]
    public void Stable_DumpsBufferOnErrorInOrderWithoutDuplicates()
    {
        using (var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable)))
        {
            sink.Emit(Events.Make(LogLevel.Debug, "acq", "acq.a.debug"));         // 1 バッファだけ
            sink.Emit(Events.Make(LogLevel.Trace, "acq", "acq.b.trace"));         // 捨てる
            sink.Emit(Events.Make(LogLevel.Trace, "obd.wire", "obd.wire.c.sent")); // 2 バッファだけ
            sink.Emit(Events.Make(LogLevel.Warning, "acq", "acq.d.warn"));         // 3 書く
            sink.Emit(Events.Make(LogLevel.Information, "acq", "acq.e.info"));     // 4 バッファだけ
            sink.Emit(Events.Make(LogLevel.Error, "acq", "acq.f.error"));          // 5 書く（1・2・4 を吐き出す）
            sink.Emit(Events.Make(LogLevel.Debug, "acq", "acq.g.debug"));          // 6 バッファだけ
            sink.Emit(Events.Make(LogLevel.Error, "acq", "acq.h.error"));          // 7 書く（6 だけ吐き出す）
            Assert.True(sink.Flush(Timeout));
        }

        var lines = Jsonl.ReadApp(_directory.Path);
        Assert.Equal(
            ["acq.d.warn", "acq.a.debug", "obd.wire.c.sent", "acq.e.info", "acq.f.error", "acq.g.debug", "acq.h.error"],
            lines.Select(line => line.Evt()));
        Assert.Equal([3L, 1L, 2L, 4L, 5L, 6L, 7L], lines.Select(line => line.Seq()));
        Assert.Equal([false, true, true, true, false, true, false], lines.Select(line => line.IsRing()));
        Assert.Equal(lines.Count, lines.Select(line => line.Seq()).Distinct().Count());
    }

    /// <summary>上限を超えたら連番の次のファイルへ移る。行はファイルをまたいで切れない。</summary>
    [Fact]
    public void Rotation_MovesToNextFileAtLimit()
    {
        using (var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev), maxFileBytes: 1024))
        {
            for (var index = 0; index < 40; index++)
            {
                sink.Emit(Events.Make(LogLevel.Information));
            }

            Assert.True(sink.Flush(Timeout));
        }

        var files = Directory.GetFiles(_directory.Path, "app-*.jsonl").Select(Path.GetFileName).ToList();
        Assert.Contains("app-20261111-7f3a9c.jsonl", files);
        Assert.Contains("app-20261111-7f3a9c.1.jsonl", files);
        Assert.All(Directory.GetFiles(_directory.Path, "app-*.jsonl"), path => Assert.True(new FileInfo(path).Length <= 1024));
        var lines = Jsonl.ReadApp(_directory.Path);
        Assert.Equal(Enumerable.Range(1, 40).Select(index => (long)index), lines.Select(line => line.Seq()).Order());
    }

    /// <summary>キューがあふれたら Trace・Debug から捨て、捨てた件数を diag.dropped に書く。</summary>
    [Fact]
    public void Overflow_DropsLowLevelsFirstAndRecordsCount()
    {
        using (var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev), queueCapacity: 8))
        {
            sink.PauseWriter();
            for (var index = 0; index < 10; index++)
            {
                sink.Emit(Events.Make(LogLevel.Debug));
            }

            for (var index = 0; index < 5; index++)
            {
                sink.Emit(Events.Make(LogLevel.Warning));
            }

            Assert.Equal(7, sink.DroppedCount);
            sink.ResumeWriter();
            Assert.True(sink.Flush(Timeout));
        }

        var lines = Jsonl.ReadApp(_directory.Path);
        Assert.Equal(6, lines.Count(line => line.Evt() == "acq.test.debug"));
        Assert.Equal(2, lines.Count(line => line.Evt() == "acq.test.warning"));
        var dropped = Assert.Single(lines, line => line.Evt() == DiagnosticEventNames.Dropped);
        Assert.Equal("warn", dropped.GetProperty("lvl").GetString());
        Assert.Equal(4, dropped.GetProperty("props").GetProperty("low").GetInt64());
        Assert.Equal(3, dropped.GetProperty("props").GetProperty("high").GetInt64());
    }

    /// <summary>複数のスレッドから同時に書いても、行は壊れず、番号は重ならない。</summary>
    [Fact]
    public void ConcurrentWriters_ProduceIntactLines()
    {
        const int Threads = 8;
        const int PerThread = 2000;
        using (var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev), maxFileBytes: 256 * 1024, queueCapacity: Threads * PerThread))
        {
            var start = new Barrier(Threads);
            var workers = Enumerable.Range(0, Threads).Select(thread => new Thread(() =>
            {
                start.SignalAndWait();
                for (var index = 0; index < PerThread; index++)
                {
                    sink.Emit(new DiagnosticEvent(
                        LogLevel.Information,
                        "acq",
                        "acq.concurrent.written",
                        $"スレッド {thread} の {index} 件目",
                        [new("thread", thread), new("index", index)]));
                }
            })).ToList();
            workers.ForEach(worker => worker.Start());
            workers.ForEach(worker => worker.Join());
            Assert.True(sink.Flush(Timeout));
            Assert.Equal(0, sink.DroppedCount);
        }

        var lines = Jsonl.ReadApp(_directory.Path);
        Assert.Equal(Threads * PerThread, lines.Count);
        Assert.Equal(Threads * PerThread, lines.Select(line => line.Seq()).Distinct().Count());
        foreach (var group in lines.GroupBy(line => line.GetProperty("props").GetProperty("thread").GetInt32()))
        {
            // 同じスレッドの行は、書いた順に番号が増える。
            var indexes = group.OrderBy(line => line.Seq()).Select(line => line.GetProperty("props").GetProperty("index").GetInt32());
            Assert.Equal(Enumerable.Range(0, PerThread), indexes);
        }
    }

    /// <summary>クラッシュはリングバッファの中身と例外を crash-*.jsonl に同期で書き、通常のログにも書く。</summary>
    [Fact]
    public void WriteCrash_WritesBufferAndExceptionSynchronously()
    {
        using var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable));
        sink.Emit(Events.Make(LogLevel.Debug, "acq", "acq.before.debug"));
        sink.Emit(Events.Make(LogLevel.Warning, "acq", "acq.before.warn"));
        var crash = new DiagnosticEvent(
            LogLevel.Critical,
            "app",
            DiagnosticEventNames.CrashUnhandled,
            "落ちます",
            exception: DiagnosticExceptionInfo.From(new InvalidOperationException("壊れた")));

        sink.WriteCrash(crash);

        // 戻った時点でクラッシュのファイルは書き終わっている。
        var crashLines = Jsonl.Read(Path.Combine(_directory.Path, "crash-20261111-7f3a9c.jsonl"));
        Assert.Equal(["acq.before.debug", "acq.before.warn", DiagnosticEventNames.CrashUnhandled], crashLines.Select(line => line.Evt()));
        Assert.Equal("fatal", crashLines[^1].GetProperty("lvl").GetString());
        Assert.Equal("壊れた", crashLines[^1].GetProperty("ex").GetProperty("msg").GetString());

        var appLines = Jsonl.ReadApp(_directory.Path);
        Assert.Equal(["acq.before.warn", "acq.before.debug", DiagnosticEventNames.CrashUnhandled], appLines.Select(line => line.Evt()));
    }

    /// <summary>決まりを入れ替えると、その場から絞り込みが変わる。</summary>
    [Fact]
    public void UpdatePolicy_ChangesFilteringImmediately()
    {
        using (var sink = MakeSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable)))
        {
            Assert.False(sink.IsEnabled("acq", LogLevel.Trace));
            sink.Emit(Events.Make(LogLevel.Information, "acq", "acq.before.info"));
            sink.UpdatePolicy(DiagnosticLevelPolicy.Verbose());
            Assert.True(sink.IsEnabled("obd.wire", LogLevel.Trace));
            sink.Emit(Events.Make(LogLevel.Information, "acq", "acq.after.info"));
            Assert.True(sink.Flush(Timeout));
        }

        Assert.Equal(["acq.after.info"], Jsonl.ReadApp(_directory.Path).Select(line => line.Evt()));
    }

    /// <summary>出力先を作ります。</summary>
    /// <param name="policy">決まり。</param>
    /// <param name="maxFileBytes">1 ファイルの上限。</param>
    /// <param name="queueCapacity">キューの上限。</param>
    /// <returns>出力先。</returns>
    private JsonlDiagnosticSink MakeSink(DiagnosticLevelPolicy policy, long maxFileBytes = 1024 * 1024, int queueCapacity = 1024) =>
        new(new JsonlDiagnosticSinkOptions
        {
            Directory = _directory.Path,
            RunId = "7f3a9c2e",
            Policy = policy,
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero)),
            MaxFileBytes = maxFileBytes,
            QueueCapacity = queueCapacity,
        });
}
