namespace ProjectZD8.Data.Tests.TestSupport;

/// <summary>Swiftの <c>Date(timeIntervalSince1970:)</c> に相当する日時を作ります。</summary>
internal static class TestDates
{
    /// <summary>Unix epochからの秒数で日時を作ります。</summary>
    /// <remarks>責務: 秒数を100ナノ秒単位へ丸めたUTCの日時へ変換します。</remarks>
    /// <param name="seconds">Unix epochからの秒数。</param>
    /// <returns>UTCの日時。</returns>
    public static DateTimeOffset Unix(double seconds) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    /// <summary>Unix epochからのTicksで日時を作ります。</summary>
    /// <remarks>責務: 100ナノ秒単位の正確な日時を作ります。</remarks>
    /// <param name="ticks">Unix epochからのTicks。</param>
    /// <returns>UTCの日時。</returns>
    public static DateTimeOffset UnixTicks(long ticks) => DateTimeOffset.UnixEpoch.AddTicks(ticks);
}
