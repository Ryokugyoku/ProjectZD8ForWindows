using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>登録車両をアカウントスコープ内で読み書きする能力です。</summary>
public interface IVehicleRepository
{
    /// <summary>指定アカウントの登録車両を読み込みます。</summary>
    /// <remarks>責務: 1件のアカウントに属する現在の車両一覧を返します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>更新日時の新しい順に並ぶ登録車両。</returns>
    Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier);

    /// <summary>指定アカウントへ車両プロフィールを保存します。</summary>
    /// <remarks>責務: 1件の車両プロフィールを同じアカウントスコープへ作成または更新します。</remarks>
    /// <param name="vehicle">保存する車両プロフィール。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier);

    /// <summary>指定アカウントから車両プロフィールを削除します。</summary>
    /// <remarks>責務: 1件のアカウントスコープから指定車両だけを削除します。</remarks>
    /// <param name="id">削除する車両ID。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    Task DeleteVehicle(VehicleID id, string accountIdentifier);
}