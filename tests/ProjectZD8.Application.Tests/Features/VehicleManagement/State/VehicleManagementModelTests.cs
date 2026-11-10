using ProjectZD8.Application.Tests.TestSupport;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.VehicleManagement.State;

/// <summary>車両管理モデルが識別失敗を原因別の表示状態へ変換することを検証します。</summary>
/// <remarks>
/// Swift 版は Data 層の <c>DemoVehicleIdentificationAdapter</c> と <c>DemoOBDAdapter</c> を使います。
/// C# には Data 層がまだないため、同じ観測（合成VIN、メーカー、エンジン型式）を返すテスト用の境界をこのファイルに置いています。
/// </remarks>
public sealed class VehicleManagementModelTests
{
    /// <summary>テスト用のEXシリアル終端です。</summary>
    private static readonly OBDConnectionEndpoint Endpoint =
        new(OBDConnectionEndpointTransport.Serial, "/dev/cu.test", "OBDLink EX");

    /// <summary>全識別段階が実機報告用の安定診断コードを持つことを検証します。</summary>
    /// <remarks>責務: 車両識別段階と画面表示用診断コードの対応を固定します。</remarks>
    [Fact]
    public void IdentificationStagesExposeStableDiagnosticCodes()
    {
        Assert.Equal("ENDPOINT", VehicleIdentificationErrorStage.EndpointValidation.DiagnosticCode());
        Assert.Equal("TRANSPORT-CREATE", VehicleIdentificationErrorStage.TransportCreation.DiagnosticCode());
        Assert.Equal("TRANSPORT-OPEN", VehicleIdentificationErrorStage.TransportOpen.DiagnosticCode());
        Assert.Equal("ATZ", VehicleIdentificationErrorStage.AdapterReset.DiagnosticCode());
        Assert.Equal("AT-CONFIG", VehicleIdentificationErrorStage.AdapterConfiguration.DiagnosticCode());
        Assert.Equal("ATI", VehicleIdentificationErrorStage.AdapterIdentity.DiagnosticCode());
        Assert.Equal("0902-REQUEST", VehicleIdentificationErrorStage.VehicleIdentificationRequest.DiagnosticCode());
        Assert.Equal("0902-PARSE", VehicleIdentificationErrorStage.VehicleIdentificationParsing.DiagnosticCode());
        Assert.Equal("ATDP", VehicleIdentificationErrorStage.ProtocolDescription.DiagnosticCode());
    }

    /// <summary>デモ車両も通常登録操作を経てRepositoryへ保存します。</summary>
    /// <remarks>責務: デモ識別結果が保存省略されず通常の車両プロフィールとして登録されることを確認します。</remarks>
    [Fact]
    public async Task DemoVehicleUsesNormalRegistrationPersistence()
    {
        var repository = new VehicleRepositoryFake();
        var model = new VehicleManagementModel(
            state: new VehicleManagementState(),
            repository: repository,
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(new DemoVehicleIdentificationPortFake()),
            photoImporter: new VehiclePhotoImportPortFake());
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        await WaitUntil(() => model.State.HasLoadedVehicles);

        model.Send(new VehicleManagementAction.IdentifyRequested(DemoVehicleIdentificationPortFake.CandidateEndpoint));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.ConfirmingIdentification);
        model.Send(new VehicleManagementAction.IdentificationConfirmed());
        var draft = model.State.EditingVehicle;
        Assert.NotNull(draft);
        model.Send(new VehicleManagementAction.VehicleSaved(draft));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.Idle && model.State.Vehicles.Count == 1);

        var expectedVIN = DemoVehicleIdentificationPortFake.SyntheticVIN;
        Assert.Equal([expectedVIN], repository.SavedVehicles.Select(vehicle => vehicle.Vin));
        Assert.Equal([expectedVIN], model.State.Vehicles.Select(vehicle => vehicle.Vin));
    }

    /// <summary>未登録車両は登録保存が成功した後にだけ接続開始へ引き渡します。</summary>
    /// <remarks>責務: 車両登録の保存完了前後で接続確定通知の発行時点が変わることを確認します。</remarks>
    [Fact]
    public async Task UnregisteredVehicleResolvesConnectionAfterRegistrationSave()
    {
        var repository = new VehicleRepositoryFake();
        var resolvedVehicles = new List<VehicleProfile>();
        var model = new VehicleManagementModel(
            state: new VehicleManagementState(),
            repository: repository,
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(new DemoVehicleIdentificationPortFake()),
            photoImporter: new VehiclePhotoImportPortFake(),
            connectionVehicleDidResolve: (vehicle, _, _) => resolvedVehicles.Add(vehicle));
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        await WaitUntil(() => model.State.HasLoadedVehicles);

        model.Send(new VehicleManagementAction.IdentifyRequested(DemoVehicleIdentificationPortFake.CandidateEndpoint));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.ConfirmingIdentification);
        Assert.Empty(resolvedVehicles);

        model.Send(new VehicleManagementAction.IdentificationConfirmed());
        var draft = model.State.EditingVehicle;
        Assert.NotNull(draft);
        model.Send(new VehicleManagementAction.VehicleSaved(draft));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.Idle && resolvedVehicles.Count == 1);

        Assert.Equal([draft.Id], resolvedVehicles.Select(vehicle => vehicle.Id));
    }

    /// <summary>未登録車両の登録をキャンセルした場合は接続開始へ引き渡しません。</summary>
    /// <remarks>責務: 登録キャンセル後に接続確定通知が発行されないことを確認します。</remarks>
    [Fact]
    public async Task RegistrationCancellationDoesNotResolveConnection()
    {
        var resolutionCount = 0;
        var model = new VehicleManagementModel(
            state: new VehicleManagementState(),
            repository: new VehicleRepositoryFake(),
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(new DemoVehicleIdentificationPortFake()),
            photoImporter: new VehiclePhotoImportPortFake(),
            connectionVehicleDidResolve: (_, _, _) => resolutionCount += 1);
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        await WaitUntil(() => model.State.HasLoadedVehicles);

        model.Send(new VehicleManagementAction.IdentifyRequested(DemoVehicleIdentificationPortFake.CandidateEndpoint));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.ConfirmingIdentification);
        model.Send(new VehicleManagementAction.IdentificationConfirmed());
        Assert.Equal(VehicleManagementStatePhase.Registering, model.State.Phase);
        model.Send(new VehicleManagementAction.RegistrationCancelled());

        Assert.Equal(VehicleManagementStatePhase.Idle, model.State.Phase);
        Assert.Null(model.State.PendingIdentification);
        Assert.Null(model.State.EditingVehicle);
        Assert.Equal(0, resolutionCount);
    }

    /// <summary>登録確認中の再読込要求が確認対象の識別観測を破棄しないことを検証します。</summary>
    /// <remarks>責務: 進行中の新規車両登録確認がGarage表示時の一覧再読込によって中断されないことを確認します。</remarks>
    [Fact]
    public async Task RefreshRequestedDuringRegistrationConfirmationPreservesPendingIdentification()
    {
        var model = new VehicleManagementModel(
            state: new VehicleManagementState(),
            repository: new VehicleRepositoryFake(),
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(new DemoVehicleIdentificationPortFake()),
            photoImporter: new VehiclePhotoImportPortFake());
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        await WaitUntil(() => model.State.HasLoadedVehicles);

        model.Send(new VehicleManagementAction.IdentifyRequested(DemoVehicleIdentificationPortFake.CandidateEndpoint));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.ConfirmingIdentification);
        var pendingIdentification = model.State.PendingIdentification;
        Assert.NotNull(pendingIdentification);

        model.Send(new VehicleManagementAction.RefreshRequested());
        for (var index = 0; index < 10; index++)
        {
            await Task.Yield();
        }

        Assert.Equal(VehicleManagementStatePhase.ConfirmingIdentification, model.State.Phase);
        Assert.Equal(pendingIdentification, model.State.PendingIdentification);
    }

    /// <summary>識別境界の型付きエラーを診断可能な表示キーへ分離します。</summary>
    /// <remarks>責務: 実車識別の主要失敗段階が同じ汎用文言へ潰れないことを確認します。</remarks>
    [Fact]
    public async Task IdentificationErrorsUseStageSpecificFailureKeys()
    {
        (VehicleIdentificationError Error, string ExpectedKey)[] expectations =
        [
            (new VehicleIdentificationError.PidCatalogUnavailable(), "garage.error.pid_catalog"),
            (new VehicleIdentificationError.ConnectionFailed(), "garage.error.connection"),
            (new VehicleIdentificationError.ResponseTimedOut(), "garage.error.timeout"),
            (new VehicleIdentificationError.CommandRejected(), "garage.error.command_rejected"),
            (new VehicleIdentificationError.MalformedResponse(), "garage.error.malformed_response"),
        ];

        foreach (var (error, expectedKey) in expectations)
        {
            var model = MakeModel(error);
            model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
            await WaitUntil(() => model.State.HasLoadedVehicles);

            model.Send(new VehicleManagementAction.IdentifyRequested(Endpoint));
            await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.Failed);

            Assert.Equal(expectedKey, model.State.FailureKey);
        }
    }

    /// <summary>通信境界が返した失敗段階と原因を別々の表示状態として保持します。</summary>
    /// <remarks>責務: <c>0902</c> 要求の期限切れが段階コードと期限切れ文言を失わず状態へ反映されることを確認します。</remarks>
    [Fact]
    public async Task StagedIdentificationErrorPreservesStageAndCause()
    {
        var model = MakeModel(new VehicleIdentificationError.StageFailed(
            VehicleIdentificationErrorStage.VehicleIdentificationRequest,
            VehicleIdentificationErrorCause.ResponseTimedOut));
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        await WaitUntil(() => model.State.HasLoadedVehicles);

        model.Send(new VehicleManagementAction.IdentifyRequested(Endpoint));
        await WaitUntil(() => model.State.Phase == VehicleManagementStatePhase.Failed);

        Assert.Equal(VehicleIdentificationErrorStage.VehicleIdentificationRequest, model.State.IdentificationFailureStage);
        Assert.Equal("garage.error.timeout", model.State.FailureKey);
    }

    /// <summary>指定識別エラーを返す車両管理モデルを生成します。</summary>
    /// <remarks>責務: 1件の識別失敗を観測できるテスト用依存関係を組み立てます。</remarks>
    /// <param name="error">識別境界から返す型付きエラー。</param>
    /// <returns>空の車両一覧を読み込み済みにできる車両管理モデル。</returns>
    private static VehicleManagementModel MakeModel(VehicleIdentificationError error) =>
        new(
            state: new VehicleManagementState(),
            repository: new VehicleRepositoryFake(),
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(new FailingVehicleIdentificationPort(error)),
            photoImporter: new VehiclePhotoImportPortFake());

    /// <summary>指定条件が成立するまで非同期処理へ実行機会を渡します。</summary>
    /// <remarks>責務: 1件のモデル状態条件を上限時間内で待機し、成立しなければテストを失敗させます。</remarks>
    /// <param name="condition">成立を待つ状態条件。</param>
    /// <returns>待機の完了を表すタスク。</returns>
    private static async Task WaitUntil(Func<bool> condition)
    {
        Assert.True(await AsyncWait.UntilAsync(condition), "期待状態が上限時間までに成立しませんでした。");
    }

    /// <summary>車両管理テストへ空の保存状態を提供します。</summary>
    private sealed class VehicleRepositoryFake : IVehicleRepository
    {
        /// <summary>保存要求として受け取った車両の保存先です。</summary>
        private readonly List<VehicleProfile> savedVehicles = [];

        /// <summary>保存要求として受け取った車両です。</summary>
        public IReadOnlyList<VehicleProfile> SavedVehicles => savedVehicles;

        /// <summary>空の登録車両一覧を返します。</summary>
        /// <remarks>責務: 指定アカウントのテスト用車両一覧を空として返します。</remarks>
        /// <param name="accountIdentifier">テスト用アカウント識別子。</param>
        /// <returns>空の車両一覧。</returns>
        public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<VehicleProfile>>([]);

        /// <summary>保存要求を記録します。</summary>
        /// <remarks>責務: 車両保存要求をテストから観測できるよう記録します。</remarks>
        /// <param name="vehicle">保存対象として受け取る車両。</param>
        /// <param name="accountIdentifier">テスト用アカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier)
        {
            savedVehicles.Add(vehicle);
            return Task.CompletedTask;
        }

        /// <summary>テストでは車両を削除しません。</summary>
        /// <remarks>責務: 車両削除要求を副作用なしで受理します。</remarks>
        /// <param name="id">削除対象として受け取る車両ID。</param>
        /// <param name="accountIdentifier">テスト用アカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteVehicle(VehicleID id, string accountIdentifier) => Task.CompletedTask;
    }

    /// <summary>注入された識別エラーだけを返す境界です。</summary>
    private sealed class FailingVehicleIdentificationPort : IVehicleIdentificationPort
    {
        /// <summary>すべての識別要求で返すエラーです。</summary>
        private readonly VehicleIdentificationError error;

        /// <summary>返すエラーを保持して生成します。</summary>
        /// <remarks>責務: 1件の識別エラーを応答として固定します。</remarks>
        /// <param name="error">返す識別エラー。</param>
        public FailingVehicleIdentificationPort(VehicleIdentificationError error)
        {
            this.error = error;
        }

        /// <summary>指定された型付きエラーで識別を失敗させます。</summary>
        /// <remarks>責務: 1件の車両識別要求へ注入済みエラーを返します。</remarks>
        /// <param name="endpoint">テストでは参照しない接続終端。</param>
        /// <returns>返りません。</returns>
        /// <exception cref="VehicleIdentificationException">初期化時に注入された識別エラー。</exception>
        public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint) =>
            Task.FromException<VehicleIdentificationSnapshot>(new VehicleIdentificationException(error));
    }

    /// <summary>Swift 版のデモ識別アダプターと同じ観測を返すテスト用境界です。</summary>
    private sealed class DemoVehicleIdentificationPortFake : IVehicleIdentificationPort
    {
        /// <summary>先頭USBデモ候補の接続終端です。</summary>
        public static readonly OBDConnectionEndpoint CandidateEndpoint = new(
            new DiscoveredAdapter(
                id: "usb:projectzd8-demo",
                transportMode: AdapterTransportMode.Usb,
                displayName: "DEMO USB",
                systemIdentifier: "projectzd8://demo-usb",
                isConnected: true,
                manufacturerName: "RevTorque Insight",
                productName: "Demo OBD Adapter",
                serialNumber: "ZD8-DEMO-0001",
                vendorIdentifier: "DEMO",
                productIdentifier: "USB"));

        /// <summary>先頭USBデモ候補に割り当てられる合成VINです。</summary>
        public const string SyntheticVIN = "TESTZD8CXR0000001";

        /// <summary>デモVINと車両情報を実通信観測と同じ型で返します。</summary>
        /// <remarks>責務: デモ終端への1件の識別要求を通常登録可能な車両観測へ変換します。</remarks>
        /// <param name="endpoint">デモOBD接続終端。</param>
        /// <returns>合成VINとデモ車両フィールド。</returns>
        /// <exception cref="VehicleIdentificationException">デモ以外の終端の場合。</exception>
        public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint)
        {
            if (endpoint.SystemIdentifier != CandidateEndpoint.SystemIdentifier)
            {
                return Task.FromException<VehicleIdentificationSnapshot>(
                    new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported()));
            }

            return Task.FromResult(new VehicleIdentificationSnapshot(
                vin: SyntheticVIN,
                fields:
                [
                    new VehicleIdentificationSnapshot.Field("manufacturer", "Manufacturer", "RevTorque Demo Motors", endpoint.DisplayName),
                    new VehicleIdentificationSnapshot.Field("engineModel", "Engine Model", "ZD8-SIM-24", endpoint.DisplayName),
                    new VehicleIdentificationSnapshot.Field("obdProtocol", "OBD Protocol", "ISO 15765-4 (simulated)", endpoint.DisplayName),
                ],
                rawResponses:
                [
                    new VehicleIdentificationSnapshot.RawResponse("vehicleIdentificationNumber", $"49 02 01 {SyntheticVIN}"),
                ],
                observedAt: DateTimeOffset.UtcNow));
        }
    }

    /// <summary>車両管理テストで画像取込を使用不能に保つ境界です。</summary>
    private sealed class VehiclePhotoImportPortFake : IVehiclePhotoImportPort
    {
        /// <summary>空の画像データを返します。</summary>
        /// <remarks>責務: 画像取込要求を外部ファイルへアクセスせず完了します。</remarks>
        /// <param name="url">テストでは参照しない画像URL。</param>
        /// <returns>空の画像データ。</returns>
        public Task<byte[]> ImportPhoto(Uri url) => Task.FromResult(Array.Empty<byte>());
    }
}
