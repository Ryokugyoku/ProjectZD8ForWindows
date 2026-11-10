using System;
using System.Globalization;

namespace ProjectZD8ForWindows.Platform.Windows.Formatting;

/// <summary>
/// 秒数を所要時間として表示します（iOS 版の各画面の <c>DateComponentsFormatter</c> の使い方に合わせる）。
/// </summary>
/// <remarks>単位の表記は言語によらず h・m・s にしています（Apple は言語ごとの略記。違いとして報告する）。</remarks>
public static class DurationText
{
    /// <summary>
    /// 履歴画面の書式：1 時間以上は「時・分」、それ未満は「分」（分は 0 埋め）。
    /// </summary>
    /// <remarks>責務: 記録時間を時と分の短い文字列にします。</remarks>
    /// <param name="seconds">秒数。</param>
    /// <returns>表示文字列。</returns>
    public static string HoursAndMinutes(double seconds)
    {
        var total = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return total.TotalSeconds >= 3_600
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)total.TotalHours}h {total.Minutes:00}m")
            : string.Create(CultureInfo.CurrentCulture, $"{(int)total.TotalMinutes}m");
    }

    /// <summary>
    /// 解析画面の書式：大きい単位から 2 つまで（時・分、分・秒、秒）。
    /// </summary>
    /// <remarks>責務: 所要時間を最大 2 単位の略記にします。</remarks>
    /// <param name="seconds">秒数。</param>
    /// <returns>表示文字列。</returns>
    public static string Abbreviated(double seconds)
    {
        var total = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        if (total.TotalSeconds >= 3_600)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)total.TotalHours}h {total.Minutes}m");
        }

        return total.TotalSeconds >= 60
            ? string.Create(CultureInfo.CurrentCulture, $"{total.Minutes}m {total.Seconds}s")
            : string.Create(CultureInfo.CurrentCulture, $"{total.Seconds}s");
    }

    /// <summary>
    /// 経過時間をタイマーの形（時:分:秒）で表示します（SwiftUI の <c>Text(date, style: .timer)</c>）。
    /// </summary>
    /// <remarks>責務: 開始時刻から今までの経過を時:分:秒にします。</remarks>
    /// <param name="startedAt">開始時刻。</param>
    /// <param name="now">今の時刻。</param>
    /// <returns>表示文字列。</returns>
    public static string Timer(DateTimeOffset startedAt, DateTimeOffset now)
    {
        var elapsed = now - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{elapsed.Minutes}:{elapsed.Seconds:00}");
    }
}
