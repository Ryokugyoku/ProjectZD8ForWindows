using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 薄い色の角丸の枠に記号を置いた、カードの見出し用の小さなタイルです。
/// </summary>
public static class IconTile
{
    /// <summary>
    /// タイルを作ります。
    /// </summary>
    /// <remarks>責務: 色の役割の薄い背景と細い線の角丸の枠に、同じ役割の色の記号を置きます。記号は <see cref="SetTone"/> で差し替えられます。</remarks>
    /// <param name="glyph">記号（<see cref="FluentSymbol"/>）。</param>
    /// <param name="tone">色の役割。</param>
    /// <param name="size">タイルの一辺（px。<see cref="Metrics"/> の IconTile* を渡す）。</param>
    /// <returns>タイル。</returns>
    public static Border Create(string glyph, Tone tone = Tone.Accent, double size = Metrics.IconTileMedium) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.3),
        Background = DesignTokens.SubtleBackground(tone),
        BorderBrush = DesignTokens.Stroke(tone),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new FontIcon
        {
            Glyph = glyph,
            FontSize = size * 0.45,
            Foreground = DesignTokens.Foreground(tone),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>
    /// 作ったタイルの記号と色を差し替えます（作り直さない）。
    /// </summary>
    /// <remarks>責務: タイルの背景・線・記号の色と記号を、新しい役割に合わせます。</remarks>
    /// <param name="tile"><see cref="Create"/> で作ったタイル。</param>
    /// <param name="glyph">記号。</param>
    /// <param name="tone">色の役割。</param>
    public static void SetTone(Border tile, string glyph, Tone tone)
    {
        tile.Background = DesignTokens.SubtleBackground(tone);
        tile.BorderBrush = DesignTokens.Stroke(tone);
        if (tile.Child is FontIcon icon)
        {
            Retained.SetGlyph(icon, glyph);
            icon.Foreground = DesignTokens.Foreground(tone);
        }
    }
}
