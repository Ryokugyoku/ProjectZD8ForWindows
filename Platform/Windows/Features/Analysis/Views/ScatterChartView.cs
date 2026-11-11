using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// 2 つの PID の値の組を点で打った散布図です（iOS 版の Swift Charts の <c>PointMark</c>）。
/// </summary>
/// <remarks>
/// グラフの部品を使わず、細い目盛りと 1 本の Path（丸い線端の点）だけで描きます。点は <see cref="SetData"/> で 1 回だけ間引き、
/// サイズが変わったときは位置の計算だけをやり直します。
/// </remarks>
public sealed partial class ScatterChartView : ChartSurface
{
    /// <summary>描く点の最大の数です（これを超えたら等間隔に間引く）。</summary>
    private const int MaximumDrawnPoints = 1_500;

    /// <summary>点の直径です。</summary>
    private const double DotSize = 5;

    /// <summary>点の群れです。</summary>
    private readonly Path _dots = new()
    {
        Stroke = DesignTokens.Brush("RevTorqueAccentBrush"),
        StrokeThickness = DotSize,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Opacity = 0.7,
        IsHitTestVisible = false,
    };

    /// <summary>間引いた点です。</summary>
    private (double X, double Y)[] _plot = [];

    /// <summary>横軸の最小です。</summary>
    private double _minX;

    /// <summary>横軸の最大です。</summary>
    private double _maxX = 1;

    /// <summary>縦軸の最小です。</summary>
    private double _minY;

    /// <summary>縦軸の最大です。</summary>
    private double _maxY = 1;

    /// <summary>空のグラフを作ります。</summary>
    public ScatterChartView()
        : base(Tone.Accent)
    {
        MinHeight = 200;
        Surface.Children.Add(_dots);
    }

    /// <summary>点と単位を指定して作ります。</summary>
    /// <param name="points">値の組の点。</param>
    /// <param name="xUnit">横軸の単位。</param>
    /// <param name="yUnit">縦軸の単位。</param>
    public ScatterChartView(IReadOnlyList<SessionPIDRelationship.Point> points, string xUnit, string yUnit)
        : this() => SetData(points, xUnit, yUnit);

    /// <summary>
    /// 描く点を差し替えます。
    /// </summary>
    /// <remarks>責務: 2 つの値の範囲を 1 回だけ求め、点を間引き、目盛りを合わせて描き直しを頼みます。要素は作り直しません。</remarks>
    /// <param name="points">値の組の点。</param>
    /// <param name="xUnit">横軸の単位。</param>
    /// <param name="yUnit">縦軸の単位。</param>
    public void SetData(IReadOnlyList<SessionPIDRelationship.Point> points, string xUnit, string yUnit)
    {
        if (points.Count == 0)
        {
            _plot = [];
            ClearAxes();
            return;
        }

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        (_minX, _maxX) = maxX - minX < 1e-9 ? (minX - 1, maxX + 1) : (minX, maxX);
        (_minY, _maxY) = maxY - minY < 1e-9 ? (minY - 1, maxY + 1) : (minY, maxY);
        var stride = Math.Max(1, (points.Count + MaximumDrawnPoints - 1) / MaximumDrawnPoints);
        var plot = new List<(double, double)>(Math.Min(points.Count, MaximumDrawnPoints) + 1);
        for (var index = 0; index < points.Count; index += stride)
        {
            plot.Add((points[index].X, points[index].Y));
        }

        _plot = [.. plot];
        SetAxes(
            _minY,
            _maxY,
            yUnit,
            $"{NumberText.Decimal(_minX, 1)} {xUnit}",
            null,
            $"{NumberText.Decimal(_maxX, 1)} {xUnit}");
    }

    /// <inheritdoc />
    protected override void DrawSeries(double left, double top, double width, double height)
    {
        if (_plot.Length == 0)
        {
            HideSeries();
            return;
        }

        Retained.SetVisible(_dots, true);
        var rangeX = _maxX - _minX;
        var rangeY = _maxY - _minY;
        var builder = new StringBuilder(_plot.Length * 20);
        foreach (var (x, y) in _plot)
        {
            var px = left + width * (x - _minX) / rangeX;
            var py = top + height * (1 - (y - _minY) / rangeY);
            // 長さのほぼ 0 の線に丸い線端を付けて点にする。
            builder.Append('M')
                .Append(px.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',')
                .Append(py.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" l0.01,0 ");
        }

        _dots.Data = ParseGeometry(builder.ToString());
    }

    /// <inheritdoc />
    protected override void HideSeries() => Retained.SetVisible(_dots, false);
}
