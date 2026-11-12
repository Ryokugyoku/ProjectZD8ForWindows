using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 幅に合わせて列の数を決め、子を同じ幅の列へ順に並べるパネルです（SwiftUI の <c>LazyVGrid(columns: [.adaptive(minimum:)])</c>）。
/// </summary>
/// <remarks>
/// 同じ行の子は、その行でいちばん高い子の高さにそろえます。
/// 幅クラス（<see cref="ResponsiveLayout"/>）に追従します：間隔を指定しなければ幅クラスのトークン（<see cref="Space.Gap"/>）を使い、
/// Compact ではカードの最小の幅を <see cref="CompactScale"/> 倍に縮め、<see cref="SetMaxColumns"/> で幅クラスごとの列の上限を決められます。
/// 子を作り直さずに並びだけが変わるので、幅を変えても重くなりません。仮想化はしないので、子は数十個までにします（それ以上は ListView / ItemsView）。
/// </remarks>
public sealed partial class AdaptiveGrid : Panel
{
    /// <summary>列の最小の幅です。</summary>
    private double _minItemWidth = Metrics.CardMinWidth;

    /// <summary>指定された間隔です。NaN なら幅クラスのトークン。</summary>
    private double _spacing = double.NaN;

    /// <summary>幅クラスごとの列の上限です（0 は上限なし）。</summary>
    private readonly int[] _maxColumns = new int[4];

    /// <summary>今の幅クラスです。</summary>
    private WidthClass _widthClass = ResponsiveLayout.Current;

    /// <summary>空のパネルを作ります。</summary>
    public AdaptiveGrid()
    {
        ResponsiveLayout.Bind(this, widthClass =>
        {
            _widthClass = widthClass;
            InvalidateMeasure();
        });
    }

    /// <summary>
    /// 数値のタイル向け（最小 168、Compact で 2 列以上）の格子を作ります。
    /// </summary>
    /// <remarks>責務: トークンの最小の幅を設定した格子を返します。</remarks>
    /// <returns>格子。</returns>
    public static AdaptiveGrid Tiles() => new() { MinItemWidth = Metrics.TileMinWidth };

    /// <summary>
    /// 標準のカード向け（最小 280）の格子を作ります。
    /// </summary>
    /// <remarks>責務: トークンの最小の幅を設定した格子を返します。</remarks>
    /// <returns>格子。</returns>
    public static AdaptiveGrid Cards() => new() { MinItemWidth = Metrics.CardMinWidth };

    /// <summary>
    /// 広いカード（グラフ・一覧）向け（最小 420、Wide でも 3 列まで）の格子を作ります。
    /// </summary>
    /// <remarks>責務: トークンの最小の幅と列の上限を設定した格子を返します。</remarks>
    /// <returns>格子。</returns>
    public static AdaptiveGrid WideCards()
    {
        var grid = new AdaptiveGrid { MinItemWidth = Metrics.WideCardMinWidth };
        grid.SetMaxColumns(1, 2, 2, 3);
        return grid;
    }

    /// <summary>列の最小の幅（px）です。</summary>
    public double MinItemWidth
    {
        get => _minItemWidth;
        set
        {
            _minItemWidth = Math.Max(1, value);
            InvalidateMeasure();
        }
    }

    /// <summary>子どうしの間隔（px）です。指定しなければ幅クラスのトークン（Compact 12、ほか 16）。</summary>
    public double Spacing
    {
        get => double.IsNaN(_spacing) ? Space.Gap(_widthClass) : _spacing;
        set
        {
            _spacing = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Compact のときに最小の幅へ掛ける倍率です（既定 0.85。1 なら縮めない）。</summary>
    public double CompactScale { get; set; } = 0.85;

    /// <summary>
    /// 幅の変化で並びが変わるとき、子を滑らかに移します（GPU の RepositionThemeTransition）。
    /// </summary>
    public bool AnimatesReflow
    {
        get => ChildrenTransitions is { Count: > 0 };
        set => ChildrenTransitions = value && Motion.IsEnabled ? [new RepositionThemeTransition()] : null;
    }

    /// <summary>
    /// 幅クラスごとの列の上限を決めます。
    /// </summary>
    /// <remarks>責務: Compact / Medium / Expanded / Wide の列の上限を覚えます。0 は上限なし。</remarks>
    /// <param name="compact">Compact の上限。</param>
    /// <param name="medium">Medium の上限。</param>
    /// <param name="expanded">Expanded の上限。</param>
    /// <param name="wide">Wide の上限。</param>
    public void SetMaxColumns(int compact, int medium, int expanded, int wide)
    {
        _maxColumns[0] = compact;
        _maxColumns[1] = medium;
        _maxColumns[2] = expanded;
        _maxColumns[3] = wide;
        InvalidateMeasure();
    }

    /// <summary>
    /// 子の大きさを測ります。
    /// </summary>
    /// <remarks>責務: 使える幅から列の数を決め、行ごとの高さを合計します。</remarks>
    /// <param name="availableSize">使える大きさ。</param>
    /// <returns>必要な大きさ。</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? EffectiveMinItemWidth : availableSize.Width;
        var columns = ColumnCount(width);
        var itemWidth = ItemWidth(width, columns);
        var spacing = Spacing;
        double height = 0;
        double rowHeight = 0;
        var visibleIndex = 0;
        var visibleCount = VisibleCount();
        foreach (var child in Children)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (visibleIndex % columns == columns - 1 || visibleIndex == visibleCount - 1)
            {
                height += rowHeight + (visibleIndex == visibleCount - 1 ? 0 : spacing);
                rowHeight = 0;
            }

            visibleIndex++;
        }

        return new Size(width, height);
    }

    /// <summary>
    /// 子を並べます。
    /// </summary>
    /// <remarks>責務: 表示されている子を左から右、上から下へ、同じ幅の列に置きます。</remarks>
    /// <param name="finalSize">最終の大きさ。</param>
    /// <returns>使った大きさ。</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnCount(finalSize.Width);
        var itemWidth = ItemWidth(finalSize.Width, columns);
        var spacing = Spacing;
        var visible = new UIElement[VisibleCount()];
        var count = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                visible[count++] = child;
            }
        }

        double y = 0;
        for (var start = 0; start < count; start += columns)
        {
            var end = Math.Min(start + columns, count);
            double rowHeight = 0;
            for (var index = start; index < end; index++)
            {
                rowHeight = Math.Max(rowHeight, visible[index].DesiredSize.Height);
            }

            for (var index = start; index < end; index++)
            {
                var x = (index - start) * (itemWidth + spacing);
                visible[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }

            y += rowHeight + spacing;
        }

        return finalSize;
    }

    /// <summary>今の幅クラスでの列の最小の幅です。</summary>
    private double EffectiveMinItemWidth => _widthClass == WidthClass.Compact ? MinItemWidth * CompactScale : MinItemWidth;

    /// <summary>表示されている子の数を返します。</summary>
    /// <returns>数。</returns>
    private int VisibleCount()
    {
        var count = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>幅に入る列の数を返します。</summary>
    /// <remarks>責務: 最小の幅と間隔と幅クラスの上限から、1 以上の列の数を決めます。</remarks>
    /// <param name="width">使える幅。</param>
    /// <returns>列の数。</returns>
    private int ColumnCount(double width)
    {
        var spacing = Spacing;
        var columns = Math.Max(1, (int)((width + spacing) / (EffectiveMinItemWidth + spacing)));
        var max = _maxColumns[(int)_widthClass];
        return max > 0 ? Math.Min(columns, max) : columns;
    }

    /// <summary>1 列の幅を返します。</summary>
    /// <remarks>責務: 使える幅を列の数で等分します。</remarks>
    /// <param name="width">使える幅。</param>
    /// <param name="columns">列の数。</param>
    /// <returns>1 列の幅。</returns>
    private double ItemWidth(double width, int columns) => Math.Max(0, (width - Spacing * (columns - 1)) / columns);
}
