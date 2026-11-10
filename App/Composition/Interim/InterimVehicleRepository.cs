using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】アカウントごとの登録車両をメモリに持つ代用の保存先です。
/// </summary>
/// <remarks>
/// Windows 版の正式な構成は「SQLite が正、CloudKit の VehicleCatalog と同期」（永続化担当とネットワーク担当）。
/// それができたら差し替えます。アカウント削除用の消去境界も兼ねます。
/// </remarks>
public sealed class InterimVehicleRepository : IVehicleRepository, IAccountVehicleDataErasurePort
{
    /// <summary>状態を守る lock です。</summary>
    private readonly Lock _gate = new();

    /// <summary>アカウントごとの車両です。</summary>
    private readonly Dictionary<string, List<VehicleProfile>> _vehicles = new(StringComparer.Ordinal);

    /// <summary>アカウントの車両を返します。</summary>
    /// <remarks>責務: 保存済みの車両を登録順で返します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>車両の一覧。</returns>
    public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier)
    {
        lock (_gate)
        {
            IReadOnlyList<VehicleProfile> result = _vehicles.TryGetValue(accountIdentifier, out var list) ? list.ToList() : [];
            return Task.FromResult(result);
        }
    }

    /// <summary>車両を追加または更新します。</summary>
    /// <remarks>責務: ID が同じ車両を置き換え、なければ末尾に追加します。</remarks>
    /// <param name="vehicle">保存する車両。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了を表すタスク。</returns>
    public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier)
    {
        lock (_gate)
        {
            var list = List(accountIdentifier);
            var index = list.FindIndex(item => item.Id == vehicle.Id);
            if (index >= 0)
            {
                list[index] = vehicle;
            }
            else
            {
                list.Add(vehicle);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>車両を削除します。</summary>
    /// <remarks>責務: ID が一致する車両を消します。</remarks>
    /// <param name="id">車両 ID。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了を表すタスク。</returns>
    public Task DeleteVehicle(VehicleID id, string accountIdentifier)
    {
        lock (_gate)
        {
            List(accountIdentifier).RemoveAll(item => item.Id == id);
        }

        return Task.CompletedTask;
    }

    /// <summary>アカウントの車両データを全部消します。</summary>
    /// <remarks>責務: アカウント削除時に、そのアカウントの車両を消します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了を表すタスク。</returns>
    public Task DeleteAllVehicleData(string accountIdentifier)
    {
        RemoveLocalVehicleCache(accountIdentifier);
        return Task.CompletedTask;
    }

    /// <summary>端末にある車両 ID を返します。</summary>
    /// <remarks>責務: 消去前に関連データ（対応 PID）を消すための ID を集めます。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>車両 ID。</returns>
    public IReadOnlyList<VehicleID> LocalVehicleIDs(string accountIdentifier)
    {
        lock (_gate)
        {
            return _vehicles.TryGetValue(accountIdentifier, out var list) ? list.Select(item => item.Id).ToList() : [];
        }
    }

    /// <summary>端末の車両キャッシュを消します。</summary>
    /// <remarks>責務: アカウントの車両一覧をメモリから消します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    public void RemoveLocalVehicleCache(string accountIdentifier)
    {
        lock (_gate)
        {
            _vehicles.Remove(accountIdentifier);
        }
    }

    /// <summary>アカウントの可変一覧を返し、なければ作ります（lock の中で呼ぶ）。</summary>
    /// <remarks>責務: アカウントごとの可変一覧を取得します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>可変の一覧。</returns>
    private List<VehicleProfile> List(string accountIdentifier)
    {
        if (!_vehicles.TryGetValue(accountIdentifier, out var list))
        {
            list = [];
            _vehicles[accountIdentifier] = list;
        }

        return list;
    }
}
