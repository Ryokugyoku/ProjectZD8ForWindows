namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 接続履歴の走行距離差分に使用できる累積距離の取得元です。
/// </summary>
public enum ConnectionSessionDistanceSource
{
    /// <summary>
    /// 故障コード消去時にリセットされるService 01 PID 31です。
    /// </summary>
    DistanceSinceCodesCleared = 0,

    /// <summary>
    /// 車両の累積走行距離を表すService 01 PID A6です。
    /// </summary>
    Odometer = 1,

    /// <summary>
    /// ZD8専用Service 21 PID 02の累積走行距離です。
    /// </summary>
    VehicleSpecificOdometer = 2,
}

/// <summary>
/// <see cref="ConnectionSessionDistanceSource"/> の整数 raw value と振る舞いを扱います。
/// </summary>
public static class ConnectionSessionDistanceSourceRawValue
{
    /// <summary>
    /// 取得元を整数の raw value へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の取得元を Swift 版と同じ整数 raw value へ変換します。</remarks>
    /// <param name="value">変換する取得元。</param>
    /// <returns>Swift 版と同じ整数 raw value。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static int ToRawValue(this ConnectionSessionDistanceSource value) => value switch
    {
        ConnectionSessionDistanceSource.DistanceSinceCodesCleared => 0,
        ConnectionSessionDistanceSource.Odometer => 1,
        ConnectionSessionDistanceSource.VehicleSpecificOdometer => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// 整数の raw value から取得元を復元します。
    /// </summary>
    /// <remarks>責務: 整数 raw value を定義済みの取得元へ変換します。</remarks>
    /// <param name="rawValue">復元する整数 raw value。</param>
    /// <param name="value">復元できた取得元。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(int rawValue, out ConnectionSessionDistanceSource value)
    {
        switch (rawValue)
        {
            case 0: value = ConnectionSessionDistanceSource.DistanceSinceCodesCleared; return true;
            case 1: value = ConnectionSessionDistanceSource.Odometer; return true;
            case 2: value = ConnectionSessionDistanceSource.VehicleSpecificOdometer; return true;
            default: value = default; return false;
        }
    }

    /// <summary>
    /// 別の取得元より優先して採用できるかを返します。
    /// </summary>
    /// <remarks>責務: 2件の累積距離取得元を接続履歴での採用優先度に従って比較します。</remarks>
    /// <param name="value">優先度を判定する取得元。</param>
    /// <param name="other">現在採用している比較対象の取得元。</param>
    /// <returns>自身を比較対象より優先できる場合は <c>true</c>。</returns>
    public static bool HasPriority(this ConnectionSessionDistanceSource value, ConnectionSessionDistanceSource other) =>
        value.ToRawValue() > other.ToRawValue();
}

/// <summary>
/// 接続履歴の走行距離差分へ渡す取得元付き累積距離です。
/// </summary>
public sealed record ConnectionSessionDistanceObservation
{
    /// <summary>
    /// 累積距離を取得した標準OBD-II PIDの意味です。
    /// </summary>
    public ConnectionSessionDistanceSource Source { get; }

    /// <summary>
    /// PID定義でキロメートルへ数値化済みの累積距離です。
    /// </summary>
    public double Kilometers { get; }

    /// <summary>
    /// 車種専用PIDから取得した場合の適用型式です。
    /// </summary>
    public string? VehicleModelCode { get; }

    /// <summary>
    /// 取得元とキロメートル値を固定して生成します。
    /// </summary>
    /// <remarks>責務: 1件の標準OBD-II累積距離を取得元付き観測へ変換します。</remarks>
    /// <param name="source">累積距離を取得したPIDの意味。</param>
    /// <param name="kilometers">キロメートルへ数値化済みの累積距離。</param>
    /// <param name="vehicleModelCode">車種専用PIDの適用型式。</param>
    public ConnectionSessionDistanceObservation(
        ConnectionSessionDistanceSource source,
        double kilometers,
        string? vehicleModelCode = null)
    {
        Source = source;
        Kilometers = kilometers;
        VehicleModelCode = vehicleModelCode;
    }
}
