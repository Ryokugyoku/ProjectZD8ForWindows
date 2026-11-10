using System.Diagnostics.Tracing;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Data.Diagnostics;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Diagnostics;

/// <summary>取得性能のETW adapterと取得runtime evidence adapterを検証します。</summary>
/// <remarks>iOS版 <c>OSSignpostAcquisitionPerformanceAdapterTests</c> の移植と、イベント出力・evidence adapterの追加テストです。</remarks>
public sealed class EventSourceAcquisitionPerformanceAdapterTests
{
    /// <summary>匿名ASCII識別子を受け付けることを確かめます。</summary>
    [Fact]
    public void FactoryAcceptsAnonymousASCIIIdentifier() =>
        Assert.NotNull(EventSourceAcquisitionPerformanceAdapter.MakeIfValid("phase4h_2026-07-29.run-01"));

    /// <summary>不正な識別子を拒否することを確かめます。</summary>
    [Fact]
    public void FactoryRejectsInvalidIdentifiers()
    {
        Assert.Null(EventSourceAcquisitionPerformanceAdapter.MakeIfValid(null));
        Assert.Null(EventSourceAcquisitionPerformanceAdapter.MakeIfValid(""));
        Assert.Null(EventSourceAcquisitionPerformanceAdapter.MakeIfValid("vehicle/id"));
        Assert.Null(EventSourceAcquisitionPerformanceAdapter.MakeIfValid("実車run"));
        Assert.Null(EventSourceAcquisitionPerformanceAdapter.MakeIfValid(new string('a', 65)));
    }

    /// <summary>何もしない計測境界が区間の流れを受け付けることを確かめます。</summary>
    [Fact]
    public void NoOpPortAcceptsCompleteIntervalLifecycle()
    {
        var port = new NoOpAcquisitionPerformanceEventPort();
        var interval = port.Begin(AcquisitionPerformanceOperation.BatchAcquisition, new AcquisitionPerformanceContext(generation: 1, batchOrdinal: 0));

        port.QueueDidEnter(interval);
        port.End(interval, AcquisitionPerformanceOutcome.Succeeded);

        Assert.Equal(0UL, interval.Identifier);
        Assert.Equal(AcquisitionPerformanceOperation.BatchAcquisition, interval.Operation);
    }

    /// <summary>区間の開始・待機終了・終了が同じ番号のイベントとして出ることを確かめます。</summary>
    [Fact]
    public void AdapterEmitsBeginQueueEntryAndEndEvents()
    {
        using var listener = new RecordingListener();
        var adapter = new EventSourceAcquisitionPerformanceAdapter("adapter-test");

        var interval = adapter.Begin(AcquisitionPerformanceOperation.RespondedPersistence, new AcquisitionPerformanceContext(batchOrdinal: 3, requestOrdinal: 1));
        adapter.QueueDidEnter(interval);
        adapter.End(interval, AcquisitionPerformanceOutcome.Failed);

        var events = listener.Events.Where(entry => entry.Payload?[0] as string == "adapter-test").ToList();
        Assert.Equal(["Begin", "QueueEntry", "End"], events.Select(entry => entry.EventName));
        Assert.All(events, entry => Assert.Equal((long)interval.Identifier, entry.Payload![1]));
        Assert.Equal("respondedPersistence", events[0].Payload![2]);
        Assert.Equal(3L, events[0].Payload![4]);
        Assert.Equal(-1L, events[0].Payload![3]);
        Assert.Equal("failed", events[2].Payload![3]);
    }

    /// <summary>evidence adapterがversionと注入値を返し、version欠落を拒否することを確かめます。</summary>
    [Fact]
    public async Task EvidenceAdapterReturnsVersionsAndRejectsMissingVersion()
    {
        var adapter = new AssemblyConnectionSessionAcquisitionEvidenceAdapter("1.2.3", "45", 7, ConnectionSessionAcquisitionPlatform.MacOS);
        var evidence = await adapter.Evidence();
        Assert.Equal(new AcquisitionApplicationVersion("1.2.3", "45"), evidence.ApplicationVersion);
        Assert.Equal(7, evidence.SchemaContractVersion);
        Assert.Equal(ConnectionSessionAcquisitionPlatform.MacOS, evidence.AcquisitionPlatform);

        var missing = new AssemblyConnectionSessionAcquisitionEvidenceAdapter(null, "45", 7, ConnectionSessionAcquisitionPlatform.MacOS);
        var error = await Assert.ThrowsAsync<AssemblyConnectionSessionAcquisitionEvidenceException>(() => missing.Evidence());
        Assert.Equal(AssemblyConnectionSessionAcquisitionEvidenceError.VersionUnavailable, error.Reason);

        var fromAssembly = await AssemblyConnectionSessionAcquisitionEvidenceAdapter
            .FromAssembly(typeof(AssemblyConnectionSessionAcquisitionEvidenceAdapter).Assembly, 1, ConnectionSessionAcquisitionPlatform.MacOS)
            .Evidence();
        Assert.False(string.IsNullOrEmpty(fromAssembly.ApplicationVersion.MarketingVersion));
        Assert.DoesNotContain('+', fromAssembly.ApplicationVersion.MarketingVersion);
    }

    /// <summary>取得性能のイベントを記録するリスナーです。</summary>
    private sealed class RecordingListener : EventListener
    {
        /// <summary>記録したイベントです。</summary>
        private readonly List<EventWrittenEventArgs> _events = [];

        /// <summary>記録したイベントの写しです。</summary>
        public IReadOnlyList<EventWrittenEventArgs> Events
        {
            get
            {
                lock (_events)
                {
                    return _events.ToList();
                }
            }
        }

        /// <summary>取得性能のイベントソースを有効にします。</summary>
        /// <remarks>責務: プロバイダー名が一致するイベントソースだけを購読します。</remarks>
        /// <param name="eventSource">作成されたイベントソース。</param>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "ProjectZD8-AcquisitionPerformance")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        /// <summary>イベントを記録します。</summary>
        /// <remarks>責務: 受け取ったイベントを一覧へ加えます。</remarks>
        /// <param name="eventData">イベント。</param>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            lock (_events)
            {
                _events.Add(eventData);
            }
        }
    }
}
