using ProjectZD8.Data.Devices;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>Bluetoothアドバタイズメント変換規則を検証します。</summary>
public sealed class BluetoothAdvertisementMapperTests
{
    /// <summary>Advertisement Local Nameが周辺機器名より優先されることを検証します。</summary>
    /// <remarks>責務: BLE候補の表示名が指定された名称優先順位に従うことを確認します。</remarks>
    [Fact]
    public void AdvertisementLocalNameTakesPriorityOverPeripheralName()
    {
        var adapter = new BluetoothAdvertisementMapper().MakeAdapter(
            "AA-BB-CC-DD-EE-FF",
            advertisementLocalName: "Advertisement Name",
            peripheralName: "Peripheral Name",
            DiscoveredAdapterConnectionState.Disconnected,
            hasManufacturerData: true);

        Assert.Equal("Advertisement Name", adapter.DisplayName);
        Assert.Equal("Advertisement Name", adapter.AdvertisementLocalName);
        Assert.Equal("Peripheral Name", adapter.ProductName);
        Assert.True(adapter.HasManufacturerData);
        Assert.Null(adapter.ManufacturerName);
    }

    /// <summary>名称を取得できない場合に識別子を表示名へ使うことを検証します。</summary>
    /// <remarks>責務: 空または取得不能のBLE名称が完全な識別子へフォールバックすることを確認します。</remarks>
    [Fact]
    public void MissingNamesFallBackToPeripheralIdentifier()
    {
        const string identifier = "11-22-33-44-55-66";

        var adapter = new BluetoothAdvertisementMapper().MakeAdapter(
            identifier,
            advertisementLocalName: "   ",
            peripheralName: null,
            DiscoveredAdapterConnectionState.Disconnected,
            hasManufacturerData: false);

        Assert.Equal(identifier, adapter.DisplayName);
        Assert.Equal(identifier, adapter.SystemIdentifier);
        Assert.Equal($"bluetooth-low-energy:{identifier}", adapter.Id);
        Assert.Equal(OBDConnectionEndpointTransport.BluetoothLowEnergy, adapter.ConnectionTransport);
        Assert.Null(adapter.AdvertisementLocalName);
    }

    /// <summary>広告Service UUIDが正規化、重複除外、整列されることを検証します。</summary>
    /// <remarks>責務: 広告のService UUID一覧が実機確認用の安定表示値になることを確認します。</remarks>
    [Fact]
    public void ServiceIdentifiersAreNormalizedForInspection()
    {
        var adapter = new BluetoothAdvertisementMapper().MakeAdapter(
            "AA-BB-CC-DD-EE-FF",
            advertisementLocalName: "OBDLink",
            peripheralName: null,
            DiscoveredAdapterConnectionState.Disconnected,
            hasManufacturerData: false,
            serviceIdentifiers: ["fff0", "B3491406-44e4-4d83-97c5-ce3190130000", "FFF0"]);

        Assert.Equal(["B3491406-44E4-4D83-97C5-CE3190130000", "FFF0"], adapter.BluetoothServiceIdentifiers);
    }
}
