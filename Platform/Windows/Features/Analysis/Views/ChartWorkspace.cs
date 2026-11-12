using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// 分析の「選ぶ面・グラフ・読み方の面」の 3 つを、幅クラスに合わせて並べる格子です。
/// </summary>
/// <remarks>
/// Expanded 以上：グラフを主役に左いっぱい、右の細い列に選ぶ面（上）と読み方（下）。
/// Compact・Medium：1 列で、選ぶ面（上）→ グラフ → 読み方（下）。PID の選択が狭い幅でもグラフの上にあって届く。
/// 面は作り直さず、行・列の割り当てだけを差し替えます（<see cref="ResponsiveLayout.Bind(FrameworkElement, System.Action{WidthClass})"/>）。
/// </remarks>
internal sealed partial class ChartWorkspace : Grid
{
    /// <summary>Expanded 以上のときの右の列の幅です。</summary>
    private const double SideColumnWidth = 340;

    /// <summary>グラフの高さの下限（Compact 以外）です。</summary>
    private const double ChartMinHeight = 320;

    /// <summary>グラフの高さの下限（Compact）です。</summary>
    private const double CompactChartMinHeight = 240;

    /// <summary>選ぶ面です。</summary>
    private readonly FrameworkElement _picker;

    /// <summary>グラフの面です。</summary>
    private readonly FrameworkElement _chartCard;

    /// <summary>読み方の面です。</summary>
    private readonly FrameworkElement _guide;

    /// <summary>グラフそのものです（高さを幅クラスで変える）。</summary>
    private readonly FrameworkElement _chart;

    /// <summary>3 つの面とグラフを受け取って作ります。</summary>
    /// <param name="picker">PID や組を選ぶ面。</param>
    /// <param name="chartCard">グラフを入れた面。</param>
    /// <param name="chart">グラフそのもの（<paramref name="chartCard"/> の中）。</param>
    /// <param name="guide">読み方・統計の面。</param>
    public ChartWorkspace(FrameworkElement picker, FrameworkElement chartCard, FrameworkElement chart, FrameworkElement guide)
    {
        _picker = picker;
        _chartCard = chartCard;
        _chart = chart;
        _guide = guide;
        Children.Add(picker);
        Children.Add(chartCard);
        Children.Add(guide);
        chart.VerticalAlignment = VerticalAlignment.Stretch;
        ResponsiveLayout.Bind(this, ApplyLayout);
    }

    /// <summary>
    /// 幅クラスに合わせて面を並べ直します。
    /// </summary>
    /// <remarks>責務: 列・行の定義と、各面の行・列・行またぎを差し替えます。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        var gap = Space.Gap(widthClass);
        _chart.MinHeight = widthClass == WidthClass.Compact ? CompactChartMinHeight : ChartMinHeight;
        if (widthClass >= WidthClass.Expanded)
        {
            ColumnSpacing = gap;
            RowSpacing = gap;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideColumnWidth) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Place(_picker, 0, 1, 1);
            Place(_chartCard, 0, 0, 2);
            Place(_guide, 1, 1, 1);
            return;
        }

        ColumnSpacing = 0;
        RowSpacing = gap;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(_picker, 0, 0, 1);
        Place(_chartCard, 1, 0, 1);
        Place(_guide, 2, 0, 1);
    }

    /// <summary>面を 1 つ置きます。</summary>
    /// <param name="element">面。</param>
    /// <param name="row">行。</param>
    /// <param name="column">列。</param>
    /// <param name="rowSpan">またぐ行の数。</param>
    private static void Place(FrameworkElement element, int row, int column, int rowSpan)
    {
        SetRow(element, row);
        SetColumn(element, column);
        SetRowSpan(element, rowSpan);
    }
}
