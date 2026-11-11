using System.Globalization;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// <c>props</c> に入れる値を、数値・真偽・短い文字列のどれかに固定します。
/// </summary>
internal static class DiagnosticPropertyValue
{
    /// <summary>文字列の長さの上限です（これより長ければ切って <c>…</c> を付ける）。</summary>
    public const int MaxStringLength = 512;

    /// <summary>
    /// 値を固定します。
    /// </summary>
    /// <remarks>
    /// 責務: 数値と真偽はそのまま、時間の長さはミリ秒の数値、それ以外は不変の文字列にします
    /// （呼び出し側のオブジェクトを背景のスレッドで触らないため）。
    /// </remarks>
    /// <param name="value">元の値。</param>
    /// <returns>固定した値。</returns>
    public static object? Normalize(object? value) => value switch
    {
        null => null,
        string text => Truncate(text),
        bool or int or long or short or byte or sbyte or uint or ulong or ushort or double or float or decimal => value,
        TimeSpan span => span.TotalMilliseconds,
        DateTimeOffset time => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime time => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Enum => value.ToString(),
        IFormattable formattable => Truncate(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => Truncate(value.ToString() ?? string.Empty),
    };

    /// <summary>長すぎる文字列を切ります。</summary>
    /// <param name="text">文字列。</param>
    /// <returns>上限以下の文字列。</returns>
    private static string Truncate(string text) =>
        text.Length <= MaxStringLength ? text : string.Concat(text.AsSpan(0, MaxStringLength), "…");
}
