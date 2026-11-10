using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>デモOBD識別とPID応答が通常境界と同じ型で返ることを検証します。</summary>
public sealed class DemoOBDAdaptersTests
{
    /// <summary>テスト用デモ終端です。</summary>
    private static OBDConnectionEndpoint Endpoint => new(DemoOBDAdapter.Candidate);

    /// <summary>合成VINを持つ登録可能な車両観測を返します。</summary>
    /// <remarks>責務: デモ識別結果がVINと車両フィールドを保持することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task IdentificationReturnsSyntheticVINThroughNormalSnapshot()
    {
        var observedAt = DateTimeOffset.FromUnixTimeSeconds(123);
        var snapshot = await new DemoVehicleIdentificationAdapter(() => observedAt).IdentifyVehicle(Endpoint);

        Assert.Equal(DemoOBDAdapter.SyntheticVIN(Endpoint), snapshot.Vin);
        Assert.Equal("RevTorque Demo Motors", snapshot.Fields.FirstOrDefault(field => field.Id == "manufacturer")?.Value);
        Assert.Equal(observedAt, snapshot.ObservedAt);
    }

    /// <summary>連続読取で車速と回転数が相関して変化します。</summary>
    /// <remarks>責務: デモPID境界が要求済み車速と回転数へ更新可能な未加工値を返すことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryReturnsChangingSpeedAndEngineSpeed()
    {
        var adapter = new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeed.Definitions);
        var speed = new OBDPIDRequest(0x01, 0x0D);
        var engineSpeed = new OBDPIDRequest(0x01, 0x0C);

        var first = await adapter.Read([speed, engineSpeed], Endpoint);
        var second = await adapter.Read([speed, engineSpeed], Endpoint);

        Assert.NotEqual(first[speed], second[speed]);
        Assert.NotEqual(first[engineSpeed], second[engineSpeed]);
    }

    /// <summary>登録済み主要PIDすべてへ数値化可能な正常域応答を返します。</summary>
    /// <remarks>責務: デモPID境界が主要カタログ全件を欠落なく正常域の値へ変換できることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryReturnsAllCatalogValuesWithinDeclaredRanges()
    {
        var definitions = StandardOBDPIDSeed.Definitions.Where(definition => definition.IsDecodable).ToArray();
        var requests = definitions.Select(definition => new OBDPIDRequest(definition.Service, definition.Pid)).ToArray();
        var readings = await new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeed.Definitions).Read(requests, Endpoint);
        var evaluator = new OBDPIDFormulaEvaluator();

        Assert.Equal(87, definitions.Length);
        Assert.Equal(87, readings.Count);
        Assert.Equal(
            OBDPIDCategoryRawValue.AllCases.ToHashSet(),
            requests.Select(OBDPIDCategoryExtensions.Category).ToHashSet());
        foreach (var category in OBDPIDCategoryRawValue.AllCases)
        {
            Assert.Equal(category, OBDPIDCategoryExtensions.Category(category.RepresentativeRequest()));
        }

        foreach (var definition in definitions)
        {
            var request = new OBDPIDRequest(definition.Service, definition.Pid);
            Assert.True(readings.TryGetValue(request, out var bytes));
            var value = evaluator.Evaluate(definition, bytes!);
            Assert.True(value >= definition.MinimumValue!.Value, $"{definition.Pid:X2}: {value}");
            Assert.True(value <= definition.MaximumValue!.Value, $"{definition.Pid:X2}: {value}");
        }
    }

    /// <summary>Bluetoothデモ終端もUSBデモと同じ合成車両へ接続されることを検証します。</summary>
    /// <remarks>責務: Bluetoothデモ終端が通常の識別・PID境界でVINと車速を返すことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothEndpointUsesNormalDemoIdentificationAndTelemetryBoundaries()
    {
        var bluetoothEndpoint = new OBDConnectionEndpoint(DemoOBDAdapter.BluetoothCandidate);
        var speed = new OBDPIDRequest(0x01, 0x0D);

        var snapshot = await new DemoVehicleIdentificationAdapter().IdentifyVehicle(bluetoothEndpoint);
        var readings = await new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeed.Definitions).Read([speed], bluetoothEndpoint);

        Assert.Equal(DemoOBDAdapter.SyntheticVIN(bluetoothEndpoint), snapshot.Vin);
        Assert.True(readings.ContainsKey(speed));
    }

    /// <summary>USBとBluetoothの全デモ終端でVINが重複しないことを検証します。</summary>
    /// <remarks>責務: 6件のデモ終端が順序依存でユニークな車両識別子を返すことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task AllDemoEndpointsUseDistinctVINs()
    {
        var candidates = DemoOBDAdapter.UsbCandidates.Concat(DemoOBDAdapter.BluetoothCandidates).ToArray();

        Assert.Equal(6, candidates.Length);
        var vins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var snapshot = await new DemoVehicleIdentificationAdapter().IdentifyVehicle(new OBDConnectionEndpoint(candidate));
            Assert.NotNull(snapshot.Vin);
            Assert.True(vins.Add(snapshot.Vin!));
        }

        Assert.Equal(6, vins.Count);
    }

    /// <summary>デモ累積走行距離を履歴画面へ渡せる正常値として返します。</summary>
    /// <remarks>責務: Service 01 PID A6の4バイト応答が12,345.6 kmへ数値化されることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryReturnsUsableOdometerValue()
    {
        var definition = StandardOBDPIDSeed.Definitions.First(item => item.Service == 0x01 && item.Pid == 0xA6);
        var request = new OBDPIDRequest(definition.Service, definition.Pid);
        var readings = await new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeed.Definitions).Read([request], Endpoint);

        Assert.Equal(12_345.6, new OBDPIDFormulaEvaluator().Evaluate(definition, readings[request]));
    }

    /// <summary>デモ累積走行距離を継続取得に伴って増加させます。</summary>
    /// <remarks>責務: Service 01 PID A6が履歴用セッション走行距離を算出できる累積値として進行することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryAdvancesOdometerForSessionDistance()
    {
        var definition = StandardOBDPIDSeed.Definitions.First(item => item.Service == 0x01 && item.Pid == 0xA6);
        var request = new OBDPIDRequest(definition.Service, definition.Pid);
        var adapter = new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeed.Definitions);
        var firstReadings = await adapter.Read([request], Endpoint);
        for (var index = 0; index < 10; index++)
        {
            _ = await adapter.Read([request], Endpoint);
        }

        var latestReadings = await adapter.Read([request], Endpoint);
        var evaluator = new OBDPIDFormulaEvaluator();

        Assert.True(evaluator.Evaluate(definition, latestReadings[request]) > evaluator.Evaluate(definition, firstReadings[request]));
    }

    /// <summary>通常終端のPID読取をデモ値生成へ流しません。</summary>
    /// <remarks>責務: 予約済みデモ識別子以外の終端が実通信PID境界だけへ委譲されることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task NonDemoEndpointRoutesTelemetryOnlyToLiveBoundary()
    {
        var request = new OBDPIDRequest(0x01, 0x0D);
        var live = new FixedRoutingTelemetryFake(42);
        var demo = new FixedRoutingTelemetryFake(99);
        var adapter = new DemoAwareOBDPIDTelemetryAdapter(live, demo);
        var liveEndpoint = new OBDConnectionEndpoint(OBDConnectionEndpointTransport.Serial, "COM3", "OBDLink EX");

        var readings = await adapter.Read([request], liveEndpoint);

        Assert.Equal([42], readings[request]);
        Assert.Equal(1, live.ReadCount);
        Assert.Equal(0, demo.ReadCount);
    }

    /// <summary>通常終端の車両識別をデモVIN生成へ流しません。</summary>
    /// <remarks>責務: 予約済みデモ識別子以外の終端が実通信車両識別境界だけへ委譲されることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task NonDemoEndpointRoutesIdentificationOnlyToLiveBoundary()
    {
        var live = new FixedRoutingIdentificationFake("LIVEVIN0000000001");
        var demo = new FixedRoutingIdentificationFake("TESTZD8CXR0000001");
        var adapter = new DemoAwareVehicleIdentificationAdapter(live, demo);
        var liveEndpoint = new OBDConnectionEndpoint(
            OBDConnectionEndpointTransport.BluetoothLowEnergy,
            "8DE3C46C-E22A-4D98-A461-REAL-ADAPTER",
            "Real BLE Adapter");

        var snapshot = await adapter.IdentifyVehicle(liveEndpoint);

        Assert.Equal("LIVEVIN0000000001", snapshot.Vin);
        Assert.Equal(1, live.ReadCount);
        Assert.Equal(0, demo.ReadCount);
    }

    /// <summary>デモ終端の車種専用PIDは実通信へ流さず非対応にします。</summary>
    /// <remarks>責務: デモ終端の専用PID要求が <c>UnsupportedPID</c> になることを確認します（iOS の実装どおり）。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task DemoEndpointRejectsVehicleSpecificPIDs()
    {
        var live = new FixedRoutingTelemetryFake(42);
        var adapter = new DemoAwareOBDPIDTelemetryAdapter(live, new FixedRoutingTelemetryFake(99));

        var error = await Assert.ThrowsAsync<OBDPIDTelemetryException>(() => adapter.ReadVehicleSpecific([], Endpoint));

        Assert.Equal(OBDPIDTelemetryError.UnsupportedPID, error.Reason);
    }
}

/// <summary>PID振分け先と呼出回数を返すテスト用取得境界です。</summary>
file sealed class FixedRoutingTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>応答へ格納する固定バイトです。</summary>
    private readonly byte value;

    /// <summary>固定応答バイトと空の呼出履歴を保持します。</summary>
    /// <remarks>責務: PID振分け検証用の固定取得境界を生成します。</remarks>
    /// <param name="value">各要求へ返す固定バイト。</param>
    public FixedRoutingTelemetryFake(byte value)
    {
        this.value = value;
    }

    /// <summary>PID読取を受けた回数です。</summary>
    public int ReadCount { get; private set; }

    /// <summary>各要求へ固定バイトを返して呼出回数を記録します。</summary>
    /// <remarks>責務: 1回のPID読取要求群を呼出履歴付き固定応答へ変換します。</remarks>
    /// <param name="requests">固定応答を割り当てるPID要求。</param>
    /// <param name="endpoint">振分け後の終端。テストでは値を使用しません。</param>
    /// <returns>各要求に固定バイトを割り当てた応答。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        ReadCount += 1;
        return Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            requests.ToDictionary(request => request, IReadOnlyList<byte> (_) => [value]));
    }
}

/// <summary>VIN振分け先と呼出回数を返すテスト用識別境界です。</summary>
file sealed class FixedRoutingIdentificationFake : IVehicleIdentificationPort
{
    /// <summary>応答へ格納する固定VINです。</summary>
    private readonly string vin;

    /// <summary>固定VINと空の呼出履歴を保持します。</summary>
    /// <remarks>責務: 車両識別振分け検証用の固定取得境界を生成します。</remarks>
    /// <param name="vin">識別観測へ返す固定VIN。</param>
    public FixedRoutingIdentificationFake(string vin)
    {
        this.vin = vin;
    }

    /// <summary>車両識別を受けた回数です。</summary>
    public int ReadCount { get; private set; }

    /// <summary>固定VINの車両識別観測を返して呼出回数を記録します。</summary>
    /// <remarks>責務: 1回の車両識別要求を呼出履歴付き固定観測へ変換します。</remarks>
    /// <param name="endpoint">振分け後の終端。テストでは値を使用しません。</param>
    /// <returns>固定VINを持つ車両識別観測。</returns>
    public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint)
    {
        ReadCount += 1;
        return Task.FromResult(new VehicleIdentificationSnapshot(vin, [], DateTimeOffset.FromUnixTimeSeconds(0)));
    }
}
