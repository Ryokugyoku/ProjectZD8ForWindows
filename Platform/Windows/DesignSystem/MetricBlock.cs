using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 数値と、その下の短い説明の組です。
/// </summary>
public sealed partial class MetricBlock : StackPanel
{
    /// <summary>数値と説明を指定して作ります。</summary>
    /// <param name="value">数値の文字列。</param>
    /// <param name="caption">説明。</param>
    /// <param name="alignment">左右の寄せ方。</param>
    /// <param name="valueForeground">数値の色。null なら標準。</param>
    /// <param name="valueStyleKey">数値の文字スタイル。</param>
    public MetricBlock(
        string value,
        string caption,
        HorizontalAlignment alignment = HorizontalAlignment.Left,
        Brush? valueForeground = null,
        string valueStyleKey = "MetricValueTextStyle")
    {
        Spacing = 2;
        MinWidth = 76;
        HorizontalAlignment = alignment;
        var valueText = Typography.Text(value, valueStyleKey, valueForeground, wraps: false);
        var captionText = Typography.Text(caption, "SecondaryCaptionTextStyle");
        valueText.HorizontalAlignment = alignment;
        captionText.HorizontalAlignment = alignment;
        captionText.HorizontalTextAlignment = alignment == HorizontalAlignment.Right ? TextAlignment.Right : TextAlignment.Left;
        Children.Add(valueText);
        Children.Add(captionText);
    }
}
