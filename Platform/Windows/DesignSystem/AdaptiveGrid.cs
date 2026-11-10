using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 幅に合わせて列の数を決め、子を同じ幅の列へ順に並べるパネルです（SwiftUI の <c>LazyVGrid(columns: [.adaptive(minimum:)])</c>）。
/// </summary>
/// <remarks>同じ行の子は、その行でいちばん高い子の高さにそろえます。</remarks>
public sealed partial class AdaptiveGrid : Panel
{
    /// <summary>列の最小の幅（px）です。</summary>
    public double MinItemWidth { get; set; } = 280;

    /// <summary>子どうしの間隔（px）です。</summary>
    public double Spacing { get; set; } = 14;

    /// <summary>
    /// 子の大きさを測ります。
    /// </summary>
    /// <remarks>責務: 使える幅から列の数を決め、行ごとの高さを合計します。</remarks>
    /// <param name="availableSize">使える大きさ。</param>
    /// <returns>必要な大きさ。</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        var columns = ColumnCount(width);
        var itemWidth = ItemWidth(width, columns);
        double height = 0;
        double rowHeight = 0;
        for (var index = 0; index < Children.Count; index++)
        {
            Children[index].Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
            if (index % columns == columns - 1 || index == Children.Count - 1)
            {
                height += rowHeight + (index == Children.Count - 1 ? 0 : Spacing);
                rowHeight = 0;
            }
        }

        return new Size(width, height);
    }

    /// <summary>
    /// 子を並べます。
    /// </summary>
    /// <remarks>責務: 子を左から右、上から下へ、同じ幅の列に置きます。</remarks>
    /// <param name="finalSize">最終の大きさ。</param>
    /// <returns>使った大きさ。</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnCount(finalSize.Width);
        var itemWidth = ItemWidth(finalSize.Width, columns);
        double y = 0;
        for (var start = 0; start < Children.Count; start += columns)
        {
            double rowHeight = 0;
            for (var index = start; index < Math.Min(start + columns, Children.Count); index++)
            {
                rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
            }

            for (var index = start; index < Math.Min(start + columns, Children.Count); index++)
            {
                var x = (index - start) * (itemWidth + Spacing);
                Children[index].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }

            y += rowHeight + Spacing;
        }

        return finalSize;
    }

    /// <summary>幅に入る列の数を返します。</summary>
    /// <remarks>責務: 最小の幅と間隔から、1 以上の列の数を決めます。</remarks>
    /// <param name="width">使える幅。</param>
    /// <returns>列の数。</returns>
    private int ColumnCount(double width) => Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));

    /// <summary>1 列の幅を返します。</summary>
    /// <remarks>責務: 使える幅を列の数で等分します。</remarks>
    /// <param name="width">使える幅。</param>
    /// <param name="columns">列の数。</param>
    /// <returns>1 列の幅。</returns>
    private double ItemWidth(double width, int columns) => Math.Max(0, (width - Spacing * (columns - 1)) / columns);
}
