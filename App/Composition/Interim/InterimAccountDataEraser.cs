using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】アカウントの端末内データ（設定・既定アダプター・走行ログ・車両キャッシュ・対応 PID）を消す境界です。
/// </summary>
/// <remarks>iOS 版の <c>ProjectZD8AccountDataEraser</c> の代わり。ネットワーク担当の実装ができたら差し替えます。消す順番は iOS 版と同じです。</remarks>
public sealed class InterimAccountDataEraser : IAccountDataErasurePort
{
    /// <summary>アカウント設定の保存先です。</summary>
    private readonly InterimAccountSettingsStore _accountSettingsStore;

    /// <summary>既定アダプターの保存先です。</summary>
    private readonly InterimDefaultAdapterPreferenceStore _defaultAdapterStore;

    /// <summary>走行ログの保存先です。</summary>
    private readonly IAccountConnectionSessionErasureRepository _connectionSessionStorage;

    /// <summary>車両の保存先です。</summary>
    private readonly InterimVehicleRepository _vehicleDataEraser;

    /// <summary>対応 PID の保存先です。</summary>
    private readonly IAccountVehiclePIDCapabilityErasureRepository _vehiclePIDCapabilityEraser;

    /// <summary>消去する保存先を指定して作ります。</summary>
    /// <param name="accountSettingsStore">アカウント設定の保存先。</param>
    /// <param name="defaultAdapterStore">既定アダプターの保存先。</param>
    /// <param name="connectionSessionStorage">走行ログの保存先。</param>
    /// <param name="vehicleDataEraser">車両の保存先。</param>
    /// <param name="vehiclePIDCapabilityEraser">対応 PID の保存先。</param>
    public InterimAccountDataEraser(
        InterimAccountSettingsStore accountSettingsStore,
        InterimDefaultAdapterPreferenceStore defaultAdapterStore,
        IAccountConnectionSessionErasureRepository connectionSessionStorage,
        InterimVehicleRepository vehicleDataEraser,
        IAccountVehiclePIDCapabilityErasureRepository vehiclePIDCapabilityEraser)
    {
        _accountSettingsStore = accountSettingsStore;
        _defaultAdapterStore = defaultAdapterStore;
        _connectionSessionStorage = connectionSessionStorage;
        _vehicleDataEraser = vehicleDataEraser;
        _vehiclePIDCapabilityEraser = vehiclePIDCapabilityEraser;
    }

    /// <summary>アカウントの端末内データを全部消します。</summary>
    /// <remarks>責務: 1 件のアカウントの設定・既定アダプター・走行ログ・対応 PID・車両キャッシュを消します。</remarks>
    /// <param name="userIdentifier">アカウント識別子。</param>
    public void EraseAllData(string userIdentifier)
    {
        _accountSettingsStore.Remove(userIdentifier);
        _defaultAdapterStore.Remove();
        _connectionSessionStorage.DeleteSessions(userIdentifier);
        _vehiclePIDCapabilityEraser.DeleteCapabilities(_vehicleDataEraser.LocalVehicleIDs(userIdentifier));
        _vehicleDataEraser.RemoveLocalVehicleCache(userIdentifier);
    }
}
