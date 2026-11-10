using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>車両DBを準備できない場合に明示的な失敗を返します。</summary>
/// <remarks>iOS版にはありません（iOS版の車両はCloudKitだけに保存されるため）。Windows版で車両をSQLiteへ保存するのに合わせて追加しました。</remarks>
public sealed class UnavailableVehicleRepository : IVehicleRepository
{
    /// <summary>車両の読込を失敗させます。</summary>
    /// <remarks>責務: 照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="accountIdentifier">読み込めないアカウント。</param>
    /// <returns>常に失敗するタスク。</returns>
    public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier) =>
        Task.FromException<IReadOnlyList<VehicleProfile>>(Unavailable());

    /// <summary>車両の保存を失敗させます。</summary>
    /// <remarks>責務: 保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="vehicle">保存できない車両。</param>
    /// <param name="accountIdentifier">保存できないアカウント。</param>
    /// <returns>常に失敗するタスク。</returns>
    public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier) => Task.FromException(Unavailable());

    /// <summary>車両の削除を失敗させます。</summary>
    /// <remarks>責務: 削除要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="id">削除できない車両。</param>
    /// <param name="accountIdentifier">削除できないアカウント。</param>
    /// <returns>常に失敗するタスク。</returns>
    public Task DeleteVehicle(VehicleID id, string accountIdentifier) => Task.FromException(Unavailable());

    /// <summary>利用不能エラーを生成します。</summary>
    /// <remarks>責務: 共通のエラー値を作ります。</remarks>
    /// <returns>例外。</returns>
    private static InvalidOperationException Unavailable() => new("Vehicle storage is unavailable");
}
