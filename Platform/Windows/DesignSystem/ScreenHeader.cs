using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 画面の見出し（記号のタイル、小見出し、題、副題、右側の要約）です。
/// </summary>
public sealed partial class ScreenHeader : Grid
{
    /// <summary>見出しの各部分を指定して作ります。</summary>
    /// <param name="glyph">記号。null なら記号なし。</param>
    /// <param name="eyebrow">小見出し（大文字の短い文）。null なら省く。</param>
    /// <param name="title">題。</param>
    /// <param name="subtitle">副題。null なら省く。</param>
    /// <param name="trailing">右側に置く要約。null なら省く。</param>
    /// <param name="tone">記号のタイルの色の役割。</param>
    public ScreenHeader(string? glyph, string? eyebrow, string title, string? subtitle, UIElement? trailing = null, Tone tone = Tone.Accent)
    {
        ColumnSpacing = 18;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (glyph is not null)
        {
            Children.Add(IconTile.Create(glyph, tone, 60));
        }

        var texts = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (eyebrow is not null)
        {
            texts.Children.Add(Typography.Text(eyebrow, "EyebrowTextStyle"));
        }

        texts.Children.Add(Typography.Text(title, "PageTitleTextStyle"));
        if (subtitle is not null)
        {
            texts.Children.Add(Typography.Text(subtitle, "SecondaryBodyTextStyle"));
        }

        SetColumn(texts, 1);
        Children.Add(texts);
        if (trailing is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(element, 2);
            Children.Add(element);
        }
    }
}
