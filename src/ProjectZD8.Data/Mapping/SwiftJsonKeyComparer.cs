namespace ProjectZD8.Data.Mapping;

/// <summary>
/// Swift の <c>JSONEncoder.OutputFormatting.sortedKeys</c> と同じ順にキーを並べる比較です。
/// </summary>
/// <remarks>
/// Foundation は <c>compare(_:options: [.numeric, .caseInsensitive, .forcedOrdering])</c> で並べます。
/// 大文字小文字を区別せず、数字の並びは数値として比べ、同じなら序数で決めます。
/// 実機の出力との完全一致は未確認です（読み込みは順番に依存しないので、同期には影響しません）。
/// </remarks>
public sealed class SwiftJsonKeyComparer : IComparer<string>
{
    /// <summary>共有のインスタンスです。</summary>
    public static SwiftJsonKeyComparer Instance { get; } = new();

    /// <summary>
    /// 2 つのキーを Swift の並べ替え規則で比べます。
    /// </summary>
    /// <remarks>責務: 2 件のキーの前後関係を、数値と大文字小文字を考慮して決めます。</remarks>
    /// <param name="x">左のキー。</param>
    /// <param name="y">右のキー。</param>
    /// <returns>x が前なら負、同じなら 0、後なら正。</returns>
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return -1;
        }
        if (y is null)
        {
            return 1;
        }
        var result = CompareNumericCaseInsensitive(x, y);
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// 数字の並びを数値として、ほかの文字を大文字小文字を区別せずに比べます。
    /// </summary>
    /// <remarks>責務: forcedOrdering を除いた比較結果を返します。</remarks>
    /// <param name="x">左のキー。</param>
    /// <param name="y">右のキー。</param>
    /// <returns>比較結果。</returns>
    private static int CompareNumericCaseInsensitive(string x, string y)
    {
        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startI = i;
                var startJ = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }
                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }
                var left = x[startI..i].TrimStart('0');
                var right = y[startJ..j].TrimStart('0');
                if (left.Length != right.Length)
                {
                    return left.Length.CompareTo(right.Length);
                }
                var digits = string.CompareOrdinal(left, right);
                if (digits != 0)
                {
                    return digits;
                }
                continue;
            }
            var a = char.ToLowerInvariant(x[i]);
            var b = char.ToLowerInvariant(y[j]);
            if (a != b)
            {
                return a.CompareTo(b);
            }
            i++;
            j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
