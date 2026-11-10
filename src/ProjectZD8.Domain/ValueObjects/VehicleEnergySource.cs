namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 車両が走行に使用するエネルギー源を表します。
/// </summary>
public enum VehicleEnergySource
{
    /// <summary>
    /// ガソリンです。
    /// </summary>
    Gasoline,

    /// <summary>
    /// ハイオク（プレミアムガソリン）です。
    /// </summary>
    GasolinePremium,

    /// <summary>
    /// レギュラー（通常ガソリン）です。
    /// </summary>
    GasolineRegular,

    /// <summary>
    /// 軽油です。
    /// </summary>
    Diesel,

    /// <summary>
    /// 液化石油ガスです。
    /// </summary>
    Lpg,

    /// <summary>
    /// 圧縮天然ガスです。
    /// </summary>
    Cng,

    /// <summary>
    /// 水素です。
    /// </summary>
    Hydrogen,

    /// <summary>
    /// 電力です。
    /// </summary>
    Electricity,

    /// <summary>
    /// 既知の選択肢に該当しないエネルギー源です。
    /// </summary>
    Other,
}

/// <summary>
/// <see cref="VehicleEnergySource"/> の raw value と全ケースを扱います。
/// </summary>
public static class VehicleEnergySourceRawValue
{
    /// <summary>
    /// 宣言順の全ケースです。
    /// </summary>
    public static IReadOnlyList<VehicleEnergySource> AllCases { get; } =
    [
        VehicleEnergySource.Gasoline,
        VehicleEnergySource.GasolinePremium,
        VehicleEnergySource.GasolineRegular,
        VehicleEnergySource.Diesel,
        VehicleEnergySource.Lpg,
        VehicleEnergySource.Cng,
        VehicleEnergySource.Hydrogen,
        VehicleEnergySource.Electricity,
        VehicleEnergySource.Other,
    ];

    /// <summary>
    /// エネルギー源を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件のエネルギー源を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換するエネルギー源。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this VehicleEnergySource value) => value switch
    {
        VehicleEnergySource.Gasoline => "gasoline",
        VehicleEnergySource.GasolinePremium => "gasolinePremium",
        VehicleEnergySource.GasolineRegular => "gasolineRegular",
        VehicleEnergySource.Diesel => "diesel",
        VehicleEnergySource.Lpg => "lpg",
        VehicleEnergySource.Cng => "cng",
        VehicleEnergySource.Hydrogen => "hydrogen",
        VehicleEnergySource.Electricity => "electricity",
        VehicleEnergySource.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列からエネルギー源を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別してエネルギー源へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたエネルギー源。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out VehicleEnergySource value)
    {
        switch (rawValue)
        {
            case "gasoline": value = VehicleEnergySource.Gasoline; return true;
            case "gasolinePremium": value = VehicleEnergySource.GasolinePremium; return true;
            case "gasolineRegular": value = VehicleEnergySource.GasolineRegular; return true;
            case "diesel": value = VehicleEnergySource.Diesel; return true;
            case "lpg": value = VehicleEnergySource.Lpg; return true;
            case "cng": value = VehicleEnergySource.Cng; return true;
            case "hydrogen": value = VehicleEnergySource.Hydrogen; return true;
            case "electricity": value = VehicleEnergySource.Electricity; return true;
            case "other": value = VehicleEnergySource.Other; return true;
            default: value = default; return false;
        }
    }
}
