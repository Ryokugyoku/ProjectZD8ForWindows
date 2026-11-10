using System;
using System.Globalization;

namespace ProjectZD8ForWindows.Platform.Windows.Formatting;

/// <summary>
/// バイト数をファイルの大きさとして表示します（Apple の <c>ByteCountFormatter</c> の <c>.file</c>、1 KB = 1,000 バイト）。
/// </summary>
public static class ByteCountText
{
    /// <summary>
    /// バイト数を読みやすい単位の文字列にします。
    /// </summary>
    /// <remarks>責務: 負の値を 0 とみなし、bytes・KB・MB・GB のいずれかで表示します。</remarks>
    /// <param name="byteCount">バイト数。</param>
    /// <returns>表示文字列。</returns>
    public static string Format(long byteCount)
    {
        var bytes = Math.Max(0, byteCount);
        if (bytes < 1_000)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{bytes} bytes");
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        var value = bytes / 1_000.0;
        var index = 0;
        while (value >= 1_000 && index < units.Length - 1)
        {
            value /= 1_000;
            index++;
        }

        var format = index == 0 ? "#,0" : "#,0.#";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + units[index];
    }
}
