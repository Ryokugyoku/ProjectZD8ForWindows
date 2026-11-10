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
        var adapter = new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeedForTests.Definitions);
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
        var definitions = StandardOBDPIDSeedForTests.Definitions.Where(definition => definition.IsDecodable).ToArray();
        var requests = definitions.Select(definition => new OBDPIDRequest(definition.Service, definition.Pid)).ToArray();
        var readings = await new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeedForTests.Definitions).Read(requests, Endpoint);
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
        var readings = await new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeedForTests.Definitions).Read([speed], bluetoothEndpoint);

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
        var definition = StandardOBDPIDSeedForTests.Definitions.First(item => item.Service == 0x01 && item.Pid == 0xA6);
        var request = new OBDPIDRequest(definition.Service, definition.Pid);
        var readings = await new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeedForTests.Definitions).Read([request], Endpoint);

        Assert.Equal(12_345.6, new OBDPIDFormulaEvaluator().Evaluate(definition, readings[request]));
    }

    /// <summary>デモ累積走行距離を継続取得に伴って増加させます。</summary>
    /// <remarks>責務: Service 01 PID A6が履歴用セッション走行距離を算出できる累積値として進行することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryAdvancesOdometerForSessionDistance()
    {
        var definition = StandardOBDPIDSeedForTests.Definitions.First(item => item.Service == 0x01 && item.Pid == 0xA6);
        var request = new OBDPIDRequest(definition.Service, definition.Pid);
        var adapter = new DemoOBDPIDTelemetryAdapter(StandardOBDPIDSeedForTests.Definitions);
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

/// <summary>Data層の <c>StandardOBDPIDSeed</c> のうち数式確認済みの87件をテスト用に写した定義です。</summary>
/// <remarks>
/// 標準 seed は永続化担当のフォルダーに置かれるため、このテストは iOS 版 <c>StandardOBDPIDSeed.verifiedDefinitions</c>
/// と同じ値をここに持ちます。デモの対応PIDビットマップは数値化可能な定義だけで決まるため、この87件で iOS と同じ結果になります。
/// </remarks>
file static class StandardOBDPIDSeedForTests
{
    /// <summary>seed と同じ出典URIです。</summary>
    private const string SourceURI = "https://www.csselectronics.com/pages/obd2-pid-table-on-board-diagnostics-j1979";

    /// <summary>数式確認済みのService 01定義一覧です。</summary>
    public static readonly IReadOnlyList<OBDPIDDefinition> Definitions =
    [
        Definition(0x04, "calculated_engine_load", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x05, "engine_coolant_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x06, "short_term_fuel_trim_bank_1", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x07, "long_term_fuel_trim_bank_1", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x08, "short_term_fuel_trim_bank_2", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x09, "long_term_fuel_trim_bank_2", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x0A, "fuel_pressure", 1, "A * 3", "kPa", 0, 765),
        Definition(0x0B, "intake_manifold_pressure", 1, "A", "kPa", 0, 255),
        Definition(0x0C, "engine_speed", 2, "(A * 256 + B) / 4", "rpm", 0, 16_383.75),
        Definition(0x0D, "vehicle_speed", 1, "A", "km/h", 0, 255),
        Definition(0x0E, "timing_advance", 1, "A / 2 - 64", "°", -64, 63.5),
        Definition(0x0F, "intake_air_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x10, "mass_air_flow_rate", 2, "(A * 256 + B) / 100", "g/s", 0, 655.35),
        Definition(0x11, "absolute_throttle_position", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x14, "oxygen_sensor_b1s1_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x15, "oxygen_sensor_b1s2_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x16, "oxygen_sensor_b1s3_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x17, "oxygen_sensor_b1s4_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x18, "oxygen_sensor_b2s1_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x19, "oxygen_sensor_b2s2_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x1A, "oxygen_sensor_b2s3_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x1B, "oxygen_sensor_b2s4_voltage", 2, "A / 200", "V", 0, 1.275),
        Definition(0x1F, "run_time_since_engine_start", 2, "A * 256 + B", "s", 0, 65_535),
        Definition(0x21, "distance_with_mil_on", 2, "A * 256 + B", "km", 0, 65_535),
        Definition(0x22, "fuel_rail_pressure_relative", 2, "(A * 256 + B) * 0.079", "kPa", 0, 5_177.265),
        Definition(0x23, "fuel_rail_gauge_pressure", 2, "(A * 256 + B) * 10", "kPa", 0, 655_350),
        Definition(0x24, "oxygen_sensor_1_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x25, "oxygen_sensor_2_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x26, "oxygen_sensor_3_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x27, "oxygen_sensor_4_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x28, "oxygen_sensor_5_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x29, "oxygen_sensor_6_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x2A, "oxygen_sensor_7_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x2B, "oxygen_sensor_8_equivalence_ratio", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x2C, "commanded_egr", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x2D, "egr_error", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x2E, "commanded_evaporative_purge", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x2F, "fuel_tank_level", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x30, "warmups_since_codes_cleared", 1, "A", "count", 0, 255),
        Definition(0x31, "distance_since_codes_cleared", 2, "A * 256 + B", "km", 0, 65_535),
        Definition(0x32, "evap_system_vapor_pressure", 2, "(A * 256 + B) / 4 - 8192", "Pa", -8192, 8191.75),
        Definition(0x33, "absolute_barometric_pressure", 1, "A", "kPa", 0, 255),
        Definition(0x34, "oxygen_sensor_1_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x35, "oxygen_sensor_2_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x36, "oxygen_sensor_3_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x37, "oxygen_sensor_4_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x38, "oxygen_sensor_5_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x39, "oxygen_sensor_6_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x3A, "oxygen_sensor_7_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x3B, "oxygen_sensor_8_equivalence_ratio_current", 4, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x3C, "catalyst_temperature_b1s1", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6_513.5),
        Definition(0x3D, "catalyst_temperature_b2s1", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6_513.5),
        Definition(0x3E, "catalyst_temperature_b1s2", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6_513.5),
        Definition(0x3F, "catalyst_temperature_b2s2", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6_513.5),
        Definition(0x42, "control_module_voltage", 2, "(A * 256 + B) / 1000", "V", 0, 65.535),
        Definition(0x43, "absolute_load_value", 2, "(A * 256 + B) * 100 / 255", "%", 0, 25_700),
        Definition(0x44, "commanded_equivalence_ratio", 2, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x45, "relative_throttle_position", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x46, "ambient_air_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x47, "absolute_throttle_position_b", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x48, "absolute_throttle_position_c", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x49, "accelerator_pedal_position_d", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4A, "accelerator_pedal_position_e", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4B, "accelerator_pedal_position_f", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4C, "commanded_throttle_actuator", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4D, "time_run_with_mil_on", 2, "A * 256 + B", "min", 0, 65_535),
        Definition(0x4E, "time_since_codes_cleared", 2, "A * 256 + B", "min", 0, 65_535),
        Definition(0x50, "maximum_mass_air_flow_rate", 1, "A * 10", "g/s", 0, 2_550),
        Definition(0x52, "ethanol_fuel_percentage", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x53, "absolute_evap_vapor_pressure", 2, "(A * 256 + B) * 0.005", "kPa", 0, 327.675),
        Definition(0x54, "evap_vapor_pressure", 2, "A * 256 + B - 32767", "Pa", -32_767, 32_768),
        Definition(0x59, "fuel_rail_absolute_pressure", 2, "(A * 256 + B) * 10", "kPa", 0, 655_350),
        Definition(0x5A, "relative_accelerator_pedal_position", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x5B, "hybrid_battery_remaining_life", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x5C, "engine_oil_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x5D, "fuel_injection_timing", 2, "(A * 256 + B) / 128 - 210", "°", -210, 301.992),
        Definition(0x5E, "engine_fuel_rate", 2, "(A * 256 + B) * 0.05", "L/h", 0, 3_276.75),
        Definition(0x61, "demanded_engine_torque", 1, "A - 125", "%", -125, 130),
        Definition(0x62, "actual_engine_torque", 1, "A - 125", "%", -125, 130),
        Definition(0x63, "engine_reference_torque", 2, "A * 256 + B", "Nm", 0, 65_535),
        Definition(0x7C, "diesel_particulate_filter_temperature", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6_513.5),
        Definition(0x8D, "throttle_position_g", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x8E, "engine_friction_torque", 1, "A - 125", "%", -125, 130),
        Definition(0xA2, "cylinder_fuel_rate", 2, "(A * 256 + B) / 32", "mg/stroke", 0, 2_047.969),
        Definition(0xA4, "transmission_actual_gear", 4, "(C * 256 + D) / 1000", "ratio", 0, 65.535),
        Definition(0xA5, "diesel_exhaust_fluid_dosing", 2, "B / 2", "%", 0, 127.5),
        Definition(0xA6, "odometer", 4, "(A * 16777216 + B * 65536 + C * 256 + D) / 10", "km", 0, 429_496_729.5),
    ];

    /// <summary>主要PIDの定型項目を1件の定義へ変換します。</summary>
    /// <remarks>責務: iOS の seed と同じ引数順でService 01の定義を組み立てます。</remarks>
    /// <param name="pid">Service 01内のPID番号。</param>
    /// <param name="name">ローカライズキーの末尾。</param>
    /// <param name="requiredByteCount">数式評価に必要なバイト数。</param>
    /// <param name="formula">応答バイトへ適用する制限付き数式。</param>
    /// <param name="unit">計算結果の単位。</param>
    /// <param name="minimumValue">規格上の最小値。</param>
    /// <param name="maximumValue">規格上の最大値。</param>
    /// <returns>Service 01 PID定義。</returns>
    private static OBDPIDDefinition Definition(
        byte pid,
        string name,
        int requiredByteCount,
        string formula,
        string unit,
        double minimumValue,
        double maximumValue) =>
        new(0x01, pid, $"obd.pid.01.{pid:X2}.name", requiredByteCount, formula, unit, minimumValue, maximumValue, SourceURI, 2);
}
