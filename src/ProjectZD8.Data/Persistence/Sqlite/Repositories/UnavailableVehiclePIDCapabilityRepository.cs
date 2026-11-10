using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>車両別PID DBを準備できない場合に明示的な失敗を返します。</summary>
/// <remarks>iOS版 <c>UnavailableVehiclePIDCapabilityRepository</c> の移植です（iOS版の非公開エラー型は <see cref="InvalidOperationException"/> にしました）。</remarks>
public sealed class UnavailableVehiclePIDCapabilityRepository : IVehiclePIDCapabilityRepository, IAccountVehiclePIDCapabilityErasureRepository
{
    /// <summary>対応PIDの読込を失敗させます。</summary>
    /// <remarks>責務: 照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="vehicleID">読み込めない車両。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID vehicleID) => throw Unavailable();

    /// <summary>初期登録を失敗させます。</summary>
    /// <remarks>責務: 登録要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="capabilities">登録できない対応PID。</param>
    /// <param name="vehicleID">登録できない車両。</param>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID) => throw Unavailable();

    /// <summary>追加登録を失敗させます。</summary>
    /// <remarks>責務: 追加要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="capabilities">追加できない対応PID。</param>
    /// <param name="vehicleID">追加できない車両。</param>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID) => throw Unavailable();

    /// <summary>収集選択の更新を失敗させます。</summary>
    /// <remarks>責務: 更新要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="isEnabled">保存できない状態。</param>
    /// <param name="request">更新できないPID。</param>
    /// <param name="vehicleID">更新できない車両。</param>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID vehicleID) => throw Unavailable();

    /// <summary>車両群の削除を失敗させます。</summary>
    /// <remarks>責務: 削除要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="vehicleIDs">削除できない車両群。</param>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public void DeleteCapabilities(IReadOnlyList<VehicleID> vehicleIDs) => throw Unavailable();

    /// <summary>利用不能エラーを生成します。</summary>
    /// <remarks>責務: 共通のエラー値を作ります。</remarks>
    /// <returns>例外。</returns>
    private static InvalidOperationException Unavailable() => new("Vehicle PID capability storage is unavailable");
}
