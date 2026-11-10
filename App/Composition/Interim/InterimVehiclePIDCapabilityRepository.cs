using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】車両ごとの対応 PID と収集の有効・無効をメモリに持つ代用の保存先です。
/// </summary>
/// <remarks>iOS 版の <c>GRDBVehiclePIDCapabilityRepository</c> の代わり。Data 層の SQLite 実装ができたら差し替えます。</remarks>
public sealed class InterimVehiclePIDCapabilityRepository :
    IVehiclePIDCapabilityRepository,
    IAccountVehiclePIDCapabilityErasureRepository
{
    /// <summary>状態を守る lock です。</summary>
    private readonly Lock _gate = new();

    /// <summary>車両ごとの対応 PID（要求ごと）です。</summary>
    private readonly Dictionary<VehicleID, Dictionary<OBDPIDRequest, VehiclePIDCapability>> _capabilities = [];

    /// <summary>車両の対応 PID を要求順に返します。</summary>
    /// <remarks>責務: 1 台分の対応 PID を Service・PID の順で返します。</remarks>
    /// <param name="vehicleID">対象車両。</param>
    /// <returns>対応 PID。</returns>
    public IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID vehicleID)
    {
        lock (_gate)
        {
            return _capabilities.TryGetValue(vehicleID, out var items)
                ? items.Values.OrderBy(item => item.Id.Request.Service).ThenBy(item => item.Id.Request.Pid).ToList()
                : [];
        }
    }

    /// <summary>初回の対応 PID を、まだなければ保存します。</summary>
    /// <remarks>責務: 未登録の車両にだけ初期の対応 PID を入れます。</remarks>
    /// <param name="capabilities">対応 PID。</param>
    /// <param name="vehicleID">対象車両。</param>
    public void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID)
    {
        lock (_gate)
        {
            if (_capabilities.ContainsKey(vehicleID))
            {
                return;
            }

            _capabilities[vehicleID] = capabilities.ToDictionary(item => item.Id.Request);
        }
    }

    /// <summary>新しく見つかった対応 PID を足します。既存の有効・無効は変えません。</summary>
    /// <remarks>責務: 未知の要求だけを追加します。</remarks>
    /// <param name="capabilities">見つかった対応 PID。</param>
    /// <param name="vehicleID">対象車両。</param>
    public void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID)
    {
        lock (_gate)
        {
            if (!_capabilities.TryGetValue(vehicleID, out var items))
            {
                items = [];
                _capabilities[vehicleID] = items;
            }

            foreach (var capability in capabilities)
            {
                items.TryAdd(capability.Id.Request, capability);
            }
        }
    }

    /// <summary>1 件の PID の収集を有効・無効にします。</summary>
    /// <remarks>責務: 既存の対応 PID の収集フラグだけを更新します。</remarks>
    /// <param name="isEnabled">収集するなら true。</param>
    /// <param name="request">対象の要求。</param>
    /// <param name="vehicleID">対象車両。</param>
    public void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID vehicleID)
    {
        lock (_gate)
        {
            if (_capabilities.TryGetValue(vehicleID, out var items) && items.TryGetValue(request, out var capability))
            {
                items[request] = capability with { IsCollectionEnabled = isEnabled };
            }
        }
    }

    /// <summary>指定車両の対応 PID を消します。</summary>
    /// <remarks>責務: アカウント削除時に、その車両の対応 PID を消します。</remarks>
    /// <param name="vehicleIDs">対象車両。</param>
    public void DeleteCapabilities(IReadOnlyList<VehicleID> vehicleIDs)
    {
        lock (_gate)
        {
            foreach (var id in vehicleIDs)
            {
                _capabilities.Remove(id);
            }
        }
    }
}
