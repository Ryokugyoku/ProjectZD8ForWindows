using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Domain.Repositories;

/// <summary>端末内の車両別整備記録を読み書きする能力です。</summary>
public interface IMaintenanceRecordRepository
{
    /// <summary>指定アカウントに属する削除墓石を含む全整備記録を取得します。</summary>
    /// <remarks>責務: 1件のアカウントスコープから現在の整備記録集合を返します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>更新日時の新しい順に並ぶ整備記録。</returns>
    /// <exception cref="Exception">永続化読込に失敗した場合のエラー。</exception>
    Task<IReadOnlyList<MaintenanceRecord>> Records(string accountIdentifier);

    /// <summary>指定アカウントへ整備記録を作成または更新します。</summary>
    /// <remarks>責務: 1件の整備記録を端末内の同一アカウントスコープへ保存します。</remarks>
    /// <param name="record">保存する整備記録。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    /// <exception cref="Exception">永続化書込に失敗した場合のエラー。</exception>
    Task Save(MaintenanceRecord record, string accountIdentifier);

    /// <summary>同期結果の記録集合で指定アカウントの端末内状態を置き換えます。</summary>
    /// <remarks>責務: 1件のアカウントに対する同期済み整備集合をトランザクション保存します。</remarks>
    /// <param name="records">墓石を含む同期済み記録集合。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>置き換えの完了を表すタスク。</returns>
    /// <exception cref="Exception">一括保存に失敗した場合のエラー。</exception>
    Task ReplaceAll(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier);
}