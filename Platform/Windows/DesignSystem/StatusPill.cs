using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 記号（または点）と短い文字を入れたカプセル形の状態表示です。
/// </summary>
/// <remarks>作ったあとは <see cref="Set"/> で中身だけを差し替えます（作り直さない）。</remarks>
public static class StatusPill
{
    /// <summary>
    /// 状態表示を作ります。
    /// </summary>
    /// <remarks>責務: 色の役割の薄い背景と細い線のカプセルに、同じ役割の色の記号と文字を並べます。記号が null なら小さな点を出します。</remarks>
    /// <param name="glyph">記号。null なら点。</param>
    /// <param name="text">文字。</param>
    /// <param name="tone">色の役割。</param>
    /// <returns>状態表示。</returns>
    public static Border Create(string? glyph, string text, Tone tone)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new FontIcon { FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center });
        var label = Typography.Text(text, "CaptionTextBlockStyle", wraps: false);
        label.FontWeight = FontWeights.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(Radius.Pill),
            Padding = new Thickness(10, 3, 10, 4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };
        Set(pill, glyph, text, tone);
        return pill;
    }

    /// <summary>
    /// 作った状態表示の中身を差し替えます。
    /// </summary>
    /// <remarks>責務: 記号（点）・文字・色を新しい状態に合わせます。</remarks>
    /// <param name="pill"><see cref="Create"/> で作った状態表示。</param>
    /// <param name="glyph">記号。null なら点。</param>
    /// <param name="text">文字。</param>
    /// <param name="tone">色の役割。</param>
    public static void Set(Border pill, string? glyph, string text, Tone tone)
    {
        if (pill.Child is not StackPanel { Children: [FontIcon icon, Ellipse dot, TextBlock label] })
        {
            return;
        }

        var foreground = DesignTokens.Foreground(tone);
        pill.Background = DesignTokens.SubtleBackground(tone);
        pill.BorderBrush = DesignTokens.Stroke(tone);
        Retained.SetVisible(icon, glyph is not null);
        Retained.SetVisible(dot, glyph is null);
        if (glyph is not null)
        {
            Retained.SetGlyph(icon, glyph);
        }

        icon.Foreground = foreground;
        dot.Fill = foreground;
        Retained.SetText(label, text);
        label.Foreground = foreground;
    }
}
