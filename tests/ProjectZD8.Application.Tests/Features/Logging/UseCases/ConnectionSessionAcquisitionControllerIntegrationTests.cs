using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Logging.UseCases;

/// <summary>保存先とfake typed transportによる方式B接続を検証します。</summary>
/// <remarks>
/// iOS 版は file-backed の Production GRDB を使います。C# 版にはまだ Data 層がないため、
/// 同じ repository 契約と性能 event（Queue 通過点）を再現するメモリ上の保存先で検証します。
/// </remarks>
public sealed partial class ConnectionSessionAcquisitionControllerIntegrationTests
{
    /// <summary>manifest保存後だけ物理要求を許可し、同じ応答をRawとsampleへ変換します。</summary>
    /// <remarks>責務: storage、manifest gate、typed transport、方式B batchの一連接続を確認します。</remarks>
    [Fact]
    public async Task ManifestGatePrecedesTransportAndPersistsRespondedBatch()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([42])]);

        await fixture.Controller.Start(fixture.StartInput);
        var countBeforeAcquire = fixture.Telemetry.ReadCount;
        Assert.Equal(0, countBeforeAcquire);

        var samples = await fixture.Controller.Acquire(fixture.BatchInput(ordinal: 0, tick: 0));

        var countAfterAcquire = fixture.Telemetry.ReadCount;
        Assert.Equal(1, countAfterAcquire);
        Assert.Equal([42.0], samples.Select(sample => sample.Value));
        Assert.Equal([new byte[] { 42 }], fixture.Storage.EntryPayloads(fixture.Session.Id));
        var batches = fixture.Storage.Batches(fixture.Session.Id);
        Assert.Equal([0L], batches.Select(batch => batch.Identity.Ordinal));
        Assert.Equal([0UL], batches.Select(batch => batch.PolicyTick));
        Assert.Equal([PIDRequestTransportOutcome.Responded], batches[0].Requests.Select(request => request.TransportOutcome));
    }

    /// <summary>session不在ではmanifest保存と物理要求を開始しません。</summary>
    /// <remarks>責務: 親session欠落を最初のphysical requestより前の失敗へ変換します。</remarks>
    [Fact]
    public async Task MissingSessionPreventsEveryPhysicalRequest()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([42])], exposesActiveSession: false);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Controller.Start(fixture.StartInput));

        Assert.Equal(0, fixture.Telemetry.ReadCount);
        Assert.Empty(fixture.Storage.EntryPayloads(fixture.Session.Id));
    }

    /// <summary>timeoutはRawを作らずterminal evidenceだけを保存します。</summary>
    /// <remarks>責務: typed timeoutをRaw 0件のfailed batchへ変換します。</remarks>
    [Fact]
    public async Task TimeoutPersistsNoRawAndSealsBatch()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.TimedOut()]);
        await fixture.Controller.Start(fixture.StartInput);

        var samples = await fixture.Controller.Acquire(fixture.BatchInput(ordinal: 0, tick: 0));

        Assert.Empty(samples);
        Assert.Empty(fixture.Storage.EntryPayloads(fixture.Session.Id));
        var batch = Assert.IsType<AcquisitionBatchEvidence>(fixture.Storage.Batches(fixture.Session.Id).FirstOrDefault());
        Assert.Equal(AcquisitionBatchCompletionState.Failed, batch.CompletionState);
        Assert.Equal(PIDRequestTransportOutcome.TimedOut, batch.Requests[0].TransportOutcome);
    }

    /// <summary>完了済みbatchのexact retryは再送せずcanonical結果を再利用します。</summary>
    /// <remarks>責務: 同じordinal、tick、開始時刻のretryがRawまたはterminal evidenceを重複させないことを確認します。</remarks>
    [Fact]
    public async Task ExactBatchRetryDoesNotRedispatchOrDuplicateRaw()
    {
        var fixture = MakeFixture(outcomes:
        [
            new OBDPIDRequestTransportOutcome.Responded([42]),
            new OBDPIDRequestTransportOutcome.Responded([99]),
        ]);
        await fixture.Controller.Start(fixture.StartInput);
        var input = fixture.BatchInput(ordinal: 0, tick: 0);
        _ = await fixture.Controller.Acquire(input);

        var retriedSamples = await fixture.Controller.Acquire(input);

        Assert.Empty(retriedSamples);
        Assert.Equal(1, fixture.Telemetry.ReadCount);
        Assert.Equal([new byte[] { 42 }], fixture.Storage.EntryPayloads(fixture.Session.Id));
        Assert.Single(fixture.Storage.Batches(fixture.Session.Id));
    }

    /// <summary>controller、transport、保存先writeが同じ匿名event境界へ順序付きで到達します。</summary>
    /// <remarks>責務: 方式Bの開始からbatch確定までに必要な区間とDB Queue通過点が欠落しないことを確認します。</remarks>
    [Fact]
    public async Task PerformanceEventsCoverControllerTransportAndDatabaseQueue()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([42])]);

        await fixture.Controller.Start(fixture.StartInput);
        _ = await fixture.Controller.Acquire(fixture.BatchInput(ordinal: 0, tick: 0));

        var events = fixture.PerformanceEvents.Snapshot();
        Assert.Equal(new PerformanceEvent.Begin(AcquisitionPerformanceOperation.AcquisitionStart), events.First());
        Assert.Contains(new PerformanceEvent.Queue(AcquisitionPerformanceOperation.ManifestPersistence), events);
        Assert.Contains(new PerformanceEvent.Begin(AcquisitionPerformanceOperation.BatchAcquisition), events);
        Assert.Contains(new PerformanceEvent.Queue(AcquisitionPerformanceOperation.BatchOpenPersistence), events);
        Assert.Contains(new PerformanceEvent.End(AcquisitionPerformanceOperation.RequestTransport, AcquisitionPerformanceOutcome.Succeeded), events);
        Assert.Contains(new PerformanceEvent.Queue(AcquisitionPerformanceOperation.RespondedPersistence), events);
        Assert.Contains(new PerformanceEvent.Queue(AcquisitionPerformanceOperation.BatchSealPersistence), events);
        Assert.Equal(new PerformanceEvent.End(AcquisitionPerformanceOperation.BatchAcquisition, AcquisitionPerformanceOutcome.Succeeded), events.Last());
    }

    /// <summary>start前のstale batch拒否と世代取消を独立eventとして記録します。</summary>
    /// <remarks>責務: 性能測定でstale拒否件数と明示取消件数を匿名操作別に集計できることを確認します。</remarks>
    [Fact]
    public async Task PerformanceEventsDistinguishStaleRejectionAndCancellation()
    {
        var fixture = MakeFixture(outcomes: []);

        var error = await Assert.ThrowsAsync<PersistAcquisitionBatchException>(
            () => fixture.Controller.Acquire(fixture.BatchInput(ordinal: 0, tick: 0)));
        Assert.Equal(PersistAcquisitionBatchError.InactiveGeneration, error.Reason);
        await fixture.Controller.Cancel(1);

        var events = fixture.PerformanceEvents.Snapshot();
        Assert.Contains(new PerformanceEvent.End(AcquisitionPerformanceOperation.StaleBatchRejection, AcquisitionPerformanceOutcome.Failed), events);
        Assert.Contains(new PerformanceEvent.End(AcquisitionPerformanceOperation.GenerationCancellation, AcquisitionPerformanceOutcome.Succeeded), events);
    }

    /// <summary>test依存を同じ保存先へ構築します。</summary>
    /// <remarks>責務: storageとfake typed transportを1件のlocal integration fixtureへまとめます。</remarks>
    /// <param name="outcomes">物理要求順に返すtyped結果。</param>
    /// <param name="exposesActiveSession">controllerへ親sessionを公開するか。</param>
    /// <returns>manifest開始とbatch取得を実行できるfixture。</returns>
    private static ControllerIntegrationFixture MakeFixture(
        IReadOnlyList<OBDPIDRequestTransportOutcome> outcomes,
        bool exposesActiveSession = true)
    {
        var performanceEvents = new ControllerIntegrationPerformanceEventSpy();
        var storage = new InMemoryAcquisitionStorage(performanceEvents);
        var session = new ConnectionSession(
            accountIdentifier: "phase4g-account",
            id: new ConnectionSessionID(Guid.NewGuid()),
            startedAt: DateTimeOffset.FromUnixTimeSeconds(1));
        var telemetry = new ControllerIntegrationTelemetry(outcomes);
        var definition = new OBDPIDDefinition(
            service: 1,
            pid: 12,
            nameKey: "test.pid",
            requiredByteCount: 1,
            formula: "A",
            unit: "unit",
            minimumValue: 0,
            maximumValue: 255,
            sourceURI: "test://phase4g",
            revision: 1);
        var controller = new ConnectionSessionAcquisitionController(
            createManifest: new CreateConnectionSessionAcquisitionManifestUseCase(
                acquisitionRepository: storage,
                evidencePort: new ControllerIntegrationEvidencePort()),
            persistBatch: new PersistAcquisitionBatchUseCase(
                repository: storage,
                telemetry: telemetry,
                monotonicNanoseconds: new ControllerIntegrationClock().Next,
                now: () => DateTimeOffset.FromUnixTimeSeconds(20),
                performanceEvents: performanceEvents),
            activeSessionID: () => Task.FromResult<ConnectionSessionID?>(exposesActiveSession ? session.Id : null),
            manifestVersion: 1,
            pollingPolicyVersion: 1,
            modelInputManifestVersion: 1,
            formulaCanonicalizationVersion: 1,
            performanceEvents: performanceEvents);
        return new ControllerIntegrationFixture(storage, telemetry, performanceEvents, controller, session, definition);
    }

    /// <summary>controller local integrationの依存と入力を保持します。</summary>
    /// <param name="Storage">session、Raw、取得証拠が同じ保存先を使うstorageです。</param>
    /// <param name="Telemetry">typed結果と送信回数を保持するfake transportです。</param>
    /// <param name="PerformanceEvents">controller、transport、保存先の匿名性能event記録先です。</param>
    /// <param name="Controller">Production loop用の取得証拠controllerです。</param>
    /// <param name="Session">親接続sessionです。</param>
    /// <param name="Definition">manifestとbatchで使用するPID定義です。</param>
    private sealed record ControllerIntegrationFixture(
        InMemoryAcquisitionStorage Storage,
        ControllerIntegrationTelemetry Telemetry,
        ControllerIntegrationPerformanceEventSpy PerformanceEvents,
        ConnectionSessionAcquisitionController Controller,
        ConnectionSession Session,
        OBDPIDDefinition Definition)
    {
        /// <summary>manifest開始入力を返します。</summary>
        public LiveTelemetryAcquisitionStartInput StartInput =>
            new(
                generation: 1,
                definitions: [Definition],
                capabilities:
                [
                    new ConnectionSessionAcquisitionPIDCapabilityInput(
                        new OBDPIDRequest(Definition.Service, Definition.Pid),
                        AcquisitionPIDCapabilitySupport.Supported,
                        isCollectionEnabled: true),
                ],
                orderedRequests: [new OBDPIDRequest(Definition.Service, Definition.Pid)]);

        /// <summary>指定identityのbatch入力を返します。</summary>
        /// <remarks>責務: ordinalとpolicy tickを同じ固定開始時刻のbatch入力へ変換します。</remarks>
        /// <param name="ordinal">session内batch番号。</param>
        /// <param name="tick">polling policy tick。</param>
        /// <returns>controllerへ渡せるbatch入力。</returns>
        public LiveTelemetryAcquisitionBatchInput BatchInput(long ordinal, ulong tick) =>
            new(
                generation: 1,
                batchOrdinal: ordinal,
                policyTick: tick,
                startedAt: DateTimeOffset.FromUnixTimeSeconds(10 + ordinal),
                definitions: [Definition],
                endpoint: new OBDConnectionEndpoint(OBDConnectionEndpointTransport.Serial, "phase4g-fake", "Phase 4G fake"));
    }

    /// <summary>integration testが観測する匿名performance eventです。</summary>
    private abstract record PerformanceEvent
    {
        /// <summary>区間開始です。</summary>
        public sealed record Begin(AcquisitionPerformanceOperation Operation) : PerformanceEvent;

        /// <summary>DB Queue通過です。</summary>
        public sealed record Queue(AcquisitionPerformanceOperation Operation) : PerformanceEvent;

        /// <summary>結果付き区間終了です。</summary>
        public sealed record End(AcquisitionPerformanceOperation Operation, AcquisitionPerformanceOutcome Outcome) : PerformanceEvent;
    }

    /// <summary>controller、transport、保存先が通知したperformance eventをthread-safeに保持します。</summary>
    private sealed class ControllerIntegrationPerformanceEventSpy : IAcquisitionPerformanceEventPort
    {
        /// <summary>event列と採番への並行アクセスを直列化します。</summary>
        private readonly Lock _lock = new();

        /// <summary>観測済みevent列です。</summary>
        private readonly List<PerformanceEvent> _events = [];

        /// <summary>次に割り当てる匿名interval番号です。</summary>
        private ulong _nextIdentifier = 1;

        /// <summary>区間開始を記録して匿名tokenを返します。</summary>
        /// <remarks>責務: 1件の開始通知を順序付きeventと一意なtest tokenへ変換します。</remarks>
        /// <param name="operation">計測する操作。</param>
        /// <param name="context">匿名取得位置。</param>
        /// <returns>Queue通過と終了に再利用するtoken。</returns>
        public AcquisitionPerformanceInterval Begin(AcquisitionPerformanceOperation operation, AcquisitionPerformanceContext context)
        {
            lock (_lock)
            {
                var interval = new AcquisitionPerformanceInterval(_nextIdentifier, operation, context);
                _nextIdentifier += 1;
                _events.Add(new PerformanceEvent.Begin(operation));
                return interval;
            }
        }

        /// <summary>DB Queue通過を記録します。</summary>
        /// <remarks>責務: 1件のQueue通過通知を開始済み操作の順序付きeventへ変換します。</remarks>
        /// <param name="interval">開始済み区間。</param>
        public void QueueDidEnter(AcquisitionPerformanceInterval interval) =>
            Append(new PerformanceEvent.Queue(interval.Operation));

        /// <summary>区間終了を結果付きで記録します。</summary>
        /// <remarks>責務: 1件の終了通知を操作と結果を保持する順序付きeventへ変換します。</remarks>
        /// <param name="interval">開始済み区間。</param>
        /// <param name="outcome">完了結果。</param>
        public void End(AcquisitionPerformanceInterval interval, AcquisitionPerformanceOutcome outcome) =>
            Append(new PerformanceEvent.End(interval.Operation, outcome));

        /// <summary>現在のevent列を返します。</summary>
        /// <remarks>責務: 並行書込み中の内部列を安定したtest検証用snapshotへ変換します。</remarks>
        /// <returns>呼出し時点のevent列。</returns>
        public IReadOnlyList<PerformanceEvent> Snapshot()
        {
            lock (_lock)
            {
                return _events.ToArray();
            }
        }

        /// <summary>eventを末尾へ追加します。</summary>
        /// <remarks>責務: 1件のperformance eventをlock保護下の観測列へ追加します。</remarks>
        /// <param name="performanceEvent">追加するevent。</param>
        private void Append(PerformanceEvent performanceEvent)
        {
            lock (_lock)
            {
                _events.Add(performanceEvent);
            }
        }
    }

    /// <summary>GRDB 保存先の代わりに、manifest、Raw、batch証拠を同じ排他区間で保存するメモリ上の保存先です。</summary>
    /// <param name="performanceEvents">write区間とQueue通過を通知する匿名計測境界。</param>
    private sealed class InMemoryAcquisitionStorage(IAcquisitionPerformanceEventPort performanceEvents)
        : IConnectionSessionAcquisitionRepository, IConnectionSessionAcquisitionBatchRepository
    {
        /// <summary>保存先のwrite Queueを表すlockです。</summary>
        private readonly Lock _queue = new();

        /// <summary>sessionごとのmanifestです。</summary>
        private readonly Dictionary<ConnectionSessionID, ConnectionSessionAcquisitionManifest> _manifests = [];

        /// <summary>sessionごとの境界eventです。</summary>
        private readonly Dictionary<ConnectionSessionID, List<AcquisitionRawBoundaryEvidence>> _boundaries = [];

        /// <summary>sessionごとのRaw観測です。</summary>
        private readonly Dictionary<ConnectionSessionID, List<OBDRawResponseObservation>> _entries = [];

        /// <summary>sessionごとのbatch証拠です。</summary>
        private readonly Dictionary<ConnectionSessionID, List<AcquisitionBatchEvidence>> _batches = [];

        /// <summary>保存済みRawのpayloadを保存順で返します。</summary>
        /// <remarks>責務: test検証用にsessionのRaw payload列を読み取ります。</remarks>
        /// <param name="sessionID">読取対象session。</param>
        /// <returns>保存順のpayload列。</returns>
        public IReadOnlyList<byte[]> EntryPayloads(ConnectionSessionID sessionID)
        {
            lock (_queue)
            {
                return _entries.TryGetValue(sessionID, out var entries)
                    ? entries.Select(entry => entry.Payload.ToArray()).ToArray()
                    : [];
            }
        }

        /// <summary>未登録sessionへmanifestとRaw開始境界を1回の原子操作で保存します。</summary>
        /// <remarks>責務: 同じ組の重複と異なる組の競合を区別して保存します。</remarks>
        /// <param name="manifest">保存するmanifest。</param>
        /// <param name="startedAt">開始境界時刻。</param>
        /// <param name="sessionID">所有session。</param>
        public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.ManifestPersistence, () =>
            {
                if (_manifests.TryGetValue(sessionID, out var stored))
                {
                    var storedStart = _boundaries[sessionID].OfType<AcquisitionRawBoundaryEvidence.Started>().First();
                    throw new ConnectionSessionAcquisitionRepositoryException(
                        stored.Equals(manifest) && storedStart.At == startedAt
                            ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                            : ConnectionSessionAcquisitionRepositoryError.Conflict);
                }
                _manifests[sessionID] = manifest;
                _boundaries[sessionID] = [new AcquisitionRawBoundaryEvidence.Started(startedAt)];
            });

        /// <summary>開始済みsessionへRaw終了境界を追記します。</summary>
        /// <remarks>責務: 開始欠落を拒否して終了境界を1件追記します。</remarks>
        /// <param name="endedAt">終了境界時刻。</param>
        /// <param name="reason">終了理由。</param>
        /// <param name="sessionID">所有session。</param>
        public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.SessionTerminationPersistence, () =>
            {
                if (!_boundaries.TryGetValue(sessionID, out var boundaries))
                {
                    throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
                }
                boundaries.Add(new AcquisitionRawBoundaryEvidence.Ended(endedAt, reason));
            });

        /// <summary>sessionに関連付けられた取得manifestを読み取ります。</summary>
        /// <remarks>責務: 保存済みmanifestを変更せず返します。</remarks>
        /// <param name="sessionID">読取対象session。</param>
        /// <returns>保存済みmanifest。</returns>
        public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID)
        {
            lock (_queue)
            {
                return _manifests.TryGetValue(sessionID, out var manifest)
                    ? manifest
                    : throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
            }
        }

        /// <summary>保存済みのRaw取得境界を追記順で読み取ります。</summary>
        /// <remarks>責務: 開始と終了eventを変更せず返します。</remarks>
        /// <param name="sessionID">読取対象session。</param>
        /// <returns>追記順の境界event。</returns>
        public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID)
        {
            lock (_queue)
            {
                return _boundaries.TryGetValue(sessionID, out var boundaries) ? boundaries.ToArray() : [];
            }
        }

        /// <summary>open batchを保存します。</summary>
        /// <remarks>責務: 同じidentityの重複を拒否してopen batchを追加します。</remarks>
        /// <param name="evidence">保存するopen batch。</param>
        /// <param name="sessionID">所有session。</param>
        public void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.BatchOpenPersistence, () =>
            {
                var batches = BatchList(sessionID);
                if (batches.Any(batch => batch.Identity == evidence.Identity))
                {
                    throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Duplicate);
                }
                batches.Add(evidence);
            });

        /// <summary>requestをdispatch開始へ遷移させます。</summary>
        /// <remarks>責務: open batch内の指定requestをdispatch開始済みにします。</remarks>
        /// <param name="requestOrdinal">batch内要求順。</param>
        /// <param name="batchIdentity">親batch identity。</param>
        /// <param name="sessionID">所有session。</param>
        public void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.RequestDispatchPersistence, () =>
                ReplaceRequest(sessionID, batchIdentity, requestOrdinal, stored => new PIDRequestEvidence(
                    stored.RequestOrdinal,
                    stored.ManifestPIDOrdinal,
                    PIDRequestDispatchState.DispatchBegun,
                    null,
                    PIDRequestValueOutcome.NotEvaluated,
                    null,
                    null,
                    null)));

        /// <summary>responded Rawとrequest terminalを原子保存します。</summary>
        /// <remarks>責務: Rawへsequenceを採番してterminal requestを返します。</remarks>
        /// <returns>採番済みrequest evidence。</returns>
        public PIDRequestEvidence SaveRespondedRequest(
            OBDRawResponseObservation observation,
            PIDRequestValueOutcome valueOutcome,
            ulong elapsedNanoseconds,
            string? reasonCode,
            int requestOrdinal,
            AcquisitionBatchIdentity batchIdentity,
            ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.RespondedPersistence, () =>
            {
                if (!_entries.TryGetValue(sessionID, out var entries))
                {
                    entries = [];
                    _entries[sessionID] = entries;
                }
                var sequence = (long)entries.Count;
                var terminal = ReplaceRequest(sessionID, batchIdentity, requestOrdinal, stored => new PIDRequestEvidence(
                    stored.RequestOrdinal,
                    stored.ManifestPIDOrdinal,
                    PIDRequestDispatchState.Terminal,
                    PIDRequestTransportOutcome.Responded,
                    valueOutcome,
                    sequence,
                    elapsedNanoseconds,
                    reasonCode));
                entries.Add(observation);
                return terminal;
            });

        /// <summary>nonresponded request terminalを保存します。</summary>
        /// <remarks>責務: Rawを作らず指定outcomeのterminal requestを返します。</remarks>
        /// <returns>terminal request evidence。</returns>
        public PIDRequestEvidence SaveNonRespondedRequest(
            PIDRequestTransportOutcome outcome,
            ulong? elapsedNanoseconds,
            string? reasonCode,
            int requestOrdinal,
            AcquisitionBatchIdentity batchIdentity,
            ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.NonRespondedPersistence, () =>
                ReplaceRequest(sessionID, batchIdentity, requestOrdinal, stored => new PIDRequestEvidence(
                    stored.RequestOrdinal,
                    stored.ManifestPIDOrdinal,
                    PIDRequestDispatchState.Terminal,
                    outcome,
                    PIDRequestValueOutcome.NotEvaluated,
                    null,
                    elapsedNanoseconds,
                    reasonCode)));

        /// <summary>terminal batchを保存します。</summary>
        /// <remarks>責務: 同じidentityのopen batchをterminal batchへ置き換えます。</remarks>
        /// <param name="evidence">保存するterminal batch。</param>
        /// <param name="sessionID">所有session。</param>
        public void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) =>
            Write(AcquisitionPerformanceOperation.BatchSealPersistence, () =>
            {
                var batches = BatchList(sessionID);
                var index = batches.FindIndex(batch => batch.Identity == evidence.Identity);
                if (index < 0)
                {
                    throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
                }
                batches[index] = evidence;
            });

        /// <summary>保存済みbatch列を返します。</summary>
        /// <remarks>責務: sessionのbatch証拠を保存順で読み取ります。</remarks>
        /// <param name="sessionID">読取対象session。</param>
        /// <returns>保存順のbatch列。</returns>
        public IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID)
        {
            lock (_queue)
            {
                return _batches.TryGetValue(sessionID, out var batches) ? batches.ToArray() : [];
            }
        }

        /// <summary>performance区間とQueue通過を通知しながら1件のwriteを実行します。</summary>
        /// <remarks>責務: 1件の保存操作を排他区間で実行し、成功か失敗で区間を閉じます。</remarks>
        /// <param name="operation">計測する保存操作。</param>
        /// <param name="body">Queue内で実行する保存処理。</param>
        private void Write(AcquisitionPerformanceOperation operation, Action body) =>
            Write(operation, () =>
            {
                body();
                return 0;
            });

        /// <summary>performance区間とQueue通過を通知しながら1件のwriteを実行します。</summary>
        /// <remarks>責務: 1件の保存操作を排他区間で実行し、成功か失敗で区間を閉じます。</remarks>
        /// <typeparam name="T">保存処理の戻り値型。</typeparam>
        /// <param name="operation">計測する保存操作。</param>
        /// <param name="body">Queue内で実行する保存処理。</param>
        /// <returns>保存処理の戻り値。</returns>
        private T Write<T>(AcquisitionPerformanceOperation operation, Func<T> body)
        {
            var interval = performanceEvents.Begin(operation, new AcquisitionPerformanceContext());
            try
            {
                T result;
                lock (_queue)
                {
                    performanceEvents.QueueDidEnter(interval);
                    result = body();
                }
                performanceEvents.End(interval, AcquisitionPerformanceOutcome.Succeeded);
                return result;
            }
            catch (Exception)
            {
                performanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
                throw;
            }
        }

        /// <summary>sessionのbatch列を返し、なければ作ります。</summary>
        /// <remarks>責務: Queue内でsessionごとの可変batch列を取得します。</remarks>
        /// <param name="sessionID">所有session。</param>
        /// <returns>可変batch列。</returns>
        private List<AcquisitionBatchEvidence> BatchList(ConnectionSessionID sessionID)
        {
            if (!_batches.TryGetValue(sessionID, out var batches))
            {
                batches = [];
                _batches[sessionID] = batches;
            }
            return batches;
        }

        /// <summary>open batch内の1件のrequestを置き換えます。</summary>
        /// <remarks>責務: request遷移をDomain aggregate不変条件へ反映します。</remarks>
        /// <param name="sessionID">所有session。</param>
        /// <param name="batchIdentity">親batch identity。</param>
        /// <param name="ordinal">置換するrequest順。</param>
        /// <param name="transform">現在requestを次状態へ変換する処理。</param>
        /// <returns>置換後のrequest。</returns>
        private PIDRequestEvidence ReplaceRequest(
            ConnectionSessionID sessionID,
            AcquisitionBatchIdentity batchIdentity,
            int ordinal,
            Func<PIDRequestEvidence, PIDRequestEvidence> transform)
        {
            var batches = BatchList(sessionID);
            var index = batches.FindIndex(batch => batch.Identity == batchIdentity && batch.CompletionState is null);
            if (index < 0)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
            }
            var batch = batches[index];
            var requests = batch.Requests.ToArray();
            requests[ordinal] = transform(requests[ordinal]);
            batches[index] = new AcquisitionBatchEvidence(
                batch.Identity,
                batch.Generation,
                batch.PolicyTick,
                batch.IsSelectionEvaluationComplete,
                batch.StartedAt,
                null,
                null,
                null,
                requests);
            return requests[ordinal];
        }
    }

    /// <summary>typed結果を要求順に返すfake transportです。</summary>
    /// <param name="outcomes">physical request順に返す結果。</param>
    private sealed class ControllerIntegrationTelemetry(IReadOnlyList<OBDPIDRequestTransportOutcome> outcomes) : IOBDPIDTelemetryPort
    {
        /// <summary>状態を保護するlockです。</summary>
        private readonly Lock _lock = new();

        /// <summary>未返却のtyped結果です。</summary>
        private readonly Queue<OBDPIDRequestTransportOutcome> _outcomes = new(outcomes);

        /// <summary>実行したphysical request数です。</summary>
        private int _readCount;

        /// <summary>実行したphysical request数です。</summary>
        public int ReadCount
        {
            get
            {
                lock (_lock)
                {
                    return _readCount;
                }
            }
        }

        /// <summary>1件の要求へ次のtyped結果を返します。</summary>
        /// <remarks>責務: 物理送信相当の呼出しを数え、次の排他観測へ変換します。</remarks>
        /// <param name="requests">実行対象要求。</param>
        /// <param name="endpoint">testでは使用しない終端。</param>
        /// <returns>先頭要求と次のtyped結果の観測。</returns>
        public Task<IReadOnlyList<OBDPIDRequestTransportObservation>> ReadObservations(
            IReadOnlyList<OBDPIDRequest> requests,
            OBDConnectionEndpoint endpoint)
        {
            lock (_lock)
            {
                _readCount += requests.Count;
                if (requests.Count == 0 || _outcomes.Count == 0)
                {
                    return Task.FromResult<IReadOnlyList<OBDPIDRequestTransportObservation>>([]);
                }
                return Task.FromResult<IReadOnlyList<OBDPIDRequestTransportObservation>>(
                    [new OBDPIDRequestTransportObservation(requests[0], _outcomes.Dequeue())]);
            }
        }

        /// <summary>辞書型readをtest対象外の空結果として実装します。</summary>
        /// <remarks>責務: protocol互換の辞書型読取を副作用のない空結果へ変換します。</remarks>
        /// <param name="requests">使用しない要求。</param>
        /// <param name="endpoint">使用しない終端。</param>
        /// <returns>常に空辞書。</returns>
        public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
            IReadOnlyList<OBDPIDRequest> requests,
            OBDConnectionEndpoint endpoint) =>
            Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
                new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>());
    }

    /// <summary>固定のProduction相当runtime evidenceを返します。</summary>
    private sealed class ControllerIntegrationEvidencePort : IConnectionSessionAcquisitionEvidencePort
    {
        /// <summary>固定runtime evidenceを返します。</summary>
        /// <remarks>責務: manifest作成を実環境へ依存しない決定的なversion証拠へ変換します。</remarks>
        /// <returns>Phase 4G local integration用runtime evidence。</returns>
        public Task<ConnectionSessionAcquisitionRuntimeEvidence> Evidence() =>
            Task.FromResult(new ConnectionSessionAcquisitionRuntimeEvidence(
                new AcquisitionApplicationVersion("phase4g", "local"),
                1,
                ConnectionSessionAcquisitionPlatform.MacOS));
    }

    /// <summary>単調増加するrequest clockです。</summary>
    private sealed class ControllerIntegrationClock
    {
        /// <summary>次回返す値です。</summary>
        private ulong _value;

        /// <summary>次の単調値を返します。</summary>
        /// <remarks>責務: 呼出しごとに100増加する時刻を1件返します。</remarks>
        /// <returns>現在値。</returns>
        public ulong Next()
        {
            var current = _value;
            _value += 100;
            return current;
        }
    }
}
