using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Data.Devices;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>既定アダプターの到来通知が照合と重複防止の規則に従うことを検証します。</summary>
public sealed class DefaultAdapterArrivalDeliveryTests
{
    /// <summary>BLE の既定設定です。</summary>
    private static readonly DefaultAdapterPreference LowEnergyPreference = new(
        adapterID: "bluetooth-low-energy:AA-BB-CC-DD-EE-FF",
        transportMode: AdapterTransportMode.Bluetooth,
        displayName: "OBDLink CX",
        systemIdentifier: "AA-BB-CC-DD-EE-FF",
        connectionTransport: OBDConnectionEndpointTransport.BluetoothLowEnergy);

    /// <summary>USB シリアルの既定設定です。</summary>
    private static readonly DefaultAdapterPreference SerialPreference = new(
        adapterID: "usb:COM5",
        transportMode: AdapterTransportMode.Usb,
        displayName: "OBDLink EX",
        systemIdentifier: "COM5");

    /// <summary>一致した BLE 機器を1回だけ既定設定の終端として通知します。</summary>
    /// <remarks>責務: 同じ世代で2回目の検出を通知しないことを確認します（iOS の <c>hasDeliveredArrival</c> と同じ）。</remarks>
    [Fact]
    public void DeliversMatchingLowEnergyArrivalOnlyOnce()
    {
        var delivery = new DefaultAdapterArrivalDelivery();
        var arrivals = new List<OBDConnectionEndpoint>();
        var generation = delivery.Start(LowEnergyPreference, arrivals.Add, context: null);

        Assert.True(delivery.TryDeliver(generation, OBDConnectionEndpointTransport.BluetoothLowEnergy, "aa:bb:cc:dd:ee:ff"));
        Assert.False(delivery.TryDeliver(generation, OBDConnectionEndpointTransport.BluetoothLowEnergy, "AA-BB-CC-DD-EE-FF"));

        Assert.Equal(
            [new OBDConnectionEndpoint(OBDConnectionEndpointTransport.BluetoothLowEnergy, "AA-BB-CC-DD-EE-FF", "OBDLink CX")],
            arrivals);
    }

    /// <summary>別の機器、別の方式、古い世代の検出を通知しません。</summary>
    /// <remarks>責務: 既定設定と一致しない検出と、再開前の監視からの遅れた検出を除外することを確認します。</remarks>
    [Fact]
    public void IgnoresOtherDevicesAndStaleGenerations()
    {
        var delivery = new DefaultAdapterArrivalDelivery();
        var arrivals = new List<OBDConnectionEndpoint>();
        var stale = delivery.Start(SerialPreference, arrivals.Add, context: null);
        var current = delivery.Start(SerialPreference, arrivals.Add, context: null);

        Assert.False(delivery.TryDeliver(current, OBDConnectionEndpointTransport.Serial, "COM6"));
        Assert.False(delivery.TryDeliver(current, OBDConnectionEndpointTransport.BluetoothLowEnergy, "COM5"));
        Assert.False(delivery.TryDeliver(stale, OBDConnectionEndpointTransport.Serial, "COM5"));
        Assert.True(delivery.TryDeliver(current, OBDConnectionEndpointTransport.Serial, "com5"));
        Assert.Single(arrivals);
    }

    /// <summary>停止後の検出を通知しません。</summary>
    /// <remarks>責務: 監視停止で通知先が破棄されることを確認します。</remarks>
    [Fact]
    public void StopDiscardsArrivalCallback()
    {
        var delivery = new DefaultAdapterArrivalDelivery();
        var arrivals = new List<OBDConnectionEndpoint>();
        var generation = delivery.Start(SerialPreference, arrivals.Add, context: null);

        delivery.Stop();

        Assert.False(delivery.TryDeliver(generation, OBDConnectionEndpointTransport.Serial, "COM5"));
        Assert.Empty(arrivals);
    }

    /// <summary>監視できる設定だけを受理します。</summary>
    /// <remarks>責務: Classic と不正な識別子の設定を監視対象から外すことを確認します。</remarks>
    [Fact]
    public void MonitorsOnlySerialAndLowEnergyPreferences()
    {
        Assert.True(DefaultAdapterArrivalDelivery.IsMonitorable(SerialPreference));
        Assert.True(DefaultAdapterArrivalDelivery.IsMonitorable(LowEnergyPreference));
        Assert.False(DefaultAdapterArrivalDelivery.IsMonitorable(new DefaultAdapterPreference(
            "bluetooth-classic:00-04-3E-12-34-56",
            AdapterTransportMode.Bluetooth,
            "OBDLink MX+",
            "00-04-3E-12-34-56",
            OBDConnectionEndpointTransport.BluetoothClassic)));
        Assert.False(DefaultAdapterArrivalDelivery.IsMonitorable(new DefaultAdapterPreference(
            "bluetooth-low-energy:8DE3C46C-E22A-4D98-A461-000000000000",
            AdapterTransportMode.Bluetooth,
            "iOS で保存した BLE",
            "8DE3C46C-E22A-4D98-A461-000000000000",
            OBDConnectionEndpointTransport.BluetoothLowEnergy)));
    }

    /// <summary>同期コンテキストがある場合はそこへ通知を戻します。</summary>
    /// <remarks>責務: バックグラウンドの検出が開始時のスレッドへ投函されることを確認します。</remarks>
    [Fact]
    public void PostsArrivalToCapturedContext()
    {
        var delivery = new DefaultAdapterArrivalDelivery();
        var context = new RecordingSynchronizationContext();
        var arrivals = new List<OBDConnectionEndpoint>();
        var generation = delivery.Start(SerialPreference, arrivals.Add, context);

        Assert.True(delivery.TryDeliver(generation, OBDConnectionEndpointTransport.Serial, "COM5"));
        Assert.Empty(arrivals);

        context.RunAll();
        Assert.Single(arrivals);
    }
}

/// <summary>投函された処理を溜めて、テストから実行する同期コンテキストです。</summary>
file sealed class RecordingSynchronizationContext : SynchronizationContext
{
    /// <summary>投函された処理です。</summary>
    private readonly Queue<(SendOrPostCallback Callback, object? State)> posted = new();

    /// <summary>処理を溜めます。</summary>
    /// <remarks>責務: 投函をすぐに実行せず記録します。</remarks>
    /// <param name="d">投函された処理。</param>
    /// <param name="state">処理へ渡す値。</param>
    public override void Post(SendOrPostCallback d, object? state) => posted.Enqueue((d, state));

    /// <summary>溜めた処理をすべて実行します。</summary>
    /// <remarks>責務: UI スレッドでの実行を模して順に呼びます。</remarks>
    public void RunAll()
    {
        while (posted.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }
}
