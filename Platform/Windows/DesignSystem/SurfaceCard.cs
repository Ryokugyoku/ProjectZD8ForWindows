using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// カードの種類です。
/// </summary>
public enum CardVariant
{
    /// <summary>標準：Mica の上の半透明の塗りと細い線（角丸 12）。</summary>
    Default,

    /// <summary>主役：グラデーションの塗りと上辺の発光（角丸 20）。各画面に 1 枚まで。</summary>
    Hero,

    /// <summary>静か：線のない薄い塗り（角丸 8）。カードの中の小さなまとまり。</summary>
    Quiet,
}

/// <summary>
/// Mica の上に置く、薄い層のカードです（WinUI の Border は継承できないため、作る関数にしています）。
/// </summary>
/// <remarks>影は付けません（影はダイアログ・フライアウトなどシステムの重なりだけ）。発光はグラデーションの線で出します。</remarks>
public static class SurfaceCard
{
    /// <summary>
    /// カードを作ります。
    /// </summary>
    /// <remarks>責務: 中身を、標準のカードか、色の役割の線（と必要なら薄い背景）を付けたカードに入れます。</remarks>
    /// <param name="content">カードの中身。</param>
    /// <param name="tone">線と背景に薄く使う色の役割。null なら標準のカード。</param>
    /// <param name="padding">内側の余白（<see cref="Space"/> のトークンを渡す）。</param>
    /// <param name="tinted">背景にも役割の色を薄く敷くなら true（記録中・確認が必要など、状態そのものを示すカード）。</param>
    /// <param name="variant">カードの種類。</param>
    /// <returns>カード。</returns>
    public static Border Create(UIElement content, Tone? tone = null, double padding = Space.XL, bool tinted = false, CardVariant variant = CardVariant.Default)
    {
        var card = new Border
        {
            Style = DesignTokens.Style(variant switch
            {
                CardVariant.Hero => "HeroCardStyle",
                CardVariant.Quiet => "QuietSurfaceStyle",
                _ => "SurfaceCardStyle",
            }),
            Padding = new Thickness(padding),
            Child = content,
        };
        if (tone is { } value)
        {
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = DesignTokens.Stroke(value == Tone.Neutral ? Tone.Accent : value);
            if (tinted)
            {
                card.Background = DesignTokens.SubtleBackground(value);
            }
        }

        return card;
    }

    /// <summary>
    /// 主役のカード（ヒーロー）を作ります。
    /// </summary>
    /// <remarks>責務: <see cref="CardVariant.Hero"/> のカードを、幅クラスに合わせた内側の余白で作ります（Compact 20、ほか 28）。</remarks>
    /// <param name="content">カードの中身。</param>
    /// <returns>カード。</returns>
    public static Border Hero(UIElement content)
    {
        var card = Create(content, variant: CardVariant.Hero);
        ResponsiveLayout.Bind(card, widthClass => card.Padding = new Thickness(widthClass == WidthClass.Compact ? Space.XL : 28));
        return card;
    }
}
