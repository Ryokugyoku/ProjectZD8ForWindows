using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログのファイルの種類です。
/// </summary>
public enum DiagnosticFileKind
{
    /// <summary>通常のログ（<c>app-*.jsonl</c>）。</summary>
    App,

    /// <summary>クラッシュのときに同期で書くログ（<c>crash-*.jsonl</c>）。</summary>
    Crash,
}

/// <summary>
/// 診断ログのファイル名です（<c>app-20261111-7f3a9c.jsonl</c>、<c>app-20261111-7f3a9c.1.jsonl</c>、<c>crash-20261111-7f3a9c.jsonl</c>）。
/// </summary>
/// <param name="Kind">種類。</param>
/// <param name="Date">実行を始めた日（UTC）。</param>
/// <param name="RunPrefix">実行 ID の先頭 6 桁。</param>
/// <param name="Index">ローテーションの連番（最初のファイルは 0）。</param>
public sealed partial record DiagnosticFileName(DiagnosticFileKind Kind, DateOnly Date, string RunPrefix, int Index)
{
    /// <summary>ファイル名に入れる実行 ID の桁数です。</summary>
    public const int RunPrefixLength = 6;

    /// <summary>
    /// ファイル名を返します。
    /// </summary>
    /// <returns>ファイル名（フォルダーなし）。</returns>
    public override string ToString()
    {
        var prefix = Kind == DiagnosticFileKind.App ? "app" : "crash";
        var date = Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Index == 0
            ? $"{prefix}-{date}-{RunPrefix}.jsonl"
            : $"{prefix}-{date}-{RunPrefix}.{Index.ToString(CultureInfo.InvariantCulture)}.jsonl";
    }

    /// <summary>
    /// ファイル名を読みます。
    /// </summary>
    /// <param name="fileName">ファイル名（フォルダーなし）。</param>
    /// <param name="name">読んだ名前。</param>
    /// <returns>診断ログのファイル名なら true。</returns>
    public static bool TryParse(string fileName, out DiagnosticFileName name)
    {
        var match = Pattern().Match(fileName);
        if (!match.Success
            || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            name = null!;
            return false;
        }

        var index = match.Groups["index"].Success
            ? int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture)
            : 0;
        name = new DiagnosticFileName(
            match.Groups["kind"].Value == "app" ? DiagnosticFileKind.App : DiagnosticFileKind.Crash,
            date,
            match.Groups["run"].Value,
            index);
        return true;
    }

    /// <summary>ファイル名の形です。</summary>
    /// <returns>正規表現。</returns>
    [GeneratedRegex(@"^(?<kind>app|crash)-(?<date>\d{8})-(?<run>[0-9a-f]{6})(?:\.(?<index>\d{1,6}))?\.jsonl$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
