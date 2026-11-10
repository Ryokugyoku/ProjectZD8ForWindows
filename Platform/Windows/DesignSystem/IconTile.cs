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
    /// <remarks>責務: 色の役割の薄い背景の角丸の枠に、同じ役割の色の記号を置きます。</remarks>
    /// <param name="glyph">記号（<see cref="FluentSymbol"/>）。</param>
    /// <param name="tone">色の役割。</param>
    /// <param name="size">タイルの一辺（px）。</param>
    /// <returns>タイル。</returns>
    public static Border Create(string glyph, Tone tone = Tone.Accent, double size = 40) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.28),
        Background = DesignTokens.SubtleBackground(tone),
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
}
