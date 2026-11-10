using System;
using System.Threading.Tasks;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】iOS 版の <c>DemoVehicleIdentificationAdapter</c> を写した、デモアダプターの合成 VIN を返す車両識別です。
/// </summary>
/// <remarks>デモ以外の接続先は「通信方式が未対応」で失敗させます（実機の識別はデバイス担当の Data 層で作る）。</remarks>
public sealed class InterimDemoVehicleIdentificationAdapter : IVehicleIdentificationPort
{
    /// <summary>現在時刻を返す時計です。</summary>
    private readonly Func<DateTimeOffset> _now;

    /// <summary>時計を指定して作ります。</summary>
    /// <param name="now">現在時刻を返す時計。省略時はシステム時計。</param>
    public InterimDemoVehicleIdentificationAdapter(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>デモ車両の識別結果を返します。</summary>
    /// <remarks>責務: デモアダプターの接続先を、合成 VIN と模擬の識別項目へ変換します。</remarks>
    /// <param name="endpoint">接続先。</param>
    /// <returns>識別結果。</returns>
    public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint)
    {
        if (!InterimDemoOBDAdapter.Matches(endpoint))
        {
            return Task.FromException<VehicleIdentificationSnapshot>(
                new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported()));
        }

        var vin = InterimDemoOBDAdapter.SyntheticVIN(endpoint);
        return Task.FromResult(new VehicleIdentificationSnapshot(
            vin: vin,
            fields:
            [
                new VehicleIdentificationSnapshot.Field("manufacturer", "Manufacturer", "RevTorque Demo Motors", endpoint.DisplayName),
                new VehicleIdentificationSnapshot.Field("engineModel", "Engine Model", "ZD8-SIM-24", endpoint.DisplayName),
                new VehicleIdentificationSnapshot.Field("obdProtocol", "OBD Protocol", "ISO 15765-4 (simulated)", endpoint.DisplayName),
            ],
            observedAt: _now(),
            rawResponses: [new VehicleIdentificationSnapshot.RawResponse("vehicleIdentificationNumber", "49 02 01 " + vin)]));
    }
}
