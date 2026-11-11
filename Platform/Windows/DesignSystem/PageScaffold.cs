using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 画面の外枠です。縦にスクロールし、中身を最大幅（<see cref="Metrics.ContentMaxWidth"/>）で中央に置き、余白と節の間隔を幅クラスに合わせます。
/// </summary>
/// <remarks>
/// 画面は <see cref="Body"/> に節（見出し・カード・格子）を足すだけでよい。幅クラスが変わっても中身は作り直さず、余白と間隔だけを差し替えます。
/// 横スクロールは出しません（中身は幅に合わせて折り返すこと）。
/// </remarks>
public sealed partial class PageScaffold : UserControl
{
    /// <summary>空の外枠を作ります。</summary>
    /// <param name="maxContentWidth">中身の最大の幅。省略時は <see cref="Metrics.ContentMaxWidth"/>。</param>
    public PageScaffold(double maxContentWidth = Metrics.ContentMaxWidth)
    {
        Scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Body = new StackPanel { MaxWidth = maxContentWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        // ScrollViewer の中で MaxWidth と Stretch を組み合わせると中央に置かれずはみ出すので、外側の Grid で中央に置く。
        _frame = new Grid { Children = { Body } };
        Scroller.Content = _frame;
        Content = Scroller;
        ResponsiveLayout.Bind(this, ApplyLayout);
    }

    /// <summary>中身を中央に置き、外周の余白を持つ枠です。</summary>
    private readonly Grid _frame;

    /// <summary>縦のスクロールです（スクロール位置の保存・復元に使う）。</summary>
    public ScrollViewer Scroller { get; }

    /// <summary>中身を積む縦のパネルです。</summary>
    public StackPanel Body { get; }

    /// <summary>
    /// 幅クラスに合わせて余白と間隔を切り替えます。
    /// </summary>
    /// <remarks>責務: ページの外周の余白と節の間隔を、幅クラスのトークンへ差し替えます。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        var gutter = Space.PageGutter(widthClass);
        _frame.Padding = new Thickness(gutter, gutter * 0.75, gutter, gutter);
        Body.Spacing = Space.Section(widthClass);
    }
}
