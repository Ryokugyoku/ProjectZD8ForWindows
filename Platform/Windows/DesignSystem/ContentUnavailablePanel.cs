using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 表示するものがないことを伝える、記号・題・説明の組です（SwiftUI の <c>ContentUnavailableView</c>）。
/// </summary>
public sealed partial class ContentUnavailablePanel : StackPanel
{
    /// <summary>記号・題・説明を指定して作ります。</summary>
    /// <param name="glyph">記号。</param>
    /// <param name="title">題。</param>
    /// <param name="description">説明。null なら省く。</param>
    public ContentUnavailablePanel(string glyph, string title, string? description = null)
    {
        Spacing = Space.M;
        Padding = new Thickness(Space.XXL, 36, Space.XXL, 36);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        var tile = IconTile.Create(glyph, Tone.Neutral, 64);
        tile.CornerRadius = new CornerRadius(32);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        tile.Margin = new Thickness(0, 0, 0, Space.XS);
        Children.Add(tile);
        var titleText = Typography.Text(title, "SubtitleTextBlockStyle");
        titleText.HorizontalAlignment = HorizontalAlignment.Center;
        titleText.HorizontalTextAlignment = TextAlignment.Center;
        Children.Add(titleText);
        if (description is not null)
        {
            var body = Typography.Text(description, "SecondaryBodyTextStyle");
            body.HorizontalAlignment = HorizontalAlignment.Center;
            body.HorizontalTextAlignment = TextAlignment.Center;
            body.MaxWidth = Metrics.ReadingMaxWidth * 0.75;
            Children.Add(body);
        }
    }
}
