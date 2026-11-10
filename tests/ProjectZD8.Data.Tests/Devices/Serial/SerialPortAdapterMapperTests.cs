using ProjectZD8.Data.Devices.Serial;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.Serial;

/// <summary>Windows の COM ポート情報を USB 候補へ変換する規則を検証します。</summary>
public sealed class SerialPortAdapterMapperTests
{
    /// <summary>USB シリアルの COM ポートを識別情報付きの候補にします。</summary>
    /// <remarks>責務: VID/PID、シリアル番号、表示名が macOS 版と同じ形になることを確認します。</remarks>
    [Fact]
    public void MapsUSBSerialPortWithIdentifiers()
    {
        var adapter = new SerialPortAdapterMapper().MakeAdapter(
            "COM3",
            @"USB\VID_0403&PID_6015\DN05ABCD",
            "FTDI",
            "OBDLink EX");

        Assert.NotNull(adapter);
        Assert.Equal("usb:COM3", adapter.Id);
        Assert.Equal(AdapterTransportMode.Usb, adapter.TransportMode);
        Assert.Equal(OBDConnectionEndpointTransport.Serial, adapter.ConnectionTransport);
        Assert.Equal("FTDI · OBDLink EX", adapter.DisplayName);
        Assert.Equal("COM3", adapter.SystemIdentifier);
        Assert.Equal("0x0403", adapter.VendorIdentifier);
        Assert.Equal("0x6015", adapter.ProductIdentifier);
        Assert.Equal("DN05ABCD", adapter.SerialNumber);
        Assert.True(adapter.IsConnected);
    }

    /// <summary>FTDI バスや OS 採番のIDではシリアル番号を推測しません。</summary>
    /// <remarks>責務: VID/PID は読み取り、シリアル番号は確実な場合だけ返すことを確認します。</remarks>
    [Fact]
    public void ReadsVendorFromFTDIBusWithoutGuessingSerial()
    {
        var mapper = new SerialPortAdapterMapper();

        var ftdi = mapper.MakeAdapter("COM4", @"FTDIBUS\VID_0403+PID_6015+DN05ABCDA\0000", null, "USB Serial Port (COM4)");
        var generated = mapper.MakeAdapter("COM5", @"USB\VID_10C4&PID_EA60\6&2A1B3C4D&0&2", null, null);

        Assert.Equal("0x0403", ftdi?.VendorIdentifier);
        Assert.Null(ftdi?.SerialNumber);
        Assert.Equal("USB Serial Port (COM4)", ftdi?.DisplayName);
        Assert.Null(generated?.SerialNumber);
        Assert.Equal("COM5", generated?.DisplayName);
    }

    /// <summary>Bluetooth の仮想COMとUSB以外のポートを除外します。</summary>
    /// <remarks>責務: SPP 機器が USB と Bluetooth の両方に並ばないことと、内蔵ポートを除くことを確認します。</remarks>
    [Fact]
    public void RejectsBluetoothVirtualAndNonUSBPorts()
    {
        var mapper = new SerialPortAdapterMapper();

        Assert.Null(mapper.MakeAdapter("COM7", @"BTHENUM\{00001101-0000-1000-8000-00805F9B34FB}_LOCALMFG&0002\7&1234&0&00043E123456_C00000000", null, "Standard Serial over Bluetooth link (COM7)"));
        Assert.Null(mapper.MakeAdapter("COM1", @"ACPI\PNP0501\0", null, "Communications Port (COM1)"));
        Assert.Null(mapper.MakeAdapter(null, @"USB\VID_0403&PID_6015\DN05ABCD", null, null));
        Assert.Null(mapper.MakeAdapter("LPT1", @"USB\VID_0403&PID_6015\DN05ABCD", null, null));
    }

    /// <summary>フレンドリ名の末尾からCOMポート名を取り出します。</summary>
    /// <remarks>責務: 「Ports」クラスのデバイス名からポート名を読み取れることを確認します。</remarks>
    [Fact]
    public void ReadsPortNameFromFriendlyName()
    {
        Assert.Equal("COM12", SerialPortAdapterMapper.PortNameFromFriendlyName("USB-SERIAL CH340 (com12)"));
        Assert.Null(SerialPortAdapterMapper.PortNameFromFriendlyName("USB Serial Converter"));
        Assert.Null(SerialPortAdapterMapper.PortNameFromFriendlyName(null));
    }
}
