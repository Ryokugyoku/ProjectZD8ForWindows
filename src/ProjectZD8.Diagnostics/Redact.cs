using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログに入れる値から、個人を特定できる部分を伏せます（docs/LOGGING_STRATEGY.md の 4）。
/// </summary>
/// <remarks>
/// 伏せる処理はここに集めます。計装では、VIN・アカウント ID・Bluetooth のアドレス・URL・ファイルのパスを
/// そのまま <c>props</c> や <c>msg</c> に入れず、必ずここを通します。トークン・秘密はここを通しても書きません。
/// </remarks>
public static partial class Redact
{
    /// <summary>伏せたことを示す記号です。</summary>
    private const string Ellipsis = "…";

    /// <summary>データのフォルダーです（パスを相対にする基準）。</summary>
    private static string? s_dataDirectory;

    /// <summary>
    /// データのフォルダーです。<see cref="DiagnosticSession.Start"/> が設定します。
    /// </summary>
    public static string? DataDirectory
    {
        get => Volatile.Read(ref s_dataDirectory);
        set => Volatile.Write(ref s_dataDirectory, value);
    }

    /// <summary>
    /// VIN を末尾 4 桁だけにします。
    /// </summary>
    /// <param name="vin">VIN。</param>
    /// <returns><c>…1234</c>。空なら空文字。</returns>
    public static string Vin(string? vin)
    {
        var trimmed = vin?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return Ellipsis + (trimmed.Length <= 4 ? trimmed : trimmed[^4..]);
    }

    /// <summary>
    /// アカウントの識別子（Apple の sub、ローカルモードの ID）を SHA-256 の先頭 8 桁にします。
    /// </summary>
    /// <param name="accountId">識別子。</param>
    /// <returns>16 進の小文字 8 桁。空なら空文字。</returns>
    public static string AccountId(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            return string.Empty;
        }

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(accountId), hash);
        return Convert.ToHexStringLower(hash[..4]);
    }

    /// <summary>
    /// Bluetooth のアドレス（48 ビットの数値）を末尾 2 バイトだけにします。
    /// </summary>
    /// <param name="address">アドレス。</param>
    /// <returns><c>…:EE:FF</c>。</returns>
    public static string BluetoothAddress(ulong address) =>
        $"{Ellipsis}:{(address >> 8) & 0xFF:X2}:{address & 0xFF:X2}";

    /// <summary>
    /// 文字列の中の Bluetooth のアドレス（<c>AA:BB:CC:DD:EE:FF</c>・<c>AA-BB-…</c>）をすべて末尾 2 バイトだけにします。
    /// </summary>
    /// <remarks>Windows の機器 ID（<c>BluetoothLE#BluetoothLEb8:…-aa:bb:…</c>）のように、アドレスが入った文字列にも使えます。</remarks>
    /// <param name="text">アドレス、またはアドレスを含む文字列。</param>
    /// <returns>伏せた文字列。空なら空文字。</returns>
    public static string BluetoothAddress(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return MacAddressPattern().Replace(
            text,
            match => $"{Ellipsis}:{match.Groups["e"].Value.ToUpperInvariant()}:{match.Groups["f"].Value.ToUpperInvariant()}");
    }

    /// <summary>
    /// シリアル番号を末尾 2 バイト（16 進の末尾 4 文字）だけにします。
    /// </summary>
    /// <param name="serialNumber">シリアル番号。</param>
    /// <returns><c>…A1B2</c>。空なら空文字。</returns>
    public static string SerialNumber(string? serialNumber)
    {
        var trimmed = serialNumber?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return Ellipsis + (trimmed.Length <= 4 ? trimmed : trimmed[^4..]);
    }

    /// <summary>
    /// URL のクエリ・フラグメント・ユーザー情報を落とします（トークンが入りうるため）。
    /// </summary>
    /// <param name="url">URL。</param>
    /// <returns><c>scheme://host[:port]/path</c>。空なら空文字。</returns>
    public static string Url(Uri? url)
    {
        if (url is null)
        {
            return string.Empty;
        }

        return url.IsAbsoluteUri
            ? url.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped)
            : CutQuery(url.OriginalString);
    }

    /// <summary>
    /// URL の文字列のクエリ・フラグメント・ユーザー情報を落とします。
    /// </summary>
    /// <param name="url">URL の文字列。</param>
    /// <returns>伏せた URL。空なら空文字。</returns>
    public static string Url(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Url(uri) : CutQuery(url);
    }

    /// <summary>文字列の <c>?</c> か <c>#</c> から後ろを落とします。</summary>
    /// <param name="text">文字列。</param>
    /// <returns>落とした文字列。</returns>
    private static string CutQuery(string text)
    {
        var cut = text.IndexOfAny(['?', '#']);
        return cut >= 0 ? text[..cut] : text;
    }

    /// <summary>
    /// ファイルのパスを、データのフォルダーからの相対パスにします（ユーザー名を含むため）。
    /// </summary>
    /// <remarks>データのフォルダーの外なら、ファイル名だけを <c>…\name</c> の形で返します。</remarks>
    /// <param name="path">パス。</param>
    /// <param name="dataDirectory">基準のフォルダー。省略すると <see cref="DataDirectory"/>。</param>
    /// <returns>伏せたパス。空なら空文字。</returns>
    public static string Path(string? path, string? dataDirectory = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var root = dataDirectory ?? DataDirectory;
        try
        {
            if (!string.IsNullOrEmpty(root) && System.IO.Path.IsPathFullyQualified(path))
            {
                var relative = System.IO.Path.GetRelativePath(root, path);
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative))
                {
                    return relative;
                }
            }

            if (!System.IO.Path.IsPathRooted(path) && !path.StartsWith("..", StringComparison.Ordinal))
            {
                return path;
            }
        }
        catch (ArgumentException)
        {
        }

        return Ellipsis + System.IO.Path.DirectorySeparatorChar + System.IO.Path.GetFileName(path);
    }

    /// <summary>
    /// Bluetooth のアドレスの形です（区切りは <c>:</c> か <c>-</c>）。
    /// 前に英字が続いていても当てる（<c>BluetoothLEb8:…</c>）が、もっと長い区切りの並びの一部には当てない。
    /// </summary>
    /// <returns>正規表現。</returns>
    [GeneratedRegex(
        @"(?<![0-9A-Fa-f]:)(?:[0-9A-Fa-f]{2}:){4}(?<e>[0-9A-Fa-f]{2}):(?<f>[0-9A-Fa-f]{2})(?!:?[0-9A-Fa-f])"
        + @"|(?<![0-9A-Fa-f]-)(?:[0-9A-Fa-f]{2}-){4}(?<e>[0-9A-Fa-f]{2})-(?<f>[0-9A-Fa-f]{2})(?!-?[0-9A-Fa-f])",
        RegexOptions.CultureInvariant)]
    private static partial Regex MacAddressPattern();
}
