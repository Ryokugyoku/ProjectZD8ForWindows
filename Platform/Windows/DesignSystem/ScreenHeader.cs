using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 画面の見出し（記号のタイル、小見出し、題、副題、右側の要約）です。
/// </summary>
/// <remarks>
/// Compact では記号のタイルを小さくし、右側の要約を題の下へ回します（作り直さず、行・列だけを差し替える）。
/// 題・副題は <see cref="SetTitle"/>・<see cref="SetSubtitle"/> で差し替えられます。副題は本文の最大幅で折り返します。
/// </remarks>
public sealed partial class ScreenHeader : Grid
{
    /// <summary>記号のタイルです。</summary>
    private readonly Border? _tile;

    /// <summary>右側の要約です。</summary>
    private readonly FrameworkElement? _trailing;

    /// <summary>題の文字です。</summary>
    private readonly TextBlock _title;

    /// <summary>副題の文字です。</summary>
    private readonly TextBlock _subtitle;

    /// <summary>見出しの各部分を指定して作ります。</summary>
    /// <param name="glyph">記号。null なら記号なし。</param>
    /// <param name="eyebrow">小見出し（大文字の短い文）。null なら省く。</param>
    /// <param name="title">題。</param>
    /// <param name="subtitle">副題。null なら省く。</param>
    /// <param name="trailing">右側に置く要約。null なら省く。</param>
    /// <param name="tone">記号のタイルの色の役割。</param>
    public ScreenHeader(string? glyph, string? eyebrow, string title, string? subtitle, UIElement? trailing = null, Tone tone = Tone.Accent)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (glyph is not null)
        {
            _tile = IconTile.Create(glyph, tone, Metrics.IconTileLarge);
            _tile.VerticalAlignment = VerticalAlignment.Top;
            Children.Add(_tile);
        }

        var texts = new StackPanel { Spacing = Space.XS, VerticalAlignment = VerticalAlignment.Center };
        if (eyebrow is not null)
        {
            texts.Children.Add(Typography.Text(eyebrow, TextStyles.Eyebrow));
        }

        _title = Typography.Text(title, TextStyles.PageTitle);
        texts.Children.Add(_title);
        _subtitle = Typography.Text(subtitle ?? string.Empty, TextStyles.SecondaryBody);
        _subtitle.MaxWidth = Metrics.ReadingMaxWidth;
        _subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        _subtitle.Visibility = subtitle is null ? Visibility.Collapsed : Visibility.Visible;
        texts.Children.Add(_subtitle);
        SetColumn(texts, 1);
        Children.Add(texts);
        if (trailing is FrameworkElement element)
        {
            _trailing = element;
            Children.Add(element);
        }

        ResponsiveLayout.Bind(this, ApplyLayout);
    }

    /// <summary>
    /// 題を差し替えます。
    /// </summary>
    /// <remarks>責務: 題の文字だけを変えます。</remarks>
    /// <param name="title">題。</param>
    public void SetTitle(string title) => Retained.SetText(_title, title);

    /// <summary>
    /// 副題を差し替えます。
    /// </summary>
    /// <remarks>責務: 副題の文字を変え、null なら隠します。</remarks>
    /// <param name="subtitle">副題。</param>
    public void SetSubtitle(string? subtitle)
    {
        Retained.SetText(_subtitle, subtitle);
        Retained.SetVisible(_subtitle, subtitle is not null);
    }

    /// <summary>
    /// 幅クラスに合わせて並びを切り替えます。
    /// </summary>
    /// <remarks>責務: Compact では要約を題の下の段へ回し、タイルを小さくします。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        var compact = widthClass == WidthClass.Compact;
        ColumnSpacing = compact ? Space.M : Space.L + Space.XS;
        RowSpacing = compact ? Space.M : 0;
        if (_tile is not null)
        {
            var size = compact ? Metrics.IconTileMedium : Metrics.IconTileLarge;
            _tile.Width = size;
            _tile.Height = size;
            _tile.CornerRadius = new CornerRadius(size * 0.3);
            if (_tile.Child is FontIcon icon)
            {
                icon.FontSize = size * 0.45;
            }
        }

        if (_trailing is not null)
        {
            SetRow(_trailing, compact ? 1 : 0);
            SetColumn(_trailing, compact ? 1 : 2);
            _trailing.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            _trailing.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
