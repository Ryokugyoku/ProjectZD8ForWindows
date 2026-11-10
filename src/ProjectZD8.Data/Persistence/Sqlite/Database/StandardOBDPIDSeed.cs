using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Database;

/// <summary>一次資料で数式まで確認できた標準PID定義を提供します。</summary>
public static class StandardOBDPIDSeed
{
    /// <summary>現行Service 01定義の一次資料URIです。</summary>
    private const string SourceURI = "https://www.csselectronics.com/pages/obd2-pid-table-on-board-diagnostics-j1979";

    /// <summary>数式まで確認済みのService 01 PID定義です。</summary>
    private static readonly OBDPIDDefinition[] VerifiedDefinitions =
    [
        Definition(0x04, "calculated_engine_load", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x05, "engine_coolant_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x06, "short_term_fuel_trim_bank_1", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x07, "long_term_fuel_trim_bank_1", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x08, "short_term_fuel_trim_bank_2", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x09, "long_term_fuel_trim_bank_2", 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x0A, "fuel_pressure", 1, "A * 3", "kPa", 0, 765),
        Definition(0x0B, "intake_manifold_pressure", 1, "A", "kPa", 0, 255),
        Definition(0x0C, "engine_speed", 2, "(A * 256 + B) / 4", "rpm", 0, 16383.75),
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
        Definition(0x1F, "run_time_since_engine_start", 2, "A * 256 + B", "s", 0, 65535),
        Definition(0x21, "distance_with_mil_on", 2, "A * 256 + B", "km", 0, 65535),
        Definition(0x22, "fuel_rail_pressure_relative", 2, "(A * 256 + B) * 0.079", "kPa", 0, 5177.265),
        Definition(0x23, "fuel_rail_gauge_pressure", 2, "(A * 256 + B) * 10", "kPa", 0, 655350),
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
        Definition(0x31, "distance_since_codes_cleared", 2, "A * 256 + B", "km", 0, 65535),
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
        Definition(0x3C, "catalyst_temperature_b1s1", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6513.5),
        Definition(0x3D, "catalyst_temperature_b2s1", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6513.5),
        Definition(0x3E, "catalyst_temperature_b1s2", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6513.5),
        Definition(0x3F, "catalyst_temperature_b2s2", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6513.5),
        Definition(0x42, "control_module_voltage", 2, "(A * 256 + B) / 1000", "V", 0, 65.535),
        Definition(0x43, "absolute_load_value", 2, "(A * 256 + B) * 100 / 255", "%", 0, 25700),
        Definition(0x44, "commanded_equivalence_ratio", 2, "(A * 256 + B) / 32768", "ratio", 0, 1.99997),
        Definition(0x45, "relative_throttle_position", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x46, "ambient_air_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x47, "absolute_throttle_position_b", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x48, "absolute_throttle_position_c", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x49, "accelerator_pedal_position_d", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4A, "accelerator_pedal_position_e", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4B, "accelerator_pedal_position_f", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4C, "commanded_throttle_actuator", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4D, "time_run_with_mil_on", 2, "A * 256 + B", "min", 0, 65535),
        Definition(0x4E, "time_since_codes_cleared", 2, "A * 256 + B", "min", 0, 65535),
        Definition(0x50, "maximum_mass_air_flow_rate", 1, "A * 10", "g/s", 0, 2550),
        Definition(0x52, "ethanol_fuel_percentage", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x53, "absolute_evap_vapor_pressure", 2, "(A * 256 + B) * 0.005", "kPa", 0, 327.675),
        Definition(0x54, "evap_vapor_pressure", 2, "A * 256 + B - 32767", "Pa", -32767, 32768),
        Definition(0x59, "fuel_rail_absolute_pressure", 2, "(A * 256 + B) * 10", "kPa", 0, 655350),
        Definition(0x5A, "relative_accelerator_pedal_position", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x5B, "hybrid_battery_remaining_life", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x5C, "engine_oil_temperature", 1, "A - 40", "°C", -40, 215),
        Definition(0x5D, "fuel_injection_timing", 2, "(A * 256 + B) / 128 - 210", "°", -210, 301.992),
        Definition(0x5E, "engine_fuel_rate", 2, "(A * 256 + B) * 0.05", "L/h", 0, 3276.75),
        Definition(0x61, "demanded_engine_torque", 1, "A - 125", "%", -125, 130),
        Definition(0x62, "actual_engine_torque", 1, "A - 125", "%", -125, 130),
        Definition(0x63, "engine_reference_torque", 2, "A * 256 + B", "Nm", 0, 65535),
        Definition(0x7C, "diesel_particulate_filter_temperature", 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6513.5),
        Definition(0x8D, "throttle_position_g", 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x8E, "engine_friction_torque", 1, "A - 125", "%", -125, 130),
        Definition(0xA2, "cylinder_fuel_rate", 2, "(A * 256 + B) / 32", "mg/stroke", 0, 2047.969),
        Definition(0xA4, "transmission_actual_gear", 4, "(C * 256 + D) / 1000", "ratio", 0, 65.535),
        Definition(0xA5, "diesel_exhaust_fluid_dosing", 2, "B / 2", "%", 0, 127.5),
        Definition(0xA6, "odometer", 4, "(A * 16777216 + B * 65536 + C * 256 + D) / 10", "km", 0, 429496729.5),
    ];

    /// <summary>指定資料にPID番号が掲載されているService 01範囲です。</summary>
    private static readonly byte[] CatalogPIDs =
        Enumerable.Range(0x00, 0x94 - 0x00 + 1)
            .Concat(Enumerable.Range(0x98, 0xA8 - 0x98 + 1))
            .Append(0xC0)
            .Select(pid => (byte)pid)
            .ToArray();

    /// <summary>指定資料に掲載されたService 01 PIDを数式確認済み定義と未確認式メタデータへ統合した一覧です。</summary>
    public static IReadOnlyList<OBDPIDDefinition> Definitions { get; } = MakeDefinitions();

    /// <summary>掲載PIDの順に確認済み定義または未確認式定義を並べます。</summary>
    /// <remarks>責務: 掲載PIDごとに確認済み定義を優先して1件の定義へ統合します。</remarks>
    /// <returns>掲載PID順の定義一覧。</returns>
    private static OBDPIDDefinition[] MakeDefinitions()
    {
        var verified = VerifiedDefinitions.ToDictionary(definition => definition.Pid);
        return CatalogPIDs
            .Select(pid => verified.TryGetValue(pid, out var definition) ? definition : MetadataOnlyDefinition(pid))
            .ToArray();
    }

    /// <summary>主要PIDの定型項目を1件の永続化定義へ変換します。</summary>
    /// <remarks>責務: Service 01の表示対象PIDを共通出典と改訂番号を持つ定義へ固定します。</remarks>
    /// <param name="pid">Service 01内のPID番号。</param>
    /// <param name="name">資料上の名称（iOS版と同じく表示名キーには使いません）。</param>
    /// <param name="requiredByteCount">数式評価に必要なバイト数。</param>
    /// <param name="formula">応答バイトへ適用する制限付き数式。</param>
    /// <param name="unit">計算結果の単位。</param>
    /// <param name="minimumValue">規格上の最小値。</param>
    /// <param name="maximumValue">規格上の最大値。</param>
    /// <returns>永続化できるService 01 PID定義。</returns>
    private static OBDPIDDefinition Definition(
        byte pid,
        string name,
        int requiredByteCount,
        string formula,
        string unit,
        double minimumValue,
        double maximumValue)
    {
        _ = name;
        var category = CategoryRawValue(pid);
        return new OBDPIDDefinition(
            service: 0x01,
            pid: pid,
            nameKey: NameKey(pid),
            requiredByteCount: requiredByteCount,
            formula: formula,
            unit: unit,
            minimumValue: minimumValue,
            maximumValue: maximumValue,
            sourceURI: SourceURI,
            revision: 2,
            summaryKey: $"obd.pid.help.{category}.summary",
            highValueKey: $"obd.pid.help.{category}.high",
            lowValueKey: $"obd.pid.help.{category}.low",
            correlationKey: $"obd.pid.help.{category}.correlation");
    }

    /// <summary>名称は掲載済みだが安全な単一数式を確認できないPIDを生成します。</summary>
    /// <remarks>責務: 1件の掲載PIDを推測した変換式を持たないカタログ定義へ変換します。</remarks>
    /// <param name="pid">Service 01内のPID番号。</param>
    /// <returns>数式未確認の表示用PID定義。</returns>
    private static OBDPIDDefinition MetadataOnlyDefinition(byte pid)
    {
        var category = CategoryRawValue(pid);
        return new OBDPIDDefinition(
            service: 0x01,
            pid: pid,
            nameKey: NameKey(pid),
            requiredByteCount: null,
            formula: null,
            unit: "",
            minimumValue: null,
            maximumValue: null,
            sourceURI: SourceURI,
            revision: 2,
            summaryKey: $"obd.pid.help.{category}.summary",
            highValueKey: $"obd.pid.help.{category}.high",
            lowValueKey: $"obd.pid.help.{category}.low",
            correlationKey: $"obd.pid.help.{category}.correlation");
    }

    /// <summary>Service 01 PIDの表示分類のraw valueを返します。</summary>
    /// <remarks>責務: 1件のPIDを説明キーに使う分類名へ変換します。</remarks>
    /// <param name="pid">Service 01内のPID番号。</param>
    /// <returns>表示分類のraw value。</returns>
    private static string CategoryRawValue(byte pid) =>
        OBDPIDCategoryExtensions.Category(new OBDPIDRequest(Service: 0x01, Pid: pid)).ToRawValue();

    /// <summary>Service 01 PIDの表示名キーを返します。</summary>
    /// <remarks>責務: PID番号を2桁大文字16進のローカライズキーへ変換します（Swiftの <c>%02X</c> と同じ）。</remarks>
    /// <param name="pid">Service 01内のPID番号。</param>
    /// <returns><c>obd.pid.01.XX.name</c> 形式のキー。</returns>
    private static string NameKey(byte pid) =>
        $"obd.pid.01.{pid.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)}.name";

    /// <summary>確認済み定義を改訂後退なしでリポジトリへ登録します。</summary>
    /// <remarks>責務: 現在バンドルする全標準PID定義を1件ずつ永続化します。</remarks>
    /// <param name="repository">定義の保存先。</param>
    public static void Install(IOBDPIDDefinitionRepository repository)
    {
        foreach (var definition in Definitions)
        {
            repository.Upsert(definition);
        }
    }
}
