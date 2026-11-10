using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.Maintenance.Actions;
using ProjectZD8.Application.Maintenance.Ports;
using ProjectZD8.Application.Maintenance.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Maintenance.State;

/// <summary>車両別整備の一覧、編集、GRDB保存、CloudKit同期を調整します。</summary>
/// <remarks>
/// UI スレッド（単一スレッド）から呼ぶ前提です。
/// Swift で <c>Task { ... }</c> として起動していた処理は、async メソッドを直接開始して最初の await まで同期で進めます。
/// </remarks>
public sealed class MaintenanceModel : INotifyPropertyChanged
{
    /// <summary>端末内の整備記録保存先です。</summary>
    private readonly IMaintenanceRecordRepository repository;

    /// <summary>GRDBとCloudKitを双方向統合するユースケースです。</summary>
    private readonly SynchronizeMaintenanceRecordsUseCase synchronize;

    /// <summary>ユーザー選択写真の読込境界です。</summary>
    private readonly IMaintenancePhotoImportPort photoImporter;

    /// <summary>更新日時をテスト可能に生成する処理です。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>現在の整備記録を所有するAppleアカウント識別子です。</summary>
    private string? accountIdentifier;

    /// <summary>現在状態の保存先です。</summary>
    private MaintenanceState state;

    /// <summary>状態と整備保存・同期境界を注入してモデルを生成します。</summary>
    /// <remarks>責務: 整備画面状態を端末内保存、遠隔同期、写真読込へ結び付けます。</remarks>
    /// <param name="state">初期表示状態。</param>
    /// <param name="repository">端末内の整備記録保存先。</param>
    /// <param name="synchronize">双方向同期ユースケース。</param>
    /// <param name="photoImporter">選択写真の読込境界。</param>
    /// <param name="now">作成、更新、削除日時の生成処理。省略時は現在日時。</param>
    public MaintenanceModel(
        MaintenanceState state,
        IMaintenanceRecordRepository repository,
        SynchronizeMaintenanceRecordsUseCase synchronize,
        IMaintenancePhotoImportPort photoImporter,
        Func<DateTimeOffset>? now = null)
    {
        this.state = state;
        this.repository = repository;
        this.synchronize = synchronize;
        this.photoImporter = photoImporter;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Platformが描画する現在状態です。</summary>
    public MaintenanceState State
    {
        get => state;
        set => SetProperty(ref state, value);
    }

    /// <summary>1件の型付き操作を整備状態と副作用へ反映します。</summary>
    /// <remarks>責務: 整備操作を対応する状態更新または非同期ワークフロー1件へ振り分けます。</remarks>
    /// <param name="action">PlatformまたはAppから通知された操作。</param>
    public void Send(MaintenanceAction action)
    {
        switch (action)
        {
            case MaintenanceAction.AccountIdentifierChanged changed:
                ActivateAccount(changed.Identifier);
                break;
            case MaintenanceAction.VehiclesChanged changed:
                UpdateVehicles(changed.Vehicles);
                break;
            case MaintenanceAction.VehicleSelected selected:
                State = State with { SelectedVehicleID = selected.Id };
                break;
            case MaintenanceAction.KindFilterChanged changed:
                State = State with { KindFilter = changed.Kind };
                break;
            case MaintenanceAction.CreateRequested requested:
                BeginCreation(requested.Kind);
                break;
            case MaintenanceAction.EditRequested requested:
                BeginEditing(requested.Id);
                break;
            case MaintenanceAction.DraftChanged changed:
                State = State with { Draft = changed.Draft };
                break;
            case MaintenanceAction.WorkItemAdded:
                AddWorkItem();
                break;
            case MaintenanceAction.WorkItemRemoved removed:
                UpdateDraft(draft => draft with { WorkItems = draft.WorkItems.Where(item => item.Id != removed.Id).ToArray() });
                break;
            case MaintenanceAction.FastenerGroupAdded:
                AddFastenerGroup();
                break;
            case MaintenanceAction.FastenerAdded added:
                AddFastener(added.GroupID);
                break;
            case MaintenanceAction.FastenerConfirmed confirmed:
                ConfirmFastener(confirmed.GroupID, confirmed.EvidenceID);
                break;
            case MaintenanceAction.LatestPhotoLinked linked:
                LinkLatestPhoto(linked.GroupID, linked.EvidenceID);
                break;
            case MaintenanceAction.FastenerGroupRemoved removed:
                UpdateDraft(draft => draft with
                {
                    FastenerGroups = draft.FastenerGroups.Where(group => group.Id != removed.Id).ToArray(),
                });
                break;
            case MaintenanceAction.PhotoSelected selected:
                _ = ImportPhoto(selected.Url);
                break;
            case MaintenanceAction.PhotoRemoved removed:
                UpdateDraft(draft => draft with { Photos = draft.Photos.Where(photo => photo.Id != removed.Id).ToArray() });
                break;
            case MaintenanceAction.SaveRequested:
                _ = SaveDraft();
                break;
            case MaintenanceAction.EditingCancelled:
                FinishEditing();
                break;
            case MaintenanceAction.DeleteRequested requested:
                _ = DeleteRecord(requested.Id);
                break;
            case MaintenanceAction.RefreshRequested:
                _ = SynchronizeRecords();
                break;
        }
    }

    /// <summary>編集入力がある場合だけ変換して反映します。</summary>
    /// <remarks>責務: Swift の <c>state.draft?.xxx</c> による任意連鎖の更新を1か所で表します。</remarks>
    /// <param name="transform">現在の編集入力を新しい入力へ変換する処理。</param>
    private void UpdateDraft(Func<MaintenanceEditorDraft, MaintenanceEditorDraft> transform)
    {
        if (State.Draft is { } draft)
        {
            State = State with { Draft = transform(draft) };
        }
    }

    /// <summary>認証アカウント変更時に状態を初期化してローカル読込と同期を開始します。</summary>
    /// <remarks>責務: 1件の認証識別子変更を整備スコープの解除または読込開始へ変換します。</remarks>
    /// <param name="identifier">新しいAppleアカウント識別子。</param>
    private void ActivateAccount(string? identifier)
    {
        if (string.Equals(identifier, accountIdentifier, StringComparison.Ordinal))
        {
            return;
        }

        accountIdentifier = identifier;
        var vehicles = State.Vehicles;
        State = new MaintenanceState { Vehicles = vehicles, SelectedVehicleID = vehicles.FirstOrDefault()?.Id };
        if (string.IsNullOrEmpty(identifier))
        {
            return;
        }

        _ = LoadLocalThenSynchronize();
    }

    /// <summary>Garage由来の登録車両集合を整備対象選択へ反映します。</summary>
    /// <remarks>責務: 現在の車両集合を保持し無効な選択だけを先頭車両へ修正します。</remarks>
    /// <param name="vehicles">現在のアカウントに属する登録車両。</param>
    private void UpdateVehicles(IReadOnlyList<VehicleProfile> vehicles)
    {
        if (State.SelectedVehicleID is { } selected && vehicles.Any(vehicle => vehicle.Id == selected))
        {
            State = State with { Vehicles = vehicles };
            return;
        }

        State = State with { Vehicles = vehicles, SelectedVehicleID = vehicles.FirstOrDefault()?.Id };
    }

    /// <summary>選択車両へ新規整備入力を作成します。</summary>
    /// <remarks>責務: 1件の整備区分を現在車両へ必須関連付けした編集入力へ変換します。</remarks>
    /// <param name="kind">開始する軽整備または重整備。</param>
    private void BeginCreation(MaintenanceKind kind)
    {
        if (State.SelectedVehicleID is not { } vehicleID)
        {
            State = State with { FailureKey = "maintenance.error.vehicle_required" };
            return;
        }

        State = State with
        {
            Draft = new MaintenanceEditorDraft(vehicleID, kind, now()),
            Phase = MaintenanceStatePhase.Editing,
            FailureKey = null,
        };
        AddWorkItem();
    }

    /// <summary>保存済み整備記録を編集入力へ変換します。</summary>
    /// <remarks>責務: 指定IDの1件を見つけて編集段階へ移行します。</remarks>
    /// <param name="id">編集する整備記録ID。</param>
    private void BeginEditing(MaintenanceRecordID id)
    {
        var record = State.Records.FirstOrDefault(candidate => candidate.Id == id);
        if (record is null)
        {
            return;
        }

        State = State with
        {
            Draft = new MaintenanceEditorDraft(record),
            Phase = MaintenanceStatePhase.Editing,
            FailureKey = null,
        };
    }

    /// <summary>編集中区分に適した初期作業項目を追加します。</summary>
    /// <remarks>責務: 現在の入力へ軽整備または重整備の既定作業項目を1件追加します。</remarks>
    private void AddWorkItem()
    {
        UpdateDraft(draft =>
        {
            var item = new MaintenanceWorkItem(
                component: draft.Kind == MaintenanceKind.Light ? MaintenanceComponent.EngineOil : MaintenanceComponent.EngineAssembly,
                operation: draft.Kind == MaintenanceKind.Light ? MaintenanceOperation.Replacement : MaintenanceOperation.Inspection);
            return draft with { WorkItems = [.. draft.WorkItems, item] };
        });
    }

    /// <summary>重整備入力へ可変本数の締結グループを追加します。</summary>
    /// <remarks>責務: 現在の重整備入力へ空の分解・締結追跡単位を1件追加します。</remarks>
    private void AddFastenerGroup()
    {
        if (State.Draft is not { Kind: MaintenanceKind.Heavy } draft)
        {
            return;
        }

        State = State with { Draft = draft with { FastenerGroups = [.. draft.FastenerGroups, new MaintenanceFastenerGroup(name: "")] } };
    }

    /// <summary>指定締結グループへ個別位置を追加します。</summary>
    /// <remarks>責務: 1件の締結グループへ追跡可能な個別締結証跡を追加します。</remarks>
    /// <param name="groupID">追加先の締結グループID。</param>
    private void AddFastener(Guid groupID)
    {
        if (State.Draft is not { } draft)
        {
            return;
        }

        var groups = draft.FastenerGroups.ToList();
        var index = groups.FindIndex(group => group.Id == groupID);
        if (index < 0)
        {
            return;
        }

        groups[index] = groups[index] with
        {
            Installations = [.. groups[index].Installations, new FastenerInstallationEvidence(position: "")],
        };
        State = State with { Draft = draft with { FastenerGroups = groups } };
    }

    /// <summary>個別締結へApplicationが生成した完了日時を記録します。</summary>
    /// <remarks>責務: 指定した1本の締結証跡へ現在の完了日時を設定します。</remarks>
    /// <param name="groupID">締結グループID。</param>
    /// <param name="evidenceID">個別締結証跡ID。</param>
    private void ConfirmFastener(Guid groupID, Guid evidenceID)
    {
        UpdateEvidence(groupID, evidenceID, (_, evidence) => evidence with { TightenedAt = now() });
    }

    /// <summary>最新の作業写真を個別締結証跡へ関連付けます。</summary>
    /// <remarks>責務: 現在入力の最新写真IDを指定した1本の締結証跡へ重複なく追加します。</remarks>
    /// <param name="groupID">締結グループID。</param>
    /// <param name="evidenceID">個別締結証跡ID。</param>
    private void LinkLatestPhoto(Guid groupID, Guid evidenceID)
    {
        if (State.Draft is not { } draft || draft.Photos.Count == 0)
        {
            return;
        }

        var photoID = draft.Photos[^1].Id;
        UpdateEvidence(groupID, evidenceID, (_, evidence) => evidence.PhotoIDs.Contains(photoID)
            ? evidence
            : evidence with { PhotoIDs = [.. evidence.PhotoIDs, photoID] });
    }

    /// <summary>指定した1本の締結証跡を変換して編集入力へ反映します。</summary>
    /// <remarks>
    /// 責務: グループと証跡の両方が見つかったときだけ変換結果を反映します。
    /// 見つからない場合は Swift の <c>guard ... else { return }</c> と同じく何もしません。
    /// </remarks>
    /// <param name="groupID">締結グループID。</param>
    /// <param name="evidenceID">個別締結証跡ID。</param>
    /// <param name="transform">証跡を変換する処理。</param>
    private void UpdateEvidence(
        Guid groupID,
        Guid evidenceID,
        Func<MaintenanceEditorDraft, FastenerInstallationEvidence, FastenerInstallationEvidence> transform)
    {
        if (State.Draft is not { } draft)
        {
            return;
        }

        var groups = draft.FastenerGroups.ToList();
        var groupIndex = groups.FindIndex(group => group.Id == groupID);
        if (groupIndex < 0)
        {
            return;
        }

        var installations = groups[groupIndex].Installations.ToList();
        var evidenceIndex = installations.FindIndex(evidence => evidence.Id == evidenceID);
        if (evidenceIndex < 0)
        {
            return;
        }

        installations[evidenceIndex] = transform(draft, installations[evidenceIndex]);
        groups[groupIndex] = groups[groupIndex] with { Installations = installations };
        State = State with { Draft = draft with { FastenerGroups = groups } };
    }

    /// <summary>選択写真を現在の編集入力へ追加します。</summary>
    /// <remarks>責務: 1件の選択URLを読み込み追跡可能な写真エビデンスへ変換します。</remarks>
    /// <param name="url">Platformのファイル選択で得たURL。</param>
    /// <returns>取込の完了を表すタスク。</returns>
    private async Task ImportPhoto(Uri url)
    {
        if (State.Draft is null)
        {
            return;
        }

        try
        {
            var data = await photoImporter.ImportPhoto(url);
            if (data.Length == 0)
            {
                throw new InvalidDataException("選択された写真のデータが空です。");
            }

            var photo = new MaintenancePhoto(data: data, capturedAt: now());
            UpdateDraft(draft => draft with { Photos = [.. draft.Photos, photo] });
            State = State with { FailureKey = null };
        }
        catch (Exception)
        {
            State = State with { FailureKey = "maintenance.error.photo" };
        }
    }

    /// <summary>現在の編集入力を車両別Domain記録として保存します。</summary>
    /// <remarks>責務: 1件の有効な整備入力をローカル保存しCloudKit同期へ進めます。</remarks>
    /// <returns>保存の完了を表すタスク。</returns>
    private async Task SaveDraft()
    {
        if (accountIdentifier is not { } currentAccountIdentifier || State.Draft is not { } draft)
        {
            return;
        }

        var trimmedTitle = draft.Title.Trim();
        if (trimmedTitle.Length == 0 || draft.WorkItems.Count == 0)
        {
            State = State with { FailureKey = "maintenance.error.required" };
            return;
        }

        State = State with { Phase = MaintenanceStatePhase.Saving };
        var timestamp = now();
        var record = new MaintenanceRecord(
            id: draft.RecordID ?? MaintenanceRecordID.New(),
            vehicleID: draft.VehicleID,
            kind: draft.Kind,
            title: trimmedTitle,
            performedAt: draft.PerformedAt,
            odometerKilometers: draft.OdometerKilometers,
            notes: draft.Notes,
            workItems: draft.WorkItems,
            photos: draft.Photos,
            fastenerGroups: draft.Kind == MaintenanceKind.Heavy ? draft.FastenerGroups : [],
            createdAt: draft.CreatedAt,
            updatedAt: timestamp);
        try
        {
            await repository.Save(record, currentAccountIdentifier);
            State = State with { Draft = null };
            await SynchronizeRecords();
        }
        catch (Exception)
        {
            State = State with { Phase = MaintenanceStatePhase.Failed, FailureKey = "maintenance.error.save" };
        }
    }

    /// <summary>指定記録を削除墓石へ変換して全端末へ同期します。</summary>
    /// <remarks>責務: 1件の整備記録へ削除日時を設定しローカル保存後に同期します。</remarks>
    /// <param name="id">削除する整備記録ID。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    private async Task DeleteRecord(MaintenanceRecordID id)
    {
        if (accountIdentifier is not { } currentAccountIdentifier)
        {
            return;
        }

        var existing = State.Records.FirstOrDefault(candidate => candidate.Id == id);
        if (existing is null)
        {
            return;
        }

        var timestamp = now();
        var record = existing with { DeletedAt = timestamp, UpdatedAt = timestamp };
        try
        {
            await repository.Save(record, currentAccountIdentifier);
            State = State with { Draft = null };
            await SynchronizeRecords();
        }
        catch (Exception)
        {
            State = State with { Phase = MaintenanceStatePhase.Failed, FailureKey = "maintenance.error.delete" };
        }
    }

    /// <summary>編集入力を破棄して一覧へ戻ります。</summary>
    /// <remarks>責務: 現在の未保存入力だけを破棄し整備一覧を再表示します。</remarks>
    private void FinishEditing()
    {
        State = State with { Draft = null, Phase = MaintenanceStatePhase.Idle, FailureKey = null };
    }

    /// <summary>ローカル記録を先に表示してからCloudKit双方向同期を実行します。</summary>
    /// <remarks>責務: 1件のアカウントについてオフライン表示を確保した後に遠隔同期します。</remarks>
    /// <returns>読込と同期の完了を表すタスク。</returns>
    private async Task LoadLocalThenSynchronize()
    {
        if (accountIdentifier is not { } currentAccountIdentifier)
        {
            return;
        }

        State = State with { Phase = MaintenanceStatePhase.Syncing };
        try
        {
            var records = await repository.Records(currentAccountIdentifier);
            State = State with { Records = VisibleSorted(records) };
        }
        catch (Exception)
        {
            State = State with { Phase = MaintenanceStatePhase.Failed, FailureKey = "maintenance.error.load" };
            return;
        }

        await SynchronizeRecords();
    }

    /// <summary>現在アカウントのローカル記録とCloudKit記録を同期します。</summary>
    /// <remarks>責務: 1件のアカウントに対する同期結果を一覧と同期状態へ反映します。</remarks>
    /// <returns>同期の完了を表すタスク。</returns>
    private async Task SynchronizeRecords()
    {
        if (accountIdentifier is not { } currentAccountIdentifier)
        {
            return;
        }

        State = State with { Phase = MaintenanceStatePhase.Syncing };
        try
        {
            var records = await synchronize.Execute(currentAccountIdentifier);
            State = State with
            {
                Records = records,
                LastSynchronizedAt = now(),
                Phase = MaintenanceStatePhase.Idle,
                FailureKey = null,
            };
        }
        catch (Exception)
        {
            IReadOnlyList<MaintenanceRecord> fallback;
            try
            {
                fallback = await repository.Records(currentAccountIdentifier);
            }
            catch (Exception)
            {
                fallback = [];
            }

            State = State with
            {
                Records = VisibleSorted(fallback),
                Phase = MaintenanceStatePhase.Failed,
                FailureKey = "maintenance.error.sync",
            };
        }
    }

    /// <summary>削除墓石を除いて実施日時の新しい順に並べます。</summary>
    /// <remarks>責務: 一覧表示用の記録集合を Swift と同じ規則で整えます。</remarks>
    /// <param name="records">整える整備記録集合。</param>
    /// <returns>表示用に整えた整備記録。</returns>
    private static IReadOnlyList<MaintenanceRecord> VisibleSorted(IReadOnlyList<MaintenanceRecord> records) =>
        records
            .Where(record => record.DeletedAt is null)
            .OrderByDescending(record => record.PerformedAt)
            .ToArray();

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
