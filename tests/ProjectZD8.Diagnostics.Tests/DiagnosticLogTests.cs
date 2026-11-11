using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics.Tests.Support;
using Xunit.Abstractions;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>静的な入口（ロガーの取得、出力先の差し替え、sid、[LoggerMessage]、性能）を確かめます。</summary>
[Collection(GlobalDiagnosticLogCollection.Name)]
public sealed class DiagnosticLogTests(ITestOutputHelper output)
{
    /// <summary>既定の出力先は何もしない。</summary>
    [Fact]
    public void Default_IsNoOp()
    {
        Assert.Same(NullDiagnosticSink.Instance, DiagnosticLog.Sink);
        var logger = DiagnosticLog.For("acq");
        Assert.False(logger.IsEnabled(LogLevel.Critical));
        SampleLog.BatchSaved(logger, 1, 1);
    }

    /// <summary>差し込んだ出力先に書き、Dispose で元に戻す。差し込む前に取ったロガーも新しい出力先へ書く。</summary>
    [Fact]
    public void Install_RoutesExistingLoggersAndRestores()
    {
        var logger = DiagnosticLog.For(DiagnosticCategories.Acquisition);
        var sink = new RecordingSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev));
        using (DiagnosticLog.Install(sink))
        {
            SampleLog.BatchSaved(logger, 12, 3.5);
        }

        SampleLog.BatchSaved(logger, 99, 0);
        var recorded = Assert.Single(sink.Events);
        Assert.Equal("acq", recorded.Category);
        Assert.Equal("acq.batch.saved", recorded.EventName);
        Assert.Equal(LogLevel.Debug, recorded.Level);
        Assert.Equal("バッチを保存しました（12 件、3.5 ms）", recorded.Message);
        Assert.Equal(
            [new KeyValuePair<string, object?>("Count", 12), new KeyValuePair<string, object?>("ElapsedMs", 3.5)],
            recorded.Properties);
        Assert.Same(NullDiagnosticSink.Instance, DiagnosticLog.Sink);
    }

    /// <summary>[LoggerMessage] の Error は例外の中身を持つ。</summary>
    [Fact]
    public void LoggerMessage_ErrorCarriesException()
    {
        var sink = new RecordingSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable));
        using (DiagnosticLog.Install(sink))
        {
            SampleLog.ConnectFailed(DiagnosticLog.For<CategorizedType>(), new TimeoutException("応答なし"), "ble");
        }

        var recorded = Assert.Single(sink.Events);
        Assert.Equal("obd.connect", recorded.Category);
        Assert.Equal("obd.connect.failed", recorded.EventName);
        Assert.Equal("System.TimeoutException", recorded.Exception!.Type);
        Assert.Equal("応答なし", recorded.Exception.Message);
    }

    /// <summary>For&lt;T&gt; は属性の分類、なければ型の完全名を使い、同じ分類なら同じロガーを返す。</summary>
    [Fact]
    public void For_UsesCategoryAttributeOrTypeName()
    {
        var sink = new RecordingSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev));
        using (DiagnosticLog.Install(sink))
        {
            SampleLog.BatchSaved(DiagnosticLog.For<CategorizedType>(), 1, 1);
            SampleLog.BatchSaved(DiagnosticLog.For<UncategorizedType>(), 1, 1);
        }

        Assert.Equal(["obd.connect", typeof(UncategorizedType).FullName], sink.Events.Select(e => e.Category));
        Assert.Same(DiagnosticLog.For("obd.connect"), DiagnosticLog.For<CategorizedType>());
    }

    /// <summary>接続セッション ID は async の続きにも付き、Dispose で戻る。BeginScope の sid でも付けられる。</summary>
    [Fact]
    public async Task ConnectionSession_FlowsAcrossAwait()
    {
        var sink = new RecordingSink(DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev));
        var logger = DiagnosticLog.For("acq");
        using (DiagnosticLog.Install(sink))
        {
            using (DiagnosticLog.BeginConnectionSession("SESSION-1"))
            {
                await Task.Yield();
                await Task.Run(() => SampleLog.BatchSaved(logger, 1, 1));
            }

            SampleLog.BatchSaved(logger, 2, 2);
            using (logger.BeginScope(new Dictionary<string, object?> { ["sid"] = "SESSION-2" }))
            {
                SampleLog.BatchSaved(logger, 3, 3);
            }
        }

        Assert.Equal(["SESSION-1", null, "SESSION-2"], sink.Events.Select(e => e.SessionId));
        Assert.Null(DiagnosticLog.CurrentSessionId);
    }

    /// <summary>無効なレベルの呼び出しは何も割り当てない（Stable の Trace、何もしない出力先）。</summary>
    [Fact]
    public void DisabledLevel_DoesNotAllocate()
    {
        using var directory = new TempDirectory();
        using var sink = new JsonlDiagnosticSink(new JsonlDiagnosticSinkOptions
        {
            Directory = directory.Path,
            RunId = "abcdef01",
            Policy = DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable),
        });
        var acquisition = DiagnosticLog.For("acq");
        var categorized = DiagnosticLog.For<CategorizedType>();
        long stableBytes;
        long nullBytes;
        using (DiagnosticLog.Install(sink))
        {
            Exercise(acquisition, categorized);
            stableBytes = AllocatedBy(() => Exercise(acquisition, categorized));
        }

        Exercise(acquisition, categorized);
        nullBytes = AllocatedBy(() => Exercise(acquisition, categorized));

        output.WriteLine($"無効なレベルの呼び出し 30,000 回の割り当て: Stable {stableBytes} バイト、既定（何もしない）{nullBytes} バイト");
        Assert.Equal(0, stableBytes);
        Assert.Equal(0, nullBytes);

        static void Exercise(ILogger acquisition, ILogger categorized)
        {
            for (var index = 0; index < 10_000; index++)
            {
                SampleLog.WireSent(acquisition, "010C", index);
                acquisition.IsEnabled(LogLevel.Trace);
                SampleLog.WireSent(categorized, "010C", index);
            }
        }
    }

    /// <summary>有効な 1 行の書込みで、呼び出し側が待たされる時間を測ります（結果はテストの出力に出す）。</summary>
    [Fact]
    public void EnabledWrite_CallerCostIsSmall()
    {
        const int Count = 50_000;
        using var directory = new TempDirectory();
        using var sink = new JsonlDiagnosticSink(new JsonlDiagnosticSinkOptions
        {
            Directory = directory.Path,
            RunId = "abcdef02",
            Policy = DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev),
            QueueCapacity = Count * 2,
        });
        var logger = DiagnosticLog.For("acq");
        using (DiagnosticLog.Install(sink))
        {
            for (var index = 0; index < 1_000; index++)
            {
                SampleLog.BatchSaved(logger, index, 1.25);
            }

            Assert.True(sink.Flush(TimeSpan.FromSeconds(10)));
            var samples = new long[Count];
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (var index = 0; index < Count; index++)
            {
                var start = Stopwatch.GetTimestamp();
                SampleLog.BatchSaved(logger, index, 1.25);
                samples[index] = Stopwatch.GetTimestamp() - start;
            }

            total.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var flushWatch = Stopwatch.StartNew();
            Assert.True(sink.Flush(TimeSpan.FromSeconds(30)));
            flushWatch.Stop();
            Assert.Equal(0, sink.DroppedCount);

            Array.Sort(samples);
            double Microseconds(long ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;
            var mean = total.Elapsed.TotalMicroseconds / Count;
            output.WriteLine(
                $"有効な 1 行（Debug、props 2 つ）の呼び出し側の時間: 平均 {mean:F2} µs、"
                + $"中央値 {Microseconds(samples[Count / 2]):F2} µs、99% {Microseconds(samples[Count * 99 / 100]):F2} µs、"
                + $"最大 {Microseconds(samples[^1]):F1} µs、1 行あたりの割り当て {allocated / (double)Count:F0} バイト、"
                + $"{Count:N0} 行を書き終えるまでの残り {flushWatch.Elapsed.TotalMilliseconds:F0} ms");
            Assert.True(mean < 50, $"呼び出し側の平均が {mean:F2} µs で、目安の 50 µs を超えました");
        }

        Assert.Equal(Count + 1_000, Jsonl.ReadApp(directory.Path).Count);
    }

    /// <summary>処理の間にこのスレッドで割り当てたバイト数を返します。</summary>
    /// <param name="action">処理。</param>
    /// <returns>バイト数。</returns>
    private static long AllocatedBy(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
