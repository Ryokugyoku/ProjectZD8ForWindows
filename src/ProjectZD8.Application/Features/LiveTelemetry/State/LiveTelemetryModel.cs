using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Application.Logging;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.State;

/// <summary>車両接続中の優先度付きPID継続取得ライフサイクルをApplicationユースケースへ結び付けます。</summary>
/// <remarks>
/// UI スレッド（単一スレッド）から呼ぶ前提です（Swift では <c>@MainActor</c>）。
/// Swift の <c>Task { ... }</c> と同じく、開始・停止の非同期処理は呼出元へ戻ったあとで走るよう、先頭で一度 <see cref="Task.Yield"/> します。
/// </remarks>
public sealed class LiveTelemetryModel : INotifyPropertyChanged
{
    /// <summary>Platformが描画する現在状態です。</summary>
    private LiveTelemetryState state;

    /// <summary>検証済み主要PIDを数値化するユースケースです。</summary>
    private readonly ReadMajorOBDPIDsUseCase readMajorPIDs;

    /// <summary>車両別の対応PIDを再利用または初回探索するユースケースです。</summary>
    private readonly LoadVehiclePIDCapabilitiesUseCase? loadVehicleCapabilities;

    /// <summary>PID更新対象と間引き周期を決める方針です。</summary>
    private readonly OBDPIDPollingPolicy pollingPolicy;

    /// <summary>Production取得loopを方式Bの永続証拠へ接続する境界です。</summary>
    private readonly ILiveTelemetryAcquisitionEvidencePort? acquisitionEvidence;

    /// <summary>batch開始日時を供給するclockです。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>polling sleepの予定wake遅延を測る単調clockです。</summary>
    private readonly Func<ulong> monotonicNanoseconds;

    /// <summary>PID取得セッションが終了した原因をLoggingへ通知する処理です。</summary>
    private readonly Action<ConnectionSessionEndReason> sessionDidEnd;

    /// <summary>周期の遅れを診断ログに Warning で書く下限です（1 周期分の 250 ミリ秒）。</summary>
    private const ulong ScheduleDelayWarningNanoseconds = 250_000_000UL;

    /// <summary>取得できた取得元付き累積距離をLoggingへ通知する処理です。</summary>
    private readonly Action<ConnectionSessionDistanceObservation> distanceDidChange;

    /// <summary>車両接続中のシステムスリープ抑止を切り替えるOS境界です。</summary>
    private readonly IVehicleConnectionSystemSleepInhibiting? systemSleepInhibitor;

    /// <summary>現在画面の表示有無から独立して動く取得タスクです。</summary>
    private PollingTask? pollingTask;

    /// <summary>古い取得完了を新しい接続状態へ反映しないための世代です。</summary>
    private ulong pollingGeneration;

    /// <summary>初期状態、PID読取ユースケース、更新方針、終了通知先を固定します。</summary>
    /// <remarks>
    /// 責務: PID表示状態を1件の読取ユースケースへ結び付けます。
    /// Swift の指定イニシャライザと簡易イニシャライザ（空の表示状態と既定更新方針）を1つにまとめています。
    /// </remarks>
    /// <param name="readMajorPIDs">主要PID読取と数値化を行うユースケース。</param>
    /// <param name="state">Platformへ公開する初期状態。<c>null</c> なら空の表示状態。</param>
    /// <param name="loadVehicleCapabilities">車両別対応PIDの再利用または初回探索を行うユースケース。</param>
    /// <param name="pollingPolicy">PIDごとの更新優先度と間引き周期を決める方針。<c>null</c> なら既定方針。</param>
    /// <param name="acquisitionEvidence">Production取得loopを方式Bの永続証拠へ接続する境界。</param>
    /// <param name="now">batch開始日時を供給するclock。<c>null</c> なら現在時刻。</param>
    /// <param name="monotonicNanoseconds">polling sleepの予定wake遅延を測る単調clock。<c>null</c> なら <see cref="Stopwatch"/>。</param>
    /// <param name="sessionDidEnd">PID取得終了原因をLoggingへ通知する処理。</param>
    /// <param name="distanceDidChange">車種専用PID、Service 01 PID A6、またはPID 31の累積距離をLoggingへ通知する処理。</param>
    /// <param name="systemSleepInhibitor">車両接続中だけシステムスリープを抑止する境界。</param>
    public LiveTelemetryModel(
        ReadMajorOBDPIDsUseCase readMajorPIDs,
        LiveTelemetryState? state = null,
        LoadVehiclePIDCapabilitiesUseCase? loadVehicleCapabilities = null,
        OBDPIDPollingPolicy? pollingPolicy = null,
        ILiveTelemetryAcquisitionEvidencePort? acquisitionEvidence = null,
        Func<DateTimeOffset>? now = null,
        Func<ulong>? monotonicNanoseconds = null,
        Action<ConnectionSessionEndReason>? sessionDidEnd = null,
        Action<ConnectionSessionDistanceObservation>? distanceDidChange = null,
        IVehicleConnectionSystemSleepInhibiting? systemSleepInhibitor = null)
    {
        this.state = state ?? new LiveTelemetryState();
        this.readMajorPIDs = readMajorPIDs;
        this.loadVehicleCapabilities = loadVehicleCapabilities;
        this.pollingPolicy = pollingPolicy ?? new OBDPIDPollingPolicy();
        this.acquisitionEvidence = acquisitionEvidence;
        this.now = now ?? (() => DateTimeOffset.Now);
        this.monotonicNanoseconds = monotonicNanoseconds ?? SystemMonotonicNanoseconds;
        this.sessionDidEnd = sessionDidEnd ?? (_ => { });
        this.distanceDidChange = distanceDidChange ?? (_ => { });
        this.systemSleepInhibitor = systemSleepInhibitor;
    }

    /// <summary>プロパティの値が変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Platformが描画する現在状態です。</summary>
    public LiveTelemetryState State
    {
        get => state;
        set => SetProperty(ref state, value);
    }

    /// <summary>型付き操作をPID読取ワークフローへ変換します。</summary>
    /// <remarks>責務: 1件のLiveTelemetry操作を新規読取または再読取に振り分けます。</remarks>
    /// <param name="action">Platformから通知された型付き操作。</param>
    public void Send(LiveTelemetryAction action)
    {
        switch (action)
        {
            case LiveTelemetryAction.StartRequested start:
                Start(start.Endpoint, start.VehicleID, start.VehicleModelCode);
                break;
            case LiveTelemetryAction.RetryRequested:
                if (State.Endpoint is { } endpoint && State.VehicleID is { } vehicleID)
                {
                    Start(endpoint, vehicleID, State.VehicleModelCode);
                }

                break;
            case LiveTelemetryAction.StopRequested:
                Stop();
                break;
        }
    }

    /// <summary>指定OBD終端からPID継続取得を開始します。</summary>
    /// <remarks>
    /// 責務: 以前の取得を無効化して最新接続のスリープ抑止と継続取得を開始します。
    /// C# では取消の通知が同期的に旧タスクの続きを走らせることがあるため、世代と状態を更新したあとで旧タスクを取り消します。
    /// </remarks>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <param name="vehicleID">対応PID設定を参照する車両ID。</param>
    /// <param name="vehicleModelCode">確認済み車種専用PIDを適用する型式コード。</param>
    private void Start(OBDConnectionEndpoint endpoint, VehicleID vehicleID, string? vehicleModelCode)
    {
        var previousTask = pollingTask;
        var previousGeneration = pollingGeneration;
        pollingGeneration = unchecked(pollingGeneration + 1);
        var generation = pollingGeneration;
        State = State with
        {
            Endpoint = endpoint,
            VehicleID = vehicleID,
            VehicleModelCode = vehicleModelCode,
            Phase = LiveTelemetryStatePhase.Reading,
            FailureKey = null,
            Samples = [],
            SupportedPIDCount = 0,
        };
        AcquisitionLog.Started(AcquisitionLog.Logger, endpoint.Transport.ToRawValue(), generation, acquisitionEvidence is not null);
        systemSleepInhibitor?.SetVehicleConnectionActive(true);
        previousTask?.Cancel();
        var cancellation = new CancellationTokenSource();
        pollingTask = new PollingTask(
            RunPolling(previousTask, previousGeneration, endpoint, vehicleID, vehicleModelCode, generation, cancellation.Token),
            cancellation);
    }

    /// <summary>以前の取得完了を待ってから新しい世代の継続取得を実行します。</summary>
    /// <remarks>責務: Swift の取得 <c>Task</c> 本体として、旧世代の証拠停止後に新世代の取得を始めます。</remarks>
    /// <param name="previousTask">完了を待つ以前の取得タスク。</param>
    /// <param name="previousGeneration">以前の取得世代。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <param name="vehicleID">対応PID設定を参照する車両ID。</param>
    /// <param name="vehicleModelCode">確認済み車種専用PIDを適用する型式コード。</param>
    /// <param name="generation">この取得開始時の世代。</param>
    /// <param name="cancellationToken">この取得タスクの取消。</param>
    /// <returns>取得の完了。</returns>
    private async Task RunPolling(
        PollingTask? previousTask,
        ulong previousGeneration,
        OBDConnectionEndpoint endpoint,
        VehicleID vehicleID,
        string? vehicleModelCode,
        ulong generation,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (previousTask is not null)
        {
            await previousTask.Task;
            if (acquisitionEvidence is not null)
            {
                await acquisitionEvidence.Cancel(previousGeneration);
            }
        }

        await Poll(endpoint, vehicleID, vehicleModelCode, generation, cancellationToken);
    }

    /// <summary>現在のPID継続取得を無効化します。</summary>
    /// <remarks>
    /// 責務: 現在世代の取得を終了してスリープ抑止解除済みの待機状態へ戻します。
    /// C# では取消の通知が同期的に旧タスクの続きを走らせることがあるため、世代と状態を更新したあとで旧タスクを取り消します。
    /// </remarks>
    private void Stop()
    {
        var shouldNotifySessionEnd = pollingTask is not null || State.IsConnectionActive;
        var previousTask = pollingTask;
        var stoppedGeneration = pollingGeneration;
        pollingTask = null;
        pollingGeneration = unchecked(pollingGeneration + 1);
        var generation = pollingGeneration;
        State = State with { Phase = LiveTelemetryStatePhase.Stopping, FailureKey = null };
        previousTask?.Cancel();
        _ = RunStop(previousTask, stoppedGeneration, generation, shouldNotifySessionEnd);
    }

    /// <summary>取得停止と通信資源の解放を待ってから待機状態へ戻します。</summary>
    /// <remarks>責務: Swift の停止 <c>Task</c> 本体として、同じ世代のままなら待機状態とスリープ抑止解除を反映します。</remarks>
    /// <param name="previousTask">完了を待つ取得タスク。</param>
    /// <param name="stoppedGeneration">停止した取得世代。</param>
    /// <param name="generation">停止要求時に割り当てた世代。</param>
    /// <param name="shouldNotifySessionEnd">終了原因をLoggingへ通知するかどうか。</param>
    /// <returns>停止処理の完了。</returns>
    private async Task RunStop(
        PollingTask? previousTask,
        ulong stoppedGeneration,
        ulong generation,
        bool shouldNotifySessionEnd)
    {
        await Task.Yield();
        if (previousTask is not null)
        {
            await previousTask.Task;
        }

        if (acquisitionEvidence is not null)
        {
            await acquisitionEvidence.Cancel(stoppedGeneration);
        }

        await readMajorPIDs.EndSession();
        if (pollingGeneration != generation)
        {
            AcquisitionLog.Stopped(AcquisitionLog.Logger, "superseded", stoppedGeneration);
            return;
        }

        AcquisitionLog.Stopped(AcquisitionLog.Logger, "user", stoppedGeneration);
        State = State with { Phase = LiveTelemetryStatePhase.Idle };
        systemSleepInhibitor?.SetVehicleConnectionActive(false);
        if (shouldNotifySessionEnd)
        {
            sessionDidEnd(ConnectionSessionEndReason.UserDisconnected);
        }
    }

    /// <summary>初回全件探索後に優先度付きPID更新を繰り返します。</summary>
    /// <remarks>責務: 1件の接続終端を応答済みPIDの継続的な最新値へ変換します。</remarks>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <param name="vehicleID">対応PID設定を参照する車両ID。</param>
    /// <param name="vehicleModelCode">確認済み車種専用PIDを適用する型式コード。</param>
    /// <param name="generation">この取得開始時の世代。</param>
    /// <param name="cancellationToken">この取得タスクの取消。</param>
    /// <returns>取得の完了。</returns>
    private async Task Poll(
        OBDConnectionEndpoint endpoint,
        VehicleID vehicleID,
        string? vehicleModelCode,
        ulong generation,
        CancellationToken cancellationToken)
    {
        IDisposable? sessionScope = null;
        var loadStarted = Stopwatch.GetTimestamp();
        try
        {
            IReadOnlyList<OBDPIDDefinition> definitions;
            List<ConnectionSessionAcquisitionPIDCapabilityInput> capabilityInputs;
            if (loadVehicleCapabilities is not null)
            {
                var capabilities = await loadVehicleCapabilities.Execute(
                    vehicleID: vehicleID,
                    endpoint: endpoint,
                    vehicleModelCode: vehicleModelCode);
                definitions = readMajorPIDs.LoadDefinitions(capabilities);
                var capabilityByRequest = capabilities.ToDictionary(capability => capability.Id.Request);
                capabilityInputs = [];
                foreach (var definition in definitions)
                {
                    var request = new OBDPIDRequest(definition.Service, definition.Pid);
                    if (capabilityByRequest.TryGetValue(request, out var capability))
                    {
                        capabilityInputs.Add(new ConnectionSessionAcquisitionPIDCapabilityInput(
                            request,
                            AcquisitionPIDCapabilitySupport.Supported,
                            capability.IsCollectionEnabled));
                    }
                }
            }
            else
            {
                definitions = readMajorPIDs.LoadDefinitions();
                capabilityInputs = [];
            }

            AcquisitionLog.CapabilitiesLoaded(
                AcquisitionLog.Logger,
                definitions.Count,
                capabilityInputs.Count,
                (long)Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds);

            if (acquisitionEvidence is not null)
            {
                if (capabilityInputs.Count != definitions.Count)
                {
                    throw new CreateConnectionSessionAcquisitionManifestException(
                        CreateConnectionSessionAcquisitionManifestError.CapabilityEvidenceMissing);
                }

                await acquisitionEvidence.Start(new LiveTelemetryAcquisitionStartInput(
                    generation: generation,
                    definitions: definitions,
                    capabilities: capabilityInputs,
                    orderedRequests: pollingPolicy.DefinitionsToPoll(definitions, tick: 0)
                        .Select(definition => new OBDPIDRequest(definition.Service, definition.Pid))
                        .ToList()));

                // 取得のループ全体に接続セッション ID を 1 回だけ付けます（バッチごとに割り当てを増やさないため）。
                if (acquisitionEvidence is ConnectionSessionAcquisitionController { DiagnosticSessionId: { } sessionId })
                {
                    sessionScope = DiagnosticLog.BeginConnectionSession(sessionId);
                }

                await PollPersisted(definitions, endpoint, generation, cancellationToken);
            }
            else
            {
                await PollStandard(definitions, endpoint, generation, cancellationToken);
            }
        }
        catch (Exception error)
        {
            await HandlePollingFailure(error, generation);
        }
        finally
        {
            sessionScope?.Dispose();
        }
    }

    /// <summary>PID取得失敗を終了処理と表示状態へ統一的に反映します。</summary>
    /// <remarks>責務: 1件の取得エラーをスリープ抑止解除済みの接続終了状態へ変換します。</remarks>
    /// <param name="error">取得処理から伝播したエラー。</param>
    /// <param name="generation">エラーが属する取得世代。</param>
    /// <returns>反映の完了。</returns>
    private async Task HandlePollingFailure(Exception error, ulong generation)
    {
        if (acquisitionEvidence is not null)
        {
            await acquisitionEvidence.Cancel(generation);
        }

        await readMajorPIDs.EndSession();
        if (pollingGeneration != generation)
        {
            return;
        }

        if (error is OperationCanceledException)
        {
            AcquisitionLog.Stopped(AcquisitionLog.Logger, "cancelled", generation);
            return;
        }

        systemSleepInhibitor?.SetVehicleConnectionActive(false);
        var reason = (error as OBDPIDTelemetryException)?.Reason;
        LogPollingFailure(error, reason, generation);
        switch (reason)
        {
            case OBDPIDTelemetryError.DefinitionCatalogUnavailable:
                State = State with
                {
                    Phase = LiveTelemetryStatePhase.Failed,
                    FailureKey = "telemetry.error.pid_catalog_unavailable",
                };
                sessionDidEnd(ConnectionSessionEndReason.AcquisitionFailed);
                break;
            case OBDPIDTelemetryError.Unavailable:
                State = State with
                {
                    Phase = LiveTelemetryStatePhase.Failed,
                    FailureKey = "telemetry.error.unavailable",
                };
                sessionDidEnd(ConnectionSessionEndReason.AcquisitionFailed);
                break;
            case OBDPIDTelemetryError.NoVehicleResponse:
                pollingTask = null;
                State = State with
                {
                    Phase = LiveTelemetryStatePhase.Idle,
                    FailureKey = "telemetry.disconnected.no_response",
                };
                sessionDidEnd(ConnectionSessionEndReason.VehicleNoResponse);
                break;
            case OBDPIDTelemetryError.ConnectionLost:
                pollingTask = null;
                State = State with
                {
                    Phase = LiveTelemetryStatePhase.Idle,
                    FailureKey = "telemetry.disconnected.connection_lost",
                };
                sessionDidEnd(ConnectionSessionEndReason.ConnectionLost);
                break;
            default:
                State = State with
                {
                    Phase = LiveTelemetryStatePhase.Failed,
                    FailureKey = "telemetry.error.read_failed",
                };
                sessionDidEnd(ConnectionSessionEndReason.AcquisitionFailed);
                break;
        }
    }

    /// <summary>既存の優先度付き標準PIDポーリングを繰り返します。</summary>
    /// <remarks>責務: 対応済みPID定義を従来方式の継続的な最新値へ変換します。</remarks>
    /// <param name="definitions">車両で対応確認済みの数値化可能定義。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <param name="generation">この取得開始時の世代。</param>
    /// <param name="cancellationToken">この取得タスクの取消。</param>
    /// <returns>取得の完了。</returns>
    private async Task PollStandard(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint,
        ulong generation,
        CancellationToken cancellationToken)
    {
        var initialSamples = await readMajorPIDs.Execute(definitions, endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (pollingGeneration != generation || initialSamples.Count == 0)
        {
            if (initialSamples.Count == 0)
            {
                throw new OBDPIDTelemetryException(OBDPIDTelemetryError.NoVehicleResponse);
            }

            await readMajorPIDs.EndSession();
            return;
        }

        var supportedRequests = initialSamples.Select(sample => sample.Request).ToHashSet();
        var supportedDefinitions = definitions
            .Where(definition => supportedRequests.Contains(new OBDPIDRequest(definition.Service, definition.Pid)))
            .ToList();
        State = State with { Samples = Ordered(initialSamples, supportedDefinitions) };
        NotifyDistance(initialSamples);
        State = State with { SupportedPIDCount = supportedDefinitions.Count, Phase = LiveTelemetryStatePhase.Loaded };
        ulong tick = 1;
        while (!cancellationToken.IsCancellationRequested && pollingGeneration == generation)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            var batch = pollingPolicy.DefinitionsToPoll(supportedDefinitions, tick);
            var samples = await readMajorPIDs.Execute(batch, endpoint);
            cancellationToken.ThrowIfCancellationRequested();
            if (samples.Count == 0)
            {
                throw new OBDPIDTelemetryException(OBDPIDTelemetryError.NoVehicleResponse);
            }

            if (pollingGeneration != generation)
            {
                await readMajorPIDs.EndSession();
                return;
            }

            Merge(samples, supportedDefinitions);
            tick = unchecked(tick + 1);
        }

        await readMajorPIDs.EndSession();
    }

    /// <summary>方式Bの保存ゲート下で優先度付きPID更新を繰り返します。</summary>
    /// <remarks>責務: 対応済みPID定義をstable batch identity付きの永続取得と最新表示値へ変換します。</remarks>
    /// <param name="definitions">capability確定済みの数値化可能定義。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <param name="generation">この取得開始時の世代。</param>
    /// <param name="cancellationToken">この取得タスクの取消。</param>
    /// <returns>取得の完了。</returns>
    /// <exception cref="Exception">manifest、batch、通信、永続化、または数値化を完了できない場合のエラー。</exception>
    private async Task PollPersisted(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint,
        ulong generation,
        CancellationToken cancellationToken)
    {
        if (acquisitionEvidence is null)
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable);
        }

        ulong tick = 0;
        long batchOrdinal = 0;
        IReadOnlyList<OBDPIDDefinition> supportedDefinitions = definitions;
        while (!cancellationToken.IsCancellationRequested && pollingGeneration == generation)
        {
            ulong? scheduleDelayNanoseconds;
            if (tick > 0)
            {
                var sleepStarted = monotonicNanoseconds();
                var scheduledWake = unchecked(sleepStarted + 250_000_000UL);
                var overflow = scheduledWake < sleepStarted;
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                var actualWake = monotonicNanoseconds();
                scheduleDelayNanoseconds = overflow || actualWake < scheduledWake
                    ? null
                    : actualWake - scheduledWake;
                if (scheduleDelayNanoseconds >= ScheduleDelayWarningNanoseconds)
                {
                    AcquisitionLog.ScheduleDelayed(
                        AcquisitionLog.Logger,
                        tick,
                        (long)(scheduleDelayNanoseconds.Value / 1_000_000UL));
                }
            }
            else
            {
                scheduleDelayNanoseconds = null;
            }

            var selectedDefinitions = pollingPolicy.DefinitionsToPoll(supportedDefinitions, tick);
            var samples = await acquisitionEvidence.Acquire(new LiveTelemetryAcquisitionBatchInput(
                generation: generation,
                batchOrdinal: batchOrdinal,
                policyTick: tick,
                startedAt: now(),
                definitions: selectedDefinitions,
                endpoint: endpoint,
                scheduleDelayNanoseconds: scheduleDelayNanoseconds));
            cancellationToken.ThrowIfCancellationRequested();
            if (pollingGeneration != generation)
            {
                return;
            }

            if (samples.Count == 0)
            {
                throw new OBDPIDTelemetryException(OBDPIDTelemetryError.NoVehicleResponse);
            }

            if (tick == 0)
            {
                var responded = samples.Select(sample => sample.Request).ToHashSet();
                supportedDefinitions = definitions
                    .Where(definition => responded.Contains(new OBDPIDRequest(definition.Service, definition.Pid)))
                    .ToList();
                State = State with
                {
                    Samples = Ordered(samples, supportedDefinitions),
                    SupportedPIDCount = supportedDefinitions.Count,
                    Phase = LiveTelemetryStatePhase.Loaded,
                };
                NotifyDistance(samples);
            }
            else
            {
                Merge(samples, supportedDefinitions);
            }

            tick = unchecked(tick + 1);
            batchOrdinal += 1;
        }

        await acquisitionEvidence.Cancel(generation);
        await readMajorPIDs.EndSession();
    }

    /// <summary>取得の失敗を、画面に出す区別に合わせて診断ログへ書きます。</summary>
    /// <remarks>
    /// 責務: 車両の無応答・接続の喪失（画面には切断として出る）は Warning、それ以外の失敗（画面にエラーが出る）は Error で、
    /// どちらも元の例外つきで残します。
    /// </remarks>
    /// <param name="error">取得処理から伝播したエラー。</param>
    /// <param name="reason">取得エラーの理由。取得エラーでなければ <c>null</c>。</param>
    /// <param name="generation">エラーが属する取得世代。</param>
    private static void LogPollingFailure(Exception error, OBDPIDTelemetryError? reason, ulong generation)
    {
        var logger = AcquisitionLog.Logger;
        switch (reason)
        {
            case OBDPIDTelemetryError.NoVehicleResponse:
                AcquisitionLog.Disconnected(logger, error, "vehicle-no-response", generation);
                break;
            case OBDPIDTelemetryError.ConnectionLost:
                AcquisitionLog.Disconnected(logger, error, "connection-lost", generation);
                break;
            case OBDPIDTelemetryError.DefinitionCatalogUnavailable:
                AcquisitionLog.Failed(logger, error, "telemetry.error.pid_catalog_unavailable", generation);
                break;
            case OBDPIDTelemetryError.Unavailable:
                AcquisitionLog.Failed(logger, error, "telemetry.error.unavailable", generation);
                break;
            default:
                AcquisitionLog.Failed(logger, error, "telemetry.error.read_failed", generation);
                break;
        }
    }

    /// <summary>新しい観測をPIDごとの最新値へ統合します。</summary>
    /// <remarks>責務: 1回分の観測値で同一PIDの現在表示値だけを置き換えます。</remarks>
    /// <param name="samples">今回更新できた観測値。</param>
    /// <param name="definitions">表示順を決定する対応済み定義。</param>
    private void Merge(IReadOnlyList<OBDPIDSample> samples, IReadOnlyList<OBDPIDDefinition> definitions)
    {
        var latest = State.Samples.ToDictionary(sample => sample.Request);
        foreach (var sample in samples)
        {
            latest[sample.Request] = sample;
        }

        State = State with { Samples = Ordered(latest.Values.ToList(), definitions) };
        NotifyDistance(samples);
    }

    /// <summary>PID観測一覧から優先順位が最も高い累積距離をLoggingへ通知します。</summary>
    /// <remarks>責務: 1回分のPID観測を車種専用距離、A6、PID 31の優先順で取得元付き累積距離通知へ変換します。</remarks>
    /// <param name="samples">今回取得できた数値化済みPID観測。</param>
    private void NotifyDistance(IReadOnlyList<OBDPIDSample> samples)
    {
        (OBDPIDRequest Request, ConnectionSessionDistanceSource Source)[] candidates =
        [
            (new OBDPIDRequest(0x21, 0x02), ConnectionSessionDistanceSource.VehicleSpecificOdometer),
            (new OBDPIDRequest(0x01, 0xA6), ConnectionSessionDistanceSource.Odometer),
            (new OBDPIDRequest(0x01, 0x31), ConnectionSessionDistanceSource.DistanceSinceCodesCleared),
        ];
        foreach (var (request, source) in candidates)
        {
            var sample = samples.FirstOrDefault(sample => sample.Request == request);
            if (sample is null)
            {
                continue;
            }

            distanceDidChange(new ConnectionSessionDistanceObservation(
                source: source,
                kilometers: sample.Value,
                vehicleModelCode: sample.VehicleModelCode));
            return;
        }
    }

    /// <summary>観測値を優先度付き定義順へ並べます。</summary>
    /// <remarks>責務: PID観測一覧を高優先PIDが先頭になる安定表示順へ変換します。</remarks>
    /// <param name="samples">並べ替えるPID観測。</param>
    /// <param name="definitions">対応済みPID定義。</param>
    /// <returns>高優先PIDを先頭にした観測一覧。</returns>
    private IReadOnlyList<OBDPIDSample> Ordered(IReadOnlyList<OBDPIDSample> samples, IReadOnlyList<OBDPIDDefinition> definitions)
    {
        var order = pollingPolicy.DefinitionsToPoll(definitions, tick: 0)
            .Select(definition => new OBDPIDRequest(definition.Service, definition.Pid))
            .ToList();
        var ranks = new Dictionary<OBDPIDRequest, int>();
        for (var index = 0; index < order.Count; index++)
        {
            ranks.Add(order[index], index);
        }

        return samples
            .OrderBy(sample => ranks.TryGetValue(sample.Request, out var rank) ? rank : int.MaxValue)
            .ToList();
    }

    /// <summary>値が変わったときだけフィールドを更新して変更を通知します。</summary>
    /// <remarks>責務: 観測対象プロパティの変更を <see cref="PropertyChanged"/> へ伝えます。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">変更したプロパティ名。</param>
    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>OSの単調クロックをナノ秒で返します。</summary>
    /// <remarks>責務: Swift の <c>DispatchTime.now().uptimeNanoseconds</c> に相当する既定の単調時刻を供給します。</remarks>
    /// <returns>単調クロックのナノ秒値。</returns>
    private static ulong SystemMonotonicNanoseconds() =>
        (ulong)Stopwatch.GetElapsedTime(0).Ticks * 100UL;

    /// <summary>保持して取り消せる取得タスクです。</summary>
    /// <remarks>Swift の <c>Task&lt;Void, Never&gt;</c> を、実行中の <see cref="System.Threading.Tasks.Task"/> と取消元の組で表します。</remarks>
    /// <param name="Task">実行中の取得処理。</param>
    /// <param name="Cancellation">取得処理の取消元。</param>
    private sealed record PollingTask(Task Task, CancellationTokenSource Cancellation)
    {
        /// <summary>取得処理へ取消を通知します。</summary>
        /// <remarks>責務: Swift の <c>Task.cancel()</c> と同じく協調的な取消を要求します。</remarks>
        public void Cancel() => Cancellation.Cancel();
    }
}
