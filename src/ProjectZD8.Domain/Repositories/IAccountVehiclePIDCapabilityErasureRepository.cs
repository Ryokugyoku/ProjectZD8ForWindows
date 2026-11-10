using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>アカウント削除時に登録車両別PID収集設定を消去します。</summary>
public interface IAccountVehiclePIDCapabilityErasureRepository
{
    /// <summary>指定車両群のPID対応情報と収集選択を削除します。</summary>
    /// <remarks>責務: 登録車両ID群に属する車両別PID設定をローカル保存先から削除済み状態へします。</remarks>
    /// <param name="vehicleIDs">削除対象アカウントに登録されていた車両ID群。</param>
    /// <exception cref="Exception">車両別PID設定を完全に削除できない場合の保存先エラー。</exception>
    void DeleteCapabilities(IReadOnlyList<VehicleID> vehicleIDs);
}