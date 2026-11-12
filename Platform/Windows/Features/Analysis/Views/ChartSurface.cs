using System;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// グラフの共通の土台です。細い目盛り線・縦の目盛りの文字・単位・下の目盛りの文字を、1 回だけ作って持ちます。
/// </summary>
/// <remarks>
/// サイズが変わっても要素は作り直さず、位置だけを差し替えます。描き直しは <see cref="UiUpdateThrottle"/> で 1 フレームに 1 回へまとめます。
/// 系列（折れ線・点）は派生クラスが <see cref="DrawSeries"/> で描きます。形は 1 つの Path のデータ文字列にして、図形の数を増やしません。
/// </remarks>
public abstract partial class ChartSurface : Grid
{
    /// <summary>横の目盛り線の数（上端から下端まで）です。</summary>
    private const int GridLineCount = 5;

    /// <summary>左の目盛りの幅です。</summary>
    private const double AxisWidth = 56;

    /// <summary>下の目盛りの高さです。</summary>
    private const double AxisHeight = 22;

    /// <summary>上の余白です（最上段の目盛りの文字がはみ出さないように）。</summary>
    private const double PlotTopPadding = 8;

    /// <summary>右の余白です。</summary>
    private const double PlotRightPadding = 6;

    /// <summary>下の中央の目盛りを出す最小の描画幅です。</summary>
    private const double MiddleLabelMinimumWidth = 300;

    /// <summary>下の目盛りの文字の幅です。</summary>
    private const double BottomLabelWidth = 96;

    /// <summary>描く面です。</summary>
    private readonly Canvas _canvas = new();

    /// <summary>横の目盛り線です。</summary>
    private readonly Line[] _gridLines = new Line[GridLineCount];

    /// <summary>縦の目盛りの文字です。</summary>
    private readonly TextBlock[] _yLabels = new TextBlock[GridLineCount];

    /// <summary>単位の文字です。</summary>
    private readonly TextBlock _unitLabel;

    /// <summary>下の左・中央・右の目盛りの文字です。</summary>
    private readonly TextBlock _leftLabel;

    /// <summary>下の中央の目盛りの文字です。</summary>
    private readonly TextBlock _middleLabel;

    /// <summary>下の右の目盛りの文字です。</summary>
    private readonly TextBlock _rightLabel;

    /// <summary>描き直しを 1 フレームに 1 回へまとめる仕組みです。</summary>
    private readonly UiUpdateThrottle _redraw;

    /// <summary>今の大きさです。</summary>
    private Size _size;

    /// <summary>最後に描いた大きさです（同じ大きさなら描き直さない）。</summary>
    private Size _drawnSize;

    /// <summary>データが入っているかどうかです。</summary>
    private bool _hasData;

    /// <summary>データが変わって描き直しが要るかどうかです。</summary>
    private bool _dataDirty;

    /// <summary>下の中央の目盛りを使うかどうかです。</summary>
    private bool _hasMiddleLabel;

    /// <summary>単位の色の役割を指定して作ります。</summary>
    /// <param name="unitTone">単位の文字の色の役割。</param>
    protected ChartSurface(Tone unitTone)
    {
        Children.Add(_canvas);
        var gridBrush = DesignTokens.Brush("RevTorqueChartGridBrush");
        for (var index = 0; index < GridLineCount; index++)
        {
            _gridLines[index] = new Line { Stroke = gridBrush, StrokeThickness = 1, IsHitTestVisible = false };
            _canvas.Children.Add(_gridLines[index]);
        }

        for (var index = 0; index < GridLineCount; index++)
        {
            _yLabels[index] = AxisLabel(AxisWidth - Space.S, TextAlignment.Right);
            _canvas.Children.Add(_yLabels[index]);
        }

        Surface = _canvas;
        _unitLabel = Typography.Text(string.Empty, TextStyles.Monospace, DesignTokens.Foreground(unitTone), wraps: false);
        _leftLabel = AxisLabel(BottomLabelWidth, TextAlignment.Left);
        _middleLabel = AxisLabel(BottomLabelWidth, TextAlignment.Center);
        _rightLabel = AxisLabel(BottomLabelWidth, TextAlignment.Right);
        _canvas.Children.Add(_unitLabel);
        _canvas.Children.Add(_leftLabel);
        _canvas.Children.Add(_middleLabel);
        _canvas.Children.Add(_rightLabel);
        _redraw = new UiUpdateThrottle(this, Redraw);
        SizeChanged += (_, args) =>
        {
            _size = args.NewSize;
            _redraw.Request();
        };
    }

    /// <summary>派生クラスが系列の図形を足す面です（目盛りの上に重なる）。</summary>
    protected Canvas Surface { get; }

    /// <summary>
    /// 目盛りと単位を差し替えます。
    /// </summary>
    /// <remarks>責務: 縦の範囲から目盛りの文字を作り、データありとして描き直しを頼みます。</remarks>
    /// <param name="yMinimum">縦軸の最小。</param>
    /// <param name="yMaximum">縦軸の最大。</param>
    /// <param name="unit">縦軸の単位。</param>
    /// <param name="left">下の左の目盛り。</param>
    /// <param name="middle">下の中央の目盛り。なければ null。</param>
    /// <param name="right">下の右の目盛り。</param>
    protected void SetAxes(double yMinimum, double yMaximum, string unit, string left, string? middle, string right)
    {
        for (var index = 0; index < GridLineCount; index++)
        {
            Retained.SetText(_yLabels[index], NumberText.Decimal(yMaximum - (yMaximum - yMinimum) * index / (GridLineCount - 1), 1));
        }

        Retained.SetText(_unitLabel, unit);
        Retained.SetText(_leftLabel, left);
        Retained.SetText(_middleLabel, middle);
        Retained.SetText(_rightLabel, right);
        _hasMiddleLabel = middle is not null;
        _hasData = true;
        _dataDirty = true;
        _redraw.Request();
    }

    /// <summary>
    /// データなしにします。
    /// </summary>
    /// <remarks>責務: 面を隠します（要素は残す）。</remarks>
    protected void ClearAxes()
    {
        _hasData = false;
        _dataDirty = true;
        _redraw.Request();
    }

    /// <summary>
    /// 系列を描きます。
    /// </summary>
    /// <remarks>責務: 派生クラスが、描く範囲に合わせて系列の図形の位置を差し替えます。</remarks>
    /// <param name="left">描く範囲の左端。</param>
    /// <param name="top">描く範囲の上端。</param>
    /// <param name="width">描く範囲の幅。</param>
    /// <param name="height">描く範囲の高さ。</param>
    protected abstract void DrawSeries(double left, double top, double width, double height);

    /// <summary>データがなくなったときに、系列の図形を隠します。</summary>
    protected abstract void HideSeries();

    /// <summary>
    /// 座標の並びを Path のデータ文字列の続きに足します。
    /// </summary>
    /// <remarks>責務: 「M x,y L x,y …」の形にします（小数 1 桁、カルチャーに依らない）。</remarks>
    /// <param name="builder">文字列の組み立て先。</param>
    /// <param name="x">X 座標。</param>
    /// <param name="y">Y 座標。</param>
    /// <param name="isFirst">最初の点なら true（M で始める）。</param>
    protected static void AppendPoint(StringBuilder builder, double x, double y, bool isFirst)
    {
        builder.Append(isFirst ? 'M' : 'L')
            .Append(x.ToString("0.#", CultureInfo.InvariantCulture))
            .Append(',')
            .Append(y.ToString("0.#", CultureInfo.InvariantCulture))
            .Append(' ');
    }

    /// <summary>
    /// Path のデータ文字列を図形にします。
    /// </summary>
    /// <remarks>責務: 1 回の呼び出しで図形全体を差し替えます（点ごとの要素を作らない）。</remarks>
    /// <param name="data">データ文字列。</param>
    /// <returns>図形。</returns>
    protected static Geometry ParseGeometry(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    /// <summary>目盛りの文字を作ります。</summary>
    /// <param name="width">文字の幅（右寄せ・中央寄せのため固定）。</param>
    /// <param name="alignment">文字の寄せ。</param>
    /// <returns>文字。</returns>
    private static TextBlock AxisLabel(double width, TextAlignment alignment)
    {
        var label = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);
        label.Width = width;
        label.TextAlignment = alignment;
        label.IsHitTestVisible = false;
        return label;
    }

    /// <summary>
    /// 目盛りと系列の位置を、今の大きさに合わせます。
    /// </summary>
    /// <remarks>責務: 大きさもデータも変わっていなければ何もしません。要素は作り直さず、位置と文字だけを差し替えます。</remarks>
    private void Redraw()
    {
        if (!_dataDirty && _drawnSize == _size)
        {
            return;
        }

        _dataDirty = false;
        _drawnSize = _size;
        var visible = _hasData && _size.Width > AxisWidth + 40 && _size.Height > AxisHeight + PlotTopPadding + 40;
        Retained.SetVisible(_canvas, visible);
        if (!visible)
        {
            HideSeries();
            return;
        }

        var plotLeft = AxisWidth;
        var plotTop = PlotTopPadding;
        var plotWidth = _size.Width - AxisWidth - PlotRightPadding;
        var plotHeight = _size.Height - AxisHeight - PlotTopPadding;
        for (var index = 0; index < GridLineCount; index++)
        {
            var y = plotTop + plotHeight * index / (GridLineCount - 1);
            var line = _gridLines[index];
            line.X1 = plotLeft;
            line.X2 = plotLeft + plotWidth;
            line.Y1 = y;
            line.Y2 = y;
            Canvas.SetLeft(_yLabels[index], 0);
            Canvas.SetTop(_yLabels[index], Math.Max(0, y - 9));
        }

        Canvas.SetLeft(_unitLabel, plotLeft + Space.S);
        Canvas.SetTop(_unitLabel, plotTop + Space.XS);
        var bottom = plotTop + plotHeight + Space.XS;
        Canvas.SetLeft(_leftLabel, plotLeft);
        Canvas.SetTop(_leftLabel, bottom);
        Canvas.SetLeft(_rightLabel, plotLeft + plotWidth - BottomLabelWidth);
        Canvas.SetTop(_rightLabel, bottom);
        var showsMiddle = _hasMiddleLabel && plotWidth >= MiddleLabelMinimumWidth;
        Retained.SetVisible(_middleLabel, showsMiddle);
        if (showsMiddle)
        {
            Canvas.SetLeft(_middleLabel, plotLeft + plotWidth / 2 - BottomLabelWidth / 2);
            Canvas.SetTop(_middleLabel, bottom);
        }

        DrawSeries(plotLeft, plotTop, plotWidth, plotHeight);
    }
}
