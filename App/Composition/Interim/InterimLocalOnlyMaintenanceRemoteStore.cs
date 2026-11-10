using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectZD8.Application.Maintenance.Ports;
using ProjectZD8.Domain.Entities;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】クラウドを使わないローカルモード用の整備記録の同期先です。何も持たず、保存も捨てます。
/// </summary>
/// <remarks>iOS 版の <c>CloudKitMaintenanceRemoteStore</c> の代わり。ネットワーク担当の CloudKit 実装ができたら差し替えます。</remarks>
public sealed class InterimLocalOnlyMaintenanceRemoteStore : IMaintenanceRemoteStore
{
    /// <summary>クラウドの記録として、いつも空を返します。</summary>
    /// <remarks>責務: ローカルモードでは同期相手がいないことを空の一覧で表します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>空の一覧。</returns>
    public Task<IReadOnlyList<MaintenanceRecord>> FetchRecords(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<MaintenanceRecord>>([]);

    /// <summary>クラウドへの保存を行いません。</summary>
    /// <remarks>責務: ローカルモードでは送信しないため、何もせずに完了します。</remarks>
    /// <param name="records">保存する記録。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task SaveRecords(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier) => Task.CompletedTask;
}
