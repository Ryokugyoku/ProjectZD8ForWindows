using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.Analysis.UseCases;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Authentication.UseCases;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.Ports;
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
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Configuration;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Data.Diagnostics;
using ProjectZD8.Data.Files;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Persistence;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Windows.Authentication;
using ProjectZD8.Data.Windows.Devices;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;

namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// Windows アプリで使う実装の依存関係を組み立てます（iOS 版の <c>MacOSApplicationComposition</c> と <c>ProjectZD8App.init</c>）。
/// </summary>
/// <remarks>
/// 保存はマスタ系を SQLite、生ログと取得の証跡を追記専用のログストアに置きます（iOS 版は全部 GRDB）。
/// Apple と CloudKit の設定（appsettings.Local.json か環境変数 PROJECTZD8_*）がなければ、アカウントなしのローカルモードで動きます。
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
        var dataDirectory = WindowsApplicationDataLocation.DataDirectory();
        var settingsPath = WindowsApplicationDataLocation.LocalSettingsPath(dataDirectory);
        // 機能フラグ（AppleSignIn:Enabled）が無効なら、設定がそろっていても「設定なし」として扱う（Coming Soon）。
        var loadedConfiguration = CloudServiceConfigurationLoader.Load(settingsPath);

        // 診断ログの出力先を、ほかの何よりも先に差し込む（app.start にローカルモードかを書くため、設定の読み込みだけ先に行う）。
        var diagnostics = WindowsDiagnosticsBootstrap.Start(dataDirectory, settingsPath, isLocalMode: !loadedConfiguration.IsAppleSignInActive);
        if (loadedConfiguration.AppleSignInAvailability == AppleSignInAvailability.MissingSettings)
        {
            CompositionLog.AppleSignInSettingsIncomplete(
                CompositionLog.App,
                hasServicesId: !string.IsNullOrWhiteSpace(loadedConfiguration.AppleServicesId),
                hasRedirectUri: loadedConfiguration.AppleRedirectUri is not null,
                hasApiToken: !string.IsNullOrWhiteSpace(loadedConfiguration.CloudKitApiToken),
                cloudKitEnvironment: loadedConfiguration.CloudKitEnvironment);
        }

        var cloudConfiguration = loadedConfiguration.ForActiveFeatures();
        var acquisitionConfiguration = ProductionConnectionSessionAcquisitionConfiguration.Phase4G;
        var performanceEvents = MakeAcquisitionPerformanceEventPort();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var defaults = new JsonFileLocalKeyValueStore(Path.Combine(dataDirectory, "preferences.json"));
        var secretStore = WindowsSecretStoreFactory.Create(Path.Combine(dataDirectory, "secrets"));

        // CloudKit。設定がなければ、どのリポジトリも端末内だけで動く（取得は空、削除は何もしない）。
        var cloudKit = new CloudKitWebServicesClient(httpClient, cloudConfiguration, new SecretStoreCloudKitWebAuthTokenStore(secretStore));
        var callbackBroker = new AuthorizationCallbackBroker(cloudConfiguration.CallbackScheme);
        var browserLauncher = new WindowsExternalBrowserLauncher();
        ISynchronizedKeyValueStore synchronizedStore = cloudConfiguration.IsConfigured
            ? new CloudKitSynchronizedKeyValueStore(cloudKit)
            : new InMemorySynchronizedKeyValueStore();

        var connectionSessionStorage = OpenConnectionSessionRepository(dataDirectory, performanceEvents);
        IConnectionSessionTransferRepository transferRepository = new CloudKitConnectionSessionTransferRepository(cloudKit);
        var definitionRepository = MakeOBDPIDDefinitionRepository(dataDirectory);
        var capabilityRepository = MakeVehiclePIDCapabilityRepository(dataDirectory);
        var vehicleRepository = new CloudKitVehicleRepository(
            SqliteVehicleRepository.OpenApplicationRepository(dataDirectory),
            cloudKit,
            defaults);
        var telemetry = new DemoAwareOBDPIDTelemetryAdapter(
            live: new OBDLinkEXPIDTelemetryAdapter(WindowsOBDCommandTransportFactory.Make),
            demo: new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeed.Definitions));

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
            acquisitionConfiguration,
            connectionSessionStorage,
            telemetry,
            definitionRepository,
            capabilityRepository,
            performanceEvents,
            activeSessionID: () => Task.FromResult(connectionSessionLifecycleModel.ActiveSession?.Id),
            sessionDidEnd: reason => connectionSessionLifecycleModel.Send(new ConnectionSessionLifecycleAction.EndRequested(reason)),
            distanceDidChange: observation => connectionSessionLifecycleModel.Send(new ConnectionSessionLifecycleAction.DistanceObserved(observation)));

        var vehicleManagementModel = new VehicleManagementModel(
            state: new VehicleManagementState(),
            repository: vehicleRepository,
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(
                new DemoAwareVehicleIdentificationAdapter(
                    live: new SerialELMVehicleIdentificationAdapter(WindowsOBDCommandTransportFactory.Make),
                    demo: new DemoVehicleIdentificationAdapter())),
            photoImporter: new VehiclePhotoFileImporter(),
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

        var maintenanceRepository = MakeMaintenanceRepository(dataDirectory);
        var maintenanceModel = new MaintenanceModel(
            state: new MaintenanceState(),
            repository: maintenanceRepository,
            synchronize: new SynchronizeMaintenanceRecordsUseCase(maintenanceRepository, new CloudKitMaintenanceRemoteStore(cloudKit)),
            photoImporter: new MaintenancePhotoFileImporter());

        var accountSettingsStore = new SynchronizedAccountSettingsStore(defaults, synchronizedStore);
        var defaultAdapterStore = new JsonFileDefaultAdapterPreferenceStore(dataDirectory);
        var accountSettingsModel = new AccountSettingsModel(new AccountSettings(), new AccountSettingsUseCase(accountSettingsStore));

        var authenticationModel = MakeAuthenticationSessionModel(
            arguments,
            dataDirectory,
            cloudConfiguration,
            httpClient,
            callbackBroker,
            browserLauncher,
            secretStore,
            defaults,
            synchronizedStore,
            connectionSessionStorage,
            transferRepository,
            accountSettingsStore,
            defaultAdapterStore,
            vehicleRepository,
            capabilityRepository);

        var startVehicleConnection = new StartVehicleConnectionUseCase(
            endpoint => vehicleManagementModel.Send(new VehicleManagementAction.IdentifyRequested(endpoint)));

        var promptedRequest = new PromptedVehicleConnectionRequestBuffer();
        var connectionPrompt = new InAppDefaultAdapterConnectionPrompt();
        var arrivalPromptModel = new WindowsAdapterArrivalPromptModel(connectionPrompt.Accept, connectionPrompt.Decline);
        connectionPrompt.PromptRequested += (_, endpoint) => arrivalPromptModel.Present(endpoint);
        connectionPrompt.PromptDismissed += (_, _) => arrivalPromptModel.Dismiss();
        var promptForDefaultAdapterConnection = new PromptForDefaultAdapterConnectionUseCase(
            new DefaultAdapterPreferenceUseCase(defaultAdapterStore),
            new WindowsDefaultAdapterArrivalMonitor(),
            connectionPrompt,
            promptedRequest.Store);

        var cloudKitAuthenticator = new CloudKitWebAuthenticator(cloudKit, callbackBroker, browserLauncher);
        var cloudSyncModel = new WindowsCloudSyncModel(
            availability: () => cloudKit.Availability switch
            {
                CloudKitAvailability.Available => WindowsCloudSyncAvailability.Available,
                CloudKitAvailability.SignedOut => WindowsCloudSyncAvailability.SignedOut,
                _ => WindowsCloudSyncAvailability.NotConfigured,
            },
            signIn: () => cloudKitAuthenticator.SignIn(),
            signOut: cloudKitAuthenticator.SignOut);
        cloudKit.AvailabilityChanged += (_, _) => cloudSyncModel.AvailabilityDidChange();

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
            MakeSettingsPresentationModel: () => MakeSettingsPresentationModel(defaultAdapterStore),
            IsLocalMode: !loadedConfiguration.IsAppleSignInActive,
            CloudSync: cloudSyncModel,
            PromptForDefaultAdapterConnection: promptForDefaultAdapterConnection,
            PromptedVehicleConnectionRequest: promptedRequest,
            AdapterArrivalPrompt: arrivalPromptModel,
            DeliverProtocolActivation: uri => callbackBroker.TryDeliver(uri),
            SynchronizeAccountState: () => synchronizedStore is CloudKitSynchronizedKeyValueStore cloudStore
                ? cloudStore.SynchronizeAsync()
                : Task.CompletedTask,
            Shutdown: () =>
            {
                promptForDefaultAdapterConnection.Stop();
                (connectionSessionStorage as IDisposable)?.Dispose();
                httpClient.Dispose();
                diagnostics?.Stop();
            },
            Diagnostics: diagnostics);
    }

    /// <summary>
    /// 接続セッション一式（SQLite の概要＋ログストアの生ログ・取得証跡）を開きます。
    /// </summary>
    /// <remarks>責務: 製品の保存先を開き、開けなければ明示的に使えない保存先へ切り替えます。</remarks>
    /// <param name="dataDirectory">データのフォルダ。</param>
    /// <param name="performanceEvents">取得証拠の書込みを知らせる計測境界。</param>
    /// <returns>接続セッションの保存一式。</returns>
    private static IConnectionSessionStorage OpenConnectionSessionRepository(
        string dataDirectory,
        IAcquisitionPerformanceEventPort performanceEvents)
    {
        try
        {
            return SqliteConnectionSessionRepository.OpenApplicationRepository(dataDirectory, performanceEvents);
        }
        catch (Exception exception)
        {
            CompositionLog.StoreOpenFailed(CompositionLog.StoreSqlite, exception, "connection-sessions");
            return new UnavailableConnectionSessionRepository();
        }
    }

    /// <summary>
    /// 製品の PID 定義 DB を開き、確認済みの初期データを非破壊で登録します。
    /// </summary>
    /// <remarks>責務: PID 定義の永続化を、使える SQLite 実装か明示的に使えない境界へ変換します。</remarks>
    /// <param name="dataDirectory">データのフォルダ。</param>
    /// <returns>初期データ登録済みの PID 定義リポジトリ。準備に失敗したら使えないリポジトリ。</returns>
    private static IOBDPIDDefinitionRepository MakeOBDPIDDefinitionRepository(string dataDirectory)
    {
        try
        {
            var repository = SqliteOBDPIDDefinitionRepository.OpenApplicationRepository(dataDirectory);
            StandardOBDPIDSeed.Install(repository);
            ZD8OBDPIDSeed.Install(repository);
            return repository;
        }
        catch (Exception exception)
        {
            CompositionLog.StoreOpenFailed(CompositionLog.StoreSqlite, exception, "pid-definitions");
            return new UnavailableOBDPIDDefinitionRepository();
        }
    }

    /// <summary>
    /// 製品 DB の車両別対応 PID の保存先を作ります。
    /// </summary>
    /// <remarks>責務: 車両別対応 PID の永続化を、使える実装か明示的に使えない境界へ変換します。</remarks>
    /// <param name="dataDirectory">データのフォルダ。</param>
    /// <returns>車両別対応 PID のリポジトリ。</returns>
    private static IVehiclePIDCapabilityStorage MakeVehiclePIDCapabilityRepository(string dataDirectory)
    {
        try
        {
            return SqliteVehiclePIDCapabilityRepository.OpenApplicationRepository(dataDirectory);
        }
        catch (Exception exception)
        {
            CompositionLog.StoreOpenFailed(CompositionLog.StoreSqlite, exception, "vehicle-pid-capabilities");
            return new UnavailableVehiclePIDCapabilityRepository();
        }
    }

    /// <summary>
    /// 製品 DB の整備記録の保存先を作ります。
    /// </summary>
    /// <remarks>責務: 整備記録の永続化を、使える SQLite 実装か明示的に使えない境界へ変換します。</remarks>
    /// <param name="dataDirectory">データのフォルダ。</param>
    /// <returns>整備記録のリポジトリ。</returns>
    private static IMaintenanceRecordRepository MakeMaintenanceRepository(string dataDirectory)
    {
        try
        {
            return SqliteMaintenanceRecordRepository.OpenApplicationRepository(dataDirectory);
        }
        catch (Exception exception)
        {
            CompositionLog.StoreOpenFailed(CompositionLog.StoreSqlite, exception, "maintenance-records");
            return new UnavailableMaintenanceRecordRepository();
        }
    }

    /// <summary>
    /// PID 定義・通信・取得証拠の保存を結び付けたライブ取得モデルを作ります。
    /// </summary>
    /// <remarks>責務: ライブ取得の依存（定義、通信、取得コントローラー、スリープ抑止）を 1 件のモデルへ注入します。</remarks>
    /// <returns>ライブ取得のモデル。</returns>
    private static LiveTelemetryModel MakeLiveTelemetryModel(
        ProductionConnectionSessionAcquisitionConfiguration configuration,
        IConnectionSessionStorage connectionSessionStorage,
        IOBDPIDTelemetryPort telemetry,
        IOBDPIDDefinitionRepository definitionRepository,
        IVehiclePIDCapabilityRepository capabilityRepository,
        IAcquisitionPerformanceEventPort performanceEvents,
        Func<Task<ConnectionSessionID?>> activeSessionID,
        Action<ConnectionSessionEndReason> sessionDidEnd,
        Action<ConnectionSessionDistanceObservation> distanceDidChange)
    {
        var acquisitionController = new ConnectionSessionAcquisitionController(
            createManifest: new CreateConnectionSessionAcquisitionManifestUseCase(
                connectionSessionStorage,
                AssemblyConnectionSessionAcquisitionEvidenceAdapter.FromAssembly(
                    typeof(WindowsApplicationComposition).Assembly,
                    configuration.SchemaContractVersion,
                    ConnectionSessionAcquisitionPlatform.MacOS)),
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
            systemSleepInhibitor: new SetThreadExecutionStateVehicleConnectionSystemSleepInhibitor());
    }

    /// <summary>
    /// 認証・資格情報の保存・アカウント削除を結び付けた認証セッションモデルを作ります。
    /// </summary>
    /// <remarks>
    /// 責務: 機能フラグが有効で設定もあれば Web 版の Sign in with Apple（ローカルモードのデータの引き継ぎ付き）、なければローカルモードの認可を、
    /// 資格情報の保存と端末内データの消去とともに Authentication のユースケースへ注入します。
    /// </remarks>
    /// <returns>認証セッションのモデル。</returns>
    private static AuthenticationSessionModel MakeAuthenticationSessionModel(
        string[] arguments,
        string dataDirectory,
        CloudServiceConfiguration cloudConfiguration,
        HttpClient httpClient,
        AuthorizationCallbackBroker callbackBroker,
        IExternalBrowserLauncher browserLauncher,
        ISecretStore secretStore,
        ILocalKeyValueStore defaults,
        ISynchronizedKeyValueStore synchronizedStore,
        IConnectionSessionStorage connectionSessionStorage,
        IConnectionSessionTransferRepository transferRepository,
        SynchronizedAccountSettingsStore accountSettingsStore,
        JsonFileDefaultAdapterPreferenceStore defaultAdapterStore,
        CloudKitVehicleRepository vehicleRepository,
        IVehiclePIDCapabilityStorage capabilityRepository)
    {
        // cloudConfiguration は機能フラグを当てはめた後の設定。フラグが無効（Coming Soon）ならローカルモードの認可になる。
        IAppleAccountAuthorizationPort authorization = cloudConfiguration.IsConfigured
            ? new LocalAccountDataAdoptingAuthorizationPort(
                new WebAppleAccountAuthorizationClient(
                    cloudConfiguration,
                    callbackBroker,
                    browserLauncher,
                    new AppleIdentityTokenValidator(new AppleJsonWebKeySetCache(httpClient), cloudConfiguration.AppleServicesId!)),
                defaults,
                SqliteAccountDataReassignment.OpenApplicationRepository(dataDirectory).Reassign,
                accountSettingsStore)
            : new LocalAccountAuthorizationAdapter(defaults);
        var sessionStore = new SecretStoreAuthenticationSessionStore(secretStore);
        var sessionRevocation = new SynchronizedAccountSessionRevocationStore(defaults, synchronizedStore);
        var dataEraser = new ProjectZD8AccountDataEraser(
            accountSettingsStore,
            defaultAdapterStore.Remove,
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
    /// <remarks>責務: COM ポート・Bluetooth Classic・BLE の探索をデモ候補付きで、既定アダプターの保存とともに設定画面へ注入します。</remarks>
    /// <param name="defaultAdapterStore">既定アダプターの保存先。</param>
    /// <returns>設定画面の表示モデル。</returns>
    private static WindowsSettingsPresentationModel MakeSettingsPresentationModel(IDefaultAdapterPreferencePort defaultAdapterStore)
    {
        var discovery = new DemoIncludedAdapterDiscovery(
            new CombinedAdapterDiscovery(new WindowsSystemAdapterDiscovery(), new WindowsBluetoothLEAdapterDiscovery()),
            DemoOBDAdapter.UsbCandidates);
        var discoverAdapters = new DiscoverAdaptersUseCase(discovery);
        return new WindowsSettingsPresentationModel(
            new WindowsSettingsState(),
            new LatestAdapterDiscoveryUseCase(discoverAdapters),
            new DefaultAdapterPreferenceUseCase(defaultAdapterStore));
    }

    /// <summary>
    /// 計測を明示したときだけ ETW の取得性能イベントを出す境界を作ります。
    /// </summary>
    /// <remarks>責務: 環境変数の実行識別子を、取得性能イベントの出力先へ変換します。</remarks>
    /// <returns>有効な識別子があれば ETW の境界、なければ何もしない境界。</returns>
    private static IAcquisitionPerformanceEventPort MakeAcquisitionPerformanceEventPort() =>
        (IAcquisitionPerformanceEventPort?)EventSourceAcquisitionPerformanceAdapter.MakeIfValid(
            Environment.GetEnvironmentVariable("PROJECTZD8_ACQUISITION_PERFORMANCE_RUN_ID"))
        ?? new NoOpAcquisitionPerformanceEventPort();

    /// <summary>
    /// 新しいセッションの取得元として表示する端末を返します。
    /// </summary>
    /// <remarks>
    /// 責務: 取得元端末の表示情報を決めます。
    /// Domain の <see cref="ConnectionSessionAcquisitionPlatform"/> に Windows がなく、macOS と名乗ると事実と違うため、
    /// 今は取得元を記録しません（履歴には「不明な端末」と出る）。PROGRESS.md の検討事項を参照。
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
