using System.Diagnostics;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging.UseCases;

/// <summary>Production LiveTelemetry loopをmanifestおよび方式B batch保存へ接続します。</summary>
/// <remarks>Swift では actor でした。共有状態は lock で守り、await をまたいでロックしません。</remarks>
public sealed class ConnectionSessionAcquisitionController : ILiveTelemetryAcquisitionEvidencePort
{
    /// <summary>manifestと開始境界を原子的に保存するuse caseです。</summary>
    private readonly CreateConnectionSessionAcquisitionManifestUseCase _createManifest;

    /// <summary>typed観測を方式B batchとして保存するuse caseです。</summary>
    private readonly PersistAcquisitionBatchUseCase _persistBatch;

    /// <summary>現在Logging sessionの識別子を返す境界です。</summary>
    private readonly Func<Task<ConnectionSessionID?>> _activeSessionID;

    /// <summary>manifest構造versionです。</summary>
    private readonly int _manifestVersion;

    /// <summary>polling policy契約versionです。</summary>
    private readonly int _pollingPolicyVersion;

    /// <summary>model入力変換契約versionです。</summary>
    private readonly int _modelInputManifestVersion;

    /// <summary>formula canonicalization契約versionです。</summary>
    private readonly int _formulaCanonicalizationVersion;

    /// <summary>batch全体の匿名性能event通知先です。</summary>
    private readonly IAcquisitionPerformanceEventPort _performanceEvents;

    /// <summary>actor の直列化を表す同期区間のロックです。</summary>
    private readonly Lock _gate = new();

    /// <summary>現在世代へ保存済みのRaw取得許可です。</summary>
    private ConnectionSessionRawAcquisitionPermission? _permission;

    /// <summary>現在の取得許可の接続セッション ID（診断ログの <c>sid</c>）です。</summary>
    private string? _diagnosticSessionId;

    /// <summary>Production取得証拠のuse case、session境界、version authorityを固定します。</summary>
    /// <remarks>責務: LiveTelemetry portを1組のLogging取得証拠use caseへ結び付けます。</remarks>
    /// <param name="createManifest">manifestと開始境界を原子的に保存するuse case。</param>
    /// <param name="persistBatch">typed観測を方式B batchとして保存するuse case。</param>
    /// <param name="activeSessionID">現在Logging sessionの識別子を返す境界。</param>
    /// <param name="manifestVersion">manifest構造version。</param>
    /// <param name="pollingPolicyVersion">polling policy契約version。</param>
    /// <param name="modelInputManifestVersion">model入力変換契約version。</param>
    /// <param name="formulaCanonicalizationVersion">formula canonicalization契約version。</param>
    /// <param name="performanceEvents">batch全体の匿名性能event通知先。<c>null</c> なら何もしない実装。</param>
    public ConnectionSessionAcquisitionController(
        CreateConnectionSessionAcquisitionManifestUseCase createManifest,
        PersistAcquisitionBatchUseCase persistBatch,
        Func<Task<ConnectionSessionID?>> activeSessionID,
        int manifestVersion,
        int pollingPolicyVersion,
        int modelInputManifestVersion,
        int formulaCanonicalizationVersion,
        IAcquisitionPerformanceEventPort? performanceEvents = null)
    {
        _createManifest = createManifest;
        _persistBatch = persistBatch;
        _activeSessionID = activeSessionID;
        _manifestVersion = manifestVersion;
        _pollingPolicyVersion = pollingPolicyVersion;
        _modelInputManifestVersion = modelInputManifestVersion;
        _formulaCanonicalizationVersion = formulaCanonicalizationVersion;
        _performanceEvents = performanceEvents ?? new NoOpAcquisitionPerformanceEventPort();
        _permission = null;
    }

    /// <summary>
    /// 最後に取得を許可した接続セッションの ID（診断ログの <c>sid</c>）です。まだなければ null。
    /// </summary>
    /// <remarks>取得のループが、ループ全体に 1 回だけ <c>sid</c> を付けるために読みます（バッチごとに割り当てを増やさないため）。</remarks>
    internal string? DiagnosticSessionId
    {
        get
        {
            lock (_gate)
            {
                return _diagnosticSessionId;
            }
        }
    }

    /// <summary>
    /// 今の接続セッションの ID（診断ログの <c>sid</c>）を返します。セッションがなければ null。
    /// </summary>
    /// <remarks>責務: 取得を始めたことを書く時点（証拠の保存より前）で、その取得の <c>sid</c> を得ます。</remarks>
    /// <returns><c>sid</c>。接続セッションがなければ null。</returns>
    internal async Task<string?> ResolveDiagnosticSessionId() =>
        await _activeSessionID() is { } sessionID ? AcquisitionLog.SessionId(sessionID) : null;

    /// <summary>manifestと開始境界を保存し、成功した世代だけを取得可能にします。</summary>
    /// <remarks>責務: 1世代の確定済み取得入力を保存済みRaw取得許可へ変換します。</remarks>
    /// <param name="input">定義、capability、policy順を含む取得開始入力。</param>
    /// <exception cref="Exception">session不在、入力不正、stale世代、または保存失敗。</exception>
    public async Task Start(LiveTelemetryAcquisitionStartInput input)
    {
        var interval = _performanceEvents.Begin(
            AcquisitionPerformanceOperation.AcquisitionStart,
            new AcquisitionPerformanceContext(generation: input.Generation));
        if (await _activeSessionID() is not { } sessionID)
        {
            AcquisitionLog.EvidenceNoSession(AcquisitionLog.Logger, input.Generation);
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        var diagnosticSessionId = AcquisitionLog.SessionId(sessionID);
        using var sessionScope = DiagnosticLog.BeginConnectionSession(diagnosticSessionId);
        var started = Stopwatch.GetTimestamp();
        _createManifest.Activate(input.Generation);
        _persistBatch.Activate(input.Generation);
        try
        {
            var permission = await _createManifest.Execute(
                new CreateConnectionSessionAcquisitionManifestInput(
                    sessionID: sessionID,
                    generation: input.Generation,
                    manifestVersion: _manifestVersion,
                    pollingPolicyVersion: _pollingPolicyVersion,
                    modelInputManifestVersion: _modelInputManifestVersion,
                    formulaCanonicalizationVersion: _formulaCanonicalizationVersion,
                    pidDefinitions: input.Definitions,
                    pidCapabilities: input.Capabilities,
                    orderedRequestedPIDs: input.OrderedRequests));
            lock (_gate)
            {
                _permission = permission;
                _diagnosticSessionId = diagnosticSessionId;
            }
            AcquisitionLog.EvidenceStarted(
                AcquisitionLog.Logger,
                input.Generation,
                input.OrderedRequests.Count,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Succeeded);
        }
        catch (Exception)
        {
            _persistBatch.Cancel(input.Generation);
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
            throw;
        }
    }

    /// <summary>1回のpolling tickを方式Bで取得・保存して表示用sampleを返します。</summary>
    /// <remarks>責務: 1 batchの物理観測を原子保存済みRaw証拠と表示用sampleへ変換します。</remarks>
    /// <param name="input">stable ordinalとpolicy tickを持つbatch入力。</param>
    /// <returns>同じ物理応答から生成した表示用sample。</returns>
    /// <exception cref="Exception">start未完了、stale世代、入力不正、または永続化失敗。</exception>
    public async Task<IReadOnlyList<OBDPIDSample>> Acquire(LiveTelemetryAcquisitionBatchInput input)
    {
        ConnectionSessionRawAcquisitionPermission? permission;
        string? diagnosticSessionId;
        lock (_gate)
        {
            permission = _permission;
            diagnosticSessionId = _diagnosticSessionId;
        }
        if (permission is null || permission.Generation != input.Generation)
        {
            var staleInterval = _performanceEvents.Begin(
                AcquisitionPerformanceOperation.StaleBatchRejection,
                new AcquisitionPerformanceContext(
                    generation: input.Generation,
                    batchOrdinal: input.BatchOrdinal,
                    policyTick: input.PolicyTick,
                    scheduleDelayNanoseconds: input.ScheduleDelayNanoseconds));
            _performanceEvents.End(staleInterval, AcquisitionPerformanceOutcome.Failed);
            AcquisitionLog.BatchDropped(AcquisitionLog.Logger, input.Generation, input.BatchOrdinal, "stale-generation");
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InactiveGeneration);
        }
        var interval = _performanceEvents.Begin(
            AcquisitionPerformanceOperation.BatchAcquisition,
            new AcquisitionPerformanceContext(
                generation: input.Generation,
                batchOrdinal: input.BatchOrdinal,
                policyTick: input.PolicyTick,
                scheduleDelayNanoseconds: input.ScheduleDelayNanoseconds));
        try
        {
            // 取得のループが sid を付けていればそのまま使う（バッチごとに割り当てを増やさない）。
            using var sessionScope = DiagnosticLog.CurrentSessionId is null
                ? DiagnosticLog.BeginConnectionSession(diagnosticSessionId)
                : null;
            var result = await _persistBatch.ExecuteWithSamples(
                new PersistAcquisitionBatchInput(
                    permission: permission,
                    batchIdentity: new AcquisitionBatchIdentity(input.BatchOrdinal),
                    policyTick: input.PolicyTick,
                    startedAt: input.StartedAt,
                    definitions: input.Definitions,
                    endpoint: input.Endpoint));
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Succeeded);
            return result.Samples;
        }
        catch (PersistAcquisitionBatchException error) when (error.Reason == PersistAcquisitionBatchError.InactiveGeneration)
        {
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Cancelled);
            AcquisitionLog.BatchDropped(AcquisitionLog.Logger, input.Generation, input.BatchOrdinal, "inactive-generation");
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InactiveGeneration);
        }
        catch (Exception)
        {
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
            throw;
        }
    }

    /// <summary>指定世代からの新しい証拠作成を停止します。</summary>
    /// <remarks>責務: 1世代のmanifestとbatch orchestrationをstale状態へ遷移させます。</remarks>
    /// <param name="generation">停止する取得世代。</param>
    /// <returns>停止処理の完了。</returns>
    public Task Cancel(ulong generation)
    {
        var interval = _performanceEvents.Begin(
            AcquisitionPerformanceOperation.GenerationCancellation,
            new AcquisitionPerformanceContext(generation: generation));
        string? diagnosticSessionId;
        lock (_gate)
        {
            diagnosticSessionId = _permission?.Generation == generation ? _diagnosticSessionId : null;
        }
        // たまっているバッチの合計と世代の取消に、この世代の sid を付ける（呼び出し元が付けていればそのまま使う）。
        using var sessionScope = diagnosticSessionId is not null && DiagnosticLog.CurrentSessionId is null
            ? DiagnosticLog.BeginConnectionSession(diagnosticSessionId)
            : null;
        _createManifest.Cancel(generation);
        _persistBatch.Cancel(generation);
        lock (_gate)
        {
            if (_permission?.Generation == generation)
            {
                _permission = null;
            }
        }
        AcquisitionLog.GenerationCancelled(AcquisitionLog.Logger, generation);
        _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Succeeded);
        return Task.CompletedTask;
    }
}
