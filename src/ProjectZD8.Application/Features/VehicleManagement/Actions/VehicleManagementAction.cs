using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.Actions;

/// <summary>車両一覧、登録確認、編集からApplicationへ通知する操作です。</summary>
public abstract record VehicleManagementAction
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: 操作の種類を下のケースに限定します。</remarks>
    private VehicleManagementAction()
    {
    }

    /// <summary>認証済みアカウントへ車両スコープを切り替えます。</summary>
    /// <param name="Identifier">Appleアカウント識別子。未認証の場合は <c>null</c>。</param>
    public sealed record AccountIdentifierChanged(string? Identifier) : VehicleManagementAction;

    /// <summary>HOME接続操作からOBD識別を要求します。</summary>
    /// <param name="Endpoint">識別に使うOBD物理終端。</param>
    public sealed record IdentifyRequested(OBDConnectionEndpoint Endpoint) : VehicleManagementAction;

    /// <summary>直前と同じOBD終端で識別を再試行します。</summary>
    public sealed record IdentificationRetryRequested : VehicleManagementAction;

    /// <summary>未登録の車両識別結果を受け入れて編集へ進みます。</summary>
    public sealed record IdentificationConfirmed : VehicleManagementAction;

    /// <summary>未登録の車両識別子に対する登録導線をキャンセルします。</summary>
    public sealed record RegistrationCancelled : VehicleManagementAction;

    /// <summary>新規または既存プロフィールを保存します。</summary>
    /// <param name="Vehicle">保存するプロフィール。</param>
    public sealed record VehicleSaved(VehicleProfile Vehicle) : VehicleManagementAction;

    /// <summary>指定車両の編集を開始します。</summary>
    /// <param name="Id">編集対象の車両ID。</param>
    public sealed record EditRequested(VehicleID Id) : VehicleManagementAction;

    /// <summary>プロフィール編集を終了します。</summary>
    public sealed record EditCancelled : VehicleManagementAction;

    /// <summary>編集中プロフィールへユーザーが選んだ写真を読み込みます。</summary>
    /// <param name="Url">選択された画像URL。</param>
    public sealed record PhotoSelected(Uri Url) : VehicleManagementAction;

    /// <summary>指定車両と車両に紐づく全セッションを削除します。</summary>
    /// <param name="Id">削除対象の車両ID。</param>
    public sealed record VehicleDeleted(VehicleID Id) : VehicleManagementAction;

    /// <summary>同期先から車両一覧を再読込します。</summary>
    public sealed record RefreshRequested : VehicleManagementAction;

    /// <summary>指定車両の対応PID収集設定を開きます。</summary>
    /// <param name="VehicleID">設定対象の車両ID。</param>
    public sealed record PidSettingsRequested(VehicleID VehicleID) : VehicleManagementAction;

    /// <summary>1件のPID収集選択を変更します。</summary>
    /// <param name="Request">変更するService/PID。</param>
    /// <param name="IsEnabled">新しい収集有効状態。</param>
    public sealed record PidCollectionChanged(OBDPIDRequest Request, bool IsEnabled) : VehicleManagementAction;

    /// <summary>PID収集設定を閉じます。</summary>
    public sealed record PidSettingsClosed : VehicleManagementAction;
}
