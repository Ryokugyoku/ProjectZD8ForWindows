namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// リアルタイム表示で標準PIDを意味の近いまとまりへ分類します。
/// </summary>
public enum OBDPIDCategory
{
    /// <summary>
    /// エンジンの負荷、回転、点火に関する項目です。
    /// </summary>
    Engine,

    /// <summary>
    /// 温度に関する項目です。
    /// </summary>
    Temperature,

    /// <summary>
    /// 燃料、吸気、排気、酸素センサーに関する項目です。
    /// </summary>
    FuelAndAir,

    /// <summary>
    /// 車速と運転者入力に関する項目です。
    /// </summary>
    Driving,

    /// <summary>
    /// 稼働時間、走行距離、電源に関する項目です。
    /// </summary>
    Diagnostics,
}

/// <summary>
/// <see cref="OBDPIDCategory"/> の raw value と全ケースを扱います。
/// </summary>
public static class OBDPIDCategoryRawValue
{
    /// <summary>
    /// 宣言順の全ケースです。
    /// </summary>
    public static IReadOnlyList<OBDPIDCategory> AllCases { get; } =
    [
        OBDPIDCategory.Engine,
        OBDPIDCategory.Temperature,
        OBDPIDCategory.FuelAndAir,
        OBDPIDCategory.Driving,
        OBDPIDCategory.Diagnostics,
    ];

    /// <summary>
    /// 表示分類を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の表示分類を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する表示分類。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this OBDPIDCategory value) => value switch
    {
        OBDPIDCategory.Engine => "engine",
        OBDPIDCategory.Temperature => "temperature",
        OBDPIDCategory.FuelAndAir => "fuelAndAir",
        OBDPIDCategory.Driving => "driving",
        OBDPIDCategory.Diagnostics => "diagnostics",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から表示分類を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して表示分類へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた表示分類。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out OBDPIDCategory value)
    {
        switch (rawValue)
        {
            case "engine": value = OBDPIDCategory.Engine; return true;
            case "temperature": value = OBDPIDCategory.Temperature; return true;
            case "fuelAndAir": value = OBDPIDCategory.FuelAndAir; return true;
            case "driving": value = OBDPIDCategory.Driving; return true;
            case "diagnostics": value = OBDPIDCategory.Diagnostics; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>
/// <see cref="OBDPIDCategory"/> の表示用の振る舞いを扱います。
/// </summary>
public static class OBDPIDCategoryExtensions
{
    /// <summary>
    /// 画面表示に使用するローカライズキーを返します。
    /// </summary>
    /// <remarks>責務: 表示分類の raw value からローカライズキーを組み立てます。</remarks>
    /// <param name="value">キーを求める表示分類。</param>
    /// <returns><c>telemetry.category.</c> に raw value を続けたローカライズキー。</returns>
    public static string NameKey(this OBDPIDCategory value) => $"telemetry.category.{value.ToRawValue()}";

    /// <summary>
    /// 分類一覧で代表値として優先するPID要求を返します。
    /// </summary>
    /// <remarks>責務: 表示分類ごとに固定された代表Service/PID要求を返します。</remarks>
    /// <param name="value">代表要求を求める表示分類。</param>
    /// <returns>分類の代表PID要求。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static OBDPIDRequest RepresentativeRequest(this OBDPIDCategory value) => value switch
    {
        OBDPIDCategory.Engine => new OBDPIDRequest(Service: 0x01, Pid: 0x0C),
        OBDPIDCategory.Temperature => new OBDPIDRequest(Service: 0x01, Pid: 0x05),
        OBDPIDCategory.FuelAndAir => new OBDPIDRequest(Service: 0x01, Pid: 0x10),
        OBDPIDCategory.Driving => new OBDPIDRequest(Service: 0x01, Pid: 0x0D),
        OBDPIDCategory.Diagnostics => new OBDPIDRequest(Service: 0x01, Pid: 0x42),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// Service/PIDを表示分類へ割り当てます。
    /// </summary>
    /// <remarks>責務: 1件の標準PID要求をリアルタイム表示用の単一分類へ変換します。</remarks>
    /// <param name="request">分類するService/PID要求。</param>
    /// <returns>要求に対応する表示分類。</returns>
    public static OBDPIDCategory Category(OBDPIDRequest request) => request.Pid switch
    {
        0x04 or 0x0C or 0x0E or 0x43 => OBDPIDCategory.Engine,
        0x05 or 0x0F or (>= 0x3C and <= 0x3F) or 0x46 => OBDPIDCategory.Temperature,
        (>= 0x06 and <= 0x0B) or 0x10 or (>= 0x14 and <= 0x1B) or 0x22 or 0x23
            or (>= 0x2C and <= 0x2F) or 0x32 or 0x33 or 0x44 => OBDPIDCategory.FuelAndAir,
        0x0D or 0x11 or 0x45 or (>= 0x47 and <= 0x4C) => OBDPIDCategory.Driving,
        _ => OBDPIDCategory.Diagnostics,
    };
}
