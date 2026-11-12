using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 保持した要素の値だけを差し替えるための小さな補助です（同じ値なら触らないので、無駄な再配置や再描画を起こさない）。
/// </summary>
/// <remarks>
/// 画面は要素を 1 回だけ作ってフィールドに持ち、モデルが変わったら <c>Update(state)</c> の中でこれらを呼んで値だけを直します。
/// </remarks>
public static class Retained
{
    /// <summary>
    /// 文字を差し替えます。
    /// </summary>
    /// <remarks>責務: 違うときだけ Text を設定します。</remarks>
    /// <param name="block">文字の要素。</param>
    /// <param name="text">新しい文字。</param>
    public static void SetText(TextBlock block, string? text)
    {
        text ??= string.Empty;
        if (!string.Equals(block.Text, text, System.StringComparison.Ordinal))
        {
            block.Text = text;
        }
    }

    /// <summary>
    /// 出す・隠すを切り替えます。
    /// </summary>
    /// <remarks>責務: 違うときだけ Visibility を設定します。</remarks>
    /// <param name="element">要素。</param>
    /// <param name="visible">出すなら true。</param>
    public static void SetVisible(UIElement element, bool visible)
    {
        var next = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != next)
        {
            element.Visibility = next;
        }
    }

    /// <summary>
    /// 押せる・押せないを切り替えます。
    /// </summary>
    /// <remarks>責務: 違うときだけ IsEnabled を設定します。</remarks>
    /// <param name="control">コントロール。</param>
    /// <param name="enabled">押せるなら true。</param>
    public static void SetEnabled(Control control, bool enabled)
    {
        if (control.IsEnabled != enabled)
        {
            control.IsEnabled = enabled;
        }
    }

    /// <summary>
    /// スタイルを差し替えます。
    /// </summary>
    /// <remarks>責務: 違うスタイルのときだけ設定します（同じスタイルを入れ直すとテンプレートが組み直される）。</remarks>
    /// <param name="element">要素。</param>
    /// <param name="styleKey">スタイルのキー。</param>
    public static void SetStyle(FrameworkElement element, string styleKey)
    {
        var style = DesignTokens.Style(styleKey);
        if (!ReferenceEquals(element.Style, style))
        {
            element.Style = style;
        }
    }

    /// <summary>
    /// 前景の色を差し替えます。
    /// </summary>
    /// <remarks>責務: 違うブラシのときだけ設定します。</remarks>
    /// <param name="block">文字の要素。</param>
    /// <param name="brush">ブラシ。</param>
    public static void SetForeground(TextBlock block, Brush brush)
    {
        if (!ReferenceEquals(block.Foreground, brush))
        {
            block.Foreground = brush;
        }
    }

    /// <summary>
    /// 記号を差し替えます。
    /// </summary>
    /// <remarks>責務: 違うときだけ Glyph を設定します。</remarks>
    /// <param name="icon">記号の要素。</param>
    /// <param name="glyph">記号。</param>
    public static void SetGlyph(FontIcon icon, string glyph)
    {
        if (!string.Equals(icon.Glyph, glyph, System.StringComparison.Ordinal))
        {
            icon.Glyph = glyph;
        }
    }

    /// <summary>
    /// 文字だけのツールチップを差し替えます。
    /// </summary>
    /// <remarks>責務: 違うときだけ設定します（設定するたびにツールチップの部品が作り直されるので、同じ文字では設定しない）。</remarks>
    /// <param name="element">ツールチップを付ける要素。</param>
    /// <param name="text">ツールチップの文字。</param>
    public static void SetToolTip(DependencyObject element, string text)
    {
        if (!(ToolTipService.GetToolTip(element) is string current && string.Equals(current, text, System.StringComparison.Ordinal)))
        {
            ToolTipService.SetToolTip(element, text);
        }
    }

    /// <summary>
    /// 読み上げの名前を差し替えます。
    /// </summary>
    /// <remarks>責務: 違うときだけ設定します（変わるたびに読み上げソフトへ知らせが飛ぶ）。</remarks>
    /// <param name="element">要素。</param>
    /// <param name="name">名前。</param>
    public static void SetAutomationName(DependencyObject element, string name)
    {
        if (!string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(element), name, System.StringComparison.Ordinal))
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
        }
    }
}
