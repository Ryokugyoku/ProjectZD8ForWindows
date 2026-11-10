using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 記号と文字を横に並べたラベルです（SwiftUI の <c>Label</c>）。
/// </summary>
public sealed partial class GlyphLabel : StackPanel
{
    /// <summary>記号と文字を指定して作ります。</summary>
    /// <param name="glyph">記号。</param>
    /// <param name="text">文字。</param>
    /// <param name="styleKey">文字スタイル。</param>
    /// <param name="foreground">記号と文字の色。null なら標準。</param>
    public GlyphLabel(string glyph, string text, string styleKey = "BodyTextBlockStyle", Brush? foreground = null)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        var icon = new FontIcon { Glyph = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var label = Typography.Text(text, styleKey, foreground);
        label.VerticalAlignment = VerticalAlignment.Center;
        if (foreground is not null)
        {
            icon.Foreground = foreground;
        }

        Children.Add(icon);
        Children.Add(label);
    }
}
