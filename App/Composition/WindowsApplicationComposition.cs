using System;
using System.Diagnostics;
using System.Linq;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.Analysis.UseCases;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Authentication.UseCases;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Application.Logging.Actions;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Application.Logging.State;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Application.LogHistory.UseCases;
using ProjectZD8.Application.Maintenance.State;
using ProjectZD8.Application.Maintenance.UseCases;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Application.Settings.UseCases;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.AppRoot.Composition.Interim;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// Windows アプリで使う実装の依存関係を組み立てます（iOS 版の <c>MacOSApplicationComposition</c> と <c>ProjectZD8App.init</c>）。
/// </summary>
/// <remarks>
/// Data 層はまだないため、保存先・通信・認証は <c>Interim/</c> の暫定実装を使います。
/// Data 層ができたら、このファイルの「暫定」と書いた生成箇所だけを差し替えます（画面と Application 層は変えない）。
/// UI スレッドから呼ぶ前提です。
/// </remarks>
public static class WindowsApplicationComposition
{
    /// <summary>
    /// アプリのルートの依存一式を作ります。
    /// </summary>
    /// <remarks>責務: すべての Application モデルを生成し、iOS 版と同じ順序・同じ通知で互いに結び付けます。</remarks>
    /// <param name="arguments">コマンドライン引数（Debug の画面確認用の起動状態に使う）。</param>
    /// <returns>ルートの依存一式。</returns>
    public static WindowsApplicationRoot MakeRoot(string[] arguments)
    {
        var configuration = ProductionConnectionSessionAcquisitionConfiguration.Phase4G;
        IAcquisitionPerformanceEventPort performanceEvents = new NoOpAcquisitionPerformanceEventPort();

        // 暫定: Data 層の SQLite 実装（GRDBConnectionSessionRepository 相当）に差し替える。
        var connectionSessionStorage = new InterimConnectionSessionStore(performanceEvents);
        // 暫定: ネットワーク担当の CloudKit 転送に差し替える。
        IConnectionSessionTransferRepository transferRepository = new InterimLocalOnlyConnectionSessionTransferRepository();
        // 暫定: Data 層の PID 定義 DB（seed 登録済み）と車両別対応 PID に差し替える。
        IOBDPIDDefinitionRepository definitionRepository = new InterimOBDPIDDefinitionRepository();
        var capabilityRepository = new InterimVehiclePIDCapabilityRepository();
        // 暫定: SQLite＋CloudKit 同期の車両保存先に差し替える。
        var vehicleRepository = new InterimVehicleRepository();
        // 暫定: デバイス担当の Windows 通信（シリアル・BLE）をデモ付きで包んだものに差し替える。
        var telemetry = new InterimDemoOBDPIDTelemetryAdapter();
        var photoImporter = new InterimPhotoFileImporter();

        var connectionHistoryModel = new ConnectionHistoryModel(
            repository: connectionSessionStorage,
            synchronizeSessions: new SynchronizeConnectionSessionsUseCase(
                sessionRepository: connectionSessionStorage,
                rawLogRepository: connectionSessionStorage,
                sessionErasureRepository: connectionSessionStorage,
                transferRepository: transferRepository,
                role: ConnectionSessionSyncDeviceRole.MacOS),
            releaseRawCache: new ReleaseConnectionSessionRawCacheUseCase(connectionSessionStorage),
            deleteSessionEverywhere: new DeleteConnectionSessionEverywhereUseCase(connectionSessionStorage, transferRepository),
            reviewInterruptedSession: new ReviewInterruptedConnectionSessionUseCase(connectionSessionStorage),
            evictStaleRawLogs: new EvictStaleConnectionSessionRawLogsUseCase(connectionSessionStorage, connectionSessionStorage));

        var sessionLogAnalysisModel = new SessionLogAnalysisModel(
            state: new SessionLogAnalysisState(),
            decodeTimeline: new DecodeSessionLogTimelineUseCase(connectionSessionStorage, definitionRepository),
            prepareRawLog: new PrepareConnectionSessionRawLogUseCase(connectionSessionStorage, connectionSessionStorage, transferRepository));

        var connectionSessionLifecycleModel = new ConnectionSessionLifecycleModel(
            repository: connectionSessionStorage,
            rawLogRepository: connectionSessionStorage,
            acquisitionTerminationRepository: connectionSessionStorage,
            acquisitionDevice: MakeConnectionSessionAcquisitionDevice(),
            historyDidChange: () => connectionHistoryModel.Send(new ConnectionHistoryAction.LocalDataChanged()),
            endedSessionUploadRequested: session => connectionHistoryModel.Send(new ConnectionHistoryAction.SessionUploadRequested(session.Id)));

        var liveTelemetryModel = MakeLiveTelemetryModel(
            configuration,
            connectionSessionStorage,
            telemetry,
            definitionRepository,
            capabilityRepository,
            performanceEvents,
            activeSessionID: () => System.Threading.Tasks.Task.FromResult(connectionSessionLifecycleModel.ActiveSession?.Id),
            sessionDidEnd: reason => connectionSessionLifecycleModel.Send(new ConnectionSessionLifecycleAction.EndRequested(reason)),
            distanceDidChange: observation => connectionSessionLifecycleModel.Send(new ConnectionSessionLifecycleAction.DistanceObserved(observation)));

        var vehicleManagementModel = new VehicleManagementModel(
            state: new VehicleManagementState(),
            repository: vehicleRepository,
            // 暫定: デバイス担当の ELM327 識別をデモ付きで包んだものに差し替える。
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(new InterimDemoVehicleIdentificationAdapter()),
            photoImporter: photoImporter,
            pidCapabilityRepository: capabilityRepository,
            pidDefinitionRepository: definitionRepository,
            loadVehicleActivitySummaries: new LoadVehicleActivitySummariesUseCase(connectionSessionStorage),
            deleteVehicleWithSessions: new DeleteVehicleWithSessionsUseCase(
                sessionRepository: connectionSessionStorage,
                localSessionErasureRepository: connectionSessionStorage,
                sessionTransferRepository: transferRepository,
                vehicleRepository: vehicleRepository),
            vehicleSessionsDidDelete: () => connectionHistoryModel.Send(new ConnectionHistoryAction.RefreshRequested()),
            connectionVehicleDidResolve: (vehicle, endpoint, identification) =>
            {
                connectionSessionLifecycleModel.Send(new ConnectionSessionLifecycleAction.StartRequested());
                connectionSessionLifecycleModel.Send(new ConnectionSessionLifecycleAction.VehicleResolved(vehicle));
                var policy = new ZD8VehicleModelPolicy();
                var vehicleModelCode = policy.Matches(identification.Vin) || policy.Matches(identification.ObdIdentifier)
                    ? ZD8VehicleModelPolicy.ModelCode
                    : null;
                liveTelemetryModel.Send(new LiveTelemetryAction.StartRequested(endpoint, vehicle.Id, vehicleModelCode));
            });

        // 暫定: SQLite の整備記録と CloudKit の MaintenanceCatalog 同期に差し替える。
        var maintenanceRepository = new InterimMaintenanceRecordRepository();
        var maintenanceModel = new MaintenanceModel(
            state: new MaintenanceState(),
            repository: maintenanceRepository,
            synchronize: new SynchronizeMaintenanceRecordsUseCase(maintenanceRepository, new InterimLocalOnlyMaintenanceRemoteStore()),
            photoImporter: photoImporter);

        // 暫定: ネットワーク担当の設定ストア・既定アダプターの保存先に差し替える。
        var accountSettingsStore = new InterimAccountSettingsStore();
        var defaultAdapterStore = new InterimDefaultAdapterPreferenceStore();
        var accountSettingsModel = new AccountSettingsModel(new AccountSettings(), new AccountSettingsUseCase(accountSettingsStore));

        var authenticationModel = MakeAuthenticationSessionModel(
            arguments,
            connectionSessionStorage,
            transferRepository,
            accountSettingsStore,
            defaultAdapterStore,
            vehicleRepository,
            capabilityRepository);

        var startVehicleConnection = new StartVehicleConnectionUseCase(
            endpoint => vehicleManagementModel.Send(new VehicleManagementAction.IdentifyRequested(endpoint)));

        return new WindowsApplicationRoot(
            Authentication: authenticationModel,
            AccountSettings: accountSettingsModel,
            VehicleManagement: vehicleManagementModel,
            Maintenance: maintenanceModel,
            LiveTelemetry: liveTelemetryModel,
            ConnectionSessionLifecycle: connectionSessionLifecycleModel,
            ConnectionHistory: connectionHistoryModel,
            SessionLogAnalysis: sessionLogAnalysisModel,
            StartVehicleConnection: startVehicleConnection,
            MakeSettingsPresentationModel: () => MakeSettingsPresentationModel(defaultAdapterStore));
    }

    /// <summary>
    /// PID 定義・通信・取得証拠の保存を結び付けたライブ取得モデルを作ります。
    /// </summary>
    /// <remarks>責務: ライブ取得の依存（定義、通信、取得コントローラー）を 1 件のモデルへ注入します。</remarks>
    /// <returns>ライブ取得のモデル。</returns>
    private static LiveTelemetryModel MakeLiveTelemetryModel(
        ProductionConnectionSessionAcquisitionConfiguration configuration,
        InterimConnectionSessionStore connectionSessionStorage,
        InterimDemoOBDPIDTelemetryAdapter telemetry,
        IOBDPIDDefinitionRepository definitionRepository,
        IVehiclePIDCapabilityRepository capabilityRepository,
        IAcquisitionPerformanceEventPort performanceEvents,
        Func<System.Threading.Tasks.Task<ConnectionSessionID?>> activeSessionID,
        Action<ConnectionSessionEndReason> sessionDidEnd,
        Action<ConnectionSessionDistanceObservation> distanceDidChange)
    {
        var acquisitionController = new ConnectionSessionAcquisitionController(
            createManifest: new CreateConnectionSessionAcquisitionManifestUseCase(
                connectionSessionStorage,
                // 暫定: ネットワーク担当の Data/Mapping（BundleConnectionSessionAcquisitionEvidenceAdapter 相当）に差し替える。
                new InterimConnectionSessionAcquisitionEvidence(configuration.SchemaContractVersion, ConnectionSessionAcquisitionPlatform.MacOS)),
            persistBatch: new PersistAcquisitionBatchUseCase(
                connectionSessionStorage,
                telemetry,
                monotonicNanoseconds: MonotonicNanoseconds,
                performanceEvents: performanceEvents),
            activeSessionID: activeSessionID,
            manifestVersion: configuration.ManifestVersion,
            pollingPolicyVersion: configuration.PollingPolicyVersion,
            modelInputManifestVersion: configuration.ModelInputManifestVersion,
            formulaCanonicalizationVersion: configuration.FormulaCanonicalizationVersion,
            performanceEvents: performanceEvents);
        return new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(definitionRepository, telemetry),
            loadVehicleCapabilities: new LoadVehiclePIDCapabilitiesUseCase(capabilityRepository, telemetry, definitionRepository),
            acquisitionEvidence: acquisitionController,
            sessionDidEnd: sessionDidEnd,
            distanceDidChange: distanceDidChange,
            // 暫定: デバイス担当の SetThreadExecutionState によるスリープ抑止に差し替える（今は抑止しない）。
            systemSleepInhibitor: null);
    }

    /// <summary>
    /// 認証・資格情報の保存・アカウント削除を結び付けた認証セッションモデルを作ります。
    /// </summary>
    /// <remarks>責務: ローカルモードの認可と端末内データの消去を Authentication のユースケースへ注入します。</remarks>
    /// <returns>認証セッションのモデル。</returns>
    private static AuthenticationSessionModel MakeAuthenticationSessionModel(
        string[] arguments,
        InterimConnectionSessionStore connectionSessionStorage,
        IConnectionSessionTransferRepository transferRepository,
        InterimAccountSettingsStore accountSettingsStore,
        InterimDefaultAdapterPreferenceStore defaultAdapterStore,
        InterimVehicleRepository vehicleRepository,
        InterimVehiclePIDCapabilityRepository capabilityRepository)
    {
        // 暫定: ネットワーク担当の Sign in with Apple（Web）と資格情報の保存に差し替える。設定がないときはローカルモードのまま。
        var authorization = new InterimLocalModeAccountAuthorization();
        var sessionStore = new InterimAuthenticationSessionStore();
        var sessionRevocation = new InterimAccountSessionRevocation();
        var dataEraser = new InterimAccountDataEraser(
            accountSettingsStore,
            defaultAdapterStore,
            connectionSessionStorage,
            vehicleRepository,
            capabilityRepository);
        return new AuthenticationSessionModel(
            state: InitialAuthenticationState(arguments),
            restoreSession: new RestoreAuthenticationSessionUseCase(authorization, sessionStore),
            signInWithApple: new SignInWithAppleUseCase(authorization, sessionStore),
            deleteAccount: new DeleteAccountUseCase(sessionRevocation, transferRepository, vehicleRepository, dataEraser, sessionStore),
            remoteAccountLogout: new RemoteAccountLogoutUseCase(dataEraser, sessionStore),
            sessionRevocation: sessionRevocation);
    }

    /// <summary>
    /// アダプター探索と既定アダプター保存を結び付けた設定画面の表示モデルを作ります。
    /// </summary>
    /// <remarks>責務: 探索・保存の実装を DeviceConnection のユースケースと設定画面の表示モデルへ注入します。</remarks>
    /// <param name="defaultAdapterStore">既定アダプターの保存先。</param>
    /// <returns>設定画面の表示モデル。</returns>
    private static WindowsSettingsPresentationModel MakeSettingsPresentationModel(InterimDefaultAdapterPreferenceStore defaultAdapterStore)
    {
        // 暫定: デバイス担当の Windows 探索（シリアルポート・BLE）をデモ候補付きで包んだものに差し替える。
        var discoverAdapters = new DiscoverAdaptersUseCase(new InterimDemoAdapterDiscovery());
        return new WindowsSettingsPresentationModel(
            new WindowsSettingsState(),
            new LatestAdapterDiscoveryUseCase(discoverAdapters),
            new DefaultAdapterPreferenceUseCase(defaultAdapterStore));
    }

    /// <summary>
    /// 新しいセッションの取得元として表示する端末を返します。
    /// </summary>
    /// <remarks>
    /// 責務: 取得元端末の表示情報を決めます。
    /// Domain の <see cref="ConnectionSessionAcquisitionPlatform"/> に Windows がなく、macOS と名乗ると事実と違うため、
    /// 今は取得元を記録しません（履歴には「不明な端末」と出る）。Windows を足すかはリードの検討事項です。
    /// </remarks>
    /// <returns>取得元の端末。記録しないなら null。</returns>
    private static ConnectionSessionAcquisitionDevice? MakeConnectionSessionAcquisitionDevice() => null;

    /// <summary>
    /// 単調増加する経過時間をナノ秒で返します。
    /// </summary>
    /// <remarks>責務: <see cref="Stopwatch"/> の高分解能タイマーをナノ秒へ変換します。</remarks>
    /// <returns>経過ナノ秒。</returns>
    private static ulong MonotonicNanoseconds() =>
        (ulong)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));

    /// <summary>
    /// Debug の画面確認用の起動状態、または通常の「確認中」状態を返します。
    /// </summary>
    /// <remarks>責務: iOS 版と同じ起動引数（--ui-test-authenticated、--ui-test-signed-out）を認証の初期状態へ変換します。</remarks>
    /// <param name="arguments">コマンドライン引数。</param>
    /// <returns>認証の初期状態。</returns>
    private static AuthenticationState InitialAuthenticationState(string[] arguments)
    {
#if DEBUG
        if (arguments.Contains("--ui-test-authenticated", StringComparer.Ordinal))
        {
            return new AuthenticationState(
                phase: AuthenticationPhase.SignedIn,
                session: new AppleAccountSession("ui-test-apple-account"));
        }

        if (arguments.Contains("--ui-test-signed-out", StringComparer.Ordinal))
        {
            return new AuthenticationState(phase: AuthenticationPhase.SignedOut);
        }
#endif
        return new AuthenticationState();
    }
}
