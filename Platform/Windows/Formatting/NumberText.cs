using System;
using System.Globalization;

namespace ProjectZD8ForWindows.Platform.Windows.Formatting;

/// <summary>
/// 数値を表示言語の書式で文字列にします（SwiftUI の <c>.number.precision(.fractionLength(0...n))</c>）。
/// </summary>
public static class NumberText
{
    /// <summary>
    /// 小数を最大 <paramref name="maximumFractionDigits"/> 桁で表示します。
    /// </summary>
    /// <remarks>責務: 桁区切りあり、末尾の 0 を省いた数値文字列を作ります。</remarks>
    /// <param name="value">値。</param>
    /// <param name="maximumFractionDigits">小数の最大桁数。</param>
    /// <returns>表示文字列。</returns>
    public static string Decimal(double value, int maximumFractionDigits = 1)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "—";
        }

        var format = maximumFractionDigits <= 0 ? "#,0" : "#,0." + new string('#', maximumFractionDigits);
        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 整数を桁区切り付きで表示します。
    /// </summary>
    /// <remarks>責務: 件数などの整数を表示文字列にします。</remarks>
    /// <param name="value">値。</param>
    /// <returns>表示文字列。</returns>
    public static string Integer(long value) => value.ToString("#,0", CultureInfo.CurrentCulture);

    /// <summary>
    /// 0〜1 の割合を百分率で表示します。
    /// </summary>
    /// <remarks>責務: 割合を小数なしの百分率文字列にします。</remarks>
    /// <param name="fraction">割合（0〜1）。</param>
    /// <returns>表示文字列。</returns>
    public static string Percent(double fraction) =>
        Math.Clamp(fraction, 0, 1).ToString("P0", CultureInfo.CurrentCulture);

    /// <summary>
    /// 値と単位をまとめて表示します。
    /// </summary>
    /// <remarks>責務: 小数 1 桁までの値と単位を空白でつなぎます。</remarks>
    /// <param name="value">値。</param>
    /// <param name="unit">単位。</param>
    /// <returns>表示文字列。</returns>
    public static string Measurement(double value, string unit) => $"{Decimal(value, 1)} {unit}";
}
