namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 車両の動力構成を燃料種別から独立して分類します。
/// </summary>
public enum VehiclePowertrainKind
{
    /// <summary>
    /// 内燃機関のみで走行する構成です。
    /// </summary>
    Combustion,

    /// <summary>
    /// 外部充電を行わないハイブリッド構成です。
    /// </summary>
    Hybrid,

    /// <summary>
    /// 外部充電可能なハイブリッド構成です。
    /// </summary>
    PlugInHybrid,

    /// <summary>
    /// 駆動用電池のみを主動力とする構成です。
    /// </summary>
    BatteryElectric,

    /// <summary>
    /// 燃料電池を主電源とする構成です。
    /// </summary>
    FuelCell,

    /// <summary>
    /// 既知の分類へ確定できない構成です。
    /// </summary>
    Other,
}

/// <summary>
/// <see cref="VehiclePowertrainKind"/> の raw value と全ケースを扱います。
/// </summary>
public static class VehiclePowertrainKindRawValue
{
    /// <summary>
    /// 宣言順の全ケースです。
    /// </summary>
    public static IReadOnlyList<VehiclePowertrainKind> AllCases { get; } =
    [
        VehiclePowertrainKind.Combustion,
        VehiclePowertrainKind.Hybrid,
        VehiclePowertrainKind.PlugInHybrid,
        VehiclePowertrainKind.BatteryElectric,
        VehiclePowertrainKind.FuelCell,
        VehiclePowertrainKind.Other,
    ];

    /// <summary>
    /// 動力構成を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の動力構成を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する動力構成。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this VehiclePowertrainKind value) => value switch
    {
        VehiclePowertrainKind.Combustion => "combustion",
        VehiclePowertrainKind.Hybrid => "hybrid",
        VehiclePowertrainKind.PlugInHybrid => "plugInHybrid",
        VehiclePowertrainKind.BatteryElectric => "batteryElectric",
        VehiclePowertrainKind.FuelCell => "fuelCell",
        VehiclePowertrainKind.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から動力構成を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して動力構成へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた動力構成。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out VehiclePowertrainKind value)
    {
        switch (rawValue)
        {
            case "combustion": value = VehiclePowertrainKind.Combustion; return true;
            case "hybrid": value = VehiclePowertrainKind.Hybrid; return true;
            case "plugInHybrid": value = VehiclePowertrainKind.PlugInHybrid; return true;
            case "batteryElectric": value = VehiclePowertrainKind.BatteryElectric; return true;
            case "fuelCell": value = VehiclePowertrainKind.FuelCell; return true;
            case "other": value = VehiclePowertrainKind.Other; return true;
            default: value = default; return false;
        }
    }
}
