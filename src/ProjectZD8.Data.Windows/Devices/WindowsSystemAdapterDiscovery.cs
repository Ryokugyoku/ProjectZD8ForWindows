using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.Serial;
using ProjectZD8.Data.Windows.Devices.Bluetooth;
using ProjectZD8.Domain.ValueObjects;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace ProjectZD8.Data.Windows.Devices;

/// <summary>Windows のデバイス情報から USB シリアルと Bluetooth Classic（SPP）のアダプター候補を取得します。</summary>
/// <remarks>
/// iOS 版の <c>MacOSSystemAdapterDiscovery</c> に対応します。USB は COM ポートを、Bluetooth はペアリング済みで
/// RFCOMM の Serial Port サービスを持つ機器を返します。Bluetooth の仮想 COM ポートは USB 側から除くため、
/// 同じ機器が二重に並びません。BLE は <see cref="WindowsBluetoothLEAdapterDiscovery"/> が担当し、
/// <see cref="CombinedAdapterDiscovery"/> で統合します。UI スレッドから呼ぶ前提です。
/// </remarks>
public sealed class WindowsSystemAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>COM ポート名のインターフェイス属性です。</summary>
    private const string PortNameProperty = "System.DeviceInterface.Serial.PortName";

    /// <summary>デバイスインスタンスIDの属性です。</summary>
    private const string InstanceIdProperty = "System.Devices.DeviceInstanceId";

    /// <summary>USB バスが報告した製品名（DEVPKEY_Device_BusReportedDeviceDesc）です。</summary>
    private const string BusReportedDescriptionProperty = "{540b947e-8b40-45bc-a8a2-6a0b894cbda2} 4";

    /// <summary>ドライバーのメーカー名（DEVPKEY_Device_Manufacturer）です。</summary>
    private const string ManufacturerProperty = "{a45c254e-df1c-4efd-8020-67d146a850e0} 13";

    /// <summary>「Ports（COM と LPT）」デバイスクラスのうち接続中のものを選ぶ AQS です。</summary>
    private const string PortsClassSelector =
        "System.Devices.ClassGuid:=\"{4d36e978-e325-11ce-bfc1-08002be10318}\" AND System.Devices.Present:=System.StructuredQueryType.Boolean#True";

    /// <summary>COM ポート情報を USB 候補へ変換します。</summary>
    private readonly SerialPortAdapterMapper serialMapper = new();

    /// <summary>Bluetooth Classic 機器を OBD 候補へ変換します。</summary>
    private readonly BluetoothClassicOBDCandidateMapper classicMapper = new();

    /// <summary>指定された接続方式で Windows から参照できる候補を取得します。</summary>
    /// <remarks>責務: 1種類の接続方式を対応する Windows デバイス探索処理へ振り分けます。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>Windows のデバイス情報から取得できた候補。</returns>
    /// <exception cref="AdapterDiscoveryException">Bluetooth を使えない場合の探索エラー。</exception>
    public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode) => mode switch
    {
        AdapterTransportMode.Usb => DiscoverUSBDevices(),
        AdapterTransportMode.Bluetooth => DiscoverBluetoothClassicDevices(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>接続中の USB シリアル（COM）ポートを候補へ変換します。</summary>
    /// <remarks>
    /// 責務: COM ポートのインターフェイスと「Ports」クラスのデバイスの両方を列挙し、ポート名で重複を除いて候補にします
    /// （ドライバーによってはインターフェイスを登録しないため）。
    /// </remarks>
    /// <returns>USB シリアル候補（ポート名順）。</returns>
    private async Task<IReadOnlyList<DiscoveredAdapter>> DiscoverUSBDevices()
    {
        var adapters = new Dictionary<string, DiscoveredAdapter>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var interfaces = await DeviceInformation.FindAllAsync(
                SerialDevice.GetDeviceSelector(),
                [PortNameProperty, InstanceIdProperty]);
            foreach (var information in interfaces)
            {
                var portName = information.Properties.TryGetValue(PortNameProperty, out var port) ? port as string : null;
                var instanceId = information.Properties.TryGetValue(InstanceIdProperty, out var id) ? id as string : null;
                var adapter = await MakeUSBAdapter(portName, instanceId ?? information.Id, information.Name);
                if (adapter is not null)
                {
                    adapters.TryAdd(adapter.SystemIdentifier, adapter);
                }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // インターフェイスを列挙できない場合も、下のデバイスクラスの列挙で補います。
        }

        try
        {
            var devices = await DeviceInformation.FindAllAsync(
                PortsClassSelector,
                [InstanceIdProperty],
                DeviceInformationKind.Device);
            foreach (var information in devices)
            {
                var portName = SerialPortAdapterMapper.PortNameFromFriendlyName(information.Name);
                if (portName is null || adapters.ContainsKey(portName))
                {
                    continue;
                }

                var adapter = await MakeUSBAdapter(portName, information.Id, information.Name);
                if (adapter is not null)
                {
                    adapters.TryAdd(adapter.SystemIdentifier, adapter);
                }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // デバイスクラスを列挙できない環境では、インターフェイスから得た候補だけを返します。
        }

        return adapters.Values.OrderBy(adapter => adapter.SystemIdentifier, StringComparer.Ordinal).ToArray();
    }

    /// <summary>1件の COM ポートを、親デバイスの名称を補って候補へ変換します。</summary>
    /// <remarks>責務: デバイスインスタンスからバスが報告した製品名とメーカー名を読み、マッパーへ渡します。</remarks>
    /// <param name="portName">COM ポート名。</param>
    /// <param name="instanceId">デバイスインスタンスID。</param>
    /// <param name="friendlyName">OS のフレンドリ名。</param>
    /// <returns>USB シリアル候補。対象外の場合は <c>null</c>。</returns>
    private async Task<DiscoveredAdapter?> MakeUSBAdapter(string? portName, string instanceId, string? friendlyName)
    {
        string? product = null;
        string? manufacturer = null;
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(
                instanceId,
                [BusReportedDescriptionProperty, ManufacturerProperty],
                DeviceInformationKind.Device);
            product = device.Properties.TryGetValue(BusReportedDescriptionProperty, out var description) ? description as string : null;
            manufacturer = device.Properties.TryGetValue(ManufacturerProperty, out var maker) ? maker as string : null;
        }
        catch (Exception)
        {
            // 親デバイスの属性を読めない場合はフレンドリ名だけで表示します。
        }

        return serialMapper.MakeAdapter(portName, instanceId, manufacturer, string.IsNullOrWhiteSpace(product) ? friendlyName : product);
    }

    /// <summary>ペアリング済みで Serial Port サービスを持つ Bluetooth Classic 機器を候補へ変換します。</summary>
    /// <remarks>責務: Windows のペアリング一覧を OBD 名称とSPP公開で絞り込み、アドレス順の候補一覧へ写像します。</remarks>
    /// <returns>Bluetooth Classic 候補。</returns>
    /// <exception cref="AdapterDiscoveryException">Bluetooth を使えない場合。</exception>
    private async Task<IReadOnlyList<DiscoveredAdapter>> DiscoverBluetoothClassicDevices()
    {
        await WindowsBluetoothAvailability.Ensure(requiresLowEnergy: false);
        var paired = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        var adapters = new List<DiscoveredAdapter>();
        foreach (var information in paired)
        {
            try
            {
                using var device = await BluetoothDevice.FromIdAsync(information.Id);
                if (device is null)
                {
                    continue;
                }

                var services = await device.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached);
                var hasSerialPortService = services.Error == BluetoothError.Success && services.Services.Count > 0;
                foreach (var service in services.Services)
                {
                    service.Dispose();
                }

                var adapter = classicMapper.MakeAdapter(
                    BluetoothAddressText.Format(device.BluetoothAddress),
                    device.Name,
                    isPaired: device.DeviceInformation.Pairing.IsPaired,
                    hasSerialPortService: hasSerialPortService,
                    isConnected: device.ConnectionStatus == BluetoothConnectionStatus.Connected);
                if (adapter is not null)
                {
                    adapters.Add(adapter);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // 1件の機器を読めなくても、ほかの機器の探索を続けます。
            }
        }

        return classicMapper.Sorted(adapters);
    }
}
