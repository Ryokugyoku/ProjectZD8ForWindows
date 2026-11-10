using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>実探索結果へ接続方式別にデモ候補が追加されることを検証します。</summary>
public sealed class DemoIncludedAdapterDiscoveryTests
{
    /// <summary>空のUSB探索にもDEMO USBを3件返します。</summary>
    /// <remarks>責務: USB探索結果が常にデモ候補を含むことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task USBDiscoveryAlwaysIncludesDemoCandidate()
    {
        var discovery = new DemoIncludedAdapterDiscovery(new EmptyAdapterDiscovery(), DemoOBDAdapter.UsbCandidates);

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Usb);

        Assert.Equal(DemoOBDAdapter.UsbCandidates, adapters);
    }

    /// <summary>Bluetooth探索にはUSBデモ候補を混在させません。</summary>
    /// <remarks>責務: Bluetooth探索結果がデモUSB候補を含まないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothDiscoveryDoesNotIncludeDemoUSB()
    {
        var discovery = new DemoIncludedAdapterDiscovery(new EmptyAdapterDiscovery(), DemoOBDAdapter.UsbCandidates);

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        Assert.Empty(adapters);
    }

    /// <summary>Bluetoothデモ候補3件をBluetooth探索へ追加します。</summary>
    /// <remarks>責務: Bluetooth向け構成がデモ候補を返すことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothDiscoveryIncludesBluetoothDemoCandidate()
    {
        var discovery = new DemoIncludedAdapterDiscovery(new EmptyAdapterDiscovery(), DemoOBDAdapter.BluetoothCandidates);

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        Assert.Equal(DemoOBDAdapter.BluetoothCandidates, adapters);
    }

    /// <summary>実Bluetooth探索が利用不能でもデモ候補3件は残します。</summary>
    /// <remarks>責務: Bluetooth利用不可をデモ候補まで消す失敗へ変換しないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothFailureStillReturnsBluetoothDemoCandidate()
    {
        var discovery = new DemoIncludedAdapterDiscovery(new FailingBluetoothAdapterDiscovery(), DemoOBDAdapter.BluetoothCandidates);

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        Assert.Equal(DemoOBDAdapter.BluetoothCandidates, adapters);
    }

    /// <summary>デモ候補の識別子と表示が iOS と同じことを検証します。</summary>
    /// <remarks>責務: 予約済みデモ候補の ID、名前、シリアル番号、合成VINが変わらないことを確認します。</remarks>
    [Fact]
    public void DemoCandidatesMatchSwiftDefinitions()
    {
        Assert.Equal(
            ["usb:projectzd8-demo", "usb:projectzd8-demo-2", "usb:projectzd8-demo-3"],
            DemoOBDAdapter.UsbCandidates.Select(candidate => candidate.Id));
        Assert.Equal(
            ["DEMO Bluetooth", "DEMO Bluetooth 2", "DEMO Bluetooth 3"],
            DemoOBDAdapter.BluetoothCandidates.Select(candidate => candidate.DisplayName));
        Assert.Equal("ZD8-DEMO-0002", DemoOBDAdapter.UsbCandidates[1].SerialNumber);
        Assert.Equal("ZD8-DEMO-BLE-0003", DemoOBDAdapter.BluetoothCandidates[2].SerialNumber);
        Assert.Equal("TESTZD8CXR0000004", DemoOBDAdapter.SyntheticVIN(new OBDConnectionEndpoint(DemoOBDAdapter.BluetoothCandidate)));
        Assert.Equal(
            "TESTZD8CXR0000000",
            DemoOBDAdapter.SyntheticVIN(new OBDConnectionEndpoint(OBDConnectionEndpointTransport.Serial, "COM3", "EX")));
    }
}

/// <summary>Bluetooth利用不能を返すテスト用探索境界です。</summary>
file sealed class FailingBluetoothAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>Bluetooth利用不能エラーを返します。</summary>
    /// <remarks>責務: 1回の探索要求をBluetooth利用不能結果へ変換します。</remarks>
    /// <param name="mode">テストでは使用しない接続方式。</param>
    /// <returns>この実装は候補を返しません。</returns>
    public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode) =>
        Task.FromException<IReadOnlyList<DiscoveredAdapter>>(new AdapterDiscoveryException(AdapterDiscoveryError.BluetoothPoweredOff));
}

/// <summary>候補を返さないテスト用探索境界です。</summary>
file sealed class EmptyAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>任意の接続方式へ空の候補一覧を返します。</summary>
    /// <remarks>責務: 1回の探索要求を空結果へ変換します。</remarks>
    /// <param name="mode">テストでは使用しない接続方式。</param>
    /// <returns>常に空の候補一覧。</returns>
    public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode) =>
        Task.FromResult<IReadOnlyList<DiscoveredAdapter>>([]);
}
