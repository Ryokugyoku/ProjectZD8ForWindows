using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Application.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LiveTelemetry.State;

/// <summary>Production LiveTelemetry loopから取得証拠portまでの順序とidentityを検証します。</summary>
public sealed class LiveTelemetryProductionAcquisitionTests
{
    /// <summary>manifest start後にだけbatchを開始し、tickとordinalを同じ順序で増加させます。</summary>
    /// <remarks>責務: Production polling loopが1 tickを1 stable batch identityへ対応付けることを確認します。</remarks>
    [Fact]
    public async Task PollingStartsEvidenceBeforeStableBatchOrdinals()
    {
        var evidence = new LiveTelemetryAcquisitionEvidenceSpy();
        var model = MakeModel(evidence);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => evidence.BatchInputs.Count >= 2, timeoutMilliseconds: 1000);

        var events = evidence.Events;
        var batches = evidence.BatchInputs;
        Assert.Equal(
            [AcquisitionEvidenceEvent.Start, AcquisitionEvidenceEvent.Batch, AcquisitionEvidenceEvent.Batch],
            events.Take(3));
        Assert.Equal([0L, 1L], batches.Take(2).Select(batch => batch.BatchOrdinal));
        Assert.Equal([0UL, 1UL], batches.Take(2).Select(batch => batch.PolicyTick));
        model.Send(new LiveTelemetryAction.StopRequested());
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Idle, timeoutMilliseconds: 1000);
    }

    /// <summary>manifest開始失敗時はbatch取得を1件も許可しません。</summary>
    /// <remarks>責務: Production polling loopのmanifest保存失敗をphysical batch 0件の取得失敗へ変換します。</remarks>
    [Fact]
    public async Task ManifestFailurePreventsEveryBatchDispatch()
    {
        var evidence = new LiveTelemetryAcquisitionEvidenceSpy(failsStart: true);
        var endReasons = new List<ConnectionSessionEndReason>();
        var model = MakeModel(evidence, sessionDidEnd: endReasons.Add);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Failed);

        Assert.Empty(evidence.BatchInputs);
        Assert.Equal([ConnectionSessionEndReason.AcquisitionFailed], endReasons);
    }

    /// <summary>250 ms sleepの予定wakeからの超過だけを後続batchへ渡します。</summary>
    /// <remarks>責務: tick 0を未計測、tick 1を10 ms遅延として匿名schedule contextへ変換することを確認します。</remarks>
    [Fact]
    public async Task PollingRecordsDelayBeyondScheduledWake()
    {
        var evidence = new LiveTelemetryAcquisitionEvidenceSpy();
        var clock = new LiveTelemetryMonotonicClock([1_000_000_000, 1_260_000_000]);
        var model = MakeModel(evidence, monotonicNanoseconds: clock.Next);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => evidence.BatchInputs.Count >= 2, timeoutMilliseconds: 1000);

        var batches = evidence.BatchInputs;
        Assert.Equal([null, 10_000_000UL], batches.Take(2).Select(batch => batch.ScheduleDelayNanoseconds));
        model.Send(new LiveTelemetryAction.StopRequested());
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Idle, timeoutMilliseconds: 1000);
    }

    /// <summary>Production相当の定義、保存済みcapability、取得証拠portをモデルへ注入します。</summary>
    /// <remarks>責務: capability discovery通信を発生させないProduction polling fixtureを構築します。</remarks>
    /// <param name="evidence">manifestとbatch呼出しを記録するport。</param>
    /// <param name="monotonicNanoseconds">sleep前後の単調時刻を返すclock。<c>null</c> ならモデルの既定clock。</param>
    /// <param name="sessionDidEnd">取得終了理由の通知先。</param>
    /// <returns>方式B経路を使用するLiveTelemetry model。</returns>
    private static LiveTelemetryModel MakeModel(
        LiveTelemetryAcquisitionEvidenceSpy evidence,
        Func<ulong>? monotonicNanoseconds = null,
        Action<ConnectionSessionEndReason>? sessionDidEnd = null)
    {
        var definitionRepository = new LiveTelemetryProductionDefinitionRepository();
        return new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(
                definitionRepository: definitionRepository,
                telemetry: new LiveTelemetryProductionCapabilityTelemetry()),
            loadVehicleCapabilities: new LoadVehiclePIDCapabilitiesUseCase(
                repository: new LiveTelemetryProductionCapabilityRepository(VehicleID),
                telemetry: new LiveTelemetryProductionCapabilityTelemetry(),
                definitionRepository: definitionRepository),
            acquisitionEvidence: evidence,
            monotonicNanoseconds: monotonicNanoseconds,
            sessionDidEnd: sessionDidEnd ?? (_ => { }));
    }

    /// <summary>test用OBD終端です。</summary>
    private static OBDConnectionEndpoint Endpoint =>
        new(OBDConnectionEndpointTransport.Serial, "phase4g-live-loop", "Phase 4G live loop");

    /// <summary>test用登録車両IDです。</summary>
    private static VehicleID VehicleID => new(Guid.Parse("10000000-0000-0000-0000-000000000004"));

    /// <summary>testが指定した単調時刻をthread-safeに返します。</summary>
    /// <param name="values">呼出し順に返す単調時刻。</param>
    private sealed class LiveTelemetryMonotonicClock(IEnumerable<ulong> values)
    {
        /// <summary>値列への並行アクセスを直列化します。</summary>
        private readonly Lock _lock = new();

        /// <summary>次回以降に返す単調時刻です。</summary>
        private readonly Queue<ulong> _values = new(values);

        /// <summary>次の単調時刻を返します。</summary>
        /// <remarks>責務: 並行安全に先頭時刻を1件消費し、欠落時は直前相当の0を返します。</remarks>
        /// <returns>次の固定単調時刻、または値列が空の場合は0。</returns>
        public ulong Next()
        {
            lock (_lock)
            {
                return _values.Count == 0 ? 0 : _values.Dequeue();
            }
        }
    }

    /// <summary>Production取得証拠portの呼出し種別です。</summary>
    private enum AcquisitionEvidenceEvent
    {
        /// <summary>manifest開始要求です。</summary>
        Start,

        /// <summary>batch取得要求です。</summary>
        Batch,

        /// <summary>世代取消要求です。</summary>
        Cancel,
    }

    /// <summary>manifestとbatchの呼出し順を保持する取得証拠spyです。</summary>
    /// <param name="failsStart"><c>true</c> の場合にmanifest開始を失敗させます。</param>
    private sealed class LiveTelemetryAcquisitionEvidenceSpy(bool failsStart = false) : ILiveTelemetryAcquisitionEvidencePort
    {
        /// <summary>状態を保護するlockです。</summary>
        private readonly Lock _lock = new();

        /// <summary>観測した呼出し順です。</summary>
        private readonly List<AcquisitionEvidenceEvent> _events = [];

        /// <summary>観測したbatch入力です。</summary>
        private readonly List<LiveTelemetryAcquisitionBatchInput> _batchInputs = [];

        /// <summary>観測した呼出し順です。</summary>
        public IReadOnlyList<AcquisitionEvidenceEvent> Events
        {
            get
            {
                lock (_lock)
                {
                    return _events.ToArray();
                }
            }
        }

        /// <summary>観測したbatch入力です。</summary>
        public IReadOnlyList<LiveTelemetryAcquisitionBatchInput> BatchInputs
        {
            get
            {
                lock (_lock)
                {
                    return _batchInputs.ToArray();
                }
            }
        }

        /// <summary>manifest開始要求を記録します。</summary>
        /// <remarks>責務: 取得開始順を記録し、指定時だけ保存失敗を返します。</remarks>
        /// <param name="input">取得開始入力。</param>
        /// <returns>開始の完了。<c>failsStart</c> の場合は保存先利用不能で失敗します。</returns>
        public Task Start(LiveTelemetryAcquisitionStartInput input)
        {
            lock (_lock)
            {
                _events.Add(AcquisitionEvidenceEvent.Start);
            }
            return failsStart
                ? Task.FromException(new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable))
                : Task.CompletedTask;
        }

        /// <summary>batch入力を記録して固定sampleを返します。</summary>
        /// <remarks>責務: polling tickとordinalを記録可能な表示用sampleへ変換します。</remarks>
        /// <param name="input">Production loopが割り当てたbatch入力。</param>
        /// <returns>入力先頭定義に対応する固定sample。</returns>
        public Task<IReadOnlyList<OBDPIDSample>> Acquire(LiveTelemetryAcquisitionBatchInput input)
        {
            lock (_lock)
            {
                _events.Add(AcquisitionEvidenceEvent.Batch);
                _batchInputs.Add(input);
            }
            if (input.Definitions.FirstOrDefault() is not { } definition)
            {
                return Task.FromResult<IReadOnlyList<OBDPIDSample>>([]);
            }
            return Task.FromResult<IReadOnlyList<OBDPIDSample>>(
            [
                new OBDPIDSample(
                    request: new OBDPIDRequest(definition.Service, definition.Pid),
                    nameKey: definition.NameKey,
                    value: 42,
                    unit: definition.Unit,
                    observedAt: input.StartedAt,
                    vehicleModelCode: definition.VehicleModelCode,
                    summaryKey: definition.SummaryKey,
                    highValueKey: definition.HighValueKey,
                    lowValueKey: definition.LowValueKey,
                    correlationKey: definition.CorrelationKey),
            ]);
        }

        /// <summary>世代取消しを記録します。</summary>
        /// <remarks>責務: polling停止を取得証拠portの取消eventへ変換します。</remarks>
        /// <param name="generation">取消対象世代。</param>
        /// <returns>記録の完了。</returns>
        public Task Cancel(ulong generation)
        {
            lock (_lock)
            {
                _events.Add(AcquisitionEvidenceEvent.Cancel);
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>Production polling test用の固定PID定義を返します。</summary>
    private sealed class LiveTelemetryProductionDefinitionRepository : IOBDPIDDefinitionRepository
    {
        /// <summary>数値化可能なPID定義を返します。</summary>
        /// <remarks>責務: Production polling testへ完全range付き定義を1件供給します。</remarks>
        /// <returns>Service 01 PID 0Cの固定定義。</returns>
        public IReadOnlyList<OBDPIDDefinition> Definitions() =>
        [
            new OBDPIDDefinition(
                service: 1,
                pid: 12,
                nameKey: "test.phase4g.pid",
                requiredByteCount: 1,
                formula: "A",
                unit: "unit",
                minimumValue: 0,
                maximumValue: 255,
                sourceURI: "test://phase4g-live-loop",
                revision: 1),
        ];

        /// <summary>test対象外の保存を受理します。</summary>
        /// <remarks>責務: 未使用の定義保存要求を変更なしで完了します。</remarks>
        /// <param name="definition">testでは使用しない定義。</param>
        public void Upsert(OBDPIDDefinition definition)
        {
        }

        /// <summary>test対象外の単一照会へ未登録を返します。</summary>
        /// <remarks>責務: 未使用の単一定義照会を <c>null</c> へ変換します。</remarks>
        /// <param name="service">照会Service。</param>
        /// <param name="pid">照会PID。</param>
        /// <returns>常に <c>null</c>。</returns>
        public OBDPIDDefinition? Definition(byte service, byte pid) => null;
    }

    /// <summary>保存済みcapabilityを返すrepositoryです。</summary>
    /// <param name="vehicleID">capabilityを所有する車両ID。</param>
    private sealed class LiveTelemetryProductionCapabilityRepository(VehicleID vehicleID) : IVehiclePIDCapabilityRepository
    {
        /// <summary>保存済みcapabilityを返します。</summary>
        /// <remarks>責務: 対象車両を収集有効な確認済みPIDへ変換します。</remarks>
        /// <param name="requestedVehicleID">照会対象車両ID。</param>
        /// <returns>Service 01 PID 0Cの保存済みcapability。</returns>
        public IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID requestedVehicleID) =>
        [
            new VehiclePIDCapability(vehicleID, new OBDPIDRequest(1, 12), isCollectionEnabled: true, DateTimeOffset.FromUnixTimeSeconds(1)),
        ];

        /// <summary>初回保存をtest対象外として拒否します。</summary>
        /// <remarks>責務: 想定外のcapability discovery保存を明示失敗へ変換します。</remarks>
        /// <param name="capabilities">testでは保存しないcapability群。</param>
        /// <param name="requestedVehicleID">testでは保存しない対象車両ID。</param>
        public void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID requestedVehicleID) =>
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);

        /// <summary>追加発見保存をtest対象外として拒否します。</summary>
        /// <remarks>責務: 想定外のcapability追加を明示失敗へ変換します。</remarks>
        /// <param name="capabilities">testでは追加しないcapability群。</param>
        /// <param name="requestedVehicleID">testでは追加しない対象車両ID。</param>
        public void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID requestedVehicleID) =>
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);

        /// <summary>収集選択更新をtest対象外として拒否します。</summary>
        /// <remarks>責務: 想定外の収集選択更新を明示失敗へ変換します。</remarks>
        /// <param name="isEnabled">testでは保存しない収集有効状態。</param>
        /// <param name="request">testでは更新しないService/PID。</param>
        /// <param name="requestedVehicleID">testでは更新しない対象車両ID。</param>
        public void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID requestedVehicleID) =>
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
    }

    /// <summary>capability探索では呼ばれない通信境界です。</summary>
    private sealed class LiveTelemetryProductionCapabilityTelemetry : IOBDPIDTelemetryPort
    {
        /// <summary>想定外の物理要求を失敗させます。</summary>
        /// <remarks>責務: capability discovery通信が発生していないことを明示失敗で監視します。</remarks>
        /// <param name="requests">使用しない要求。</param>
        /// <param name="endpoint">使用しない終端。</param>
        /// <returns>常に通信利用不能で失敗するTask。</returns>
        public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
            IReadOnlyList<OBDPIDRequest> requests,
            OBDConnectionEndpoint endpoint) =>
            Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
                new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable));
    }
}
