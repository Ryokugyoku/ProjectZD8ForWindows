using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Maintenance.State;

/// <summary>整備編集画面へ表示する未保存入力です。</summary>
public sealed record MaintenanceEditorDraft
{
    /// <summary>新規整備入力を車両と区分から生成します。</summary>
    /// <remarks>責務: 1台分の未保存整備入力を安全な初期値で生成します。</remarks>
    /// <param name="vehicleID">整備対象の登録車両ID。</param>
    /// <param name="kind">軽整備または重整備。</param>
    /// <param name="now">初期の実施日時と作成日時。省略時は現在日時。</param>
    public MaintenanceEditorDraft(VehicleID vehicleID, MaintenanceKind kind, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        RecordID = null;
        VehicleID = vehicleID;
        Kind = kind;
        Title = "";
        PerformedAt = timestamp;
        OdometerKilometers = null;
        Notes = "";
        WorkItems = [];
        Photos = [];
        FastenerGroups = [];
        CreatedAt = timestamp;
    }

    /// <summary>保存済み記録から編集入力を生成します。</summary>
    /// <remarks>責務: 1件のDomain整備記録を変更可能な画面入力へ写像します。</remarks>
    /// <param name="record">編集する保存済み整備記録。</param>
    public MaintenanceEditorDraft(MaintenanceRecord record)
    {
        RecordID = record.Id;
        VehicleID = record.VehicleID;
        Kind = record.Kind;
        Title = record.Title;
        PerformedAt = record.PerformedAt;
        OdometerKilometers = record.OdometerKilometers;
        Notes = record.Notes;
        WorkItems = record.WorkItems;
        Photos = record.Photos;
        FastenerGroups = record.FastenerGroups;
        CreatedAt = record.CreatedAt;
    }

    /// <summary>編集元記録IDです。</summary>
    public MaintenanceRecordID? RecordID { get; }

    /// <summary>必ず関連付ける登録車両IDです。</summary>
    public VehicleID VehicleID { get; init; }

    /// <summary>軽整備または重整備の区分です。</summary>
    public MaintenanceKind Kind { get; init; }

    /// <summary>作業表題です。</summary>
    public string Title { get; init; }

    /// <summary>作業実施日時です。</summary>
    public DateTimeOffset PerformedAt { get; init; }

    /// <summary>走行距離入力です。</summary>
    public double? OdometerKilometers { get; init; }

    /// <summary>作業全体の詳細記録です。</summary>
    public string Notes { get; init; }

    /// <summary>部品ごとの作業入力です。</summary>
    public IReadOnlyList<MaintenanceWorkItem> WorkItems { get; init; }

    /// <summary>作業写真です。</summary>
    public IReadOnlyList<MaintenancePhoto> Photos { get; init; }

    /// <summary>分解・締結証跡です。</summary>
    public IReadOnlyList<MaintenanceFastenerGroup> FastenerGroups { get; init; }

    /// <summary>編集元の作成日時です。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>全プロパティを Swift の Equatable と同じ意味で比較します。</summary>
    /// <remarks>責務: 一覧は要素順で比較します。</remarks>
    /// <param name="other">比較相手。</param>
    /// <returns>等しい場合は <c>true</c>。</returns>
    public bool Equals(MaintenanceEditorDraft? other) =>
        other is not null
        && RecordID == other.RecordID
        && VehicleID == other.VehicleID
        && Kind == other.Kind
        && string.Equals(Title, other.Title, StringComparison.Ordinal)
        && PerformedAt == other.PerformedAt
        && OdometerKilometers == other.OdometerKilometers
        && string.Equals(Notes, other.Notes, StringComparison.Ordinal)
        && WorkItems.SequenceEqual(other.WorkItems)
        && Photos.SequenceEqual(other.Photos)
        && FastenerGroups.SequenceEqual(other.FastenerGroups)
        && CreatedAt == other.CreatedAt;

    /// <summary>比較と矛盾しないハッシュ値を返します。</summary>
    /// <remarks>責務: 単純な値と要素数からハッシュ値を作ります。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(RecordID, VehicleID, Kind, Title, PerformedAt, WorkItems.Count, Photos.Count, FastenerGroups.Count);
}

/// <summary>整備機能の現在段階です。</summary>
/// <remarks>Swift の <c>MaintenanceState.Phase</c> に対応します。C# では型と同名のプロパティ <see cref="MaintenanceState.Phase"/> を入れ子にできないため外へ出しています。</remarks>
public enum MaintenanceStatePhase
{
    /// <summary>操作待ちです。</summary>
    Idle,

    /// <summary>ローカルとCloudKitを同期しています。</summary>
    Syncing,

    /// <summary>新規または既存記録を編集中です。</summary>
    Editing,

    /// <summary>編集内容を保存しています。</summary>
    Saving,

    /// <summary>読込、保存、同期のいずれかに失敗しました。</summary>
    Failed,
}

/// <summary>整備画面が描画する同期済み一覧と編集状態です。</summary>
public sealed record MaintenanceState
{
    /// <summary>現在のワークフロー段階です。</summary>
    public MaintenanceStatePhase Phase { get; init; } = MaintenanceStatePhase.Idle;

    /// <summary>整備対象として選べる登録車両です。</summary>
    public IReadOnlyList<VehicleProfile> Vehicles { get; init; } = [];

    /// <summary>一覧表示中の登録車両IDです。</summary>
    public VehicleID? SelectedVehicleID { get; init; }

    /// <summary>削除墓石を除く整備記録です。</summary>
    public IReadOnlyList<MaintenanceRecord> Records { get; init; } = [];

    /// <summary>軽整備または重整備で絞り込む選択です。</summary>
    public MaintenanceKind? KindFilter { get; init; }

    /// <summary>表示中の未保存編集入力です。</summary>
    public MaintenanceEditorDraft? Draft { get; init; }

    /// <summary>直近の失敗を説明するローカライズキーです。</summary>
    public string? FailureKey { get; init; }

    /// <summary>CloudKitとの同期を最後に完了した日時です。</summary>
    public DateTimeOffset? LastSynchronizedAt { get; init; }

    /// <summary>選択車両と区分に一致する表示用記録です。</summary>
    public IReadOnlyList<MaintenanceRecord> VisibleRecords =>
        Records
            .Where(record =>
                (SelectedVehicleID is null || record.VehicleID == SelectedVehicleID)
                && (KindFilter is null || record.Kind == KindFilter))
            .ToArray();

    /// <summary>全プロパティを Swift の Equatable と同じ意味で比較します。</summary>
    /// <remarks>責務: 一覧は要素順で比較します。</remarks>
    /// <param name="other">比較相手。</param>
    /// <returns>等しい場合は <c>true</c>。</returns>
    public bool Equals(MaintenanceState? other) =>
        other is not null
        && Phase == other.Phase
        && Vehicles.SequenceEqual(other.Vehicles)
        && SelectedVehicleID == other.SelectedVehicleID
        && Records.SequenceEqual(other.Records)
        && KindFilter == other.KindFilter
        && Equals(Draft, other.Draft)
        && string.Equals(FailureKey, other.FailureKey, StringComparison.Ordinal)
        && LastSynchronizedAt == other.LastSynchronizedAt;

    /// <summary>比較と矛盾しないハッシュ値を返します。</summary>
    /// <remarks>責務: 単純な値と要素数からハッシュ値を作ります。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Phase, Vehicles.Count, SelectedVehicleID, Records.Count, KindFilter, Draft, FailureKey, LastSynchronizedAt);
}
