using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// 1 つの PID の値を時間の順に結んだ折れ線のグラフです（iOS 版の Swift Charts の <c>LineMark</c>）。
/// </summary>
/// <remarks>グラフの部品を使わず、Polyline と目盛りの文字だけで描きます。点が多いときは間引いて描きます。</remarks>
public sealed partial class LineChartView : Grid
{
    /// <summary>描く点の最大の数です（これを超えたら間引く）。</summary>
    private const int MaximumDrawnPoints = 1_500;

    /// <summary>左の目盛りの幅です。</summary>
    private const double AxisWidth = 64;

    /// <summary>下の目盛りの高さです。</summary>
    private const double AxisHeight = 22;

    /// <summary>描く点です。</summary>
    private readonly IReadOnlyList<SessionPIDSeries.Point> _points;

    /// <summary>縦軸の単位です。</summary>
    private readonly string _unit;

    /// <summary>描く面です。</summary>
    private readonly Canvas _canvas = new();

    /// <summary>点と単位を指定して作ります。</summary>
    /// <param name="points">時刻と値の点。</param>
    /// <param name="unit">縦軸の単位。</param>
    public LineChartView(IReadOnlyList<SessionPIDSeries.Point> points, string unit)
    {
        _points = points;
        _unit = unit;
        MinHeight = 300;
        Children.Add(_canvas);
        SizeChanged += (_, args) => Draw(args.NewSize);
    }

    /// <summary>
    /// 大きさに合わせてグラフを描き直します。
    /// </summary>
    /// <remarks>責務: 値の範囲から目盛りと折れ線の位置を計算して描きます。</remarks>
    /// <param name="size">大きさ。</param>
    private void Draw(Size size)
    {
        _canvas.Children.Clear();
        if (_points.Count == 0 || size.Width <= AxisWidth + 10 || size.Height <= AxisHeight + 10)
        {
            return;
        }

        var plotWidth = size.Width - AxisWidth;
        var plotHeight = size.Height - AxisHeight;
        var start = _points[0].ObservedAt;
        var end = _points[^1].ObservedAt;
        var span = Math.Max((end - start).TotalSeconds, 1);
        var minimum = _points.Min(point => point.Value);
        var maximum = _points.Max(point => point.Value);
        if (maximum - minimum < 1e-9)
        {
            minimum -= 1;
            maximum += 1;
        }

        var gridBrush = DesignTokens.Brush("RevTorqueChartGridBrush");
        for (var step = 0; step <= 4; step++)
        {
            var y = plotHeight * step / 4;
            _canvas.Children.Add(new Line { X1 = AxisWidth, X2 = size.Width, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
            var label = Typography.Text(NumberText.Decimal(maximum - (maximum - minimum) * step / 4, 1), "MonospaceCaptionTextStyle", wraps: false);
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Math.Max(0, y - 9));
            _canvas.Children.Add(label);
        }

        var unitLabel = Typography.Text(_unit, "MonospaceCaptionTextStyle", DesignTokens.Foreground(Tone.Accent), wraps: false);
        Canvas.SetLeft(unitLabel, AxisWidth + 6);
        Canvas.SetTop(unitLabel, 2);
        _canvas.Children.Add(unitLabel);

        foreach (var (time, alignRight) in new[] { (start, false), (end, true) })
        {
            var label = Typography.Text(time.ToLocalTime().ToString("T", System.Globalization.CultureInfo.CurrentCulture), "MonospaceCaptionTextStyle", wraps: false);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, alignRight ? size.Width - label.DesiredSize.Width : AxisWidth);
            Canvas.SetTop(label, plotHeight + 4);
            _canvas.Children.Add(label);
        }

        var stride = Math.Max(1, _points.Count / MaximumDrawnPoints);
        var line = new Polyline
        {
            Stroke = DesignTokens.Foreground(Tone.Accent),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
        };
        for (var index = 0; index < _points.Count; index += stride)
        {
            line.Points.Add(Position(_points[index]));
        }

        if ((_points.Count - 1) % stride != 0)
        {
            line.Points.Add(Position(_points[^1]));
        }

        _canvas.Children.Add(line);

        Point Position(SessionPIDSeries.Point point) => new(
            AxisWidth + plotWidth * (point.ObservedAt - start).TotalSeconds / span,
            plotHeight * (1 - (point.Value - minimum) / (maximum - minimum)));
    }
}
