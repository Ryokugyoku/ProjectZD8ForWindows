using System.Globalization;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>ELM/STN応答文字列を Swift の Foundation と同じ規則で行とトークンへ分けます。</summary>
/// <remarks>
/// iOS 版では各パーサーが <c>components(separatedBy: .newlines)</c> などを直接呼んでいます。
/// C# には同じ文字集合の API がないため、その規則だけをここに集めています。
/// </remarks>
internal static class ELM327ResponseText
{
    /// <summary>Foundation の <c>CharacterSet.newlines</c> に含まれる文字です。</summary>
    private static readonly char[] NewlineCharacters = ['\n', '\u000B', '\u000C', '\r', '\u0085', (char)0x2028, (char)0x2029];

    /// <summary>改行と復帰文字で応答を行へ分けます。</summary>
    /// <remarks>責務: 1件の応答を <c>components(separatedBy: .newlines)</c> と <c>components(separatedBy: "\r")</c> の組合せと同じ行列へ変換します。</remarks>
    /// <param name="response">分割する応答。</param>
    /// <returns>空行を含む行の一覧。</returns>
    public static string[] SplitLines(string response) => response.Split(NewlineCharacters);

    /// <summary>行の前後から空白と改行を取り除きます。</summary>
    /// <remarks>責務: <c>trimmingCharacters(in: .whitespacesAndNewlines)</c> と同じ結果を返します。</remarks>
    /// <param name="value">整理する文字列。</param>
    /// <returns>前後の空白を除いた文字列。</returns>
    public static string TrimWhitespaceAndNewlines(string value) => value.Trim();

    /// <summary>空白と改行で区切り、空の要素を除いたトークンを返します。</summary>
    /// <remarks>責務: <c>components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }</c> と同じ結果を返します。</remarks>
    /// <param name="value">分割する文字列。</param>
    /// <returns>空でないトークンの一覧。</returns>
    public static IEnumerable<string> WhitespaceSeparatedTokens(string value)
    {
        var start = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                if (start >= 0)
                {
                    yield return value[start..index];
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = index;
            }
        }

        if (start >= 0)
        {
            yield return value[start..];
        }
    }

    /// <summary>行に「SEARCHING」が大文字小文字を問わず含まれるかを返します。</summary>
    /// <remarks>責務: ELMのプロトコル探索中表示を応答本文から区別します。</remarks>
    /// <param name="line">判定する行。</param>
    /// <returns>含まれる場合は <c>true</c>。</returns>
    public static bool ContainsSearching(string line) =>
        line.Contains("SEARCHING", StringComparison.OrdinalIgnoreCase);

    /// <summary>2桁の16進文字列を1バイトへ変換します。</summary>
    /// <remarks>責務: <c>UInt8(token, radix: 16)</c> のうち ELM が出力する2桁の形だけを受理します。</remarks>
    /// <param name="token">変換する文字列。</param>
    /// <param name="value">変換できたバイト。</param>
    /// <returns>変換できた場合は <c>true</c>。</returns>
    public static bool TryParseHexByte(ReadOnlySpan<char> token, out byte value) =>
        byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
