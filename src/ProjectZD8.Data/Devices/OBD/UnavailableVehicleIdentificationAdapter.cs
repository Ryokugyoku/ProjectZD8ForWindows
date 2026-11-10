using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>実OBD識別プロトコルが提供されるまで明示的に利用不能を返します。</summary>
public sealed class UnavailableVehicleIdentificationAdapter : IVehicleIdentificationPort
{
    /// <summary>呼び出し元へ返す利用不能理由です。</summary>
    private readonly VehicleIdentificationError error;

    /// <summary>利用不能理由を保持する境界を生成します。</summary>
    /// <remarks>責務: 1件の製品構成上の利用不能理由を識別境界へ固定します。</remarks>
    /// <param name="error">すべての識別要求へ返す理由。<c>null</c> の場合は <c>Unavailable</c>。</param>
    public UnavailableVehicleIdentificationAdapter(VehicleIdentificationError? error = null)
    {
        this.error = error ?? new VehicleIdentificationError.Unavailable();
    }

    /// <summary>実装未提供を成功へ変換せず通知します。</summary>
    /// <remarks>責務: OBD識別の未提供状態を型付き失敗として返します。</remarks>
    /// <param name="endpoint">使用されない接続終端。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="VehicleIdentificationException">常に保持した理由。</exception>
    public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint) =>
        Task.FromException<VehicleIdentificationSnapshot>(new VehicleIdentificationException(error));
}
