using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Application.Maintenance.Ports;

/// <summary>CloudKitなどの端末間共有先へ整備記録集合を読み書きする能力です。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public interface IMaintenanceRemoteStore
{
    /// <summary>指定アカウントの同期対象整備記録を取得します。</summary>
    /// <remarks>責務: 1件のアカウントスコープから墓石を含む遠隔整備集合を返します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>遠隔保存されている整備記録集合。</returns>
    /// <exception cref="Exception">遠隔読込に失敗した場合のエラー。</exception>
    Task<IReadOnlyList<MaintenanceRecord>> FetchRecords(string accountIdentifier);

    /// <summary>指定アカウントの同期対象整備記録を保存します。</summary>
    /// <remarks>責務: 1件のアカウントに対する整備記録集合を遠隔保存します。</remarks>
    /// <param name="records">墓石と写真を含む整備記録集合。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    /// <exception cref="Exception">遠隔保存に失敗した場合のエラー。</exception>
    Task SaveRecords(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier);
}
