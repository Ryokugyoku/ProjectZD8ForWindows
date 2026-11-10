using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.State;

/// <summary>車両一覧、OBD識別確認、登録編集を永続化とOBD識別へ結び付けます。</summary>
/// <remarks>
/// UI スレッド（単一スレッド）から呼ぶ前提です。
/// Swift で <c>Task { ... }</c> として起動していた処理は、async メソッドを直接開始して最初の await まで同期で進めます。
/// </remarks>
public sealed class VehicleManagementModel : INotifyPropertyChanged
{
    /// <summary>車両の読込、保存、削除を行う永続化境界です。</summary>
    private readonly IVehicleRepository repository;

    /// <summary>接続操作を同種の車両識別子照合へ変換するユースケースです。</summary>
    private readonly IdentifyVehicleForConnectionUseCase identifyForConnection;

    /// <summary>ユーザーが選択した画像を読み込む境界です。</summary>
    private readonly IVehiclePhotoImportPort photoImporter;

    /// <summary>車両別対応PID設定の永続化境界です。</summary>
    private readonly IVehiclePIDCapabilityRepository? pidCapabilityRepository;

    /// <summary>PID名称を解決するカタログ境界です。</summary>
    private readonly IOBDPIDDefinitionRepository? pidDefinitionRepository;

    /// <summary>接続履歴を車両別の軽量集計へ変換するユースケースです。</summary>
    private readonly LoadVehicleActivitySummariesUseCase? loadVehicleActivitySummaries;

    /// <summary>車両と関連セッションを全端末から削除するユースケースです。</summary>
    private readonly DeleteVehicleWithSessionsUseCase? deleteVehicleWithSessions;

    /// <summary>車両関連セッション削除後に履歴表示へ再読込を通知する処理です。</summary>
    private readonly Action vehicleSessionsDidDelete;

    /// <summary>接続対象車両、終端、編集前のOBD識別観測をLoggingと監視開始へ通知する処理です。</summary>
    private readonly Action<VehicleProfile, OBDConnectionEndpoint, VehicleIdentificationSnapshot> connectionVehicleDidResolve;

    /// <summary>現在の車両を所有するAppleアカウント識別子です。</summary>
    private string? accountIdentifier;

    /// <summary>現在状態の保存先です。</summary>
    private VehicleManagementState state;

    /// <summary>初期状態、注入済み境界、車両確定通知先を使ってモデルを生成します。</summary>
    /// <remarks>責務: 車両管理状態を永続化、OBD識別、画像取込の各境界へ結び付けます。</remarks>
    /// <param name="state">初期表示状態。</param>
    /// <param name="repository">アカウントスコープ付き車両保存先。</param>
    /// <param name="identifyForConnection">VINまたは非VIN識別子の取得と登録照合を行うユースケース。</param>
    /// <param name="photoImporter">選択画像をプロフィールデータへ変換する境界。</param>
    /// <param name="pidCapabilityRepository">車両別対応PID設定の保存先。</param>
    /// <param name="pidDefinitionRepository">PID名称を解決するカタログ境界。</param>
    /// <param name="loadVehicleActivitySummaries">接続履歴をGarage表示用集計へ変換するユースケース。</param>
    /// <param name="deleteVehicleWithSessions">車両と関連セッションを全端末から削除するユースケース。</param>
    /// <param name="vehicleSessionsDidDelete">車両関連セッション削除後に履歴表示へ再読込を通知する処理。</param>
    /// <param name="connectionVehicleDidResolve">接続対象車両、終端、編集前のOBD識別観測を通知する処理。</param>
    public VehicleManagementModel(
        VehicleManagementState state,
        IVehicleRepository repository,
        IdentifyVehicleForConnectionUseCase identifyForConnection,
        IVehiclePhotoImportPort photoImporter,
        IVehiclePIDCapabilityRepository? pidCapabilityRepository = null,
        IOBDPIDDefinitionRepository? pidDefinitionRepository = null,
        LoadVehicleActivitySummariesUseCase? loadVehicleActivitySummaries = null,
        DeleteVehicleWithSessionsUseCase? deleteVehicleWithSessions = null,
        Action? vehicleSessionsDidDelete = null,
        Action<VehicleProfile, OBDConnectionEndpoint, VehicleIdentificationSnapshot>? connectionVehicleDidResolve = null)
    {
        this.state = state;
        this.repository = repository;
        this.identifyForConnection = identifyForConnection;
        this.photoImporter = photoImporter;
        this.pidCapabilityRepository = pidCapabilityRepository;
        this.pidDefinitionRepository = pidDefinitionRepository;
        this.loadVehicleActivitySummaries = loadVehicleActivitySummaries;
        this.deleteVehicleWithSessions = deleteVehicleWithSessions;
        this.vehicleSessionsDidDelete = vehicleSessionsDidDelete ?? (() => { });
        this.connectionVehicleDidResolve = connectionVehicleDidResolve ?? ((_, _, _) => { });
    }

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Platformが描画する現在状態です。</summary>
    public VehicleManagementState State
    {
        get => state;
        set => SetProperty(ref state, value);
    }

    /// <summary>1件の型付き操作を車両管理状態と副作用へ反映します。</summary>
    /// <remarks>責務: 車両管理操作を対応する非同期ワークフロー1件へ振り分けます。</remarks>
    /// <param name="action">PlatformまたはAppから通知された操作。</param>
    public void Send(VehicleManagementAction action)
    {
        switch (action)
        {
            case VehicleManagementAction.AccountIdentifierChanged changed:
                ActivateAccount(changed.Identifier);
                break;
            case VehicleManagementAction.IdentifyRequested requested:
                _ = IdentifyVehicle(requested.Endpoint);
                break;
            case VehicleManagementAction.IdentificationRetryRequested:
                if (State.ConnectionEndpoint is { } endpoint)
                {
                    _ = IdentifyVehicle(endpoint);
                }

                break;
            case VehicleManagementAction.IdentificationConfirmed:
                BeginRegistration();
                break;
            case VehicleManagementAction.RegistrationCancelled:
                CancelRegistration();
                break;
            case VehicleManagementAction.VehicleSaved saved:
                _ = Save(saved.Vehicle);
                break;
            case VehicleManagementAction.EditRequested requested:
                BeginEditing(requested.Id);
                break;
            case VehicleManagementAction.EditCancelled:
                FinishEditing();
                break;
            case VehicleManagementAction.PhotoSelected selected:
                _ = ImportPhoto(selected.Url);
                break;
            case VehicleManagementAction.VehicleDeleted deleted:
                _ = Delete(deleted.Id);
                break;
            case VehicleManagementAction.RefreshRequested:
                _ = RefreshVehicleCatalogIfIdle();
                break;
            case VehicleManagementAction.PidSettingsRequested requested:
                LoadPIDSettings(requested.VehicleID);
                break;
            case VehicleManagementAction.PidCollectionChanged changed:
                UpdatePIDSelection(changed.Request, changed.IsEnabled);
                break;
            case VehicleManagementAction.PidSettingsClosed:
                State = State with { PidSettingsVehicleID = null, PidSelectionItems = [] };
                break;
        }
    }

    /// <summary>指定車両の対応PIDと名称を収集設定状態へ読み込みます。</summary>
    /// <remarks>責務: 1件の車両IDをPIDテーブル名称付き収集選択一覧へ変換します。</remarks>
    /// <param name="vehicleID">設定対象のアプリ内車両ID。</param>
    private void LoadPIDSettings(VehicleID vehicleID)
    {
        if (pidCapabilityRepository is null || pidDefinitionRepository is null)
        {
            State = State with { FailureKey = "garage.pid_settings.error" };
            return;
        }

        try
        {
            var definitions = pidDefinitionRepository.Definitions();
            var names = definitions.ToDictionary(
                definition => new OBDPIDRequest(definition.Service, definition.Pid),
                definition => definition.NameKey);
            var modelCodes = definitions.ToDictionary(
                definition => new OBDPIDRequest(definition.Service, definition.Pid),
                definition => definition.VehicleModelCode);
            var items = pidCapabilityRepository.Capabilities(vehicleID)
                .Select(capability => new VehiclePIDSelectionItem(
                    id: capability.Id.Request,
                    nameKey: names.GetValueOrDefault(capability.Id.Request),
                    isEnabled: capability.IsCollectionEnabled,
                    vehicleModelCode: modelCodes.GetValueOrDefault(capability.Id.Request)))
                .ToArray();
            State = State with
            {
                PidSelectionItems = items,
                PidSettingsVehicleID = vehicleID,
                FailureKey = null,
            };
        }
        catch (Exception)
        {
            State = State with { FailureKey = "garage.pid_settings.error" };
        }
    }

    /// <summary>現在表示中の車両で1件のPID収集選択を更新します。</summary>
    /// <remarks>責務: 1件のPID選択操作を永続化して現在表示へ反映します。</remarks>
    /// <param name="request">更新するService/PID。</param>
    /// <param name="isEnabled">新しい収集有効状態。</param>
    private void UpdatePIDSelection(OBDPIDRequest request, bool isEnabled)
    {
        if (State.PidSettingsVehicleID is not { } vehicleID || pidCapabilityRepository is null)
        {
            return;
        }

        try
        {
            pidCapabilityRepository.SetCollectionEnabled(isEnabled, request, vehicleID);
            var items = State.PidSelectionItems.ToList();
            var index = items.FindIndex(item => item.Id == request);
            if (index >= 0)
            {
                items[index] = items[index] with { IsEnabled = isEnabled };
                State = State with { PidSelectionItems = items };
            }
        }
        catch (Exception)
        {
            State = State with { FailureKey = "garage.pid_settings.error" };
        }
    }

    /// <summary>新しいアカウントスコープを有効化または解除します。</summary>
    /// <remarks>責務: 1件の認証識別子変更を車両状態の初期化と再読込へ反映します。</remarks>
    /// <param name="identifier">新しいAppleアカウント識別子。</param>
    private void ActivateAccount(string? identifier)
    {
        if (string.Equals(identifier, accountIdentifier, StringComparison.Ordinal))
        {
            return;
        }

        accountIdentifier = identifier;
        State = new VehicleManagementState();
        if (string.IsNullOrEmpty(identifier))
        {
            return;
        }

        _ = LoadVehicles();
    }

    /// <summary>現在アカウントの車両一覧を同期先またはキャッシュから読み込みます。</summary>
    /// <remarks>責務: 現在の1件のアカウントスコープへ車両一覧読込結果を反映し、更新日時ベースで同期待ち合わせします。</remarks>
    /// <returns>読込の完了を表すタスク。</returns>
    private async Task LoadVehicles()
    {
        if (accountIdentifier is not { } currentAccountIdentifier)
        {
            return;
        }

        State = State with { Phase = VehicleManagementStatePhase.Loading };
        try
        {
            var remote = await repository.LoadVehicles(currentAccountIdentifier);
            var (merged, uploadTargets) = ReconcileVehicles(State.Vehicles, remote);
            State = State with { Vehicles = merged, HasLoadedVehicles = true };
            LoadActivitySummaries(currentAccountIdentifier);
            LoadSpecialPIDRegistrations();
            State = State with { Phase = VehicleManagementStatePhase.Idle, FailureKey = null };
            _ = UploadVehicles(uploadTargets, currentAccountIdentifier);
        }
        catch (Exception)
        {
            State = State with { Phase = VehicleManagementStatePhase.Failed, FailureKey = "garage.error.sync" };
        }
    }

    /// <summary>突合でローカルを採用した車両を同期先へ保存します。</summary>
    /// <remarks>責務: アップロード対象を1件ずつ保存し、失敗は無視して次へ進みます（Swift の <c>try?</c> と同じ）。</remarks>
    /// <param name="vehicles">アップロード対象の車両。</param>
    /// <param name="targetAccountIdentifier">保存先のAppleアカウント識別子。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    private async Task UploadVehicles(IReadOnlyList<VehicleProfile> vehicles, string targetAccountIdentifier)
    {
        foreach (var vehicle in vehicles)
        {
            try
            {
                await repository.SaveVehicle(vehicle, targetAccountIdentifier);
            }
            catch (Exception)
            {
                // Swift の try? と同じく失敗を無視します。
            }
        }
    }

    /// <summary>現在アカウントの接続履歴をGarage用の車両別集計へ読み込みます。</summary>
    /// <remarks>責務: 1件のアカウント識別子へ対応する車両別ログ集計を現在状態へ反映します。</remarks>
    /// <param name="targetAccountIdentifier">集計対象のAppleアカウント識別子。</param>
    private void LoadActivitySummaries(string targetAccountIdentifier)
    {
        if (loadVehicleActivitySummaries is null)
        {
            return;
        }

        IReadOnlyDictionary<VehicleID, VehicleActivitySummary> summaries;
        try
        {
            summaries = loadVehicleActivitySummaries.Execute(targetAccountIdentifier);
        }
        catch (Exception)
        {
            summaries = new Dictionary<VehicleID, VehicleActivitySummary>();
        }

        State = State with { ActivityByVehicleID = summaries };
    }

    /// <summary>登録車両ごとの車種専用PID対応状態を読み込みます。</summary>
    /// <remarks>責務: 保存済みPID能力と定義をGarage用の車両別型式表示へ変換します。</remarks>
    private void LoadSpecialPIDRegistrations()
    {
        if (pidCapabilityRepository is null || pidDefinitionRepository is null)
        {
            return;
        }

        IReadOnlyList<OBDPIDDefinition> definitions;
        try
        {
            definitions = pidDefinitionRepository.Definitions();
        }
        catch (Exception)
        {
            return;
        }

        var modelCodes = definitions
            .Where(definition => definition.VehicleModelCode is not null)
            .ToDictionary(
                definition => new OBDPIDRequest(definition.Service, definition.Pid),
                definition => definition.VehicleModelCode!);
        var registrations = new Dictionary<VehicleID, string>();
        var policy = new ZD8VehicleModelPolicy();
        foreach (var vehicle in State.Vehicles.Where(policy.Matches))
        {
            IReadOnlyList<VehiclePIDCapability> capabilities;
            try
            {
                capabilities = pidCapabilityRepository.Capabilities(vehicle.Id);
            }
            catch (Exception)
            {
                continue;
            }

            var modelCode = capabilities
                .Select(capability => modelCodes.GetValueOrDefault(capability.Id.Request))
                .FirstOrDefault(code => code is not null);
            if (modelCode is null)
            {
                continue;
            }

            registrations[vehicle.Id] = modelCode;
        }

        State = State with { SpecialPIDModelCodeByVehicleID = registrations };
    }

    /// <summary>操作待ち状態で要求された車両一覧の再読込だけを開始します。</summary>
    /// <remarks>責務: 進行中の識別・登録・編集状態を保持したまま、操作待ち時だけ車両カタログ再読込を開始します。</remarks>
    /// <returns>再読込の完了を表すタスク。</returns>
    private async Task RefreshVehicleCatalogIfIdle()
    {
        if (State.Phase != VehicleManagementStatePhase.Idle)
        {
            return;
        }

        await LoadVehicles();
    }

    /// <summary>ローカルとキャッシュ読込データを更新日時で突合し、同期方針を作成します。</summary>
    /// <remarks>責務: 1回の読込結果をローカル一覧と比較し、採用対象とクラウド反映候補を返します。</remarks>
    /// <param name="local">画面保持中の車両一覧。</param>
    /// <param name="remote">リポジトリ読込時点の車両一覧。</param>
    /// <returns>反映先一覧とアップロード対象。</returns>
    private static (IReadOnlyList<VehicleProfile> Merged, IReadOnlyList<VehicleProfile> UploadTargets) ReconcileVehicles(
        IReadOnlyList<VehicleProfile> local,
        IReadOnlyList<VehicleProfile> remote)
    {
        var remoteByID = remote.ToDictionary(vehicle => vehicle.Id);
        var merged = new List<VehicleProfile>();
        var uploadTargets = new List<VehicleProfile>();
        var syncedIDs = new HashSet<VehicleID>();

        foreach (var localVehicle in local)
        {
            if (remoteByID.TryGetValue(localVehicle.Id, out var remoteVehicle))
            {
                syncedIDs.Add(localVehicle.Id);
                if (localVehicle.UpdatedAt > remoteVehicle.UpdatedAt)
                {
                    merged.Add(localVehicle);
                    uploadTargets.Add(localVehicle);
                }
                else
                {
                    merged.Add(remoteVehicle);
                }
            }
            else
            {
                merged.Add(localVehicle);
                uploadTargets.Add(localVehicle);
            }
        }

        foreach (var remoteVehicle in remote.Where(vehicle => !syncedIDs.Contains(vehicle.Id)))
        {
            merged.Add(remoteVehicle);
        }

        return (merged.OrderByDescending(vehicle => vehicle.UpdatedAt).ToArray(), uploadTargets);
    }

    /// <summary>OBD識別結果を登録済み接続または登録確認へ反映します。</summary>
    /// <remarks>責務: 1回のHOME接続要求を車両識別子の照合結果に応じた状態へ進めます。</remarks>
    /// <param name="endpoint">接続するOBDアダプターの物理終端。</param>
    /// <returns>識別の完了を表すタスク。</returns>
    private async Task IdentifyVehicle(OBDConnectionEndpoint endpoint)
    {
        if (accountIdentifier is null || State.Phase == VehicleManagementStatePhase.Identifying)
        {
            return;
        }

        State = State with
        {
            ConnectionEndpoint = endpoint,
            Phase = VehicleManagementStatePhase.Identifying,
            FailureKey = null,
            IdentificationFailureStage = null,
        };
        try
        {
            switch (await identifyForConnection.Execute(endpoint, State.Vehicles))
            {
                case IdentifyVehicleForConnectionUseCase.Outcome.Registered registered:
                    State = State with
                    {
                        ConnectionVehicle = registered.Vehicle,
                        Phase = VehicleManagementStatePhase.ReadyToConnect,
                    };
                    connectionVehicleDidResolve(registered.Vehicle, endpoint, registered.Snapshot);
                    if (accountIdentifier is { } currentAccountIdentifier)
                    {
                        LoadActivitySummaries(currentAccountIdentifier);
                    }

                    break;
                case IdentifyVehicleForConnectionUseCase.Outcome.RequiresRegistration requiresRegistration:
                    State = State with
                    {
                        PendingIdentification = requiresRegistration.Snapshot,
                        Phase = VehicleManagementStatePhase.ConfirmingIdentification,
                    };
                    break;
            }
        }
        catch (VehicleIdentificationException error)
        {
            switch (error.Reason)
            {
                case VehicleIdentificationError.StageFailed stageFailed:
                    ApplyIdentificationFailure(stageFailed.Stage, stageFailed.Cause);
                    break;
                case VehicleIdentificationError.VinUnavailable:
                    ApplyFailure("garage.error.vin_unavailable");
                    break;
                case VehicleIdentificationError.PidCatalogUnavailable:
                    ApplyFailure("garage.error.pid_catalog");
                    break;
                case VehicleIdentificationError.ConnectionFailed:
                    ApplyFailure("garage.error.connection");
                    break;
                case VehicleIdentificationError.ResponseTimedOut:
                    ApplyFailure("garage.error.timeout");
                    break;
                case VehicleIdentificationError.CommandRejected:
                    ApplyFailure("garage.error.command_rejected");
                    break;
                case VehicleIdentificationError.MalformedResponse:
                    ApplyFailure("garage.error.malformed_response");
                    break;
                default:
                    ApplyFailure("garage.error.obd_unavailable");
                    break;
            }
        }
        catch (Exception)
        {
            ApplyFailure("garage.error.obd_unavailable");
        }
    }

    /// <summary>失敗段階と表示キーを現在状態へ反映します。</summary>
    /// <remarks>責務: 1件の失敗を失敗状態とローカライズキーへ変換します。</remarks>
    /// <param name="failureKey">失敗を説明するローカライズキー。</param>
    private void ApplyFailure(string failureKey)
    {
        State = State with { Phase = VehicleManagementStatePhase.Failed, FailureKey = failureKey };
    }

    /// <summary>型付き識別失敗をmacOSとiOSが描画できる段階と原因文言へ反映します。</summary>
    /// <remarks>責務: 1件の車両識別失敗を失敗状態、段階、原因ローカライズキーへ変換します。</remarks>
    /// <param name="stage">完了できなかった車両識別段階。</param>
    /// <param name="cause">その段階で観測した単一原因。</param>
    private void ApplyIdentificationFailure(VehicleIdentificationErrorStage stage, VehicleIdentificationErrorCause cause)
    {
        var failureKey = cause switch
        {
            VehicleIdentificationErrorCause.Unavailable or VehicleIdentificationErrorCause.TransportUnsupported =>
                "garage.error.obd_unavailable",
            VehicleIdentificationErrorCause.ConnectionFailed => "garage.error.connection",
            VehicleIdentificationErrorCause.ResponseTimedOut => "garage.error.timeout",
            VehicleIdentificationErrorCause.CommandRejected => "garage.error.command_rejected",
            VehicleIdentificationErrorCause.MalformedResponse => "garage.error.malformed_response",
            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
        };
        State = State with
        {
            Phase = VehicleManagementStatePhase.Failed,
            IdentificationFailureStage = stage,
            FailureKey = failureKey,
        };
    }

    /// <summary>確認済みOBD識別子から新規プロフィール入力を開始します。</summary>
    /// <remarks>責務: 保持中のVINまたは非VIN識別観測1件を編集可能な新規プロフィールへ変換します。</remarks>
    private void BeginRegistration()
    {
        if (State.PendingIdentification is not { } snapshot
            || (snapshot.Vin is null && snapshot.ObdIdentifier is null))
        {
            return;
        }

        var manufacturer = snapshot.Fields.FirstOrDefault(field => field.Id == "manufacturer")?.Value ?? "";
        var engineModel = snapshot.Fields.FirstOrDefault(field => field.Id == "engineModel")?.Value ?? "";
        State = State with
        {
            EditingVehicle = new VehicleProfile(
                vin: snapshot.Vin ?? "",
                obdIdentifier: snapshot.ObdIdentifier,
                name: manufacturer.Length == 0 ? "" : manufacturer,
                manufacturer: manufacturer,
                engineModel: engineModel,
                isDefault: false),
            Phase = VehicleManagementStatePhase.Registering,
        };
    }

    /// <summary>未完了の新規登録を破棄して一覧へ戻ります。</summary>
    /// <remarks>責務: 新規登録用の観測と編集値だけを現在状態から解除します。</remarks>
    private void CancelRegistration()
    {
        State = State with
        {
            PendingIdentification = null,
            EditingVehicle = null,
            Phase = VehicleManagementStatePhase.Idle,
        };
    }

    /// <summary>指定車両のプロフィール編集を開始します。</summary>
    /// <remarks>責務: 登録一覧から指定IDの車両1件を編集状態へ設定します。</remarks>
    /// <param name="id">編集対象のアプリ内車両ID。</param>
    private void BeginEditing(VehicleID id)
    {
        var vehicle = State.Vehicles.FirstOrDefault(candidate => candidate.Id == id);
        if (vehicle is null)
        {
            return;
        }

        State = State with { EditingVehicle = vehicle, Phase = VehicleManagementStatePhase.Editing };
    }

    /// <summary>現在のプロフィール編集を保存せず終了します。</summary>
    /// <remarks>責務: 編集対象1件を解除して車両一覧表示へ戻します。</remarks>
    private void FinishEditing()
    {
        State = State with { EditingVehicle = null, Phase = VehicleManagementStatePhase.Idle };
    }

    /// <summary>選択画像を現在の編集プロフィールへ反映します。</summary>
    /// <remarks>責務: 1件のファイル選択結果を編集中車両の写真データへ変換します。</remarks>
    /// <param name="url">Platformが選択した画像URL。</param>
    /// <returns>取込の完了を表すタスク。</returns>
    private async Task ImportPhoto(Uri url)
    {
        if (State.EditingVehicle is not { } vehicle)
        {
            return;
        }

        try
        {
            var photoData = await photoImporter.ImportPhoto(url);
            State = State with { EditingVehicle = vehicle with { PhotoData = photoData }, FailureKey = null };
        }
        catch (Exception)
        {
            State = State with { FailureKey = "garage.error.photo" };
        }
    }

    /// <summary>1件のプロフィールを現在アカウントへ保存します。</summary>
    /// <remarks>責務: 編集済み車両1件を保存し最新一覧へ置き換えます。</remarks>
    /// <param name="vehicle">保存する編集済みプロフィール。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    private async Task Save(VehicleProfile vehicle)
    {
        if (accountIdentifier is not { } currentAccountIdentifier)
        {
            return;
        }

        var connectionIdentification = State.Phase == VehicleManagementStatePhase.Registering
            ? State.PendingIdentification
            : null;
        var saved = vehicle with { UpdatedAt = DateTimeOffset.UtcNow };
        try
        {
            await repository.SaveVehicle(saved, currentAccountIdentifier);
            var vehicles = State.Vehicles.ToList();
            var index = vehicles.FindIndex(candidate => candidate.Id == saved.Id);
            if (index >= 0)
            {
                vehicles[index] = saved;
            }
            else
            {
                vehicles.Add(saved);
            }

            State = State with
            {
                Vehicles = vehicles.OrderByDescending(candidate => candidate.UpdatedAt).ToArray(),
                EditingVehicle = null,
                PendingIdentification = null,
            };
            if (connectionIdentification is not null)
            {
                State = State with { ConnectionVehicle = saved };
                if (State.ConnectionEndpoint is { } endpoint)
                {
                    connectionVehicleDidResolve(saved, endpoint, connectionIdentification);
                }
            }

            LoadActivitySummaries(currentAccountIdentifier);
            State = State with { Phase = VehicleManagementStatePhase.Idle, FailureKey = null };
        }
        catch (Exception)
        {
            IReadOnlyList<VehicleProfile> reloaded;
            try
            {
                reloaded = await repository.LoadVehicles(currentAccountIdentifier);
            }
            catch (Exception)
            {
                reloaded = State.Vehicles;
            }

            State = State with
            {
                Vehicles = reloaded,
                Phase = VehicleManagementStatePhase.Failed,
                FailureKey = "garage.error.save",
            };
        }
    }

    /// <summary>指定車両と関連セッションを現在アカウントから削除します。</summary>
    /// <remarks>責務: 指定IDの車両1件と全関連セッションを削除して表示一覧へ成功結果を反映します。</remarks>
    /// <param name="id">削除対象のアプリ内車両ID。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    private async Task Delete(VehicleID id)
    {
        if (accountIdentifier is not { } currentAccountIdentifier)
        {
            return;
        }

        if (deleteVehicleWithSessions is null)
        {
            ApplyFailure("garage.error.delete");
            return;
        }

        try
        {
            await deleteVehicleWithSessions.Execute(id, currentAccountIdentifier);
            var activity = State.ActivityByVehicleID
                .Where(pair => pair.Key != id)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            State = State with
            {
                Vehicles = State.Vehicles.Where(vehicle => vehicle.Id != id).ToArray(),
                ActivityByVehicleID = activity,
                EditingVehicle = null,
                Phase = VehicleManagementStatePhase.Idle,
                FailureKey = null,
            };
            vehicleSessionsDidDelete();
        }
        catch (DeleteVehicleWithSessionsUseCaseException error)
            when (error.Reason == DeleteVehicleWithSessionsUseCaseError.ActiveSession)
        {
            ApplyFailure("garage.error.delete_active_session");
        }
        catch (Exception)
        {
            ApplyFailure("garage.error.delete");
        }
    }

    /// <summary>値が変わったときだけ保存して変更を通知します。</summary>
    /// <remarks>責務: 観測対象プロパティの変更通知を1か所にまとめます。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">保存先のフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">プロパティ名。</param>
    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
