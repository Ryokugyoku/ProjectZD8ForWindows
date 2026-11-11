using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Views;

/// <summary>
/// 設定の 1 行のカードです（左に記号・題・説明、右に操作）。
/// </summary>
/// <remarks>
/// <see cref="SettingsRow"/> と同じ見た目ですが、説明と記号の色を後から差し替えられます（行を作り直さずに値だけ更新するため）。
/// Compact の幅クラスでは操作を下の段へ回します。
/// </remarks>
internal sealed class SettingsItem
{
    /// <summary>記号です。</summary>
    private readonly FontIcon _icon;

    /// <summary>説明です。</summary>
    private readonly TextBlock _description;

    /// <summary>今の記号の色の種類です。</summary>
    private Tone _tone;

    /// <summary>行を作ります。</summary>
    /// <param name="glyph">記号。</param>
    /// <param name="title">題。</param>
    /// <param name="description">説明。なければ null。</param>
    /// <param name="trailing">右側の操作。なければ null。</param>
    /// <param name="tone">記号の色。</param>
    public SettingsItem(string glyph, string title, string? description, FrameworkElement? trailing, Tone tone = Tone.Accent)
    {
        _tone = tone;
        var grid = new Grid { ColumnSpacing = Space.L, RowSpacing = Space.M };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = Metrics.IconTileSmall * 0.625,
            Foreground = DesignTokens.Foreground(tone),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Space.XXS, 0, Space.XS, 0),
        };
        grid.Children.Add(_icon);
        var texts = new StackPanel { Spacing = Space.XXS, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(title, TextStyles.Body));
        _description = Typography.Text(string.Empty, TextStyles.SecondaryCaption);
        texts.Children.Add(_description);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        SetDescription(description);

        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
            ResponsiveLayout.Bind(grid, widthClass =>
            {
                var compact = widthClass == WidthClass.Compact;
                Grid.SetRow(trailing, compact ? 1 : 0);
                Grid.SetColumn(trailing, compact ? 1 : 2);
                trailing.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            });
        }

        Element = new Border
        {
            Background = DesignTokens.Brush("RevTorqueCardBrush"),
            BorderBrush = DesignTokens.Brush("RevTorqueCardStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius.M),
            Padding = new Thickness(Space.L, Space.M, Space.L, Space.M),
            MinHeight = Metrics.RowMinHeight,
            Child = grid,
        };
    }

    /// <summary>カードの要素です。</summary>
    public Border Element { get; }

    /// <summary>説明を差し替えます。</summary>
    /// <remarks>責務: 値が違うときだけ文字を設定し、空なら隠します。</remarks>
    /// <param name="description">説明。</param>
    public void SetDescription(string? description)
    {
        Retained.SetText(_description, description);
        Retained.SetVisible(_description, !string.IsNullOrEmpty(description));
    }

    /// <summary>記号の色を差し替えます。</summary>
    /// <param name="tone">色の種類。</param>
    public void SetTone(Tone tone)
    {
        if (tone != _tone)
        {
            _tone = tone;
            _icon.Foreground = DesignTokens.Foreground(tone);
        }
    }
}
