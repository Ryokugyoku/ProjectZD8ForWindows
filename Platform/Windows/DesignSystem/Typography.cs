using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 文字の段階のキーです（RevTorqueTheme.xaml のスタイル名）。画面のコードは文字列の直書きの代わりにこれを使います。
/// </summary>
public static class TextStyles
{
    /// <summary>小見出し（11、字間広め、シアン）。</summary>
    public const string Eyebrow = "EyebrowTextStyle";

    /// <summary>画面の大見出し（40）。ホームなど、1 画面に 1 つだけ。</summary>
    public const string Display = "DisplayTextStyle";

    /// <summary>画面の題（28）。</summary>
    public const string PageTitle = "PageTitleTextStyle";

    /// <summary>節の題（20）。</summary>
    public const string SectionTitle = "SectionTitleTextStyle";

    /// <summary>カードの題（16、太字）。</summary>
    public const string CardTitle = "CardTitleTextStyle";

    /// <summary>本文（14）。</summary>
    public const string Body = "BodyTextBlockStyle";

    /// <summary>本文の太字（14）。</summary>
    public const string BodyStrong = "BodyStrongTextBlockStyle";

    /// <summary>補足の本文（14、薄い色）。</summary>
    public const string SecondaryBody = "SecondaryBodyTextStyle";

    /// <summary>補足の小さな文字（12、薄い色）。</summary>
    public const string SecondaryCaption = "SecondaryCaptionTextStyle";

    /// <summary>小さな文字（12）。</summary>
    public const string Caption = "CaptionTextBlockStyle";

    /// <summary>計器の主役の数値（56、等幅数字）。</summary>
    public const string MetricHero = "MetricHeroTextStyle";

    /// <summary>大きな数値（36、等幅数字）。</summary>
    public const string MetricLarge = "MetricLargeTextStyle";

    /// <summary>標準の数値（20、等幅数字）。</summary>
    public const string MetricValue = "MetricValueTextStyle";

    /// <summary>数値の単位（12、薄い色）。</summary>
    public const string MetricUnit = "MetricUnitTextStyle";

    /// <summary>ID・PID など（等幅の書体）。</summary>
    public const string Monospace = "MonospaceCaptionTextStyle";
}

/// <summary>
/// RevTorqueTheme.xaml の文字スタイルを当てた TextBlock を作ります。
/// </summary>
public static class Typography
{
    /// <summary>
    /// スタイル付きの TextBlock を作ります。
    /// </summary>
    /// <remarks>責務: 表示文字列と文字スタイルのキーから、折り返し方を決めた TextBlock を作ります。数値のスタイルなら数字を等幅にします。</remarks>
    /// <param name="text">表示文字列。</param>
    /// <param name="styleKey">文字スタイルのキー（<see cref="TextStyles"/> か WinUI の TextBlock スタイル）。</param>
    /// <param name="foreground">前景の色。省略時はスタイルの色。</param>
    /// <param name="wraps">折り返すなら true。</param>
    /// <returns>TextBlock。</returns>
    public static TextBlock Text(string text, string styleKey = "BodyTextBlockStyle", Brush? foreground = null, bool wraps = true)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = DesignTokens.Style(styleKey),
            TextWrapping = wraps ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            TextTrimming = wraps ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
        if (styleKey.StartsWith("Metric", System.StringComparison.Ordinal))
        {
            TabularNumerals(block);
        }

        if (foreground is not null)
        {
            block.Foreground = foreground;
        }

        return block;
    }

    /// <summary>
    /// 太字にした TextBlock を作ります。
    /// </summary>
    /// <remarks>責務: <see cref="Text"/> の結果を太字にします。</remarks>
    /// <param name="text">表示文字列。</param>
    /// <param name="styleKey">文字スタイルのキー。</param>
    /// <param name="foreground">前景の色。</param>
    /// <returns>TextBlock。</returns>
    public static TextBlock Strong(string text, string styleKey = "BodyStrongTextBlockStyle", Brush? foreground = null)
    {
        var block = Text(text, styleKey, foreground);
        block.FontWeight = FontWeights.SemiBold;
        return block;
    }

    /// <summary>
    /// 計器の数値の TextBlock を作ります（等幅数字・折り返さない）。
    /// </summary>
    /// <remarks>責務: 値が変わっても桁の位置が揺れない数値の文字を作ります。値は <see cref="Retained.SetText"/> で差し替えます。</remarks>
    /// <param name="text">数値の文字列。</param>
    /// <param name="styleKey">数値のスタイル（<see cref="TextStyles.MetricHero"/> など）。</param>
    /// <param name="foreground">前景の色。</param>
    /// <returns>TextBlock。</returns>
    public static TextBlock Metric(string text, string styleKey = TextStyles.MetricValue, Brush? foreground = null)
    {
        var block = Text(text, styleKey, foreground, wraps: false);
        TabularNumerals(block);
        return block;
    }

    /// <summary>
    /// 数字を等幅にします。
    /// </summary>
    /// <remarks>責務: OpenType の tnum を有効にし、値が変わっても数字の幅が変わらないようにします。</remarks>
    /// <param name="block">文字の要素。</param>
    public static void TabularNumerals(TextBlock block) =>
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
}
