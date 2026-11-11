using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LiveTelemetry.Views;

/// <summary>
/// 分類のチップを並べる板です。折り返さないときは 1 行（横スクロールの中に置く）、折り返すときは幅に収めて次の行へ送ります。
/// </summary>
/// <remarks>幅クラスが変わったとき（<see cref="Wraps"/> の切り替え）にだけ並べ直します。子は作り直しません。</remarks>
internal sealed class ChipFlowPanel : Panel
{
    /// <summary>折り返すかどうかです。</summary>
    private bool _wraps;

    /// <summary>折り返すなら true、1 行に並べるなら false。</summary>
    public bool Wraps
    {
        get => _wraps;
        set
        {
            if (_wraps != value)
            {
                _wraps = value;
                InvalidateMeasure();
            }
        }
    }

    /// <summary>子の大きさを測ります。</summary>
    /// <remarks>責務: 折り返す幅（折り返さないなら無限）に合わせて並べたときの大きさを返します。</remarks>
    /// <param name="availableSize">使える大きさ。</param>
    /// <returns>必要な大きさ。</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        var limit = _wraps ? availableSize.Width : double.PositiveInfinity;
        double x = 0;
        double y = 0;
        double rowHeight = 0;
        double widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > limit)
            {
                y += rowHeight + Space.S;
                x = 0;
                rowHeight = 0;
            }

            x += size.Width + Space.S;
            widest = Math.Max(widest, x - Space.S);
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return new Size(widest, y + rowHeight);
    }

    /// <summary>子を並べます。</summary>
    /// <remarks>責務: 測ったときと同じ規則で、子を左から順に置きます。</remarks>
    /// <param name="finalSize">置ける大きさ。</param>
    /// <returns>使った大きさ。</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var limit = _wraps ? finalSize.Width : double.PositiveInfinity;
        double x = 0;
        double y = 0;
        double rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > limit)
            {
                y += rowHeight + Space.S;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Space.S;
            rowHeight = Math.Max(rowHeight, size.Height);
        }

        return finalSize;
    }
}
