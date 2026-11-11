using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging.UseCases;

/// <summary>1回の取得batchを実行するために確定済みのApplication入力です。</summary>
public sealed class PersistAcquisitionBatchInput
{
    /// <summary>manifestとRaw開始境界の保存済み許可です。</summary>
    public ConnectionSessionRawAcquisitionPermission Permission { get; }

    /// <summary>session内のbatch identityです。</summary>
    public AcquisitionBatchIdentity BatchIdentity { get; }

    /// <summary>policyが評価した単調tickです。</summary>
    public ulong PolicyTick { get; }

    /// <summary>batch開始を観測した実時間です。</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>manifest要求順と一致する取得時PID定義です。</summary>
    public IReadOnlyList<OBDPIDDefinition> Definitions { get; }

    /// <summary>物理取得に使用するOBD終端です。</summary>
    public OBDConnectionEndpoint Endpoint { get; }

    /// <summary>batch取得に必要な保存済み許可、定義、物理終端を固定します。</summary>
    /// <remarks>責務: 1回の取得batchへ必要な確定済み入力を推測なしでまとめます。</remarks>
    /// <param name="permission">manifestとRaw開始境界の保存済み許可。</param>
    /// <param name="batchIdentity">session内のbatch identity。</param>
    /// <param name="policyTick">policyが評価した単調tick。</param>
    /// <param name="startedAt">batch開始を観測した実時間。</param>
    /// <param name="definitions">manifest要求順と一致する取得時PID定義。</param>
    /// <param name="endpoint">物理取得に使用するOBD終端。</param>
    public PersistAcquisitionBatchInput(
        ConnectionSessionRawAcquisitionPermission permission,
        AcquisitionBatchIdentity batchIdentity,
        ulong policyTick,
        DateTimeOffset startedAt,
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint)
    {
        Permission = permission;
        BatchIdentity = batchIdentity;
        PolicyTick = policyTick;
        StartedAt = startedAt;
        Definitions = definitions.ToArray();
        Endpoint = endpoint;
    }
}

/// <summary>batch取得を安全に完了できなかったApplication上の理由です。</summary>
public enum PersistAcquisitionBatchError
{
    /// <summary>要求が現在のpolling世代に属していません。</summary>
    InactiveGeneration,

    /// <summary>manifest要求順と取得定義が一致しません。</summary>
    InvalidInput,

    /// <summary>metadataまたはRawの永続化に失敗しました。</summary>
    PersistenceFailure,
}

/// <summary><see cref="PersistAcquisitionBatchError"/> を理由として持つ例外です。</summary>
public sealed class PersistAcquisitionBatchException : Exception
{
    /// <summary>batch取得を完了できなかった理由です。</summary>
    public PersistAcquisitionBatchError Reason { get; }

    /// <summary>理由を指定して例外を生成します。</summary>
    /// <remarks>責務: batch取得の失敗理由を1件の例外へ包みます。</remarks>
    /// <param name="reason">batch取得を完了できなかった理由。</param>
    public PersistAcquisitionBatchException(PersistAcquisitionBatchError reason)
        : base($"{nameof(PersistAcquisitionBatchError)}.{reason}")
    {
        Reason = reason;
    }
}

/// <summary>方式Bで確定したbatch証拠と同じ物理応答から生成した表示用sampleです。</summary>
public sealed class PersistedAcquisitionBatchResult
{
    /// <summary>repositoryからcanonical readbackされたbatch証拠です。</summary>
    public AcquisitionBatchEvidence Evidence { get; }

    /// <summary>今回新しく受信した正応答から生成した表示用sampleです。</summary>
    public IReadOnlyList<OBDPIDSample> Samples { get; }

    /// <summary>永続証拠と表示用sampleを対応付けます。</summary>
    /// <remarks>責務: 1 batchの永続結果と再送不要の表示結果を同じ戻り値へまとめます。</remarks>
    /// <param name="evidence">repositoryからcanonical readbackされたbatch証拠。</param>
    /// <param name="samples">今回新しく受信した正応答から生成した表示用sample。</param>
    public PersistedAcquisitionBatchResult(AcquisitionBatchEvidence evidence, IReadOnlyList<OBDPIDSample> samples)
    {
        Evidence = evidence;
        Samples = samples.ToArray();
    }
}

/// <summary>PID単位のtyped Transport観測をRawとrequest evidenceへ順序付きで保存します。</summary>
/// <remarks>Swift では actor でした。同期区間の共有状態と repository 操作は lock で直列化し、await をまたいでロックしません。</remarks>
public sealed class PersistAcquisitionBatchUseCase
{
    /// <summary>batch、request、Raw参照を保存するDomain repositoryです。</summary>
    private readonly IConnectionSessionAcquisitionBatchRepository _repository;

    /// <summary>PID単位のtyped Transport観測を返す取得境界です。</summary>
    private readonly IOBDPIDTelemetryPort _telemetry;

    /// <summary>取得時定義の制限付き数式を評価します。</summary>
    private readonly OBDPIDFormulaEvaluator _evaluator;

    /// <summary>Raw観測日時とbatch完了日時を供給します。</summary>
    private readonly Func<DateTimeOffset> _now;

    /// <summary>request単位の単調経過時間を供給します。</summary>
    private readonly Func<ulong> _monotonicNanoseconds;

    /// <summary>request transport区間を通知する匿名計測境界です。</summary>
    private readonly IAcquisitionPerformanceEventPort _performanceEvents;

    /// <summary>actor の直列化を表す同期区間のロックです。</summary>
    private readonly Lock _gate = new();

    /// <summary>今の世代で数値化の失敗を診断ログに書いた PID です（同じ PID をバッチごとに書かないため。<see cref="_gate"/> で守る）。</summary>
    private readonly HashSet<OBDPIDRequest> _decodeFailureLogged = [];

    /// <summary>今の世代の最初のバッチをまだ書いていないか（最初の 1 件は必ず 1 行書く。<see cref="_gate"/> で守る）。</summary>
    private bool _firstBatchPending = true;

    /// <summary>まだ書いていない正常なバッチの数です（<see cref="_gate"/> で守る。以下 <c>_pending*</c> も同じ）。</summary>
    private int _pendingBatches;

    /// <summary>まだ書いていない正常なバッチのうち、最初の番号です。</summary>
    private long _pendingFirstBatch;

    /// <summary>まだ書いていない正常なバッチのうち、最後の番号です。</summary>
    private long _pendingLastBatch;

    /// <summary>まだ書いていない正常なバッチの要求の合計です。</summary>
    private long _pendingRequests;

    /// <summary>まだ書いていない正常なバッチの応答の合計です。</summary>
    private long _pendingResponded;

    /// <summary>まだ書いていない正常なバッチの数値の合計です。</summary>
    private long _pendingSamples;

    /// <summary>まだ書いていない正常なバッチの所要時間の合計（ミリ秒）です。</summary>
    private long _pendingElapsedTotalMs;

    /// <summary>まだ書いていない正常なバッチの所要時間の最大（ミリ秒）です。</summary>
    private long _pendingElapsedMaxMs;

    /// <summary>
    /// 完了したバッチを診断ログに書くか、正常なバッチならまとめに足します。
    /// </summary>
    /// <remarks>
    /// 責務: 世代の最初のバッチと異常のあるバッチはその場で 1 行書き（その前にたまった正常分を先に書く）、
    /// それ以外は <see cref="AcquisitionLog.NormalBatchSummaryInterval"/> 件ごとに 1 行へまとめます。
    /// 呼び出し側は <see cref="_gate"/> を持っていること。割り当てはしません。
    /// </remarks>
    /// <param name="batch">バッチの番号。</param>
    /// <param name="tick">周期の番号。</param>
    /// <param name="requests">要求した件数。</param>
    /// <param name="counts">結果の種類ごとの件数。</param>
    /// <param name="samples">数値にできた件数。</param>
    /// <param name="elapsedMs">バッチの所要時間（ミリ秒）。</param>
    private void RecordCompletedBatch(
        long batch,
        ulong tick,
        int requests,
        in BatchOutcomeCounts counts,
        int samples,
        long elapsedMs)
    {
        var isNormal = counts.TimedOut == 0
            && counts.TransportFailed == 0
            && counts.Unclassified == 0
            && counts.Cancelled == 0;
        if (isNormal && !_firstBatchPending)
        {
            if (_pendingBatches == 0)
            {
                _pendingFirstBatch = batch;
            }

            _pendingBatches++;
            _pendingLastBatch = batch;
            _pendingRequests += requests;
            _pendingResponded += counts.Responded;
            _pendingSamples += samples;
            _pendingElapsedTotalMs += elapsedMs;
            _pendingElapsedMaxMs = Math.Max(_pendingElapsedMaxMs, elapsedMs);
            if (_pendingBatches >= AcquisitionLog.NormalBatchSummaryInterval)
            {
                FlushBatchSummary();
            }

            return;
        }

        FlushBatchSummary();
        _firstBatchPending = false;
        AcquisitionLog.BatchCompleted(
            AcquisitionLog.Logger,
            batch,
            tick,
            requests,
            counts.Responded,
            counts.TimedOut,
            counts.TransportFailed,
            counts.Unclassified,
            counts.Cancelled,
            samples,
            elapsedMs);
    }

    /// <summary>たまっている正常なバッチの合計を 1 行書いて、たまりを空にします（なければ何もしない）。</summary>
    /// <remarks>責務: 呼び出し側は <see cref="_gate"/> を持っていること。</remarks>
    private void FlushBatchSummary()
    {
        if (_pendingBatches == 0)
        {
            return;
        }

        AcquisitionLog.BatchSummary(
            AcquisitionLog.Logger,
            _pendingFirstBatch,
            _pendingLastBatch,
            _pendingBatches,
            _pendingRequests,
            _pendingResponded,
            _pendingSamples,
            _pendingElapsedTotalMs,
            _pendingElapsedMaxMs);
        _pendingBatches = 0;
        _pendingRequests = 0;
        _pendingResponded = 0;
        _pendingSamples = 0;
        _pendingElapsedTotalMs = 0;
        _pendingElapsedMaxMs = 0;
    }

    /// <summary>現在の取得workflowが有効としたpolling世代です。</summary>
    private ulong? _activeGeneration;

    /// <summary>repository、typed Transport、評価器、clockを固定します。</summary>
    /// <remarks>責務: 1 batch取得証拠workflowの副作用境界を注入します。</remarks>
    /// <param name="repository">batch、request、Raw参照を保存するDomain repository。</param>
    /// <param name="telemetry">PID単位のtyped Transport観測を返す取得境界。</param>
    /// <param name="monotonicNanoseconds">request単位の単調clock。</param>
    /// <param name="evaluator">取得時PID定義の数式評価器。<c>null</c> なら新しい評価器。</param>
    /// <param name="now">Raw観測日時とbatch完了日時の供給元。<c>null</c> なら現在時刻。</param>
    /// <param name="performanceEvents">request transport区間を通知する匿名計測境界。<c>null</c> なら何もしない実装。</param>
    public PersistAcquisitionBatchUseCase(
        IConnectionSessionAcquisitionBatchRepository repository,
        IOBDPIDTelemetryPort telemetry,
        Func<ulong> monotonicNanoseconds,
        OBDPIDFormulaEvaluator? evaluator = null,
        Func<DateTimeOffset>? now = null,
        IAcquisitionPerformanceEventPort? performanceEvents = null)
    {
        _repository = repository;
        _telemetry = telemetry;
        _evaluator = evaluator ?? new OBDPIDFormulaEvaluator();
        _now = now ?? (() => DateTimeOffset.Now);
        _monotonicNanoseconds = monotonicNanoseconds;
        _performanceEvents = performanceEvents ?? new NoOpAcquisitionPerformanceEventPort();
        _activeGeneration = null;
    }

    /// <summary>新しいpolling世代だけをbatch取得対象として有効化します。</summary>
    /// <remarks>責務: 後続のbatch結果を照合する現在世代を置き換えます。</remarks>
    /// <param name="generation">有効化するpolling世代。</param>
    public void Activate(ulong generation)
    {
        lock (_gate)
        {
            FlushBatchSummary();
            _activeGeneration = generation;
            _decodeFailureLogged.Clear();
            _firstBatchPending = true;
        }
    }

    /// <summary>指定polling世代の後続永続化を無効化します。</summary>
    /// <remarks>責務: 現在世代と一致するbatch workflowだけをstale状態へ遷移させます。</remarks>
    /// <param name="generation">無効化するpolling世代。</param>
    public void Cancel(ulong generation)
    {
        lock (_gate)
        {
            if (_activeGeneration != generation)
            {
                return;
            }
            // 止まる前に、たまっている正常なバッチの合計を書く（acq.stopped より前になる）。
            FlushBatchSummary();
            _activeGeneration = null;
        }
    }

    /// <summary>1 batchをdispatch開始、typed観測、request保存、batch確定の順で実行します。</summary>
    /// <remarks>責務: 現在世代のPID要求列をpartial失敗を保持する永続取得証拠へ変換します。</remarks>
    /// <param name="input">保存済みmanifest許可と要求順定義を持つbatch入力。</param>
    /// <returns>repositoryからcanonical readbackされたterminal batch証拠。</returns>
    /// <exception cref="PersistAcquisitionBatchException">stale世代、入力不一致、metadataまたはRaw保存失敗。</exception>
    public async Task<AcquisitionBatchEvidence> Execute(PersistAcquisitionBatchInput input) =>
        (await ExecuteWithSamples(input)).Evidence;

    /// <summary>1 batchを方式Bで保存し、同じ正応答から表示用sampleも生成します。</summary>
    /// <remarks>責務: 現在世代のPID要求列を永続取得証拠と再送不要の表示用sampleへ変換します。</remarks>
    /// <param name="input">保存済みmanifest許可と要求順定義を持つbatch入力。</param>
    /// <returns>canonical batch証拠と今回新しく受信した表示用sample。</returns>
    /// <exception cref="PersistAcquisitionBatchException">stale世代、入力不一致、metadataまたはRaw保存失敗。</exception>
    public async Task<PersistedAcquisitionBatchResult> ExecuteWithSamples(PersistAcquisitionBatchInput input)
    {
        var batchStarted = Stopwatch.GetTimestamp();
        var counts = default(BatchOutcomeCounts);
        try
        {
            var generation = input.Permission.Generation;
            var sessionID = input.Permission.SessionID;
            IReadOnlyList<(int ManifestPIDOrdinal, OBDPIDDefinition Definition)> requests;
            AcquisitionBatchEvidence resumed;
            lock (_gate)
            {
                EnsureCurrent(generation);
                requests = ValidatedRequests(input);
                var open = new AcquisitionBatchEvidence(
                    identity: input.BatchIdentity,
                    generation: generation,
                    policyTick: input.PolicyTick,
                    isSelectionEvaluationComplete: true,
                    startedAt: input.StartedAt,
                    completionState: null,
                    completedAt: null,
                    failure: null,
                    requests: requests.Select((item, ordinal) => new PIDRequestEvidence(
                        requestOrdinal: ordinal,
                        manifestPIDOrdinal: item.ManifestPIDOrdinal,
                        dispatchState: PIDRequestDispatchState.SelectedOnly,
                        transportOutcome: null,
                        valueOutcome: PIDRequestValueOutcome.NotEvaluated,
                        rawSequence: null,
                        elapsedNanoseconds: null,
                        reasonCode: null)).ToArray());
                resumed = BeginOrResume(open, sessionID);
                if (resumed.CompletionState is not null)
                {
                    AcquisitionLog.BatchResumed(AcquisitionLog.Logger, input.BatchIdentity.Ordinal, "completed");
                    return new PersistedAcquisitionBatchResult(resumed, []);
                }
                if (resumed.Requests.Any(request => request.DispatchState == PIDRequestDispatchState.DispatchBegun))
                {
                    AcquisitionLog.BatchResumed(AcquisitionLog.Logger, input.BatchIdentity.Ordinal, "dispatch-begun");
                    throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.PersistenceFailure);
                }
            }
            var persistedRequests = resumed.Requests.ToArray();
            var samples = new List<OBDPIDSample>();
            for (var ordinal = 0; ordinal < requests.Count; ordinal++)
            {
                var item = requests[ordinal];
                AcquisitionPerformanceInterval transportInterval;
                ulong started;
                lock (_gate)
                {
                    EnsureCurrent(generation);
                    if (persistedRequests[ordinal].DispatchState == PIDRequestDispatchState.Terminal)
                    {
                        continue;
                    }
                    _repository.MarkRequestDispatchBegun(ordinal, input.BatchIdentity, sessionID);
                    transportInterval = _performanceEvents.Begin(
                        AcquisitionPerformanceOperation.RequestTransport,
                        new AcquisitionPerformanceContext(
                            generation: generation,
                            batchOrdinal: input.BatchIdentity.Ordinal,
                            requestOrdinal: ordinal,
                            policyTick: input.PolicyTick));
                    started = _monotonicNanoseconds();
                }
                var outcome = await TransportOutcome(item.Definition, input.Endpoint);
                counts.Add(outcome);
                lock (_gate)
                {
                    var ended = _monotonicNanoseconds();
                    if (ended < started)
                    {
                        _performanceEvents.End(transportInterval, AcquisitionPerformanceOutcome.Failed);
                        throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InvalidInput);
                    }
                    _performanceEvents.End(
                        transportInterval,
                        outcome is OBDPIDRequestTransportOutcome.Cancelled
                            ? AcquisitionPerformanceOutcome.Cancelled
                            : AcquisitionPerformanceOutcome.Succeeded);
                    EnsureCurrent(generation);
                    var observedAt = _now();
                    persistedRequests[ordinal] = Persist(
                        outcome,
                        item.Definition,
                        observedAt,
                        ended - started,
                        ordinal,
                        input.BatchIdentity,
                        sessionID);
                    if (outcome is OBDPIDRequestTransportOutcome.Responded responded
                        && DecodedSample(item.Definition, responded.Payload, observedAt) is { } sample)
                    {
                        samples.Add(sample);
                    }
                }
            }
            lock (_gate)
            {
                var terminal = TerminalBatch(resumed, persistedRequests, _now());
                _repository.FinishBatch(terminal, sessionID);
                RecordCompletedBatch(
                    input.BatchIdentity.Ordinal,
                    input.PolicyTick,
                    requests.Count,
                    counts,
                    samples.Count,
                    (long)Stopwatch.GetElapsedTime(batchStarted).TotalMilliseconds);
                return new PersistedAcquisitionBatchResult(terminal, samples);
            }
        }
        catch (PersistAcquisitionBatchException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InactiveGeneration);
        }
        catch (Exception error)
        {
            AcquisitionLog.BatchPersistFailed(AcquisitionLog.Logger, error, input.BatchIdentity.Ordinal, input.Permission.Generation);
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.PersistenceFailure);
        }
    }

    /// <summary>process終了後のopen batchをunknown terminalへ回復します。</summary>
    /// <remarks>責務: dispatch済み未確定要求だけをunknownとして保存しbatchをterminated状態へsealします。</remarks>
    /// <param name="batchIdentity">回復対象batch identity。</param>
    /// <param name="sessionID">batchを所有するsession。</param>
    /// <param name="completedAt">回復処理がterminalを確定した実時間。</param>
    /// <returns>selected-only要求を保持したterminated batch証拠。</returns>
    /// <exception cref="PersistAcquisitionBatchException">batch欠落、request保存失敗、またはbatch seal失敗。</exception>
    public AcquisitionBatchEvidence RecoverAfterTermination(
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID,
        DateTimeOffset completedAt)
    {
        lock (_gate)
        {
            try
            {
                var open = _repository.Batches(sessionID).FirstOrDefault(batch =>
                    batch.Identity == batchIdentity && batch.CompletionState is null)
                    ?? throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InvalidInput);
                var recovered = open.Requests.ToArray();
                foreach (var request in open.Requests.Where(request => request.DispatchState == PIDRequestDispatchState.DispatchBegun))
                {
                    recovered[request.RequestOrdinal] = _repository.SaveNonRespondedRequest(
                        outcome: PIDRequestTransportOutcome.UnknownAfterTermination,
                        elapsedNanoseconds: null,
                        reasonCode: "process_terminated_after_dispatch",
                        requestOrdinal: request.RequestOrdinal,
                        batchIdentity: batchIdentity,
                        sessionID: sessionID);
                }
                var terminal = new AcquisitionBatchEvidence(
                    identity: open.Identity,
                    generation: open.Generation,
                    policyTick: open.PolicyTick,
                    isSelectionEvaluationComplete: open.IsSelectionEvaluationComplete,
                    startedAt: open.StartedAt,
                    completionState: AcquisitionBatchCompletionState.TerminatedUnknown,
                    completedAt: completedAt,
                    failure: AcquisitionBatchFailure.ProcessTerminated,
                    requests: recovered);
                _repository.FinishBatch(terminal, sessionID);
                return terminal;
            }
            catch (PersistAcquisitionBatchException)
            {
                throw;
            }
            catch (Exception error)
            {
                AcquisitionLog.BatchPersistFailed(AcquisitionLog.Logger, error, batchIdentity.Ordinal, 0);
                throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.PersistenceFailure);
            }
        }
    }

    /// <summary>manifest要求順と取得定義を照合します。</summary>
    /// <remarks>責務: 保存済みordered PID集合を同順序の完全な取得定義列へ結び付けます。</remarks>
    /// <param name="input">manifest許可と取得定義を持つbatch入力。</param>
    /// <returns>manifest位置を付与した取得定義列。</returns>
    /// <exception cref="PersistAcquisitionBatchException">欠落、順序不一致、または片側だけのrange指定では <c>InvalidInput</c>。</exception>
    private static IReadOnlyList<(int ManifestPIDOrdinal, OBDPIDDefinition Definition)> ValidatedRequests(
        PersistAcquisitionBatchInput input)
    {
        if (input.Permission.Manifest.OrderedRequestedPIDs is not { } ordered)
        {
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InvalidInput);
        }
        var manifestOrdinalByRequest = ordered.Requests
            .Select((request, offset) => (request, offset))
            .ToDictionary(pair => pair.request, pair => pair.offset);
        var requested = input.Definitions.Select(definition => new OBDPIDRequest(definition.Service, definition.Pid)).ToArray();
        if (new HashSet<OBDPIDRequest>(requested).Count != requested.Length)
        {
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InvalidInput);
        }
        return input.Definitions.Select(definition =>
        {
            var request = new OBDPIDRequest(definition.Service, definition.Pid);
            if (!manifestOrdinalByRequest.TryGetValue(request, out var manifestOrdinal)
                || (definition.MinimumValue is null) != (definition.MaximumValue is null))
            {
                throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InvalidInput);
            }
            return (manifestOrdinal, definition);
        }).ToArray();
    }

    /// <summary>open batchを新規作成し、exact retryでは保存済みcanonical状態を返します。</summary>
    /// <remarks>責務: 1件のbatch開始を新規保存または同一入力の再開状態へ決定します。</remarks>
    /// <param name="open">今回期待するopen batch証拠。</param>
    /// <param name="sessionID">batchを所有するsession。</param>
    /// <returns>新規またはexact retryとして読み戻したcanonical batch。</returns>
    /// <exception cref="Exception">semantic差、読取失敗、または保存失敗。</exception>
    private AcquisitionBatchEvidence BeginOrResume(AcquisitionBatchEvidence open, ConnectionSessionID sessionID)
    {
        if (_repository.Batches(sessionID).FirstOrDefault(batch => batch.Identity == open.Identity) is { } stored)
        {
            if (stored.Identity != open.Identity
                || stored.Generation != open.Generation
                || stored.PolicyTick != open.PolicyTick
                || stored.StartedAt != open.StartedAt
                || !stored.Requests.Select(request => request.ManifestPIDOrdinal)
                    .SequenceEqual(open.Requests.Select(request => request.ManifestPIDOrdinal)))
            {
                throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.PersistenceFailure);
            }
            return stored;
        }
        _repository.BeginBatch(open, sessionID);
        return open;
    }

    /// <summary>1件のPIDをtyped Transport観測へ変換します。</summary>
    /// <remarks>責務: 1要求だけを実行し、throwや観測不一致をunsupportedへ推測しない排他結果へ変換します。</remarks>
    /// <param name="definition">実行する取得時PID定義。</param>
    /// <param name="endpoint">使用するOBD終端。</param>
    /// <returns>明示typed結果、または安全なcancel、transport failure、unclassified結果。</returns>
    private async Task<OBDPIDRequestTransportOutcome> TransportOutcome(
        OBDPIDDefinition definition,
        OBDConnectionEndpoint endpoint)
    {
        var request = new OBDPIDRequest(definition.Service, definition.Pid);
        try
        {
            if (definition.IsVehicleSpecific)
            {
                var responses = await _telemetry.ReadVehicleSpecific([definition], endpoint);
                if (!responses.TryGetValue(request, out var payload))
                {
                    return new OBDPIDRequestTransportOutcome.UnclassifiedResponse();
                }
                return new OBDPIDRequestTransportOutcome.Responded(payload);
            }
            var observations = await _telemetry.ReadObservations([request], endpoint);
            if (observations.Count != 1 || observations[0].Request != request)
            {
                return new OBDPIDRequestTransportOutcome.UnclassifiedResponse();
            }
            return observations[0].Outcome;
        }
        catch (OperationCanceledException)
        {
            return new OBDPIDRequestTransportOutcome.Cancelled();
        }
        catch (OBDPIDTelemetryException error)
        {
            return error.Reason switch
            {
                OBDPIDTelemetryError.ConnectionLost or OBDPIDTelemetryError.Unavailable =>
                    new OBDPIDRequestTransportOutcome.TransportFailure(),
                _ => new OBDPIDRequestTransportOutcome.UnclassifiedResponse(),
            };
        }
        catch (Exception error)
        {
            AcquisitionLog.RequestUnexpectedError(AcquisitionLog.Logger, error, PidText(request));
            return new OBDPIDRequestTransportOutcome.UnclassifiedResponse();
        }
    }

    /// <summary>typed Transport結果を対応するrepository terminal操作へ渡します。</summary>
    /// <remarks>責務: 1件のtyped観測を推測なしのDomain request evidenceへ永続化します。</remarks>
    /// <param name="outcome">Transport境界が返した排他結果。</param>
    /// <param name="definition">payload評価に使用する取得時定義。</param>
    /// <param name="observedAt">Raw正応答を観測した実時間。</param>
    /// <param name="elapsedNanoseconds">request開始からterminalまでの単調経過時間。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">親batch identity。</param>
    /// <param name="sessionID">親session。</param>
    /// <returns>repositoryから返されたcanonical request evidence。</returns>
    /// <exception cref="Exception">Domain evidence生成または永続化失敗。</exception>
    private PIDRequestEvidence Persist(
        OBDPIDRequestTransportOutcome outcome,
        OBDPIDDefinition definition,
        DateTimeOffset observedAt,
        ulong elapsedNanoseconds,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID)
    {
        switch (outcome)
        {
            case OBDPIDRequestTransportOutcome.Responded responded:
                var valueOutcome = EvaluateValueOutcome(definition, responded.Payload);
                return _repository.SaveRespondedRequest(
                    observation: new OBDRawResponseObservation(
                        observedAt: observedAt,
                        batchElapsedNanoseconds: elapsedNanoseconds,
                        request: new OBDPIDRequest(definition.Service, definition.Pid),
                        payload: responded.Payload),
                    valueOutcome: valueOutcome,
                    elapsedNanoseconds: elapsedNanoseconds,
                    reasonCode: null,
                    requestOrdinal: requestOrdinal,
                    batchIdentity: batchIdentity,
                    sessionID: sessionID);
            case OBDPIDRequestTransportOutcome.Unsupported:
                return SaveNonResponded(PIDRequestTransportOutcome.Unsupported, "explicit_unsupported", elapsedNanoseconds, requestOrdinal, batchIdentity, sessionID);
            case OBDPIDRequestTransportOutcome.TimedOut:
                return SaveNonResponded(PIDRequestTransportOutcome.TimedOut, "response_timeout", elapsedNanoseconds, requestOrdinal, batchIdentity, sessionID);
            case OBDPIDRequestTransportOutcome.Cancelled:
                return SaveNonResponded(PIDRequestTransportOutcome.Cancelled, "explicit_cancellation", elapsedNanoseconds, requestOrdinal, batchIdentity, sessionID);
            case OBDPIDRequestTransportOutcome.TransportFailure:
                return SaveNonResponded(PIDRequestTransportOutcome.TransportFailure, "transport_boundary_lost", elapsedNanoseconds, requestOrdinal, batchIdentity, sessionID);
            case OBDPIDRequestTransportOutcome.UnclassifiedResponse:
                return SaveNonResponded(PIDRequestTransportOutcome.UnclassifiedResponse, "unclassified_transport_result", elapsedNanoseconds, requestOrdinal, batchIdentity, sessionID);
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        }
    }

    /// <summary>nonresponded結果をrepositoryへ渡します。</summary>
    /// <remarks>責務: 1件の非responded typed結果をRawなしのterminal保存へ変換します。</remarks>
    /// <param name="outcome">保存する非responded Domain結果。</param>
    /// <param name="reasonCode">承認済み分類理由code。</param>
    /// <param name="elapsedNanoseconds">request開始からterminalまでの単調経過時間。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">親batch identity。</param>
    /// <param name="sessionID">親session。</param>
    /// <returns>canonical request evidence。</returns>
    /// <exception cref="Exception">repository保存失敗。</exception>
    private PIDRequestEvidence SaveNonResponded(
        PIDRequestTransportOutcome outcome,
        string reasonCode,
        ulong elapsedNanoseconds,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) =>
        _repository.SaveNonRespondedRequest(
            outcome: outcome,
            elapsedNanoseconds: elapsedNanoseconds,
            reasonCode: reasonCode,
            requestOrdinal: requestOrdinal,
            batchIdentity: batchIdentity,
            sessionID: sessionID);

    /// <summary>responded payloadの取得時値評価結果を分類します。</summary>
    /// <remarks>責務: 取得時PID定義とpayloadをdecode、finite、明示rangeの排他結果へ変換します。</remarks>
    /// <param name="definition">取得時の数式と任意の両端rangeを持つ定義。</param>
    /// <param name="payload">未デコード正応答bytes。</param>
    /// <returns>decode成功、decode失敗、または無効値の分類。</returns>
    private PIDRequestValueOutcome EvaluateValueOutcome(OBDPIDDefinition definition, IReadOnlyList<byte> payload)
    {
        try
        {
            var value = _evaluator.Evaluate(definition, payload);
            if (!double.IsFinite(value))
            {
                return PIDRequestValueOutcome.InvalidValue;
            }
            if (definition.MinimumValue is { } minimum && definition.MaximumValue is { } maximum
                && !(minimum <= value && value <= maximum))
            {
                return PIDRequestValueOutcome.InvalidValue;
            }
            return PIDRequestValueOutcome.DecodedValid;
        }
        catch (OBDPIDFormulaException error) when (error.Reason is OBDPIDFormulaError.NonFiniteResult)
        {
            return PIDRequestValueOutcome.InvalidValue;
        }
        catch (Exception error)
        {
            LogDecodeFailed(error, definition);
            return PIDRequestValueOutcome.DecodeFailure;
        }
    }

    /// <summary>正応答payloadをLiveTelemetry表示用sampleへ変換します。</summary>
    /// <remarks>責務: 1件の取得時定義と正応答を再送なしの数値観測へ変換します。</remarks>
    /// <param name="definition">表示metadataと数式を持つ取得時PID定義。</param>
    /// <param name="payload">方式BでRaw保存した正応答bytes。</param>
    /// <param name="observedAt">Rawと共有する観測日時。</param>
    /// <returns>数値化できた表示用sample、またはdecode不能時の <c>null</c>。</returns>
    private OBDPIDSample? DecodedSample(OBDPIDDefinition definition, IReadOnlyList<byte> payload, DateTimeOffset observedAt)
    {
        double value;
        try
        {
            value = _evaluator.Evaluate(definition, payload);
        }
        catch (Exception)
        {
            // 同じ失敗は値の評価（EvaluateValueOutcome）で診断ログに書いています。
            return null;
        }
        if (!double.IsFinite(value))
        {
            return null;
        }
        return new OBDPIDSample(
            request: new OBDPIDRequest(definition.Service, definition.Pid),
            nameKey: definition.NameKey,
            value: value,
            unit: definition.Unit,
            observedAt: observedAt,
            vehicleModelCode: definition.VehicleModelCode,
            summaryKey: definition.SummaryKey,
            highValueKey: definition.HighValueKey,
            lowValueKey: definition.LowValueKey,
            correlationKey: definition.CorrelationKey);
    }

    /// <summary>request結果列からbatch terminal状態を決定します。</summary>
    /// <remarks>責務: 排他的request outcome列をcompletedまたは明示partial failureへ集約します。</remarks>
    /// <param name="open">保存済みopen batch証拠。</param>
    /// <param name="requests">canonical terminal request列。</param>
    /// <param name="completedAt">batch terminal確定日時。</param>
    /// <returns>完了またはpartial失敗を持つterminal batch証拠。</returns>
    /// <exception cref="AcquisitionBatchEvidenceException">Domain batch不変条件違反。</exception>
    private static AcquisitionBatchEvidence TerminalBatch(
        AcquisitionBatchEvidence open,
        IReadOnlyList<PIDRequestEvidence> requests,
        DateTimeOffset completedAt)
    {
        var outcomes = requests
            .Where(request => request.TransportOutcome is not null)
            .Select(request => request.TransportOutcome!.Value)
            .ToArray();
        AcquisitionBatchFailure? failure;
        if (outcomes.Contains(PIDRequestTransportOutcome.Cancelled))
        {
            failure = AcquisitionBatchFailure.Cancelled;
        }
        else if (outcomes.Contains(PIDRequestTransportOutcome.TransportFailure) || outcomes.Contains(PIDRequestTransportOutcome.TimedOut))
        {
            failure = AcquisitionBatchFailure.TransportUnavailable;
        }
        else if (outcomes.Contains(PIDRequestTransportOutcome.UnclassifiedResponse))
        {
            failure = AcquisitionBatchFailure.UnclassifiedResult;
        }
        else
        {
            failure = null;
        }
        return new AcquisitionBatchEvidence(
            identity: open.Identity,
            generation: open.Generation,
            policyTick: open.PolicyTick,
            isSelectionEvaluationComplete: open.IsSelectionEvaluationComplete,
            startedAt: open.StartedAt,
            completionState: failure is null ? AcquisitionBatchCompletionState.Completed : AcquisitionBatchCompletionState.Failed,
            completedAt: completedAt,
            failure: failure,
            requests: requests);
    }

    /// <summary>Service と PID を診断ログ用の文字列にします。</summary>
    /// <remarks>責務: 要求を <c>010C</c> の形にします（失敗の経路だけで使う）。</remarks>
    /// <param name="request">要求。</param>
    /// <returns>文字列。</returns>
    private static string PidText(OBDPIDRequest request) =>
        string.Create(CultureInfo.InvariantCulture, $"{request.Service:X2}{request.Pid:X2}");

    /// <summary>応答を数値にできなかったことを、世代の中で PID ごとに 1 回だけ書きます。</summary>
    /// <remarks>責務: 想定内の数値化の失敗を Debug で残します。同じ PID の失敗はバッチごとに繰り返さない。</remarks>
    /// <param name="error">例外。</param>
    /// <param name="definition">PID 定義。</param>
    private void LogDecodeFailed(Exception error, OBDPIDDefinition definition)
    {
        var logger = AcquisitionLog.Logger;
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var request = new OBDPIDRequest(definition.Service, definition.Pid);
        lock (_gate)
        {
            if (!_decodeFailureLogged.Add(request))
            {
                return;
            }
        }

        AcquisitionLog.DecodeFailed(logger, error, PidText(request));
    }

    /// <summary>バッチの中の要求の結果の件数です（診断ログの要約用。割り当てなしで数える）。</summary>
    private struct BatchOutcomeCounts
    {
        /// <summary>応答があった件数です。</summary>
        public int Responded;

        /// <summary>期限切れの件数です。</summary>
        public int TimedOut;

        /// <summary>通信の失敗の件数です。</summary>
        public int TransportFailed;

        /// <summary>読めない応答の件数です。</summary>
        public int Unclassified;

        /// <summary>取消の件数です。</summary>
        public int Cancelled;

        /// <summary>1 件の結果を数えます。</summary>
        /// <remarks>責務: Transport の結果の種類ごとに件数を 1 増やします。</remarks>
        /// <param name="outcome">結果。</param>
        public void Add(OBDPIDRequestTransportOutcome outcome)
        {
            switch (outcome)
            {
                case OBDPIDRequestTransportOutcome.Responded:
                    Responded++;
                    break;
                case OBDPIDRequestTransportOutcome.TimedOut:
                    TimedOut++;
                    break;
                case OBDPIDRequestTransportOutcome.TransportFailure:
                    TransportFailed++;
                    break;
                case OBDPIDRequestTransportOutcome.Cancelled:
                    Cancelled++;
                    break;
                default:
                    Unclassified++;
                    break;
            }
        }
    }

    /// <summary>指定世代が現在の取得workflowに属することを検証します。</summary>
    /// <remarks>責務: staleまたはcancel済み世代を次の永続化処理より前に拒否します。</remarks>
    /// <param name="generation">検証するpolling世代。</param>
    /// <exception cref="PersistAcquisitionBatchException">現在世代と一致しない場合は <c>InactiveGeneration</c>。</exception>
    private void EnsureCurrent(ulong generation)
    {
        if (_activeGeneration != generation)
        {
            throw new PersistAcquisitionBatchException(PersistAcquisitionBatchError.InactiveGeneration);
        }
    }
}
