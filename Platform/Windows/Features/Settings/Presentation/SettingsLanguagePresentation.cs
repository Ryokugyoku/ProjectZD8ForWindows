using ProjectZD8.Application.Settings.State;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;

/// <summary>表示言語の表示用のキーと言語タグです（iOS 版の <c>SettingsLanguage+MacOSPresentation</c>）。</summary>
public static class SettingsLanguagePresentation
{
    /// <summary>言語の名前のキーを返します。</summary>
    /// <remarks>責務: 言語を xcstrings のキーへ変換します。</remarks>
    /// <param name="language">言語。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this SettingsLanguage language) => language switch
    {
        SettingsLanguage.Japanese => "settings.language.japanese",
        SettingsLanguage.English => "settings.language.english",
        _ => "settings.language.spanish",
    };

    /// <summary>Windows のリソースに使う言語タグを返します。</summary>
    /// <remarks>責務: 言語を Strings/ のフォルダ名と同じ BCP-47 タグへ変換します。</remarks>
    /// <param name="language">言語。</param>
    /// <returns>言語タグ。</returns>
    public static string LanguageTag(this SettingsLanguage language) => language switch
    {
        SettingsLanguage.Japanese => "ja-JP",
        SettingsLanguage.English => "en-US",
        _ => "es-ES",
    };
}
