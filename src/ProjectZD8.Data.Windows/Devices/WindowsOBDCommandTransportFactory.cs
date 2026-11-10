using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Data.Windows.Devices.Bluetooth;
using ProjectZD8.Data.Windows.Devices.Serial;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Windows.Devices;

/// <summary>OBD接続終端の物理方式に対応する Windows の Transport を生成します。</summary>
/// <remarks>
/// iOS 版の <c>MacOSApplicationComposition.makeOBDCommandTransport(for:)</c> に対応します。
/// <see cref="OBDLinkEXPIDTelemetryAdapter"/> と <see cref="SerialELMVehicleIdentificationAdapter"/> の
/// <c>makeTransport</c> に <see cref="Make"/> をそのまま渡します。
/// </remarks>
public static class WindowsOBDCommandTransportFactory
{
    /// <summary>終端に対応する Transport を生成します。</summary>
    /// <remarks>責務: 1件の終端をシリアル、RFCOMM、GATT UART のいずれかの Transport へ振り分けます。</remarks>
    /// <param name="endpoint">接続する終端。</param>
    /// <returns>まだ開いていない Transport。</returns>
    /// <exception cref="ProjectZD8.Application.VehicleManagement.Ports.VehicleIdentificationException">BLE 終端の識別子が不正な場合は <c>TransportUnsupported</c>。</exception>
    public static IOBDCommandTransport Make(OBDConnectionEndpoint endpoint) => endpoint.Transport switch
    {
        OBDConnectionEndpointTransport.Serial => new WindowsSerialPortOBDTransport(endpoint.SystemIdentifier),
        OBDConnectionEndpointTransport.BluetoothClassic => new WindowsBluetoothRFCOMMOBDTransport(endpoint.SystemIdentifier),
        OBDConnectionEndpointTransport.BluetoothLowEnergy => new WindowsBluetoothLEOBDTransport(endpoint),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Transport, null),
    };
}
