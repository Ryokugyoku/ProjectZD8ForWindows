using ProjectZD8.Application.Maintenance.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.Maintenance.UseCases;

/// <summary>ローカルとCloudKitの車両別整備記録を最終更新日時で双方向統合します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class SynchronizeMaintenanceRecordsUseCase
{
    /// <summary>端末内のGRDB整備保存先です。</summary>
    private readonly IMaintenanceRecordRepository localRepository;

    /// <summary>端末間で共有する遠隔保存先です。</summary>
    private readonly IMaintenanceRemoteStore remoteStore;

    /// <summary>ローカル保存先と遠隔保存先を注入します。</summary>
    /// <remarks>責務: 整備記録の双方向同期に必要な2つの境界を固定します。</remarks>
    /// <param name="localRepository">GRDBなどの端末内保存先。</param>
    /// <param name="remoteStore">CloudKitなどの端末間共有先。</param>
    public SynchronizeMaintenanceRecordsUseCase(IMaintenanceRecordRepository localRepository, IMaintenanceRemoteStore remoteStore)
    {
        this.localRepository = localRepository;
        this.remoteStore = remoteStore;
    }

    /// <summary>ローカルと遠隔の記録を統合して両方へ同一集合を保存します。</summary>
    /// <remarks>責務: 1件のアカウントについて記録IDごとの最新更新を採用し双方向へ反映します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>削除墓石を除き実施日時の新しい順に並べた整備記録。</returns>
    /// <exception cref="Exception">ローカルまたは遠隔の読書きに失敗した場合のエラー。</exception>
    public async Task<IReadOnlyList<MaintenanceRecord>> Execute(string accountIdentifier)
    {
        var localTask = localRepository.Records(accountIdentifier);
        var remote = await remoteStore.FetchRecords(accountIdentifier);
        var local = await localTask;
        var merged = Merge(local, remote);
        await localRepository.ReplaceAll(merged, accountIdentifier);
        await remoteStore.SaveRecords(merged, accountIdentifier);
        return merged
            .Where(record => record.DeletedAt is null)
            .OrderByDescending(record => record.PerformedAt)
            .ToArray();
    }

    /// <summary>同一IDの新しい更新だけを採用して同期集合を作成します。</summary>
    /// <remarks>責務: 2つの整備集合を更新日時基準の決定的な1集合へ統合します。</remarks>
    /// <param name="local">端末内の整備集合。</param>
    /// <param name="remote">遠隔保存された整備集合。</param>
    /// <returns>記録IDが重複しない同期対象集合。</returns>
    public static IReadOnlyList<MaintenanceRecord> Merge(
        IReadOnlyList<MaintenanceRecord> local,
        IReadOnlyList<MaintenanceRecord> remote)
    {
        var byID = remote.ToDictionary(record => record.Id);
        foreach (var record in local)
        {
            if (!byID.TryGetValue(record.Id, out var current))
            {
                byID[record.Id] = record;
                continue;
            }

            if (record.UpdatedAt >= current.UpdatedAt)
            {
                byID[record.Id] = record;
            }
        }

        var values = byID.Values.ToList();
        values.Sort((lhs, rhs) =>
        {
            if (lhs.UpdatedAt == rhs.UpdatedAt)
            {
                return string.CompareOrdinal(UuidString(lhs), UuidString(rhs));
            }

            return rhs.UpdatedAt.CompareTo(lhs.UpdatedAt);
        });
        return values;
    }

    /// <summary>Swift の <c>UUID.uuidString</c> と同じ表記（大文字・ハイフン区切り）を返します。</summary>
    /// <remarks>責務: 同時刻の記録を Swift と同じ順に並べるための比較キーを作ります。</remarks>
    /// <param name="record">対象の整備記録。</param>
    /// <returns>大文字の UUID 文字列。</returns>
    private static string UuidString(MaintenanceRecord record) =>
        record.Id.RawValue.ToString("D").ToUpperInvariant();
}
