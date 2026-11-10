using ProjectZD8.Application.Settings.State;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;

/// <summary>外観の表示用のキーと記号です（iOS 版の <c>SettingsAppearance+MacOSPresentation</c>）。</summary>
public static class SettingsAppearancePresentation
{
    /// <summary>外観の名前のキーを返します。</summary>
    /// <remarks>責務: 外観を xcstrings のキーへ変換します。</remarks>
    /// <param name="appearance">外観。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this SettingsAppearance appearance) => appearance switch
    {
        SettingsAppearance.System => "settings.appearance.system",
        SettingsAppearance.Light => "settings.appearance.light",
        _ => "settings.appearance.dark",
    };

    /// <summary>外観の記号（Segoe Fluent Icons）を返します。</summary>
    /// <remarks>責務: 外観を記号の文字へ変換します。</remarks>
    /// <param name="appearance">外観。</param>
    /// <returns>記号の文字。</returns>
    public static string Glyph(this SettingsAppearance appearance) => appearance switch
    {
        SettingsAppearance.System => "",
        SettingsAppearance.Light => "",
        _ => "",
    };
}
