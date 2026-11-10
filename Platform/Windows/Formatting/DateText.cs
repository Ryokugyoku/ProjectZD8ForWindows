using System;
using System.Globalization;

namespace ProjectZD8ForWindows.Platform.Windows.Formatting;

/// <summary>
/// 日時を表示言語の書式で表示します（SwiftUI の <c>.dateTime.year().month().day()...</c>）。
/// </summary>
public static class DateText
{
    /// <summary>
    /// 年月日と時分を表示します。
    /// </summary>
    /// <remarks>責務: 日時を端末の時間帯の短い日付と時刻にします。</remarks>
    /// <param name="date">日時。</param>
    /// <returns>表示文字列。</returns>
    public static string DateAndMinute(DateTimeOffset date) => date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// 年月日と時分秒を表示します。
    /// </summary>
    /// <remarks>責務: 日時を端末の時間帯の短い日付と秒までの時刻にします。</remarks>
    /// <param name="date">日時。</param>
    /// <returns>表示文字列。</returns>
    public static string DateAndSecond(DateTimeOffset date) => date.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

    /// <summary>
    /// 年月日だけを表示します。
    /// </summary>
    /// <remarks>責務: 日時を端末の時間帯の短い日付にします。</remarks>
    /// <param name="date">日時。</param>
    /// <returns>表示文字列。</returns>
    public static string DateOnly(DateTimeOffset date) => date.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}
