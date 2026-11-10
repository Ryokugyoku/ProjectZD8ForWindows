using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 中断や確認が必要なことを示す三角の警告記号です（iOS 版の <c>WarningTriangleIcon</c>）。
/// </summary>
public sealed partial class WarningTriangleIcon : FontIcon
{
    /// <summary>大きさと色の役割を指定して作ります。</summary>
    /// <param name="size">記号の大きさ（px）。</param>
    /// <param name="tone">色の役割（標準は注意のオレンジ）。</param>
    public WarningTriangleIcon(double size = 16, Tone tone = Tone.Caution)
    {
        Glyph = FluentSymbol.Warning;
        FontSize = size;
        Foreground = DesignTokens.Foreground(tone);
        VerticalAlignment = VerticalAlignment.Center;
    }
}
