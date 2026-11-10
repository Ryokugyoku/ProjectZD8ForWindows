namespace ProjectZD8.Application.Settings.State;

/// <summary>Appleアカウント単位で保持するConnection以外の設定値です。</summary>
public sealed record AccountSettings
{
    /// <summary>アカウント設定を構成する値を指定して生成します。</summary>
    /// <remarks>責務: 同期対象である表示設定とセッション自動送信設定を1件のアカウント設定へまとめます。</remarks>
    /// <param name="language">アプリ内表示に使用する言語。</param>
    /// <param name="appearance">アプリ全体へ適用する外観。</param>
    /// <param name="automaticSessionUploadEnabled">セッション終了時にiCloudへ自動送信する場合は <c>true</c>。</param>
    public AccountSettings(
        SettingsLanguage language = SettingsLanguage.Japanese,
        SettingsAppearance appearance = SettingsAppearance.System,
        bool automaticSessionUploadEnabled = false)
    {
        Language = language;
        Appearance = appearance;
        AutomaticSessionUploadEnabled = automaticSessionUploadEnabled;
    }

    /// <summary>アプリ内表示に使用する言語です。</summary>
    public SettingsLanguage Language { get; init; }

    /// <summary>アプリ全体へ適用する外観です。</summary>
    public SettingsAppearance Appearance { get; init; }

    /// <summary>セッション終了時にRawログをiCloudへ自動送信するかを示します。</summary>
    public bool AutomaticSessionUploadEnabled { get; init; }
}

/// <summary>iOSとmacOSで共有するアプリ内表示言語です。</summary>
public enum SettingsLanguage
{
    /// <summary>日本語表示を選択します。</summary>
    Japanese,

    /// <summary>英語表示を選択します。</summary>
    English,

    /// <summary>スペイン語表示を選択します。</summary>
    Spanish,
}

/// <summary><see cref="SettingsLanguage"/> の raw value と識別子を扱います。</summary>
public static class SettingsLanguageRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<SettingsLanguage> AllCases { get; } =
        [SettingsLanguage.Japanese, SettingsLanguage.English, SettingsLanguage.Spanish];

    /// <summary>raw value の文字列を返します。</summary>
    /// <remarks>責務: 1件の言語を Swift と同じ raw value へ変換します。</remarks>
    /// <param name="value">変換する言語。</param>
    /// <returns>raw value。</returns>
    public static string ToRawValue(this SettingsLanguage value) => value switch
    {
        SettingsLanguage.Japanese => "japanese",
        SettingsLanguage.English => "english",
        SettingsLanguage.Spanish => "spanish",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value の文字列から言語を復元します。</summary>
    /// <remarks>責務: Swift の <c>init(rawValue:)</c> と同じ照合を Ordinal で行います。</remarks>
    /// <param name="rawValue">復元する raw value。</param>
    /// <param name="value">復元できた言語。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out SettingsLanguage value)
    {
        switch (rawValue)
        {
            case "japanese": value = SettingsLanguage.Japanese; return true;
            case "english": value = SettingsLanguage.English; return true;
            case "spanish": value = SettingsLanguage.Spanish; return true;
            default: value = default; return false;
        }
    }

    /// <summary>選択状態とアクセシビリティ参照に使う安定識別子です。</summary>
    /// <remarks>責務: raw value をそのまま識別子として返します。</remarks>
    /// <param name="value">対象の言語。</param>
    /// <returns>安定識別子。</returns>
    public static string Id(this SettingsLanguage value) => value.ToRawValue();
}

/// <summary>iOSとmacOSで共有するアプリ外観設定です。</summary>
public enum SettingsAppearance
{
    /// <summary>システムの外観設定へ追従します。</summary>
    System,

    /// <summary>明るい外観を選択します。</summary>
    Light,

    /// <summary>暗い外観を選択します。</summary>
    Dark,
}

/// <summary><see cref="SettingsAppearance"/> の raw value と識別子を扱います。</summary>
public static class SettingsAppearanceRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<SettingsAppearance> AllCases { get; } =
        [SettingsAppearance.System, SettingsAppearance.Light, SettingsAppearance.Dark];

    /// <summary>raw value の文字列を返します。</summary>
    /// <remarks>責務: 1件の外観を Swift と同じ raw value へ変換します。</remarks>
    /// <param name="value">変換する外観。</param>
    /// <returns>raw value。</returns>
    public static string ToRawValue(this SettingsAppearance value) => value switch
    {
        SettingsAppearance.System => "system",
        SettingsAppearance.Light => "light",
        SettingsAppearance.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value の文字列から外観を復元します。</summary>
    /// <remarks>責務: Swift の <c>init(rawValue:)</c> と同じ照合を Ordinal で行います。</remarks>
    /// <param name="rawValue">復元する raw value。</param>
    /// <param name="value">復元できた外観。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out SettingsAppearance value)
    {
        switch (rawValue)
        {
            case "system": value = SettingsAppearance.System; return true;
            case "light": value = SettingsAppearance.Light; return true;
            case "dark": value = SettingsAppearance.Dark; return true;
            default: value = default; return false;
        }
    }

    /// <summary>選択状態とアクセシビリティ参照に使う安定識別子です。</summary>
    /// <remarks>責務: raw value をそのまま識別子として返します。</remarks>
    /// <param name="value">対象の外観。</param>
    /// <returns>安定識別子。</returns>
    public static string Id(this SettingsAppearance value) => value.ToRawValue();
}
