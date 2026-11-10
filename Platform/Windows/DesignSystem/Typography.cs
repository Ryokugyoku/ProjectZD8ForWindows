using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// RevTorqueTheme.xaml の文字スタイルを当てた TextBlock を作ります。
/// </summary>
public static class Typography
{
    /// <summary>
    /// スタイル付きの TextBlock を作ります。
    /// </summary>
    /// <remarks>責務: 表示文字列と文字スタイルのキーから、折り返し方を決めた TextBlock を作ります。</remarks>
    /// <param name="text">表示文字列。</param>
    /// <param name="styleKey">文字スタイルのキー（RevTorqueTheme.xaml か WinUI の TextBlock スタイル）。</param>
    /// <param name="foreground">前景の色。省略時はスタイルの色。</param>
    /// <param name="wraps">折り返すなら true。</param>
    /// <returns>TextBlock。</returns>
    public static TextBlock Text(string text, string styleKey = "BodyTextBlockStyle", Brush? foreground = null, bool wraps = true)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = DesignTokens.Style(styleKey),
            TextWrapping = wraps ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            TextTrimming = wraps ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
        if (foreground is not null)
        {
            block.Foreground = foreground;
        }

        return block;
    }

    /// <summary>
    /// 太字にした TextBlock を作ります。
    /// </summary>
    /// <remarks>責務: <see cref="Text"/> の結果を太字にします。</remarks>
    /// <param name="text">表示文字列。</param>
    /// <param name="styleKey">文字スタイルのキー。</param>
    /// <param name="foreground">前景の色。</param>
    /// <returns>TextBlock。</returns>
    public static TextBlock Strong(string text, string styleKey = "BodyStrongTextBlockStyle", Brush? foreground = null)
    {
        var block = Text(text, styleKey, foreground);
        block.FontWeight = FontWeights.SemiBold;
        return block;
    }
}
