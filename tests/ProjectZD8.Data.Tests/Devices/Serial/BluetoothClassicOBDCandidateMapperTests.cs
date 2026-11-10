using ProjectZD8.Data.Devices.Serial;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.Serial;

/// <summary>ペアリング済み Bluetooth Classic 機器を OBD 候補へ変換する規則を検証します。</summary>
public sealed class BluetoothClassicOBDCandidateMapperTests
{
    /// <summary>MX+ は macOS 版と同じメーカー名と製品名を持つ Classic 候補になります。</summary>
    /// <remarks>責務: MX+ の候補が macOS 版と同じ識別子と名称で構築されることを確認します。</remarks>
    [Fact]
    public void MapsPairedMXPlusLikeMacOS()
    {
        var adapter = new BluetoothClassicOBDCandidateMapper().MakeAdapter(
            "00-04-3E-12-34-56", " OBDLink MX+ 48318 ", isPaired: true, hasSerialPortService: true, isConnected: false);

        Assert.NotNull(adapter);
        Assert.Equal("bluetooth-classic:00-04-3E-12-34-56", adapter.Id);
        Assert.Equal(OBDConnectionEndpointTransport.BluetoothClassic, adapter.ConnectionTransport);
        Assert.Equal(AdapterTransportMode.Bluetooth, adapter.TransportMode);
        Assert.Equal("OBDLink MX+ 48318", adapter.DisplayName);
        Assert.Equal("OBD Solutions LLC", adapter.ManufacturerName);
        Assert.Equal("OBDLink MX+", adapter.ProductName);
        Assert.False(adapter.IsConnected);
    }

    /// <summary>汎用の ELM327 互換 Classic アダプターも候補にします（Windows での拡張）。</summary>
    /// <remarks>責務: 名前に OBD または ELM327 を含む SPP 機器を製品名なしの候補にすることを確認します。</remarks>
    [Fact]
    public void AcceptsGenericELMClassicAdapters()
    {
        var mapper = new BluetoothClassicOBDCandidateMapper();

        var generic = mapper.MakeAdapter("11-22-33-44-55-66", "OBDII", isPaired: true, hasSerialPortService: true, isConnected: true);

        Assert.NotNull(generic);
        Assert.Null(generic.ProductName);
        Assert.NotNull(mapper.MakeAdapter("11-22-33-44-55-67", "ELM327 v2.1", isPaired: true, hasSerialPortService: true, isConnected: false));
        Assert.NotNull(mapper.MakeAdapter("11-22-33-44-55-68", "Vgate iCar 2", isPaired: true, hasSerialPortService: true, isConnected: false));
    }

    /// <summary>未ペアリング、SPP なし、OBD 以外の名称の機器を除外します。</summary>
    /// <remarks>責務: 接続できない機器や無関係な機器を一覧へ混入させないことを確認します。</remarks>
    [Fact]
    public void RejectsUnpairedNonSerialAndUnrelatedDevices()
    {
        var mapper = new BluetoothClassicOBDCandidateMapper();

        Assert.Null(mapper.MakeAdapter("00-04-3E-12-34-56", "OBDLink MX+", isPaired: false, hasSerialPortService: true, isConnected: false));
        Assert.Null(mapper.MakeAdapter("00-04-3E-12-34-56", "OBDLink MX+", isPaired: true, hasSerialPortService: false, isConnected: false));
        Assert.Null(mapper.MakeAdapter("00-04-3E-12-34-56", "Bluetooth Printer", isPaired: true, hasSerialPortService: true, isConnected: false));
        Assert.Null(mapper.MakeAdapter(" ", "OBDLink MX+", isPaired: true, hasSerialPortService: true, isConnected: false));
    }

    /// <summary>候補をアドレス順に並べて重複を除きます。</summary>
    /// <remarks>責務: macOS 版と同じくシステム識別子の昇順になることを確認します。</remarks>
    [Fact]
    public void SortsByAddress()
    {
        var mapper = new BluetoothClassicOBDCandidateMapper();
        var later = mapper.MakeAdapter("BB-00-00-00-00-00", "OBDLink MX+", true, true, false)!;
        var earlier = mapper.MakeAdapter("AA-00-00-00-00-00", "OBDLink MX+", true, true, false)!;

        Assert.Equal([earlier, later], mapper.Sorted([later, earlier, later]));
    }
}
