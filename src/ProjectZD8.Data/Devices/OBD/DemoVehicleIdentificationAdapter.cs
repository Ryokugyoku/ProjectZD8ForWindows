using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>物理通信なしで決定的なデモ車両識別観測を返します。</summary>
public sealed class DemoVehicleIdentificationAdapter : IVehicleIdentificationPort
{
    /// <summary>観測日時を提供する注入済みクロックです。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>観測日時の供給元を保持して生成します。</summary>
    /// <remarks>責務: デモ車両識別観測を1件の注入済みクロックへ固定します。</remarks>
    /// <param name="now">観測完了日時の供給元。<c>null</c> の場合は現在時刻。</param>
    public DemoVehicleIdentificationAdapter(Func<DateTimeOffset>? now = null)
    {
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>デモVINと車両情報を実通信観測と同じ型で返します。</summary>
    /// <remarks>責務: デモ終端への1件の識別要求を通常登録可能な車両観測へ変換します。</remarks>
    /// <param name="endpoint">デモOBD接続終端。</param>
    /// <returns>検査文字を含む合成VINとデモ車両フィールド。</returns>
    /// <exception cref="VehicleIdentificationException">デモ以外の終端では <c>TransportUnsupported</c>。</exception>
    public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint)
    {
        if (!DemoOBDAdapter.Matches(endpoint))
        {
            return Task.FromException<VehicleIdentificationSnapshot>(
                new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported()));
        }

        var syntheticVIN = DemoOBDAdapter.SyntheticVIN(endpoint);
        return Task.FromResult(new VehicleIdentificationSnapshot(
            vin: syntheticVIN,
            fields:
            [
                new("manufacturer", "Manufacturer", "RevTorque Demo Motors", endpoint.DisplayName),
                new("engineModel", "Engine Model", "ZD8-SIM-24", endpoint.DisplayName),
                new("obdProtocol", "OBD Protocol", "ISO 15765-4 (simulated)", endpoint.DisplayName),
            ],
            observedAt: now(),
            rawResponses:
            [
                new("vehicleIdentificationNumber", $"49 02 01 {syntheticVIN}"),
            ]));
    }
}
