using ProjectZD8.Data.Devices;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>Bluetooth のアドレスと UUID の文字列変換を検証します。</summary>
public sealed class BluetoothIdentifierTextTests
{
    /// <summary>アドレスを大文字のハイフン区切りにし、元へ戻せることを検証します。</summary>
    /// <remarks>責務: システム識別子の往復で値が変わらないことを確認します。</remarks>
    [Fact]
    public void AddressRoundTripsThroughText()
    {
        const ulong address = 0x00043E123456;

        var text = BluetoothAddressText.Format(address);

        Assert.Equal("00-04-3E-12-34-56", text);
        Assert.True(BluetoothAddressText.TryParse(text, out var parsed));
        Assert.Equal(address, parsed);
        Assert.True(BluetoothAddressText.TryParse("00:04:3e:12:34:56", out parsed));
        Assert.Equal(address, parsed);
    }

    /// <summary>アドレスでない文字列を受理しないことを検証します。</summary>
    /// <remarks>責務: iOS の UUID 形式やCOMポート名をアドレスとして扱わないことを確認します。</remarks>
    [Fact]
    public void AddressRejectsNonAddressText()
    {
        Assert.False(BluetoothAddressText.TryParse("8DE3C46C-E22A-4D98-A461-000000000000", out _));
        Assert.False(BluetoothAddressText.TryParse("COM3", out _));
        Assert.False(BluetoothAddressText.TryParse("00-04-3E-12-34", out _));
    }

    /// <summary>Bluetooth Base UUID 上の値を CoreBluetooth と同じ短縮形にすることを検証します。</summary>
    /// <remarks>責務: GATT の Guid が UART 構成の UUID 表記と比較できることを確認します。</remarks>
    [Fact]
    public void UUIDUsesCoreBluetoothShortForms()
    {
        Assert.Equal("FFF0", BluetoothUUIDText.Format(Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb")));
        Assert.Equal("18F0", BluetoothUUIDText.Format(Guid.Parse("000018f0-0000-1000-8000-00805f9b34fb")));
        Assert.Equal("12345678", BluetoothUUIDText.Format(Guid.Parse("12345678-0000-1000-8000-00805f9b34fb")));
        Assert.Equal(
            "B3491406-44E4-4D83-97C5-CE3190130000",
            BluetoothUUIDText.Format(Guid.Parse("b3491406-44e4-4d83-97c5-ce3190130000")));
    }
}
