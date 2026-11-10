using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// この端末が持つ運転データ、同期設定、端末固有の設定を消します（iOS の <c>ProjectZD8AccountDataEraser</c>）。
/// </summary>
/// <remarks>
/// iOS は既定アダプターの保存先の具体型を受け取りますが、Application のポート（<c>IDefaultAdapterPreferencePort</c>）には
/// 削除がないため、Windows では既定アダプターの選択を消す処理を委譲で受け取ります（組み立てが <c>store.Remove</c> を渡します）。
/// </remarks>
public sealed class ProjectZD8AccountDataEraser : IAccountDataErasurePort
{
    /// <summary>同期設定と、同じ設定の端末内コピーを持つ保存先です。</summary>
    private readonly SynchronizedAccountSettingsStore _accountSettingsStore;

    /// <summary>端末固有の既定アダプターの選択を消す処理です。</summary>
    private readonly Action _removeDefaultAdapterPreference;

    /// <summary>接続履歴と未デコードの生ログを持つ端末内の保存先です。</summary>
    private readonly IAccountConnectionSessionErasureRepository _connectionSessionStorage;

    /// <summary>車両プロフィールと写真の端末内の削除境界です。</summary>
    private readonly IAccountVehicleDataErasurePort _vehicleDataEraser;

    /// <summary>登録車両別の PID 対応と収集の選択の端末内の削除境界です。</summary>
    private readonly IAccountVehiclePIDCapabilityErasureRepository _vehiclePIDCapabilityEraser;

    /// <summary>
    /// 設定の保存先と、各データの削除境界を注入して作ります。
    /// </summary>
    /// <remarks>責務: この端末が保存するローカルの運転データと 2 種類の設定の保存先を削除の境界へ固定します。</remarks>
    /// <param name="accountSettingsStore">同期設定と端末内コピーの保存先。</param>
    /// <param name="removeDefaultAdapterPreference">既定アダプターの選択を消す処理。</param>
    /// <param name="connectionSessionStorage">接続履歴と生ログの端末内の保存先。</param>
    /// <param name="vehicleDataEraser">車両プロフィールと写真の端末内の削除境界。</param>
    /// <param name="vehiclePIDCapabilityEraser">登録車両別の PID 設定の端末内の削除境界。</param>
    public ProjectZD8AccountDataEraser(
        SynchronizedAccountSettingsStore accountSettingsStore,
        Action removeDefaultAdapterPreference,
        IAccountConnectionSessionErasureRepository connectionSessionStorage,
        IAccountVehicleDataErasurePort vehicleDataEraser,
        IAccountVehiclePIDCapabilityErasureRepository vehiclePIDCapabilityEraser)
    {
        _accountSettingsStore = accountSettingsStore;
        _removeDefaultAdapterPreference = removeDefaultAdapterPreference;
        _connectionSessionStorage = connectionSessionStorage;
        _vehicleDataEraser = vehicleDataEraser;
        _vehiclePIDCapabilityEraser = vehiclePIDCapabilityEraser;
    }

    /// <summary>
    /// 指定アカウントの端末内の運転データ、同期設定、既定アダプターの選択を消します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント削除要求を、この端末のすべての接続履歴・生ログ・設定の消去へ変換します（iOS と同じ順番）。</remarks>
    /// <param name="userIdentifier">Apple がこのアプリへ割り当てた空でないユーザー識別子。</param>
    public void EraseAllData(string userIdentifier)
    {
        var vehicleIDs = _vehicleDataEraser.LocalVehicleIDs(userIdentifier);
        _connectionSessionStorage.DeleteSessions(userIdentifier);
        _vehiclePIDCapabilityEraser.DeleteCapabilities(vehicleIDs);
        _vehicleDataEraser.RemoveLocalVehicleCache(userIdentifier);
        _accountSettingsStore.Remove(userIdentifier);
        _removeDefaultAdapterPreference();
    }
}
