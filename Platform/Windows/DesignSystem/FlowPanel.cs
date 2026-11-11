using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 小さな部品（チップ・状態の印）を左から並べ、幅に収まらなければ次の行へ送る板です。折り返さない設定では 1 行に並べます。
/// </summary>
/// <remarks>既定は折り返す。子は作り直さず、並べ方だけを変えます。</remarks>
public sealed class FlowPanel : Panel
{
    /// <summary>折り返すかどうかです。</summary>
    private bool _wraps = true;

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
