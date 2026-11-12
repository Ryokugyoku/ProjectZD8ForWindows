using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// Windows 11 の「設定」アプリと同じ形の 1 行のカードです（左に記号・題・説明、右に操作）。
/// </summary>
public static class SettingsRow
{
    /// <summary>
    /// 設定の行を作ります。
    /// </summary>
    /// <remarks>責務: 記号・題・説明と右側の操作を、Windows の設定カードの並びに置きます。Compact の幅クラスでは操作を下の段へ回します（作り直さず行・列だけを差し替える）。</remarks>
    /// <param name="glyph">記号。</param>
    /// <param name="title">題。</param>
    /// <param name="description">説明。なければ null。</param>
    /// <param name="trailing">右側の操作。なければ null。</param>
    /// <param name="tone">記号の色。</param>
    /// <param name="tinted">色つきの面にするなら true。</param>
    /// <returns>設定の行。</returns>
    public static Border Create(string glyph, string title, string? description, UIElement? trailing, Tone tone = Tone.Accent, bool tinted = false)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 20,
            Foreground = DesignTokens.Foreground(tone),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 0),
        };
        grid.Children.Add(icon);
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(title, "BodyTextBlockStyle"));
        if (!string.IsNullOrEmpty(description))
        {
            texts.Children.Add(Typography.Text(description, "SecondaryCaptionTextStyle"));
        }

        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (trailing is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, 2);
            grid.Children.Add(element);
            ResponsiveLayout.Bind(grid, widthClass =>
            {
                var compact = widthClass == WidthClass.Compact;
                Grid.SetRow(element, compact ? 1 : 0);
                Grid.SetColumn(element, compact ? 1 : 2);
                element.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            });
        }

        var card = new Border
        {
            Background = tinted ? DesignTokens.SubtleBackground(tone) : DesignTokens.Brush("RevTorqueCardBrush"),
            BorderBrush = DesignTokens.Brush("RevTorqueCardStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius.M),
            Padding = new Thickness(Space.L, 14, Space.L, 14),
            MinHeight = 68,
            Child = grid,
        };
        return card;
    }

    /// <summary>設定の見出しを作ります。</summary>
    /// <remarks>責務: 行のまとまりの上に置く、Windows の設定と同じ太字の見出しを作ります。</remarks>
    /// <param name="title">見出し。</param>
    /// <returns>見出しの文字。</returns>
    public static TextBlock Header(string title)
    {
        var header = Typography.Text(title, "BodyStrongTextBlockStyle");
        header.Margin = new Thickness(2, 18, 0, 2);
        return header;
    }
}
