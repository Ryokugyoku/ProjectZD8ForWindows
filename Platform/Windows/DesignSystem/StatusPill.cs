using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 記号と短い文字を入れたカプセル形の状態表示です。
/// </summary>
public static class StatusPill
{
    /// <summary>
    /// 状態表示を作ります。
    /// </summary>
    /// <remarks>責務: 色の役割の薄い背景のカプセルに、同じ役割の色の記号と文字を並べます。</remarks>
    /// <param name="glyph">記号。null なら文字だけ。</param>
    /// <param name="text">文字。</param>
    /// <param name="tone">色の役割。</param>
    /// <returns>状態表示。</returns>
    public static Border Create(string? glyph, string text, Tone tone)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (glyph is not null)
        {
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = DesignTokens.Foreground(tone) });
        }

        var label = Typography.Text(text, "CaptionTextBlockStyle", DesignTokens.Foreground(tone), wraps: false);
        label.FontWeight = FontWeights.SemiBold;
        row.Children.Add(label);
        return new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4, 10, 4),
            Background = DesignTokens.SubtleBackground(tone),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };
    }
}
