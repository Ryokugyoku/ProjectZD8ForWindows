namespace ProjectZD8.Data.Persistence.Sqlite.Records;

/// <summary>取得境界の日時とUnix epoch基準のmicrosecondを相互変換します。</summary>
/// <remarks>iOS版の <c>canonicalMicroseconds(for:)</c> と同じく最近傍（0.5は0から遠い方）へ丸めます。</remarks>
internal static class AcquisitionMicroseconds
{
    /// <summary>Unix epochのTicksです。</summary>
    private static readonly long UnixEpochTicks = DateTimeOffset.UnixEpoch.UtcTicks;

    /// <summary>日時をUnix epoch基準の整数microsecondへ変換します。</summary>
    /// <remarks>責務: 100ナノ秒単位の日時を最近傍microsecondへcanonical化します。</remarks>
    /// <param name="date">canonical化する日時。</param>
    /// <returns>Unix epochからの符号付きmicrosecond。</returns>
    internal static long From(DateTimeOffset date)
    {
        var ticks = date.UtcTicks - UnixEpochTicks;
        var quotient = ticks / 10;
        var remainder = ticks % 10;
        if (remainder >= 5)
        {
            quotient += 1;
        }
        else if (remainder <= -5)
        {
            quotient -= 1;
        }
        return quotient;
    }

    /// <summary>Unix epoch基準のmicrosecondを日時へ戻します。</summary>
    /// <remarks>責務: 保存済みmicrosecondをUTCの日時へ変換します。</remarks>
    /// <param name="microseconds">Unix epochからのmicrosecond。</param>
    /// <returns>UTCの日時。</returns>
    internal static DateTimeOffset ToDate(long microseconds) => new(UnixEpochTicks + microseconds * 10, TimeSpan.Zero);
}
