using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// 1 つの PID の値を時間の順に結んだ折れ線のグラフです（iOS 版の Swift Charts の <c>LineMark</c>）。
/// </summary>
/// <remarks>
/// グラフの部品を使わず、細い目盛りと 1 本の Path だけで描きます。シアンの線と薄い面、最大値だけオレンジの印。
/// 点は <see cref="SetData"/> で 1 回だけ間引き（山と谷を残す）、サイズが変わったときは位置の計算だけをやり直します。
/// </remarks>
public sealed partial class LineChartView : ChartSurface
{
    /// <summary>間引きの区間の数です（1 区間から最大・最小の 2 点を残すので、描く点は最大でこの 2 倍）。</summary>
    private const int MaximumBuckets = 800;

    /// <summary>面を描くとき、折れ線の点から何点おきに使うかの目標の点数です。</summary>
    private const int AreaPointBudget = 300;

    /// <summary>薄い面です。</summary>
    private readonly Path _area = new()
    {
        Fill = DesignTokens.Brush("RevTorqueAccentBrush"),
        Opacity = 0.10,
        IsHitTestVisible = false,
    };

    /// <summary>折れ線です。</summary>
    private readonly Path _line = new()
    {
        Stroke = DesignTokens.Brush("RevTorqueAccentBrush"),
        StrokeThickness = 1.5,
        StrokeLineJoin = PenLineJoin.Round,
        IsHitTestVisible = false,
    };

    /// <summary>最大値の印です。</summary>
    private readonly Ellipse _peak = new()
    {
        Width = 8,
        Height = 8,
        Fill = DesignTokens.Brush("RevTorqueCautionBrush"),
    };

    /// <summary>間引いた点（時刻は始点からの秒）です。</summary>
    private (double Seconds, double Value)[] _plot = [];

    /// <summary>始点から終点までの秒です。</summary>
    private double _span = 1;

    /// <summary>縦軸の最小です。</summary>
    private double _minimum;

    /// <summary>縦軸の最大です。</summary>
    private double _maximum = 1;

    /// <summary>最大値の点（始点からの秒）です。</summary>
    private (double Seconds, double Value) _peakPoint;

    /// <summary>空のグラフを作ります。</summary>
    public LineChartView()
        : base(Tone.Accent)
    {
        MinHeight = 200;
        Surface.Children.Add(_area);
        Surface.Children.Add(_line);
        Surface.Children.Add(_peak);
    }

    /// <summary>点と単位を指定して作ります。</summary>
    /// <param name="points">時刻と値の点。</param>
    /// <param name="unit">縦軸の単位。</param>
    public LineChartView(IReadOnlyList<SessionPIDSeries.Point> points, string unit)
        : this() => SetData(points, unit);

    /// <summary>
    /// 描く点を差し替えます。
    /// </summary>
    /// <remarks>責務: 値の範囲を 1 回だけ求め、点を山と谷を残して間引き、目盛りを合わせて描き直しを頼みます。要素は作り直しません。</remarks>
    /// <param name="points">時刻と値の点。</param>
    /// <param name="unit">縦軸の単位。</param>
    public void SetData(IReadOnlyList<SessionPIDSeries.Point> points, string unit)
    {
        if (points.Count == 0)
        {
            _plot = [];
            ClearAxes();
            return;
        }

        var start = points[0].ObservedAt;
        var end = points[^1].ObservedAt;
        _span = Math.Max((end - start).TotalSeconds, 1);
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        var peakIndex = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var value = points[index].Value;
            if (value < minimum)
            {
                minimum = value;
            }

            if (value > maximum)
            {
                maximum = value;
                peakIndex = index;
            }
        }

        _peakPoint = ((points[peakIndex].ObservedAt - start).TotalSeconds, maximum);
        if (maximum - minimum < 1e-9)
        {
            minimum -= 1;
            maximum += 1;
        }

        _minimum = minimum;
        _maximum = maximum;
        _plot = Decimate(points, start);
        ToolTipService.SetToolTip(_peak, NumberText.Measurement(_peakPoint.Value, unit));
        SetAxes(
            minimum,
            maximum,
            unit,
            start.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture),
            (start + (end - start) / 2).ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture),
            end.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture));
    }

    /// <inheritdoc />
    protected override void DrawSeries(double left, double top, double width, double height)
    {
        if (_plot.Length == 0)
        {
            HideSeries();
            return;
        }

        Retained.SetVisible(_line, true);
        Retained.SetVisible(_area, true);
        Retained.SetVisible(_peak, true);
        var range = _maximum - _minimum;
        double X(double seconds) => left + width * seconds / _span;
        double Y(double value) => top + height * (1 - (value - _minimum) / range);

        var builder = new StringBuilder(_plot.Length * 14);
        for (var index = 0; index < _plot.Length; index++)
        {
            AppendPoint(builder, X(_plot[index].Seconds), Y(_plot[index].Value), index == 0);
        }

        _line.Data = ParseGeometry(builder.ToString());

        // 面は粗くてよい（薄い塗りなので山の細部は見えない）。
        builder.Clear();
        var step = Math.Max(1, _plot.Length / AreaPointBudget);
        var first = true;
        var lastX = left;
        for (var index = 0; index < _plot.Length; index += step)
        {
            lastX = X(_plot[index].Seconds);
            AppendPoint(builder, lastX, Y(_plot[index].Value), first);
            first = false;
        }

        var bottom = top + height;
        AppendPoint(builder, lastX, bottom, false);
        AppendPoint(builder, left, bottom, false);
        builder.Append('Z');
        _area.Data = ParseGeometry(builder.ToString());

        Canvas.SetLeft(_peak, X(_peakPoint.Seconds) - _peak.Width / 2);
        Canvas.SetTop(_peak, Y(_peakPoint.Value) - _peak.Height / 2);
    }

    /// <inheritdoc />
    protected override void HideSeries()
    {
        Retained.SetVisible(_line, false);
        Retained.SetVisible(_area, false);
        Retained.SetVisible(_peak, false);
    }

    /// <summary>
    /// 点を間引きます。
    /// </summary>
    /// <remarks>責務: 区間ごとに最小と最大の 2 点だけを時間の順に残し、山と谷を落とさずに点の数を抑えます。</remarks>
    /// <param name="points">元の点。</param>
    /// <param name="start">始点の時刻。</param>
    /// <returns>間引いた点。</returns>
    private static (double Seconds, double Value)[] Decimate(IReadOnlyList<SessionPIDSeries.Point> points, DateTimeOffset start)
    {
        if (points.Count <= MaximumBuckets * 2)
        {
            var all = new (double, double)[points.Count];
            for (var index = 0; index < all.Length; index++)
            {
                all[index] = ((points[index].ObservedAt - start).TotalSeconds, points[index].Value);
            }

            return all;
        }

        var bucketSize = (points.Count + MaximumBuckets - 1) / MaximumBuckets;
        var kept = new List<(double, double)>(MaximumBuckets * 2 + 2);
        for (var from = 0; from < points.Count; from += bucketSize)
        {
            var to = Math.Min(from + bucketSize, points.Count);
            int low = from, high = from;
            for (var index = from + 1; index < to; index++)
            {
                if (points[index].Value < points[low].Value)
                {
                    low = index;
                }

                if (points[index].Value > points[high].Value)
                {
                    high = index;
                }
            }

            foreach (var index in low == high ? new[] { low } : low < high ? new[] { low, high } : new[] { high, low })
            {
                kept.Add(((points[index].ObservedAt - start).TotalSeconds, points[index].Value));
            }
        }

        return [.. kept];
    }
}
