using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>車両単位の対応PIDと収集選択を永続化する能力です。</summary>
public interface IVehiclePIDCapabilityRepository
{
    /// <summary>指定車両で確認済みの対応PIDを取得します。</summary>
    /// <remarks>責務: 1件の車両IDに属する対応PID設定をService/PID順で返します。</remarks>
    /// <param name="vehicleID">照会するアプリ内車両識別子。</param>
    /// <returns>確認済み対応PID設定。</returns>
    /// <exception cref="Exception">永続読込に失敗した場合のエラー。</exception>
    IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID vehicleID);

    /// <summary>未収集車両へ対応PIDを全件収集有効で登録します。</summary>
    /// <remarks>責務: 0件の車両スコープへ1回の探索結果を原子的に初期登録します。</remarks>
    /// <param name="capabilities">登録する対応PID設定。</param>
    /// <param name="vehicleID">登録先のアプリ内車両識別子。</param>
    /// <exception cref="Exception">既存データまたは永続書込失敗の場合のエラー。</exception>
    void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID);

    /// <summary>応答確認済みPIDを既存の収集選択を変えず追加します。</summary>
    /// <remarks>責務: 1台の車両へ新たに応答したPIDだけを非破壊で登録します。</remarks>
    /// <param name="capabilities">新たに応答確認できたPID設定。</param>
    /// <param name="vehicleID">登録先の車両識別子。</param>
    /// <exception cref="Exception">車両不一致または永続書込失敗の場合のエラー。</exception>
    void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID);

    /// <summary>1件の対応PIDの収集選択を更新します。</summary>
    /// <remarks>責務: 指定車両の指定PIDだけの収集有効状態を変更します。</remarks>
    /// <param name="isEnabled">新しい収集有効状態。</param>
    /// <param name="request">更新するService/PID。</param>
    /// <param name="vehicleID">更新対象の車両識別子。</param>
    /// <exception cref="Exception">対象不在または永続書込失敗の場合のエラー。</exception>
    void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID vehicleID);
}