using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// 2 つの PID の値の組を点で打った散布図です（iOS 版の Swift Charts の <c>PointMark</c>）。
/// </summary>
/// <remarks>グラフの部品を使わず、1 つの Path に小さな四角を集めて描きます。点が多いときは間引いて描きます。</remarks>
public sealed partial class ScatterChartView : Grid
{
    /// <summary>描く点の最大の数です（これを超えたら間引く）。</summary>
    private const int MaximumDrawnPoints = 2_500;

    /// <summary>左の目盛りの幅です。</summary>
    private const double AxisWidth = 64;

    /// <summary>下の目盛りの高さです。</summary>
    private const double AxisHeight = 22;

    /// <summary>描く点です。</summary>
    private readonly IReadOnlyList<SessionPIDRelationship.Point> _points;

    /// <summary>横軸の単位です。</summary>
    private readonly string _xUnit;

    /// <summary>縦軸の単位です。</summary>
    private readonly string _yUnit;

    /// <summary>描く面です。</summary>
    private readonly Canvas _canvas = new();

    /// <summary>点と単位を指定して作ります。</summary>
    /// <param name="points">値の組の点。</param>
    /// <param name="xUnit">横軸の単位。</param>
    /// <param name="yUnit">縦軸の単位。</param>
    public ScatterChartView(IReadOnlyList<SessionPIDRelationship.Point> points, string xUnit, string yUnit)
    {
        _points = points;
        _xUnit = xUnit;
        _yUnit = yUnit;
        MinHeight = 300;
        Children.Add(_canvas);
        SizeChanged += (_, args) => Draw(args.NewSize);
    }

    /// <summary>
    /// 大きさに合わせて散布図を描き直します。
    /// </summary>
    /// <remarks>責務: 2 つの値の範囲から目盛りと点の位置を計算して描きます。</remarks>
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
        var (minX, maxX) = Range(_points.Select(point => point.X));
        var (minY, maxY) = Range(_points.Select(point => point.Y));
        var gridBrush = DesignTokens.Brush("RevTorqueChartGridBrush");
        for (var step = 0; step <= 4; step++)
        {
            var y = plotHeight * step / 4;
            _canvas.Children.Add(new Line { X1 = AxisWidth, X2 = size.Width, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
            var label = Typography.Text(NumberText.Decimal(maxY - (maxY - minY) * step / 4, 1), "MonospaceCaptionTextStyle", wraps: false);
            Canvas.SetTop(label, Math.Max(0, y - 9));
            _canvas.Children.Add(label);
        }

        foreach (var (value, alignRight) in new[] { (minX, false), (maxX, true) })
        {
            var label = Typography.Text($"{NumberText.Decimal(value, 1)} {_xUnit}", "MonospaceCaptionTextStyle", wraps: false);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, alignRight ? size.Width - label.DesiredSize.Width : AxisWidth);
            Canvas.SetTop(label, plotHeight + 4);
            _canvas.Children.Add(label);
        }

        var unitLabel = Typography.Text(_yUnit, "MonospaceCaptionTextStyle", DesignTokens.Foreground(Tone.Indigo), wraps: false);
        Canvas.SetLeft(unitLabel, AxisWidth + 6);
        Canvas.SetTop(unitLabel, 2);
        _canvas.Children.Add(unitLabel);

        var geometry = new GeometryGroup();
        var stride = Math.Max(1, _points.Count / MaximumDrawnPoints);
        for (var index = 0; index < _points.Count; index += stride)
        {
            var point = _points[index];
            var x = AxisWidth + plotWidth * (point.X - minX) / (maxX - minX);
            var y = plotHeight * (1 - (point.Y - minY) / (maxY - minY));
            geometry.Children.Add(new RectangleGeometry { Rect = new Rect(x - 2.5, y - 2.5, 5, 5) });
        }

        var dots = new Path { Data = geometry, Fill = DesignTokens.Foreground(Tone.Indigo), Opacity = 0.72 };
        _canvas.Children.Add(dots);
    }

    /// <summary>値の範囲を返します。</summary>
    /// <remarks>責務: 最小と最大を求め、幅が 0 なら前後に広げます。</remarks>
    /// <param name="values">値。</param>
    /// <returns>最小と最大。</returns>
    private static (double Minimum, double Maximum) Range(IEnumerable<double> values)
    {
        var list = values.ToList();
        var minimum = list.Min();
        var maximum = list.Max();
        return maximum - minimum < 1e-9 ? (minimum - 1, maximum + 1) : (minimum, maximum);
    }
}
