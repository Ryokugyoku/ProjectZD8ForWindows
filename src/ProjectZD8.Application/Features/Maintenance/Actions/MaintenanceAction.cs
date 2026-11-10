using ProjectZD8.Application.Maintenance.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Maintenance.Actions;

/// <summary>整備画面とAppからMaintenanceへ通知する型付き操作です。</summary>
public abstract record MaintenanceAction
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: 操作の種類を下のケースに限定します。</remarks>
    private MaintenanceAction()
    {
    }

    /// <summary>認証済みアカウントへ整備スコープを切り替えます。</summary>
    /// <param name="Identifier">Appleアカウント識別子。未認証の場合は <c>null</c>。</param>
    public sealed record AccountIdentifierChanged(string? Identifier) : MaintenanceAction;

    /// <summary>現在の登録車両集合を整備対象選択へ反映します。</summary>
    /// <param name="Vehicles">現在の登録車両集合。</param>
    public sealed record VehiclesChanged(IReadOnlyList<VehicleProfile> Vehicles) : MaintenanceAction;

    /// <summary>一覧表示する車両を選択します。</summary>
    /// <param name="Id">選択する車両ID。</param>
    public sealed record VehicleSelected(VehicleID Id) : MaintenanceAction;

    /// <summary>一覧の整備区分フィルターを変更します。</summary>
    /// <param name="Kind">絞り込む整備区分。絞り込まない場合は <c>null</c>。</param>
    public sealed record KindFilterChanged(MaintenanceKind? Kind) : MaintenanceAction;

    /// <summary>選択車両の新規整備入力を開始します。</summary>
    /// <param name="Kind">開始する整備区分。</param>
    public sealed record CreateRequested(MaintenanceKind Kind) : MaintenanceAction;

    /// <summary>保存済み整備記録の編集を開始します。</summary>
    /// <param name="Id">編集する整備記録ID。</param>
    public sealed record EditRequested(MaintenanceRecordID Id) : MaintenanceAction;

    /// <summary>画面で変更した未保存入力を反映します。</summary>
    /// <param name="Draft">変更後の未保存入力。</param>
    public sealed record DraftChanged(MaintenanceEditorDraft Draft) : MaintenanceAction;

    /// <summary>現在の入力へ作業項目を追加します。</summary>
    public sealed record WorkItemAdded : MaintenanceAction;

    /// <summary>現在の入力から作業項目を削除します。</summary>
    /// <param name="Id">削除する作業項目ID。</param>
    public sealed record WorkItemRemoved(Guid Id) : MaintenanceAction;

    /// <summary>重整備入力へ締結グループを追加します。</summary>
    public sealed record FastenerGroupAdded : MaintenanceAction;

    /// <summary>締結グループへ個別の締結位置を追加します。</summary>
    /// <param name="GroupID">追加先の締結グループID。</param>
    public sealed record FastenerAdded(Guid GroupID) : MaintenanceAction;

    /// <summary>個別締結を現在日時で完了証跡として確定します。</summary>
    /// <param name="GroupID">締結グループID。</param>
    /// <param name="EvidenceID">個別締結証跡ID。</param>
    public sealed record FastenerConfirmed(Guid GroupID, Guid EvidenceID) : MaintenanceAction;

    /// <summary>最新の作業写真を個別締結証跡へ関連付けます。</summary>
    /// <param name="GroupID">締結グループID。</param>
    /// <param name="EvidenceID">個別締結証跡ID。</param>
    public sealed record LatestPhotoLinked(Guid GroupID, Guid EvidenceID) : MaintenanceAction;

    /// <summary>現在の入力から締結グループを削除します。</summary>
    /// <param name="Id">削除する締結グループID。</param>
    public sealed record FastenerGroupRemoved(Guid Id) : MaintenanceAction;

    /// <summary>ファイル選択済み写真を現在の入力へ追加します。</summary>
    /// <param name="Url">選択された写真のURL。</param>
    public sealed record PhotoSelected(Uri Url) : MaintenanceAction;

    /// <summary>現在の入力から写真を削除します。</summary>
    /// <param name="Id">削除する写真ID。</param>
    public sealed record PhotoRemoved(Guid Id) : MaintenanceAction;

    /// <summary>現在の入力を保存します。</summary>
    public sealed record SaveRequested : MaintenanceAction;

    /// <summary>保存せず一覧へ戻ります。</summary>
    public sealed record EditingCancelled : MaintenanceAction;

    /// <summary>指定記録を全端末から削除する墓石へ更新します。</summary>
    /// <param name="Id">削除する整備記録ID。</param>
    public sealed record DeleteRequested(MaintenanceRecordID Id) : MaintenanceAction;

    /// <summary>ローカルとCloudKitの再同期を要求します。</summary>
    public sealed record RefreshRequested : MaintenanceAction;
}
