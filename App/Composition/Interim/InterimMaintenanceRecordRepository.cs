using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】アカウントごとの整備記録をメモリに持つ代用の保存先です。
/// </summary>
/// <remarks>iOS 版の <c>GRDBMaintenanceRecordRepository</c> の代わり。Data 層の SQLite 実装ができたら差し替えます。</remarks>
public sealed class InterimMaintenanceRecordRepository : IMaintenanceRecordRepository
{
    /// <summary>状態を守る lock です。</summary>
    private readonly Lock _gate = new();

    /// <summary>アカウントごとの整備記録です。</summary>
    private readonly Dictionary<string, List<MaintenanceRecord>> _records = new(StringComparer.Ordinal);

    /// <summary>アカウントの整備記録を返します（削除済みを含む）。</summary>
    /// <remarks>責務: 保存済みの整備記録を保存順で返します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>整備記録。</returns>
    public Task<IReadOnlyList<MaintenanceRecord>> Records(string accountIdentifier)
    {
        lock (_gate)
        {
            IReadOnlyList<MaintenanceRecord> result = _records.TryGetValue(accountIdentifier, out var list) ? list.ToList() : [];
            return Task.FromResult(result);
        }
    }

    /// <summary>整備記録を追加または更新します。</summary>
    /// <remarks>責務: ID が同じ記録を置き換え、なければ追加します。</remarks>
    /// <param name="record">保存する記録。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了を表すタスク。</returns>
    public Task Save(MaintenanceRecord record, string accountIdentifier)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(accountIdentifier, out var list))
            {
                list = [];
                _records[accountIdentifier] = list;
            }

            var index = list.FindIndex(item => item.Id == record.Id);
            if (index >= 0)
            {
                list[index] = record;
            }
            else
            {
                list.Add(record);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>アカウントの整備記録を丸ごと置き換えます。</summary>
    /// <remarks>責務: 同期の結果でアカウントの記録一式を置き換えます。</remarks>
    /// <param name="records">新しい記録一式。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了を表すタスク。</returns>
    public Task ReplaceAll(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier)
    {
        lock (_gate)
        {
            _records[accountIdentifier] = records.ToList();
        }

        return Task.CompletedTask;
    }
}
