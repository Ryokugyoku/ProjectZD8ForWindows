using System.Diagnostics.Tracing;

namespace ProjectZD8.Data.Diagnostics;

/// <summary>取得性能区間をETW（Windowsのイベントトレース）へ出すイベントソースです。</summary>
/// <remarks>
/// iOS版のsignpost log（カテゴリ <c>AcquisitionPerformance</c>）に相当します。
/// PerfView や Windows Performance Recorder でプロバイダー名 <c>ProjectZD8-AcquisitionPerformance</c> を有効にすると記録できます。
/// 個人・端末・車両の識別子は含めません。
/// </remarks>
[EventSource(Name = "ProjectZD8-AcquisitionPerformance")]
internal sealed class AcquisitionPerformanceEventSource : EventSource
{
    /// <summary>区間開始のイベントIDです。</summary>
    internal const int BeginEventId = 1;
    /// <summary>DB待機終了のイベントIDです。</summary>
    internal const int QueueEntryEventId = 2;
    /// <summary>区間終了のイベントIDです。</summary>
    internal const int EndEventId = 3;

    /// <summary>プロセスで1つのインスタンスです。</summary>
    internal static readonly AcquisitionPerformanceEventSource Log = new();

    /// <summary>外から生成させません。</summary>
    /// <remarks>責務: イベントソースを1つに限ります。</remarks>
    private AcquisitionPerformanceEventSource()
    {
    }

    /// <summary>区間の開始を記録します。</summary>
    /// <remarks>責務: 1件の取得操作の開始を匿名の位置情報付きで出します（iOS版の開始signpost）。</remarks>
    /// <param name="run">匿名run識別子。</param>
    /// <param name="intervalId">プロセス内の区間番号。</param>
    /// <param name="operation">操作名。</param>
    /// <param name="generation">取得世代（なしは -1）。</param>
    /// <param name="batch">batch番号（なしは -1）。</param>
    /// <param name="request">要求順（なしは -1）。</param>
    /// <param name="tick">policy tick（なしは -1）。</param>
    /// <param name="scheduleDelayNs">予定からの遅れ（なしは -1）。</param>
    [Event(BeginEventId, Level = EventLevel.Informational)]
    public void Begin(string run, long intervalId, string operation, long generation, long batch, long request, long tick, long scheduleDelayNs) =>
        WriteEvent(BeginEventId, run, intervalId, operation, generation, batch, request, tick, scheduleDelayNs);

    /// <summary>DB待機の終了を記録します。</summary>
    /// <remarks>責務: 1件の永続化区間で保存先の待機が終わった時点を出します（iOS版の <c>DatabaseQueueEntry</c>）。</remarks>
    /// <param name="run">匿名run識別子。</param>
    /// <param name="intervalId">プロセス内の区間番号。</param>
    /// <param name="operation">操作名。</param>
    /// <param name="generation">取得世代（なしは -1）。</param>
    /// <param name="batch">batch番号（なしは -1）。</param>
    /// <param name="request">要求順（なしは -1）。</param>
    /// <param name="tick">policy tick（なしは -1）。</param>
    /// <param name="scheduleDelayNs">予定からの遅れ（なしは -1）。</param>
    [Event(QueueEntryEventId, Level = EventLevel.Informational)]
    public void QueueEntry(string run, long intervalId, string operation, long generation, long batch, long request, long tick, long scheduleDelayNs) =>
        WriteEvent(QueueEntryEventId, run, intervalId, operation, generation, batch, request, tick, scheduleDelayNs);

    /// <summary>区間の終了を記録します。</summary>
    /// <remarks>責務: 1件の区間を結果付きで閉じます（iOS版の終了signpost）。</remarks>
    /// <param name="run">匿名run識別子。</param>
    /// <param name="intervalId">プロセス内の区間番号。</param>
    /// <param name="operation">操作名。</param>
    /// <param name="outcome">結果。</param>
    [Event(EndEventId, Level = EventLevel.Informational)]
    public void End(string run, long intervalId, string operation, string outcome) =>
        WriteEvent(EndEventId, run, intervalId, operation, outcome);
}
