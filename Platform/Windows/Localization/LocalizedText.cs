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

    /// <summary>言語を指定して引くためのリソース管理です。</summary>
    private static readonly Lazy<ResourceManager?> Manager = new(CreateManager);

    /// <summary>言語ごとのリソース文脈です（UI スレッドからだけ使う）。</summary>
    private static readonly Dictionary<string, ResourceContext> Contexts = new(StringComparer.Ordinal);

    /// <summary>
    /// アカウント設定で選ばれた表示言語（BCP-47、例 <c>en-US</c>）。null なら OS の言語に従います。
    /// </summary>
    public static string? LanguageOverride { get; private set; }

    /// <summary>
    /// 表示言語を切り替えます。
    /// </summary>
    /// <remarks>責務: 以後の <see cref="Get(string)"/> と数値・日付の書式を、指定言語へ切り替えます。</remarks>
    /// <param name="languageTag">BCP-47 の言語タグ。null なら OS の言語に戻す。</param>
    public static void SetLanguage(string? languageTag)
    {
        LanguageOverride = string.IsNullOrEmpty(languageTag) ? null : languageTag;
        var culture = LanguageOverride is null ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(LanguageOverride);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>
    /// 指定言語の文脈で 1 件のリソースを引きます。
    /// </summary>
    /// <remarks>責務: .resw の Name と言語タグから、その言語の値（なければ既定の値）を返します。</remarks>
    /// <param name="name">.resw の Name。</param>
    /// <param name="language">BCP-47 の言語タグ。</param>
    /// <returns>値。見つからなければ null。</returns>
    private static string? ResolveForLanguage(string name, string language)
    {
        if (Manager.Value is not { } manager)
        {
            return Loader.Value?.GetString(name);
        }

        if (!Contexts.TryGetValue(language, out var context))
        {
            context = manager.CreateResourceContext();
            context.QualifierValues["Language"] = language;
            Contexts[language] = context;
        }

        return manager.MainResourceMap.TryGetValue("Resources/" + name, context)?.ValueAsString;
    }

    /// <summary>
    /// リソース管理を作ります。
    /// </summary>
    /// <remarks>責務: パッケージのリソースを読めない環境では null を返します。</remarks>
    /// <returns>リソース管理。作れなければ null。</returns>
    private static ResourceManager? CreateManager()
    {
        try
        {
            return new ResourceManager();
        }
        catch (Exception)
        {
            return null;
        }
    }

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
            var name = ResourceName(key);
            var value = LanguageOverride is { } language
                ? ResolveForLanguage(name, language)
                : Loader.Value?.GetString(name);
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
    /// <remarks>責務: %@・%lld・%d・%f・%X・%%・位置指定（%1$@）・幅（%02X）を .NET の文字列化で置き換えます。</remarks>
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
            var text = match.Groups["conversion"].Value switch
            {
                "f" or "F" when argument is IFormattable number =>
                    number.ToString("F" + (precision ?? 6).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture),
                "x" when argument is IConvertible integer =>
                    Convert.ToInt64(integer, CultureInfo.InvariantCulture).ToString("x", CultureInfo.InvariantCulture),
                "X" when argument is IConvertible integer =>
                    Convert.ToInt64(integer, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture),
                _ => Convert.ToString(argument, CultureInfo.CurrentCulture) ?? string.Empty,
            };
            if (match.Groups["width"].Success
                && int.TryParse(match.Groups["width"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width))
            {
                var flags = match.Groups["flags"].Value;
                text = flags.Contains('-', StringComparison.Ordinal)
                    ? text.PadRight(width)
                    : text.PadLeft(width, flags.Contains('0', StringComparison.Ordinal) ? '0' : ' ');
            }

            return text;
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
    [GeneratedRegex(@"%%|%(?:(?<position>\d+)\$)?(?<flags>[-0]*)(?<width>[1-9]\d*)?(?:\.(?<precision>\d+))?(?:ll|l|h)?(?<conversion>[@dDuUifFxXsS])")]
    private static partial Regex PrintfSpecifier();
}
