namespace ProjectZD8.Data.Mapping;

/// <summary>
/// Swift の <c>JSONEncoder</c> が日付を数値へ変換する方式です。
/// </summary>
public enum SwiftJsonDateEncoding
{
    /// <summary>Swift の既定（<c>.deferredToDate</c>）。2001-01-01 00:00:00 UTC からの秒数（小数あり）。</summary>
    SecondsSinceReferenceDate,

    /// <summary><c>.millisecondsSince1970</c>。1970-01-01 00:00:00 UTC からのミリ秒（小数あり）。</summary>
    MillisecondsSince1970,
}

/// <summary>
/// <see cref="DateTimeOffset"/> と Swift の JSON 日付数値を相互に変換します。
/// </summary>
/// <remarks>
/// Swift の <c>Date</c> は 2001 年基準の倍精度秒、.NET は 100ns 単位なので、100ns 未満の差は丸められます。
/// </remarks>
public static class SwiftJsonDate
{
    /// <summary>Swift の基準日時（2001-01-01 00:00:00 UTC）です。</summary>
    public static readonly DateTimeOffset ReferenceDate = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 日時を Swift の JSON 数値へ変換します。
    /// </summary>
    /// <remarks>責務: 1 件の日時を指定方式の倍精度数値へ変換します。</remarks>
    /// <param name="value">変換する日時。</param>
    /// <param name="encoding">日付の方式。</param>
    /// <returns>JSON に書く数値。</returns>
    public static double ToNumber(DateTimeOffset value, SwiftJsonDateEncoding encoding) => encoding switch
    {
        SwiftJsonDateEncoding.SecondsSinceReferenceDate =>
            (value.UtcTicks - ReferenceDate.UtcTicks) / (double)TimeSpan.TicksPerSecond,
        SwiftJsonDateEncoding.MillisecondsSince1970 =>
            (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / (double)TimeSpan.TicksPerMillisecond,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null),
    };

    /// <summary>
    /// Swift の JSON 数値を日時へ変換します。
    /// </summary>
    /// <remarks>責務: 1 件の倍精度数値を 100ns 単位へ丸めて UTC の日時へ変換します。</remarks>
    /// <param name="number">JSON から読んだ数値。</param>
    /// <param name="encoding">日付の方式。</param>
    /// <returns>UTC の日時。</returns>
    /// <exception cref="System.Text.Json.JsonException">表せる範囲を超える場合。</exception>
    public static DateTimeOffset FromNumber(double number, SwiftJsonDateEncoding encoding)
    {
        if (!double.IsFinite(number))
        {
            throw new System.Text.Json.JsonException("日付の数値が有限ではありません。");
        }
        var (origin, ticksPerUnit) = encoding switch
        {
            SwiftJsonDateEncoding.SecondsSinceReferenceDate => (ReferenceDate, (double)TimeSpan.TicksPerSecond),
            SwiftJsonDateEncoding.MillisecondsSince1970 => (DateTimeOffset.UnixEpoch, (double)TimeSpan.TicksPerMillisecond),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null),
        };
        var ticks = Math.Round(number * ticksPerUnit, MidpointRounding.AwayFromZero);
        // 基準からの差を先に整数にしてから足します（0 年からの tick を double で足すと 128 tick 単位に丸まるため）。
        if (ticks < DateTimeOffset.MinValue.UtcTicks - origin.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks - origin.UtcTicks)
        {
            throw new System.Text.Json.JsonException("日付が表せる範囲を超えています。");
        }
        return new DateTimeOffset(origin.UtcTicks + (long)ticks, TimeSpan.Zero);
    }
}
