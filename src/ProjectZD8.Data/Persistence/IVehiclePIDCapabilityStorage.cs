using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence;

/// <summary>
/// 車両別対応 PID の保存と、アカウント削除のときの消去をまとめた型です。
/// </summary>
/// <remarks>
/// iOS 版の組み立ての <c>any VehiclePIDCapabilityRepository &amp; AccountVehiclePIDCapabilityErasureRepository</c> に当たります。
/// メンバーは足しません。
/// </remarks>
public interface IVehiclePIDCapabilityStorage :
    IVehiclePIDCapabilityRepository,
    IAccountVehiclePIDCapabilityErasureRepository
{
}
