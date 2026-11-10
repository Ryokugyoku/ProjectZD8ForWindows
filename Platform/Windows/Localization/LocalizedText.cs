using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Windows.ApplicationModel.Resources;

namespace ProjectZD8ForWindows.Platform.Windows.Localization;

/// <summary>
/// iOS 版の Localizable.xcstrings のキーで、.resw に変換した翻訳文字列を引きます。
/// </summary>
/// <remarks>
/// xcstrings のキー（例 <c>account.delete.cancel</c>）は、.resw の Name に使えない文字を含むため、
/// <see cref="ResourceName(string)"/> の規則で変換して引きます。変換スクリプト（scripts/ConvertXcstrings.cs）も同じ規則を使います。
/// </remarks>
public static partial class LocalizedText
{
    /// <summary>アプリの既定リソース（Strings/&lt;言語&gt;/Resources.resw）を読む読込器です。</summary>
    private static readonly Lazy<ResourceLoader?> Loader = new(CreateLoader);

    /// <summary>
    /// xcstrings のキーに対応する翻訳文字列を返します。
    /// </summary>
    /// <remarks>責務: 1 件の xcstrings キーを現在の表示言語の文字列へ変換します。</remarks>
    /// <param name="key">xcstrings のキー。</param>
    /// <returns>翻訳文字列。見つからなければキーそのもの。</returns>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return key;
        }

        try
        {
            var value = Loader.Value?.GetString(ResourceName(key));
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    /// <summary>
    /// xcstrings のキーの書式文字列（%@、%lld、%1$@ など）に値を埋め込みます。
    /// </summary>
    /// <remarks>責務: Swift の printf 形式の翻訳文字列を、引数で埋めた表示文字列へ変換します。</remarks>
    /// <param name="key">xcstrings のキー。</param>
    /// <param name="arguments">埋め込む値（Swift の引数順）。</param>
    /// <returns>埋め込み済みの文字列。</returns>
    public static string Format(string key, params object?[] arguments) =>
        FormatPrintf(Get(key), arguments);

    /// <summary>
    /// xcstrings のキーを .resw の Name へ変換します。
    /// </summary>
    /// <remarks>
    /// 責務: キーを、英数字と下線だけの一意な .resw の Name へ決定的に変換します。
    /// 規則: 英数字以外を <c>_</c> に置き換える。キーに <c>[A-Za-z0-9._-]</c> 以外の文字が含まれるか空なら、
    /// 末尾に <c>_x</c> と UTF-8 の SHA-256 の先頭 8 桁（小文字 16 進）を付ける。先頭が数字なら <c>k_</c> を付ける。
    /// </remarks>
    /// <param name="key">xcstrings のキー。</param>
    /// <returns>.resw の Name。</returns>
    public static string ResourceName(string key)
    {
        var builder = new StringBuilder(key.Length + 12);
        var needsHash = key.Length == 0;
        foreach (var character in key)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
                if (character is not ('.' or '_' or '-'))
                {
                    needsHash = true;
                }
            }
        }

        if (needsHash)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            builder.Append("_x").Append(Convert.ToHexStringLower(hash, 0, 4));
        }

        if (builder.Length == 0 || char.IsAsciiDigit(builder[0]) || builder[0] == '_')
        {
            builder.Insert(0, "k_");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Swift の printf 形式の書式に値を埋め込みます。
    /// </summary>
    /// <remarks>責務: %@・%lld・%d・%f・%%・位置指定（%1$@）を .NET の文字列化で置き換えます。</remarks>
    /// <param name="format">printf 形式の書式。</param>
    /// <param name="arguments">埋め込む値。</param>
    /// <returns>埋め込み済みの文字列。</returns>
    public static string FormatPrintf(string format, params object?[] arguments)
    {
        var next = 0;
        return PrintfSpecifier().Replace(format, match =>
        {
            if (match.Value == "%%")
            {
                return "%";
            }

            var index = match.Groups["position"].Success
                ? int.Parse(match.Groups["position"].Value, CultureInfo.InvariantCulture) - 1
                : next++;
            if (index < 0 || index >= arguments.Length)
            {
                return match.Value;
            }

            var argument = arguments[index];
            var precision = match.Groups["precision"].Success
                ? int.Parse(match.Groups["precision"].Value, CultureInfo.InvariantCulture)
                : (int?)null;
            return match.Groups["conversion"].Value switch
            {
                "f" when argument is IFormattable number =>
                    number.ToString("F" + (precision ?? 6).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture),
                _ => Convert.ToString(argument, CultureInfo.CurrentCulture) ?? string.Empty,
            };
        });
    }

    /// <summary>
    /// 既定のリソース読込器を作ります。
    /// </summary>
    /// <remarks>責務: パッケージのリソースを読めない環境（単体テストなど）では null を返します。</remarks>
    /// <returns>読込器。作れなければ null。</returns>
    private static ResourceLoader? CreateLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>printf 形式の書式指定子に一致する正規表現です。</summary>
    [GeneratedRegex(@"%%|%(?:(?<position>\d+)\$)?(?:\.(?<precision>\d+))?(?:ll|l|h)?(?<conversion>[@dDuUifFxXsS])")]
    private static partial Regex PrintfSpecifier();
}
