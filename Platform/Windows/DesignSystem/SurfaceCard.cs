using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// Mica の上に置く、半透明の塗りと細い線の角丸カードです（WinUI の Border は継承できないため、作る関数にしています）。
/// </summary>
public static class SurfaceCard
{
    /// <summary>
    /// カードを作ります。
    /// </summary>
    /// <remarks>責務: 中身を、標準のカードか、色の役割の線（と必要なら薄い背景）を付けたカードに入れます。</remarks>
    /// <param name="content">カードの中身。</param>
    /// <param name="tone">線と背景に薄く使う色の役割。null なら標準のカード。</param>
    /// <param name="padding">内側の余白。</param>
    /// <param name="tinted">背景にも役割の色を薄く敷くなら true（記録中・確認が必要など、状態そのものを示すカード）。</param>
    /// <returns>カード。</returns>
    public static Border Create(UIElement content, Tone? tone = null, double padding = 20, bool tinted = false)
    {
        var card = new Border
        {
            Style = DesignTokens.Style("SurfaceCardStyle"),
            Padding = new Thickness(padding),
            Child = content,
        };
        if (tone is { } value)
        {
            card.BorderBrush = DesignTokens.Brush(value switch
            {
                Tone.Caution => "RevTorqueCautionSubtleBrush",
                Tone.Success => "RevTorqueSuccessSubtleBrush",
                Tone.Danger => "RevTorqueDangerSubtleBrush",
                Tone.Indigo => "RevTorqueIndigoSubtleBrush",
                _ => "RevTorqueAccentStrokeBrush",
            });
            if (tinted)
            {
                card.Background = DesignTokens.SubtleBackground(value);
            }
        }

        return card;
    }
}
