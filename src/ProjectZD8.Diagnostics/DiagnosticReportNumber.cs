using System.Globalization;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 利用者に見せる報告用の番号（<c>7F3A-1842</c>）です。実行 ID の先頭 4 桁と、その実行内の通し番号（<c>seq</c>）を並べます。
/// </summary>
/// <remarks>利用者がこの番号を伝えれば、ログの該当行（<c>run</c> が <c>7f3a</c> で始まり <c>seq</c> が 1842）がすぐ見つかります。</remarks>
public static class DiagnosticReportNumber
{
    /// <summary>番号に入れる実行 ID の桁数です。</summary>
    public const int RunDigits = 4;

    /// <summary>
    /// 番号を作ります。
    /// </summary>
    /// <param name="runId">実行 ID（16 進）。</param>
    /// <param name="sequence">通し番号（<c>seq</c>）。</param>
    /// <returns><c>7F3A-1842</c> の形の文字列。</returns>
    public static string Format(string runId, long sequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        var run = runId.Length > RunDigits ? runId[..RunDigits] : runId;
        return string.Create(CultureInfo.InvariantCulture, $"{run.ToUpperInvariant()}-{sequence}");
    }
}
