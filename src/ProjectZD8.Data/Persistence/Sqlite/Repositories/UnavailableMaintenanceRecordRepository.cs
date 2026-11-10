using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>整備記録DBを準備できない場合に明示的な失敗を返します。</summary>
/// <remarks>iOS版 <c>UnavailableMaintenanceRecordRepository</c> の移植です。</remarks>
public sealed class UnavailableMaintenanceRecordRepository : IMaintenanceRecordRepository
{
    /// <summary>整備記録の読込を利用不能として失敗させます。</summary>
    /// <remarks>責務: 照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="accountIdentifier">読み込めないアカウント。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="MaintenanceRepositoryException">常に <c>Unavailable</c>。</exception>
    public Task<IReadOnlyList<MaintenanceRecord>> Records(string accountIdentifier) =>
        Task.FromException<IReadOnlyList<MaintenanceRecord>>(new MaintenanceRepositoryException(MaintenanceRepositoryError.Unavailable));

    /// <summary>整備記録の保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="record">保存できない整備記録。</param>
    /// <param name="accountIdentifier">保存できないアカウント。</param>
    /// <returns>常に失敗するタスク。</returns>
    /// <exception cref="MaintenanceRepositoryException">常に <c>Unavailable</c>。</exception>
    public Task Save(MaintenanceRecord record, string accountIdentifier) =>
        Task.FromException(new MaintenanceRepositoryException(MaintenanceRepositoryError.Unavailable));

    /// <summary>整備記録の置換を利用不能として失敗させます。</summary>
    /// <remarks>責務: 置換要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="records">置換できない記録集合。</param>
    /// <param name="accountIdentifier">置換できないアカウント。</param>
    /// <returns>常に失敗するタスク。</returns>
    /// <exception cref="MaintenanceRepositoryException">常に <c>Unavailable</c>。</exception>
    public Task ReplaceAll(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier) =>
        Task.FromException(new MaintenanceRepositoryException(MaintenanceRepositoryError.Unavailable));
}
