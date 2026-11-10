using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>探索の統合がUSBとBluetooth Classicを維持しながらBLEを統合することを検証します。</summary>
/// <remarks>iOS 版の <c>MacOSAdapterDiscoveryTests</c> に対応します。</remarks>
public sealed class CombinedAdapterDiscoveryTests
{
    /// <summary>USB要求が既存システム探索だけへ渡されることを検証します。</summary>
    /// <remarks>責務: USB探索経路がBLE探索追加後も変更されないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task USBUsesOnlyExistingSystemDiscovery()
    {
        var usb = MakeAdapter("usb:COM3", OBDConnectionEndpointTransport.Serial);
        var systemDiscovery = new AdapterDiscoveryStub([usb]);
        var lowEnergyDiscovery = new AdapterDiscoveryStub([]);
        var discovery = new CombinedAdapterDiscovery(systemDiscovery, lowEnergyDiscovery);

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Usb);

        Assert.Equal([usb], adapters);
        Assert.Equal([AdapterTransportMode.Usb], systemDiscovery.RequestedModes);
        Assert.Empty(lowEnergyDiscovery.RequestedModes);
    }

    /// <summary>Bluetooth要求がClassicとBLEの候補を同じ一覧へ統合することを検証します。</summary>
    /// <remarks>責務: 2種類のBluetooth探索結果が欠落なく返ることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothMergesClassicAndLowEnergyCandidates()
    {
        var classic = MakeAdapter("bluetooth-classic:AA-BB", OBDConnectionEndpointTransport.BluetoothClassic);
        var lowEnergy = MakeAdapter("bluetooth-low-energy:UUID", OBDConnectionEndpointTransport.BluetoothLowEnergy);
        var discovery = new CombinedAdapterDiscovery(new AdapterDiscoveryStub([classic]), new AdapterDiscoveryStub([lowEnergy]));

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        Assert.Equal([classic, lowEnergy], adapters);
    }

    /// <summary>同じ識別子の候補は先に届いた方を残すことを検証します。</summary>
    /// <remarks>責務: Classic と BLE で重複した識別子を先着優先で1件にまとめることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothRemovesDuplicateIdentifiersKeepingFirst()
    {
        var classic = MakeAdapter("same", OBDConnectionEndpointTransport.BluetoothClassic);
        var lowEnergy = MakeAdapter("same", OBDConnectionEndpointTransport.BluetoothLowEnergy);
        var discovery = new CombinedAdapterDiscovery(new AdapterDiscoveryStub([classic]), new AdapterDiscoveryStub([lowEnergy]));

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        Assert.Equal([classic], adapters);
    }

    /// <summary>Bluetooth方式の一方が失敗しても他方の候補を返すことを検証します。</summary>
    /// <remarks>責務: BLE探索の成功をBluetooth Classic探索の失敗から独立して保持することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothKeepsLowEnergyCandidatesWhenClassicFails()
    {
        var lowEnergy = MakeAdapter("bluetooth-low-energy:UUID", OBDConnectionEndpointTransport.BluetoothLowEnergy);
        var discovery = new CombinedAdapterDiscovery(
            new AdapterDiscoveryStub(AdapterDiscoveryError.BluetoothStateUnavailable),
            new AdapterDiscoveryStub([lowEnergy]));

        var adapters = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        Assert.Equal([lowEnergy], adapters);
    }

    /// <summary>Bluetooth方式が両方とも失敗した場合に利用不能を返すことを検証します。</summary>
    /// <remarks>責務: 候補取得不能を空の正常結果へ変換しないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothFailsWhenBothDiscoveriesFail()
    {
        var discovery = new CombinedAdapterDiscovery(
            new AdapterDiscoveryStub(AdapterDiscoveryError.BluetoothStateUnavailable),
            new AdapterDiscoveryStub(AdapterDiscoveryError.BluetoothPoweredOff));

        var error = await Assert.ThrowsAsync<AdapterDiscoveryException>(() => discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth));

        Assert.Equal(AdapterDiscoveryError.BluetoothStateUnavailable, error.Reason);
    }

    /// <summary>テスト用の接続方式を持つ候補を生成します。</summary>
    /// <remarks>責務: 1件の探索候補を指定された安定識別子と物理方式で構築します。</remarks>
    /// <param name="id">候補の安定識別子。</param>
    /// <param name="transport">OBD通信に使用する物理方式。</param>
    /// <returns>接続済みとして扱うBluetoothまたはUSB候補。</returns>
    private static DiscoveredAdapter MakeAdapter(string id, OBDConnectionEndpointTransport transport) =>
        new(
            id: id,
            transportMode: transport == OBDConnectionEndpointTransport.Serial ? AdapterTransportMode.Usb : AdapterTransportMode.Bluetooth,
            displayName: id,
            systemIdentifier: id,
            isConnected: true,
            connectionTransport: transport);
}

/// <summary>探索の要求履歴と決定済み結果を保持するテスト代替です。</summary>
file sealed class AdapterDiscoveryStub : IAdapterDiscoveryPort
{
    /// <summary>成功時に返す候補です。</summary>
    private readonly IReadOnlyList<DiscoveredAdapter>? adapters;

    /// <summary>失敗時に返すエラーです。</summary>
    private readonly AdapterDiscoveryError? error;

    /// <summary>成功結果を注入して生成します。</summary>
    /// <remarks>責務: 1件のテスト用探索結果を後続要求へ返せる状態で保持します。</remarks>
    /// <param name="adapters">探索要求時に返す候補。</param>
    public AdapterDiscoveryStub(IReadOnlyList<DiscoveredAdapter> adapters)
    {
        this.adapters = adapters;
    }

    /// <summary>失敗結果を注入して生成します。</summary>
    /// <remarks>責務: 1件のテスト用探索エラーを後続要求へ返せる状態で保持します。</remarks>
    /// <param name="error">探索要求時に返すエラー。</param>
    public AdapterDiscoveryStub(AdapterDiscoveryError error)
    {
        this.error = error;
    }

    /// <summary>受け取った接続方式を呼出順で保持します。</summary>
    public List<AdapterTransportMode> RequestedModes { get; } = [];

    /// <summary>探索方式を記録して決定済み結果を返します。</summary>
    /// <remarks>責務: 1回の探索要求を履歴へ追加して注入済み結果を再現します。</remarks>
    /// <param name="mode">要求された物理接続方式。</param>
    /// <returns>注入済みの探索候補。</returns>
    public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode)
    {
        RequestedModes.Add(mode);
        return error is { } reason
            ? Task.FromException<IReadOnlyList<DiscoveredAdapter>>(new AdapterDiscoveryException(reason))
            : Task.FromResult(adapters!);
    }
}
