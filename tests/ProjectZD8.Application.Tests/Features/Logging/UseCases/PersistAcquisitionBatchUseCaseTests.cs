using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Logging.UseCases;

/// <summary>PID単位typed観測からbatch取得証拠を保存するApplication orchestrationを検証します。</summary>
public sealed class PersistAcquisitionBatchUseCaseTests
{
    /// <summary>decode成功をresponded Raw参照とdecoded-validへ保存します。</summary>
    /// <remarks>責務: 正応答payloadが有限range内の値と採番済みRaw sequenceへ変換されることを確認します。</remarks>
    [Fact]
    public async Task RespondedDecodeSuccessPersistsRawSequenceAndValidOutcome()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([50])]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal(AcquisitionBatchCompletionState.Completed, result.CompletionState);
        Assert.Equal(PIDRequestValueOutcome.DecodedValid, result.Requests[0].ValueOutcome);
        Assert.Equal(0, result.Requests[0].RawSequence);
        Assert.Equal([new byte[] { 50 }], fixture.Repository.RespondedObservations.Select(observation => observation.Payload.ToArray()));
    }

    /// <summary>数式評価失敗をRawを保持したdecode failureへ保存します。</summary>
    /// <remarks>責務: bytes不足が正応答消失やtransport failureへ変換されないことを確認します。</remarks>
    [Fact]
    public async Task RespondedDecodeFailureKeepsRaw()
    {
        var definition = MakeDefinition(requiredByteCount: 2, formula: "A + B");
        var fixture = MakeFixture(definitions: [definition], outcomes: [new OBDPIDRequestTransportOutcome.Responded([1])]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal(PIDRequestValueOutcome.DecodeFailure, result.Requests[0].ValueOutcome);
        Assert.Equal(0, result.Requests[0].RawSequence);
        Assert.Single(fixture.Repository.RespondedObservations);
    }

    /// <summary>range外の有限値をinvalid valueへ保存します。</summary>
    /// <remarks>責務: 明示range外のdecode成功値をdecoded-validと混同しないことを確認します。</remarks>
    [Fact]
    public async Task RespondedOutOfRangePersistsInvalidValue()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([101])]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal(PIDRequestValueOutcome.InvalidValue, result.Requests[0].ValueOutcome);
        Assert.Equal(0, result.Requests[0].RawSequence);
    }

    /// <summary>timeoutはRawを作らずpartial batchを確定します。</summary>
    /// <remarks>責務: typed timeoutを非responded evidenceとbatch transport failureへ変換します。</remarks>
    [Fact]
    public async Task TimeoutCreatesNoRawAndFinishesPartialBatch()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.TimedOut()]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Empty(fixture.Repository.RespondedObservations);
        Assert.Equal(PIDRequestTransportOutcome.TimedOut, result.Requests[0].TransportOutcome);
        Assert.Null(result.Requests[0].RawSequence);
        Assert.Equal(AcquisitionBatchCompletionState.Failed, result.CompletionState);
        Assert.Equal(AcquisitionBatchFailure.TransportUnavailable, result.Failure);
    }

    /// <summary>explicit unsupportedだけをunsupportedへ保存します。</summary>
    /// <remarks>責務: adapter typed証拠を持つunsupportedが通常throwやunclassifiedと別経路になることを確認します。</remarks>
    [Fact]
    public async Task ExplicitUnsupportedIsPersistedWithoutRaw()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Unsupported()]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal(PIDRequestTransportOutcome.Unsupported, result.Requests[0].TransportOutcome);
        Assert.Equal(AcquisitionBatchCompletionState.Completed, result.CompletionState);
        Assert.Empty(fixture.Repository.RespondedObservations);
    }

    /// <summary>explicit cancelをunknown-after-terminationと異なるpartial結果へ保存します。</summary>
    /// <remarks>責務: typed取消しをcancelled requestとcancelled batch failureへ変換します。</remarks>
    [Fact]
    public async Task ExplicitCancellationRemainsDistinctFromTerminationRecovery()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Cancelled()]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal(PIDRequestTransportOutcome.Cancelled, result.Requests[0].TransportOutcome);
        Assert.NotEqual(PIDRequestTransportOutcome.UnknownAfterTermination, result.Requests[0].TransportOutcome);
        Assert.Equal(AcquisitionBatchFailure.Cancelled, result.Failure);
        Assert.Empty(fixture.Repository.RespondedObservations);
    }

    /// <summary>stale世代を最初のmetadata永続化前に拒否します。</summary>
    /// <remarks>責務: 現在世代と異なるbatchがopen evidenceを作らないことを確認します。</remarks>
    [Fact]
    public async Task StaleGenerationIsRejectedBeforePersistence()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([1])], activateGeneration: 2);

        var error = await Assert.ThrowsAsync<PersistAcquisitionBatchException>(() => fixture.UseCase.Execute(fixture.Input));
        Assert.Equal(PersistAcquisitionBatchError.InactiveGeneration, error.Reason);
        Assert.Empty(fixture.Repository.Events);
    }

    /// <summary>metadata保存失敗を成功結果へ変換しません。</summary>
    /// <remarks>責務: open batch永続化失敗がtransport実行や空batch成功へ進まないことを確認します。</remarks>
    [Fact]
    public async Task MetadataPersistenceFailureIsNotReportedAsSuccess()
    {
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([1])]);
        fixture.Repository.FailurePoint = AcquisitionRepositoryFailurePoint.BeginBatch;

        var error = await Assert.ThrowsAsync<PersistAcquisitionBatchException>(() => fixture.UseCase.Execute(fixture.Input));
        Assert.Equal(PersistAcquisitionBatchError.PersistenceFailure, error.Reason);
        Assert.Equal([new RepositoryEvent.BeginBatch()], fixture.Repository.Events);
        Assert.Equal(0, fixture.Telemetry.ReadCount);
    }

    /// <summary>responded Raw保存失敗を成功や空応答へ変換しません。</summary>
    /// <remarks>責務: typed正応答後の原子repository失敗を明示persistence failureとして返します。</remarks>
    [Fact]
    public async Task RawPersistenceFailureIsNotReportedAsSuccess()
    {
        IReadOnlyList<OBDPIDDefinition> definitions = [MakeDefinition(pid: 12), MakeDefinition(pid: 13)];
        var fixture = MakeFixture(
            definitions: definitions,
            outcomes: [new OBDPIDRequestTransportOutcome.Responded([50]), new OBDPIDRequestTransportOutcome.Responded([60])]);
        fixture.Repository.FailurePoint = AcquisitionRepositoryFailurePoint.Responded;

        var error = await Assert.ThrowsAsync<PersistAcquisitionBatchException>(() => fixture.UseCase.Execute(fixture.Input));
        Assert.Equal(PersistAcquisitionBatchError.PersistenceFailure, error.Reason);
        Assert.Equal(
            [new RepositoryEvent.BeginBatch(), new RepositoryEvent.Dispatch(0), new RepositoryEvent.Responded(0)],
            fixture.Repository.Events);
        Assert.Empty(fixture.Repository.RespondedObservations);
        Assert.Equal(1, fixture.Telemetry.ReadCount);
    }

    /// <summary>polling subsetのrequestへmanifest内の元ordinalを保持します。</summary>
    /// <remarks>責務: 間引かれた取得定義をbatch内順とmanifest内順の異なる決定的identityへ対応付けます。</remarks>
    [Fact]
    public async Task PollingSubsetRetainsManifestOrdinal()
    {
        IReadOnlyList<OBDPIDDefinition> manifestDefinitions = [MakeDefinition(pid: 12), MakeDefinition(pid: 13)];
        var fixture = MakeFixture(
            definitions: [manifestDefinitions[1]],
            manifestDefinitions: manifestDefinitions,
            outcomes: [new OBDPIDRequestTransportOutcome.Responded([50])]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal([0], result.Requests.Select(request => request.RequestOrdinal));
        Assert.Equal([1], result.Requests.Select(request => request.ManifestPIDOrdinal));
        Assert.Equal([PIDRequestTransportOutcome.Responded], result.Requests.Select(request => request.TransportOutcome));
    }

    /// <summary>成功要求とtimeoutを同じpartial batchへ保持します。</summary>
    /// <remarks>責務: 途中terminal failureが先行responded Rawを消さずbatch失敗として確定されることを確認します。</remarks>
    [Fact]
    public async Task MixedRespondedAndTimeoutFinishesPartialBatch()
    {
        IReadOnlyList<OBDPIDDefinition> definitions = [MakeDefinition(pid: 12), MakeDefinition(pid: 13)];
        var fixture = MakeFixture(
            definitions: definitions,
            outcomes: [new OBDPIDRequestTransportOutcome.Responded([50]), new OBDPIDRequestTransportOutcome.TimedOut()]);

        var result = await fixture.UseCase.Execute(fixture.Input);

        Assert.Equal(
            [PIDRequestTransportOutcome.Responded, PIDRequestTransportOutcome.TimedOut],
            result.Requests.Select(request => request.TransportOutcome));
        Assert.Equal(AcquisitionBatchCompletionState.Failed, result.CompletionState);
        Assert.Equal(AcquisitionBatchFailure.TransportUnavailable, result.Failure);
        Assert.Single(fixture.Repository.RespondedObservations);
    }

    /// <summary>process終了回復はdispatch済み要求だけをunknownへ遷移させます。</summary>
    /// <remarks>責務: 明示cancelと異なるunknown-after-terminationをselected-only要求と共存させます。</remarks>
    [Fact]
    public void TerminationRecoveryKeepsUnknownDistinctFromCancellation()
    {
        IReadOnlyList<OBDPIDDefinition> definitions = [MakeDefinition(pid: 12), MakeDefinition(pid: 13)];
        var fixture = MakeFixture(definitions: definitions, outcomes: []);
        fixture.Repository.BeginBatch(OpenBatch(fixture.Input), fixture.Input.Permission.SessionID);
        fixture.Repository.MarkRequestDispatchBegun(0, fixture.Input.BatchIdentity, fixture.Input.Permission.SessionID);

        var result = fixture.UseCase.RecoverAfterTermination(
            fixture.Input.BatchIdentity,
            fixture.Input.Permission.SessionID,
            DateTimeOffset.FromUnixTimeSeconds(30));

        Assert.Equal(PIDRequestTransportOutcome.UnknownAfterTermination, result.Requests[0].TransportOutcome);
        Assert.Null(result.Requests[0].ElapsedNanoseconds);
        Assert.Equal(PIDRequestDispatchState.SelectedOnly, result.Requests[1].DispatchState);
        Assert.NotEqual(PIDRequestTransportOutcome.Cancelled, result.Requests[0].TransportOutcome);
        Assert.Equal(AcquisitionBatchCompletionState.TerminatedUnknown, result.CompletionState);
    }

    /// <summary>Application test依存と入力を生成します。</summary>
    /// <remarks>責務: fake repository、typed telemetry、clock、manifest許可を同じbatch fixtureへまとめます。</remarks>
    /// <param name="outcomes">request順に返すtyped Transport結果。</param>
    /// <param name="definitions">manifest順の取得時PID定義。</param>
    /// <param name="manifestDefinitions">manifestへ固定する全取得時PID定義。省略時は <paramref name="definitions"/> と同一。</param>
    /// <param name="activateGeneration">use caseへ有効化する世代。</param>
    /// <returns>実行可能なApplication fixture。</returns>
    private static AcquisitionUseCaseFixture MakeFixture(
        IReadOnlyList<OBDPIDRequestTransportOutcome> outcomes,
        IReadOnlyList<OBDPIDDefinition>? definitions = null,
        IReadOnlyList<OBDPIDDefinition>? manifestDefinitions = null,
        ulong activateGeneration = 1)
    {
        var requestedDefinitions = definitions ?? [MakeDefinition()];
        var fixedManifestDefinitions = manifestDefinitions ?? requestedDefinitions;
        var repository = new AcquisitionBatchRepositoryFake();
        var telemetry = new AcquisitionTelemetryFake(outcomes);
        var clock = new MonotonicSequence(
            Enumerable.Range(0, Math.Max(1, requestedDefinitions.Count) * 2).Select(value => (ulong)(value * 100)));
        var useCase = new PersistAcquisitionBatchUseCase(
            repository: repository,
            telemetry: telemetry,
            monotonicNanoseconds: clock.Next,
            now: () => DateTimeOffset.FromUnixTimeSeconds(20));
        var manifest = MakeManifest(fixedManifestDefinitions);
        var permission = new ConnectionSessionRawAcquisitionPermission(
            new ConnectionSessionID(Guid.Parse("20000000-0000-0000-0000-000000000001")),
            1,
            manifest);
        var input = new PersistAcquisitionBatchInput(
            permission: permission,
            batchIdentity: new AcquisitionBatchIdentity(0),
            policyTick: 0,
            startedAt: DateTimeOffset.FromUnixTimeSeconds(10),
            definitions: requestedDefinitions,
            endpoint: new OBDConnectionEndpoint(OBDConnectionEndpointTransport.Serial, "test", "test"));
        useCase.Activate(activateGeneration);
        return new AcquisitionUseCaseFixture(repository, telemetry, useCase, input);
    }

    /// <summary>test用PID定義を生成します。</summary>
    /// <remarks>責務: decode、range、PIDだけを差し替え可能な取得定義を返します。</remarks>
    /// <param name="pid">Service 1内のPID番号。</param>
    /// <param name="requiredByteCount">数式に必要なbytes数。</param>
    /// <param name="formula">評価する制限付き式。</param>
    /// <returns>0...100の明示rangeを持つ定義。</returns>
    private static OBDPIDDefinition MakeDefinition(byte pid = 12, int requiredByteCount = 1, string formula = "A") =>
        new(
            service: 1,
            pid: pid,
            nameKey: "test.pid",
            requiredByteCount: requiredByteCount,
            formula: formula,
            unit: "unit",
            minimumValue: 0,
            maximumValue: 100,
            sourceURI: "test://pid",
            revision: 1);

    /// <summary>test用完全manifestを生成します。</summary>
    /// <remarks>責務: 入力定義順をordered PID集合と完全snapshotへ固定します。</remarks>
    /// <param name="definitions">manifestへ含める取得定義。</param>
    /// <returns>Application batch入力に使用できる完全manifest。</returns>
    private static ConnectionSessionAcquisitionManifest MakeManifest(IReadOnlyList<OBDPIDDefinition> definitions)
    {
        var requests = definitions.Select(definition => new OBDPIDRequest(definition.Service, definition.Pid)).ToArray();
        var snapshots = definitions.Select(definition => new AcquisitionPIDDefinitionSnapshot(
            request: new OBDPIDRequest(definition.Service, definition.Pid),
            capabilitySupport: AcquisitionPIDCapabilitySupport.Supported,
            isCollectionEnabled: true,
            definitionRevision: definition.Revision,
            requiredByteCount: definition.RequiredByteCount,
            definitionIdentity: new AcquisitionPIDDefinitionIdentity(1, definition.Formula!),
            unit: definition.Unit,
            validityRange: AcquisitionPIDValidityRange.Inclusive(definition.MinimumValue!.Value, definition.MaximumValue!.Value)))
            .ToArray();
        return new ConnectionSessionAcquisitionManifest(
            manifestVersion: 1,
            applicationVersion: new AcquisitionApplicationVersion("1", "1"),
            schemaContractVersion: 1,
            pollingPolicyVersion: 1,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(requests),
            pidDefinitions: snapshots,
            acquisitionPlatform: ConnectionSessionAcquisitionPlatform.MacOS,
            modelInputManifestVersion: 1);
    }

    /// <summary>inputと同じselected-only open batchを生成します。</summary>
    /// <remarks>責務: termination recovery testへProduction use caseと同形のopen evidenceを渡します。</remarks>
    /// <param name="input">manifest順定義を持つbatch入力。</param>
    /// <returns>policy評価完了済みopen batch。</returns>
    private static AcquisitionBatchEvidence OpenBatch(PersistAcquisitionBatchInput input) =>
        new(
            identity: input.BatchIdentity,
            generation: input.Permission.Generation,
            policyTick: input.PolicyTick,
            isSelectionEvaluationComplete: true,
            startedAt: input.StartedAt,
            completionState: null,
            completedAt: null,
            failure: null,
            requests: input.Definitions.Select((_, ordinal) => new PIDRequestEvidence(
                requestOrdinal: ordinal,
                manifestPIDOrdinal: ordinal,
                dispatchState: PIDRequestDispatchState.SelectedOnly,
                transportOutcome: null,
                valueOutcome: PIDRequestValueOutcome.NotEvaluated,
                rawSequence: null,
                elapsedNanoseconds: null,
                reasonCode: null)).ToArray());

    /// <summary>Application batch testが共有する依存と入力です。</summary>
    /// <param name="Repository">記録内容を検査するfake repositoryです。</param>
    /// <param name="Telemetry">typed Transport呼出しを検査するfakeです。</param>
    /// <param name="UseCase">検証対象のApplication use caseです。</param>
    /// <param name="Input">1 batch分の確定済み入力です。</param>
    private sealed record AcquisitionUseCaseFixture(
        AcquisitionBatchRepositoryFake Repository,
        AcquisitionTelemetryFake Telemetry,
        PersistAcquisitionBatchUseCase UseCase,
        PersistAcquisitionBatchInput Input);

    /// <summary>fake repositoryが失敗を注入できる永続化段階です。</summary>
    private enum AcquisitionRepositoryFailurePoint
    {
        /// <summary>open batch保存段階です。</summary>
        BeginBatch,

        /// <summary>responded Raw原子保存段階です。</summary>
        Responded,
    }

    /// <summary>呼出し順を記録するeventです。</summary>
    private abstract record RepositoryEvent
    {
        /// <summary>open batch保存です。</summary>
        public sealed record BeginBatch : RepositoryEvent;

        /// <summary>request dispatch開始です。</summary>
        public sealed record Dispatch(int Ordinal) : RepositoryEvent;

        /// <summary>responded原子保存です。</summary>
        public sealed record Responded(int Ordinal) : RepositoryEvent;

        /// <summary>nonresponded terminal保存です。</summary>
        public sealed record NonResponded(int Ordinal) : RepositoryEvent;

        /// <summary>batch terminal確定です。</summary>
        public sealed record FinishBatch : RepositoryEvent;
    }

    /// <summary>Application orchestration順とcanonical evidenceを記録するfake repositoryです。</summary>
    private sealed class AcquisitionBatchRepositoryFake : IConnectionSessionAcquisitionBatchRepository
    {
        /// <summary>現在のbatch evidence列です。</summary>
        private List<AcquisitionBatchEvidence> _storedBatches = [];

        /// <summary>注入する任意の失敗段階です。</summary>
        public AcquisitionRepositoryFailurePoint? FailurePoint { get; set; }

        /// <summary>永続化呼出し順です。</summary>
        public List<RepositoryEvent> Events { get; } = [];

        /// <summary>responded保存へ渡されたRaw observationです。</summary>
        public List<OBDRawResponseObservation> RespondedObservations { get; } = [];

        /// <summary>open batchをfake永続状態へ保存します。</summary>
        /// <remarks>責務: begin呼出しを記録して任意のmetadata失敗を注入します。</remarks>
        /// <param name="evidence">保存するopen batch。</param>
        /// <param name="sessionID">使用しない親session。</param>
        public void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID)
        {
            Events.Add(new RepositoryEvent.BeginBatch());
            if (FailurePoint == AcquisitionRepositoryFailurePoint.BeginBatch)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            _storedBatches = [evidence];
        }

        /// <summary>requestをdispatch開始へ遷移させます。</summary>
        /// <remarks>責務: fake batch内の指定requestをdispatch開始済みとして記録します。</remarks>
        /// <param name="requestOrdinal">batch内要求順。</param>
        /// <param name="batchIdentity">使用しない親batch identity。</param>
        /// <param name="sessionID">使用しない親session。</param>
        public void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID)
        {
            Events.Add(new RepositoryEvent.Dispatch(requestOrdinal));
            ReplaceRequest(requestOrdinal, stored => new PIDRequestEvidence(
                requestOrdinal: stored.RequestOrdinal,
                manifestPIDOrdinal: stored.ManifestPIDOrdinal,
                dispatchState: PIDRequestDispatchState.DispatchBegun,
                transportOutcome: null,
                valueOutcome: PIDRequestValueOutcome.NotEvaluated,
                rawSequence: null,
                elapsedNanoseconds: null,
                reasonCode: null));
        }

        /// <summary>responded Rawとrequest terminalをfake保存します。</summary>
        /// <remarks>責務: Raw入力を記録して連続sequence付きcanonical requestを返します。</remarks>
        /// <returns>fake採番済みrequest evidence。</returns>
        public PIDRequestEvidence SaveRespondedRequest(
            OBDRawResponseObservation observation,
            PIDRequestValueOutcome valueOutcome,
            ulong elapsedNanoseconds,
            string? reasonCode,
            int requestOrdinal,
            AcquisitionBatchIdentity batchIdentity,
            ConnectionSessionID sessionID)
        {
            Events.Add(new RepositoryEvent.Responded(requestOrdinal));
            if (FailurePoint == AcquisitionRepositoryFailurePoint.Responded)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            var sequence = (long)RespondedObservations.Count;
            RespondedObservations.Add(observation);
            return ReplaceRequest(requestOrdinal, stored => new PIDRequestEvidence(
                requestOrdinal: stored.RequestOrdinal,
                manifestPIDOrdinal: stored.ManifestPIDOrdinal,
                dispatchState: PIDRequestDispatchState.Terminal,
                transportOutcome: PIDRequestTransportOutcome.Responded,
                valueOutcome: valueOutcome,
                rawSequence: sequence,
                elapsedNanoseconds: elapsedNanoseconds,
                reasonCode: reasonCode));
        }

        /// <summary>nonresponded request terminalをfake保存します。</summary>
        /// <remarks>責務: Rawを生成せず指定outcomeのcanonical requestを返します。</remarks>
        /// <returns>fake terminal request evidence。</returns>
        public PIDRequestEvidence SaveNonRespondedRequest(
            PIDRequestTransportOutcome outcome,
            ulong? elapsedNanoseconds,
            string? reasonCode,
            int requestOrdinal,
            AcquisitionBatchIdentity batchIdentity,
            ConnectionSessionID sessionID)
        {
            Events.Add(new RepositoryEvent.NonResponded(requestOrdinal));
            return ReplaceRequest(requestOrdinal, stored => new PIDRequestEvidence(
                requestOrdinal: stored.RequestOrdinal,
                manifestPIDOrdinal: stored.ManifestPIDOrdinal,
                dispatchState: PIDRequestDispatchState.Terminal,
                transportOutcome: outcome,
                valueOutcome: PIDRequestValueOutcome.NotEvaluated,
                rawSequence: null,
                elapsedNanoseconds: elapsedNanoseconds,
                reasonCode: reasonCode));
        }

        /// <summary>terminal batchをfake保存します。</summary>
        /// <remarks>責務: Applicationが確定したpartialを含むterminal aggregateを記録します。</remarks>
        /// <param name="evidence">保存するterminal batch。</param>
        /// <param name="sessionID">使用しない親session。</param>
        public void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID)
        {
            Events.Add(new RepositoryEvent.FinishBatch());
            _storedBatches = [evidence];
        }

        /// <summary>現在のfake batch列を返します。</summary>
        /// <remarks>責務: termination回復へ保存済みopen batchを提供します。</remarks>
        /// <param name="sessionID">使用しない親session。</param>
        /// <returns>fake保存中のbatch列。</returns>
        public IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID) => _storedBatches.ToArray();

        /// <summary>指定requestだけを置換してopen batchを再生成します。</summary>
        /// <remarks>責務: fakeのrequest遷移をDomain aggregate不変条件へ反映します。</remarks>
        /// <param name="ordinal">置換するrequest順。</param>
        /// <param name="transform">現在requestを次状態へ変換する処理。</param>
        /// <returns>置換後のrequest。</returns>
        private PIDRequestEvidence ReplaceRequest(int ordinal, Func<PIDRequestEvidence, PIDRequestEvidence> transform)
        {
            var batch = _storedBatches.FirstOrDefault()
                ?? throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
            var requests = batch.Requests.ToArray();
            requests[ordinal] = transform(requests[ordinal]);
            _storedBatches =
            [
                new AcquisitionBatchEvidence(
                    identity: batch.Identity,
                    generation: batch.Generation,
                    policyTick: batch.PolicyTick,
                    isSelectionEvaluationComplete: batch.IsSelectionEvaluationComplete,
                    startedAt: batch.StartedAt,
                    completionState: null,
                    completedAt: null,
                    failure: null,
                    requests: requests),
            ];
            return requests[ordinal];
        }
    }

    /// <summary>request順にtyped Transport結果を返すfake telemetryです。</summary>
    /// <param name="outcomes">request順に返すtyped結果。</param>
    private sealed class AcquisitionTelemetryFake(IReadOnlyList<OBDPIDRequestTransportOutcome> outcomes) : IOBDPIDTelemetryPort
    {
        /// <summary>状態を保護するlockです。</summary>
        private readonly Lock _lock = new();

        /// <summary>未返却のtyped outcome列です。</summary>
        private readonly Queue<OBDPIDRequestTransportOutcome> _outcomes = new(outcomes);

        /// <summary>PID単位読取回数です。</summary>
        private int _readCount;

        /// <summary>PID単位読取回数です。</summary>
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

        /// <summary>1要求へ次のtyped outcomeを返します。</summary>
        /// <remarks>責務: 要求と次のfake outcomeを1件のtyped観測へ対応付けます。</remarks>
        /// <param name="requests">読み取る要求。</param>
        /// <param name="endpoint">使用しないOBD終端。</param>
        /// <returns>入力要求に対応する0件または1件の観測。</returns>
        public Task<IReadOnlyList<OBDPIDRequestTransportObservation>> ReadObservations(
            IReadOnlyList<OBDPIDRequest> requests,
            OBDConnectionEndpoint endpoint)
        {
            lock (_lock)
            {
                _readCount += 1;
                if (requests.Count == 0 || _outcomes.Count == 0)
                {
                    return Task.FromResult<IReadOnlyList<OBDPIDRequestTransportObservation>>([]);
                }
                return Task.FromResult<IReadOnlyList<OBDPIDRequestTransportObservation>>(
                    [new OBDPIDRequestTransportObservation(requests[0], _outcomes.Dequeue())]);
            }
        }

        /// <summary>未使用の辞書型readを空応答として実装します。</summary>
        /// <remarks>責務: protocol互換要件を満たしtyped読取testへ影響しない空辞書を返します。</remarks>
        /// <param name="requests">使用しない要求。</param>
        /// <param name="endpoint">使用しないOBD終端。</param>
        /// <returns>常に空の応答辞書。</returns>
        public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
            IReadOnlyList<OBDPIDRequest> requests,
            OBDConnectionEndpoint endpoint) =>
            Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
                new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>());
    }

    /// <summary>deterministicな単調時刻列を供給します。</summary>
    /// <param name="values">呼出し順に返す値。</param>
    private sealed class MonotonicSequence(IEnumerable<ulong> values)
    {
        /// <summary>未返却の単調値です。</summary>
        private readonly Queue<ulong> _values = new(values);

        /// <summary>次の単調値を返します。</summary>
        /// <remarks>責務: 値列の先頭を1回だけ消費します。</remarks>
        /// <returns>次の値、または枯渇時の0。</returns>
        public ulong Next() => _values.Count == 0 ? 0 : _values.Dequeue();
    }
}
