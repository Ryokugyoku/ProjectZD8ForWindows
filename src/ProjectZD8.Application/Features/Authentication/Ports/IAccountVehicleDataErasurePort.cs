using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>アカウントに属するCloudKit車両カタログと端末キャッシュを消去します。</summary>
public interface IAccountVehicleDataErasurePort
{
    /// <summary>CloudKitの車両カタログを削除します。</summary>
    /// <remarks>責務: 1件のアカウント識別子に属する車両カタログをCloudKitから削除済み状態へします。</remarks>
    /// <param name="accountIdentifier">削除対象のAppleアカウント識別子。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    /// <exception cref="Exception">CloudKit車両カタログを削除できない場合のエラー。</exception>
    Task DeleteAllVehicleData(string accountIdentifier);

    /// <summary>現在端末にキャッシュされた登録車両IDを返します。</summary>
    /// <remarks>責務: 1件のアカウント識別子に属するローカル車両キャッシュを車両ID群へ変換します。</remarks>
    /// <param name="accountIdentifier">照会対象のAppleアカウント識別子。</param>
    /// <returns>キャッシュから復元できた登録車両ID群。</returns>
    IReadOnlyList<VehicleID> LocalVehicleIDs(string accountIdentifier);

    /// <summary>現在端末の車両カタログキャッシュだけを削除します。</summary>
    /// <remarks>責務: 遠隔失効した1件のアカウントに属する端末内車両キャッシュを削除済み状態へします。</remarks>
    /// <param name="accountIdentifier">削除対象のAppleアカウント識別子。</param>
    void RemoveLocalVehicleCache(string accountIdentifier);
}
