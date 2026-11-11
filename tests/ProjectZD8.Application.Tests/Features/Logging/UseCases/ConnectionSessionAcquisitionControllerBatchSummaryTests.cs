using System.Globalization;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Application.Logging;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Application.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Logging.UseCases;

/// <summary>
/// 正常なバッチのまとめ（<c>acq.batch.summary</c>）と、取得の開始・停止の診断ログ（<c>sid</c>・起動時の停止）を検証します。
/// </summary>
/// <remarks>出力先を差し替えるため、このクラスのテストはほかと並行させません（もう一方の部分クラスの Collection が効く）。</remarks>
public sealed partial class ConnectionSessionAcquisitionControllerIntegrationTests
{
    /// <summary>正常なバッチ 25 個が、最初の 1 行と 10 件ごとのまとめ、停止時の残りに分かれることを確かめます。</summary>
    /// <remarks>責務: 行の数・順序・合計の値・<c>sid</c>・停止時の残りの書き出しを確認します。</remarks>
    [Fact]
    public async Task NormalBatchesAreSummarizedEveryIntervalAndFlushedOnCancel()
    {
        Assert.Equal(10, AcquisitionLog.NormalBatchSummaryInterval);
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: Enumerable.Repeat<OBDPIDRequestTransportOutcome>(
            new OBDPIDRequestTransportOutcome.Responded([42]), 25).ToArray());
        var expectedSessionId = fixture.Session.Id.RawValue.ToString("D", CultureInfo.InvariantCulture);
        await fixture.Controller.Start(fixture.StartInput);

        for (var ordinal = 0; ordinal < 25; ordinal++)
        {
            _ = await fixture.Controller.Acquire(fixture.BatchInput(ordinal, tick: (ulong)ordinal));
        }

        Assert.Equal(
            ["acq.batch.completed", "acq.batch.summary", "acq.batch.summary"],
            BatchEventNames(capture));
        await fixture.Controller.Cancel(1);

        var batchEvents = capture.Events.Where(item => item.EventName.StartsWith("acq.batch.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            ["acq.batch.completed", "acq.batch.summary", "acq.batch.summary", "acq.batch.summary"],
            batchEvents.Select(item => item.EventName));
        Assert.All(batchEvents, item => Assert.Equal(expectedSessionId, item.SessionId));
        Assert.Equal(0L, batchEvents[0].Prop("Batch"));

        var first = batchEvents[1];
        Assert.Equal(10, first.Prop("Batches"));
        Assert.Equal(1L, first.Prop("FirstBatch"));
        Assert.Equal(10L, first.Prop("LastBatch"));
        Assert.Equal(10L, first.Prop("Requests"));
        Assert.Equal(10L, first.Prop("Responded"));
        Assert.Equal(10L, first.Prop("Samples"));
        Assert.True((long)first.Prop("ElapsedTotalMs")! >= (long)first.Prop("ElapsedMaxMs")!);
        Assert.Equal(11L, batchEvents[2].Prop("FirstBatch"));
        Assert.Equal(20L, batchEvents[2].Prop("LastBatch"));

        var rest = batchEvents[3];
        Assert.Equal(4, rest.Prop("Batches"));
        Assert.Equal(21L, rest.Prop("FirstBatch"));
        Assert.Equal(24L, rest.Prop("LastBatch"));
        Assert.Equal(4L, rest.Prop("Requests"));

        // 合計は 1 バッチ 1 行のときと同じ（書かれた要求・応答・数値の全体は 25 件）。
        var totalRequests = batchEvents.Sum(item => item.EventName == "acq.batch.completed"
            ? (int)item.Prop("Requests")!
            : (long)item.Prop("Requests")!);
        Assert.Equal(25L, totalRequests);
    }

    /// <summary>異常なバッチは、その前にたまった正常分のまとめを先に書いてから、その場で 1 行書かれることを確かめます。</summary>
    /// <remarks>責務: まとめ → 異常な 1 行 → 停止時の残り、という順序と、異常の件数を確認します。</remarks>
    [Fact]
    public async Task AbnormalBatchFlushesPendingSummaryBeforeItsOwnLine()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes:
        [
            new OBDPIDRequestTransportOutcome.Responded([42]),
            new OBDPIDRequestTransportOutcome.Responded([42]),
            new OBDPIDRequestTransportOutcome.Responded([42]),
            new OBDPIDRequestTransportOutcome.TimedOut(),
            new OBDPIDRequestTransportOutcome.Responded([42]),
        ]);
        await fixture.Controller.Start(fixture.StartInput);

        for (var ordinal = 0; ordinal < 5; ordinal++)
        {
            _ = await fixture.Controller.Acquire(fixture.BatchInput(ordinal, tick: (ulong)ordinal));
        }

        await fixture.Controller.Cancel(1);

        var batchEvents = capture.Events.Where(item => item.EventName.StartsWith("acq.batch.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            ["acq.batch.completed", "acq.batch.summary", "acq.batch.completed", "acq.batch.summary"],
            batchEvents.Select(item => item.EventName));
        Assert.Equal(0L, batchEvents[0].Prop("Batch"));
        Assert.Equal(1L, batchEvents[1].Prop("FirstBatch"));
        Assert.Equal(2L, batchEvents[1].Prop("LastBatch"));
        Assert.Equal(3L, batchEvents[2].Prop("Batch"));
        Assert.Equal(1, batchEvents[2].Prop("TimedOut"));
        Assert.Equal(4L, batchEvents[3].Prop("FirstBatch"));
        Assert.Equal(1, batchEvents[3].Prop("Batches"));
    }

    /// <summary>取り消した後に始まった次の世代は、最初のバッチが必ず 1 行書かれることを確かめます。</summary>
    /// <remarks>責務: 世代ごとに「最初のバッチ」が数え直されることを確認します。</remarks>
    [Fact]
    public async Task FirstBatchOfEachGenerationIsAlwaysLogged()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: Enumerable.Repeat<OBDPIDRequestTransportOutcome>(
            new OBDPIDRequestTransportOutcome.Responded([42]), 2).ToArray());
        await fixture.Controller.Start(fixture.StartInput);
        _ = await fixture.Controller.Acquire(fixture.BatchInput(0, tick: 0));
        await fixture.Controller.Cancel(1);

        // 同じ保存先に新しい世代の証拠は作れない（manifest は 1 セッション 1 件）ため、Activate 相当の確認は
        // 1 つ目の世代で「最初の 1 行が出て、2 つ目は出ない」ことで代える。
        Assert.Single(capture.Named("acq.batch.completed"));
        Assert.Empty(capture.Named("acq.batch.summary"));
    }

    /// <summary>接続セッションのある取得の <c>acq.started</c> / <c>acq.stopped</c> に <c>sid</c> が付くことを確かめます。</summary>
    /// <remarks>責務: 実物の取得証拠 controller をつないだ model で、開始と利用者の停止の <c>sid</c> と、停止前のまとめの順序を確認します。</remarks>
    [Fact]
    public async Task StartedAndStoppedCarrySessionId()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: Enumerable.Repeat<OBDPIDRequestTransportOutcome>(
            new OBDPIDRequestTransportOutcome.Responded([42]), 50).ToArray());
        var expectedSessionId = fixture.Session.Id.RawValue.ToString("D", CultureInfo.InvariantCulture);
        var vehicleID = new VehicleID(Guid.Parse("10000000-0000-0000-0000-000000000009"));
        var definitionRepository = new SingleDefinitionRepository(fixture.Definition);
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(definitionRepository, fixture.Telemetry),
            loadVehicleCapabilities: new LoadVehiclePIDCapabilitiesUseCase(
                repository: new SingleCapabilityRepository(vehicleID, fixture.Definition),
                telemetry: fixture.Telemetry,
                definitionRepository: definitionRepository),
            acquisitionEvidence: fixture.Controller,
            now: () => DateTimeOffset.FromUnixTimeSeconds(10));
        var endpoint = fixture.BatchInput(0, 0).Endpoint;

        model.Send(new LiveTelemetryAction.StartRequested(endpoint, vehicleID, null));
        await AsyncWait.UntilAsync(() => capture.Named("acq.batch.completed").Count >= 1);
        model.Send(new LiveTelemetryAction.StopRequested());
        await AsyncWait.UntilAsync(() => capture.Named("acq.stopped").Count >= 1);

        var started = Assert.Single(capture.Named("acq.started"));
        Assert.Equal(expectedSessionId, started.SessionId);
        Assert.Equal(true, started.Prop("Persisted"));
        var stopped = Assert.Single(capture.Named("acq.stopped"));
        Assert.Equal(expectedSessionId, stopped.SessionId);
        Assert.Equal("user", stopped.Prop("Reason"));
        var names = capture.EventNames.ToList();
        Assert.True(names.IndexOf("acq.started") < names.IndexOf("acq.batch.completed"));
        Assert.True(names.IndexOf("acq.batch.completed") < names.IndexOf("acq.stopped"));
    }

    /// <summary>接続セッションがない取得の <c>acq.started</c> には <c>sid</c> を付けないことを確かめます。</summary>
    /// <remarks>責務: 取得証拠のない（セッションのない）取得で <c>sid</c> が空であることを確認します。</remarks>
    [Fact]
    public async Task StartedWithoutSessionHasNoSessionId()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: []);
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(new SingleDefinitionRepository(fixture.Definition), fixture.Telemetry));

        model.Send(new LiveTelemetryAction.StartRequested(
            fixture.BatchInput(0, 0).Endpoint,
            new VehicleID(Guid.Parse("10000000-0000-0000-0000-00000000000a")),
            null));
        await AsyncWait.UntilAsync(() => capture.Named("acq.started").Count >= 1);
        model.Send(new LiveTelemetryAction.StopRequested());
        await AsyncWait.UntilAsync(() => capture.Named("acq.stopped").Count >= 1);

        Assert.Null(Assert.Single(capture.Named("acq.started")).SessionId);
        Assert.Null(Assert.Single(capture.Named("acq.stopped")).SessionId);
    }

    /// <summary>動いている取得がないときの停止要求では、Info の <c>acq.stopped</c> を出さないことを確かめます。</summary>
    /// <remarks>責務: 起動直後の停止要求が Debug の <c>acq.stop.nothing-running</c> だけになることを確認します。</remarks>
    [Fact]
    public async Task StopWithNothingRunningDoesNotLogStopped()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: []);
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(new SingleDefinitionRepository(fixture.Definition), fixture.Telemetry));

        model.Send(new LiveTelemetryAction.StopRequested());
        await AsyncWait.UntilAsync(() => capture.Named("acq.stop.nothing-running").Count >= 1);

        Assert.Empty(capture.Named("acq.stopped"));
        var ignored = Assert.Single(capture.Named("acq.stop.nothing-running"));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, ignored.Level);
        Assert.Equal(LiveTelemetryStatePhase.Idle, model.State.Phase);
    }

    /// <summary>バッチ系のイベント名を、受け取った順に返します。</summary>
    /// <remarks>責務: 診断ログの記録から <c>acq.batch.*</c> だけを取り出します。</remarks>
    /// <param name="capture">記録。</param>
    /// <returns>イベント名。</returns>
    private static string[] BatchEventNames(DiagnosticLogCapture capture) =>
        [.. capture.EventNames.Where(name => name.StartsWith("acq.batch.", StringComparison.Ordinal))];

    /// <summary>1 件の PID 定義を返す定義リポジトリです。</summary>
    /// <param name="definition">返す定義。</param>
    private sealed class SingleDefinitionRepository(OBDPIDDefinition definition) : IOBDPIDDefinitionRepository
    {
        /// <summary>定義の一覧を返します。</summary>
        /// <remarks>責務: 1 件の定義を返します。</remarks>
        /// <returns>定義。</returns>
        public IReadOnlyList<OBDPIDDefinition> Definitions() => [definition];

        /// <summary>保存は使いません。</summary>
        /// <remarks>責務: 何もしません。</remarks>
        /// <param name="definition">使わない定義。</param>
        public void Upsert(OBDPIDDefinition definition)
        {
        }

        /// <summary>単一の照会は使いません。</summary>
        /// <remarks>責務: 常に未登録を返します。</remarks>
        /// <param name="service">Service。</param>
        /// <param name="pid">PID。</param>
        /// <returns>常に null。</returns>
        public OBDPIDDefinition? Definition(byte service, byte pid) => null;
    }

    /// <summary>確認済みの対応 PID を 1 件返すリポジトリです（探索の通信を起こさない）。</summary>
    /// <param name="vehicleID">車両 ID。</param>
    /// <param name="definition">対応している定義。</param>
    private sealed class SingleCapabilityRepository(VehicleID vehicleID, OBDPIDDefinition definition) : IVehiclePIDCapabilityRepository
    {
        /// <summary>対応 PID を返します。</summary>
        /// <remarks>責務: 収集有効な 1 件を返します。</remarks>
        /// <param name="requestedVehicleID">車両 ID。</param>
        /// <returns>対応 PID。</returns>
        public IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID requestedVehicleID) =>
        [
            new VehiclePIDCapability(
                vehicleID,
                new OBDPIDRequest(definition.Service, definition.Pid),
                isCollectionEnabled: true,
                DateTimeOffset.FromUnixTimeSeconds(1)),
        ];

        /// <summary>初回保存は使いません。</summary>
        /// <remarks>責務: 呼ばれたら失敗します。</remarks>
        /// <param name="capabilities">使わない値。</param>
        /// <param name="requestedVehicleID">使わない値。</param>
        public void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID requestedVehicleID) =>
            throw new InvalidOperationException();

        /// <summary>追加保存は使いません。</summary>
        /// <remarks>責務: 呼ばれたら失敗します。</remarks>
        /// <param name="capabilities">使わない値。</param>
        /// <param name="requestedVehicleID">使わない値。</param>
        public void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID requestedVehicleID) =>
            throw new InvalidOperationException();

        /// <summary>収集選択の更新は使いません。</summary>
        /// <remarks>責務: 呼ばれたら失敗します。</remarks>
        /// <param name="isEnabled">使わない値。</param>
        /// <param name="request">使わない値。</param>
        /// <param name="requestedVehicleID">使わない値。</param>
        public void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID requestedVehicleID) =>
            throw new InvalidOperationException();
    }
}
