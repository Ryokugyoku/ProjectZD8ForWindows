using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.Analysis.Actions;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;

/// <summary>
/// 保存済みログの解析画面です。概要・推移・関係を切り替えて出します（iOS 版の <c>MacOSSessionLogAnalysisView</c>）。
/// </summary>
/// <remarks>
/// 値はすべて解析の状態（保存した Raw から復号した値）から作ります。推定の値は「推定」と書いた文言で出します。
/// 表示の切り替えと選んだ PID・組は画面の一時的な状態です。
/// </remarks>
public sealed partial class WindowsSessionLogAnalysisView : UserControl
{
    /// <summary>解析するセッションです。</summary>
    private readonly ConnectionSession _session;

    /// <summary>解析のモデルです。</summary>
    private readonly SessionLogAnalysisModel _model;

    /// <summary>セッション詳細へ戻る処理です。</summary>
    private readonly Action _back;

    /// <summary>ページの中身です。</summary>
    private readonly StackPanel _page = new() { Spacing = 20 };

    /// <summary>画面の一時的な状態：表示の切り替えです。</summary>
    private WindowsSessionLogAnalysisMode _mode = WindowsSessionLogAnalysisMode.Overview;

    /// <summary>画面の一時的な状態：推移で選んだ PID です。</summary>
    private OBDPIDRequest? _selectedSeriesID;

    /// <summary>画面の一時的な状態：関係で選んだ組です。</summary>
    private string? _selectedRelationshipID;

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>セッションとモデルを受け取って解析画面を作ります。</summary>
    /// <param name="session">解析するセッション。</param>
    /// <param name="model">解析のモデル。</param>
    /// <param name="back">セッション詳細へ戻る処理。</param>
    public WindowsSessionLogAnalysisView(ConnectionSession session, SessionLogAnalysisModel model, Action back)
    {
        _session = session;
        _model = model;
        _back = back;
        Content = _page;
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            if (_model.State.SessionID != _session.Id)
            {
                _model.Send(new SessionLogAnalysisAction.SessionSelected(_session));
            }

            Render();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>解析しているセッションです。</summary>
    public ConnectionSessionID SessionID => _session.Id;

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>解析画面を描きます。</summary>
    /// <remarks>責務: 戻る、見出し、件数の要約、表示の切り替え、選んだ表示の中身を作ります。</remarks>
    private void Render()
    {
        _isRendering = true;
        var state = _model.State;
        _selectedSeriesID ??= state.PidSeries.FirstOrDefault()?.Id;
        _selectedRelationshipID ??= state.Relationships.FirstOrDefault()?.Id;
        _page.Children.Clear();
        var back = new Button { Content = new FontIcon { Glyph = FluentSymbol.Back, FontSize = 14 }, Width = 44, Height = 44 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back, LocalizedText.Get("analysis.back.session"));
        ToolTipService.SetToolTip(back, LocalizedText.Get("analysis.back.session"));
        back.Click += (_, _) => _back();
        _page.Children.Add(back);
        _page.Children.Add(Hero());
        _page.Children.Add(Summary(state));
        _page.Children.Add(ModePicker());
        _page.Children.Add(SelectedContent(state));
        _isRendering = false;
    }

    /// <summary>見出しのカードを作ります。</summary>
    /// <remarks>責務: 車両名・開始日時・Raw の件数を並べます。</remarks>
    /// <returns>カード。</returns>
    private Border Hero() => SurfaceCard.Create(new ScreenHeader(
        FluentSymbol.Chart,
        LocalizedText.Get("analysis.eyebrow"),
        _session.Vehicle?.Name ?? LocalizedText.Get("history.vehicle.pending"),
        DateText.DateAndSecond(_session.StartedAt),
        new MetricBlock(NumberText.Integer(_session.RawLogSummary.RecordCount), LocalizedText.Get("analysis.summary.samples"), HorizontalAlignment.Right, valueStyleKey: "TitleTextBlockStyle")),
        padding: 24);

    /// <summary>件数の要約を作ります。</summary>
    /// <remarks>責務: 時系列の件数・復号できた件数・Raw のままの件数を並べます。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private static AdaptiveGrid Summary(SessionLogAnalysisState state)
    {
        var grid = new AdaptiveGrid { MinItemWidth = 180, Spacing = 12 };
        foreach (var (value, key, glyph, tone) in new[]
        {
            (state.Timeline.Count, "analysis.summary.samples", FluentSymbol.Pulse, Tone.Accent),
            (state.DecodedSampleCount, "analysis.summary.decoded", FluentSymbol.Accept, Tone.Success),
            (state.RawOnlySampleCount, "analysis.summary.raw", FluentSymbol.Storage, Tone.Caution),
        })
        {
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = DesignTokens.Foreground(tone), HorizontalAlignment = HorizontalAlignment.Left });
            stack.Children.Add(Typography.Text(NumberText.Integer(value), "TitleTextBlockStyle", wraps: false));
            stack.Children.Add(Typography.Text(LocalizedText.Get(key), "SecondaryCaptionTextStyle"));
            grid.Children.Add(SurfaceCard.Create(stack, padding: 17));
        }

        return grid;
    }

    /// <summary>表示の切り替えを作ります。</summary>
    /// <remarks>責務: 概要・推移・関係を選ぶ SelectorBar を作ります。</remarks>
    /// <returns>表示の要素。</returns>
    private SelectorBar ModePicker()
    {
        var bar = new SelectorBar();
        foreach (var mode in Enum.GetValues<WindowsSessionLogAnalysisMode>())
        {
            bar.Items.Add(new SelectorBarItem { Text = LocalizedText.Get(mode.TitleKey()), Tag = mode, IsSelected = mode == _mode });
        }

        bar.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && bar.SelectedItem?.Tag is WindowsSessionLogAnalysisMode mode && mode != _mode)
            {
                _mode = mode;
                DispatcherQueue.TryEnqueue(Render);
            }
        };
        return bar;
    }

    /// <summary>選んだ表示の中身を作ります。</summary>
    /// <remarks>責務: ダウンロード中・読み込み中・失敗・空の表示か、選んだ表示の中身を作ります。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private UIElement SelectedContent(SessionLogAnalysisState state)
    {
        if (state.Phase == SessionLogAnalysisStatePhase.Downloading)
        {
            var progress = state.DownloadProgress ?? 0;
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(new ProgressBar { Value = progress * 100 });
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new GlyphLabel(FluentSymbol.Download, LocalizedText.Get("analysis.download.progress"), "BodyStrongTextBlockStyle"));
            var percent = Typography.Text(NumberText.Percent(progress), "BodyStrongTextBlockStyle", wraps: false);
            Grid.SetColumn(percent, 1);
            row.Children.Add(percent);
            stack.Children.Add(row);
            return SurfaceCard.Create(stack, padding: 24);
        }

        if (state.Phase == SessionLogAnalysisStatePhase.Loading && state.Timeline.Count == 0)
        {
            var loading = new StackPanel { Spacing = 12, Padding = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Center };
            loading.Children.Add(new ProgressRing { IsActive = true });
            loading.Children.Add(Typography.Text(LocalizedText.Get("analysis.loading"), "SecondaryBodyTextStyle"));
            return loading;
        }

        if (state.Phase == SessionLogAnalysisStatePhase.Failed && state.Timeline.Count == 0)
        {
            return new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Message = LocalizedText.Get("analysis.error.storage") };
        }

        if (state.Phase == SessionLogAnalysisStatePhase.Loaded && state.Timeline.Count == 0)
        {
            return new ContentUnavailablePanel(FluentSymbol.Pulse, LocalizedText.Get("analysis.empty.title"), LocalizedText.Get("analysis.empty.body"));
        }

        var content = new StackPanel { Spacing = 16 };
        if (state.Phase == SessionLogAnalysisStatePhase.Loading)
        {
            var total = Math.Max(state.TotalSampleCount ?? state.Timeline.Count, 1);
            var progress = new StackPanel { Spacing = 4 };
            var label = new Grid();
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            label.Children.Add(Typography.Text(LocalizedText.Get("analysis.loading"), "SecondaryCaptionTextStyle"));
            var count = Typography.Text($"{NumberText.Integer(state.Timeline.Count)} / {NumberText.Integer(state.TotalSampleCount ?? state.Timeline.Count)}", "MonospaceCaptionTextStyle", wraps: false);
            Grid.SetColumn(count, 1);
            label.Children.Add(count);
            progress.Children.Add(label);
            progress.Children.Add(new ProgressBar { Value = 100.0 * state.Timeline.Count / total });
            content.Children.Add(progress);
        }

        content.Children.Add(_mode switch
        {
            WindowsSessionLogAnalysisMode.Overview => Overview(state),
            WindowsSessionLogAnalysisMode.Trends => Trends(state),
            _ => Relationships(state),
        });
        return content;
    }

    /// <summary>概要を作ります。</summary>
    /// <remarks>責務: 時間・走行時間・距離、車速と回転数の帯の順位、部品ごとの見立てを並べます。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private StackPanel Overview(SessionLogAnalysisState state)
    {
        var stack = new StackPanel { Spacing = 16 };
        var metrics = new AdaptiveGrid { MinItemWidth = 190, Spacing = 12 };
        metrics.Children.Add(OverviewMetric(SessionDurationText(), "analysis.overview.session_duration", FluentSymbol.Clock));
        metrics.Children.Add(OverviewMetric(
            state.PerformanceSummary.EstimatedMovingDuration is { } moving ? DurationText.Abbreviated(moving) : LocalizedText.Get("analysis.value.unavailable"),
            "analysis.overview.moving_duration",
            FluentSymbol.Car));
        metrics.Children.Add(OverviewMetric(
            DistanceValue(state),
            _session.RecordedDistanceKilometers is null ? "analysis.overview.estimated_distance" : "analysis.overview.distance",
            FluentSymbol.Road));
        var overviewBody = new StackPanel { Spacing = 12 };
        overviewBody.Children.Add(metrics);
        overviewBody.Children.Add(Typography.Text(LocalizedText.Get("analysis.overview.estimate_note"), "SecondaryCaptionTextStyle"));
        stack.Children.Add(AnalysisCard("analysis.overview.title", "analysis.overview.subtitle", overviewBody));

        var rankings = new AdaptiveGrid { MinItemWidth = 320, Spacing = 14 };
        rankings.Children.Add(DurationRanking("analysis.overview.speed_ranking", "analysis.overview.speed_ranking_note", state.PerformanceSummary.SpeedBands, "analysis.overview.speed_band"));
        rankings.Children.Add(DurationRanking("analysis.overview.rpm_ranking", "analysis.overview.rpm_ranking_note", state.PerformanceSummary.RpmBands, "analysis.overview.rpm_band"));
        stack.Children.Add(rankings);
        stack.Children.Add(ComponentOverview(state));
        return stack;
    }

    /// <summary>推移（折れ線のグラフ）を作ります。</summary>
    /// <remarks>責務: PID の選択、説明と最小・平均・最大、時系列の折れ線を作ります。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private UIElement Trends(SessionLogAnalysisState state)
    {
        var series = state.PidSeries.FirstOrDefault(item => item.Id == _selectedSeriesID) ?? state.PidSeries.FirstOrDefault();
        if (series is null)
        {
            return new ContentUnavailablePanel(FluentSymbol.Chart, LocalizedText.Get("analysis.trend.empty.title"), LocalizedText.Get("analysis.trend.empty.body"));
        }

        var body = new StackPanel { Spacing = 14 };
        var picker = new ComboBox { Header = LocalizedText.Get("analysis.trend.pid"), MinWidth = 280 };
        foreach (var item in state.PidSeries)
        {
            picker.Items.Add(new ComboBoxItem { Content = SeriesName(item), Tag = item.Id });
        }

        picker.SelectedIndex = state.PidSeries.ToList().FindIndex(item => item.Id == series.Id);
        picker.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && picker.SelectedItem is ComboBoxItem { Tag: OBDPIDRequest id })
            {
                _selectedSeriesID = id;
                DispatcherQueue.TryEnqueue(Render);
            }
        };
        body.Children.Add(picker);

        var guide = new StackPanel { Spacing = 8 };
        var idRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        idRow.Children.Add(Typography.Text(PidLabel(series.Id), "MonospaceCaptionTextStyle", DesignTokens.Foreground(Tone.Accent), wraps: false));
        if (series.VehicleModelCode is { } modelCode)
        {
            idRow.Children.Add(new VehicleModelBadge(modelCode, 30));
        }

        guide.Children.Add(idRow);
        guide.Children.Add(Typography.Text(LocalizedText.Get(series.InterpretationKey), "SecondaryCaptionTextStyle"));
        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        stats.Children.Add(new MetricBlock(NumberText.Measurement(series.MinimumValue, series.Unit), LocalizedText.Get("analysis.trend.minimum"), valueStyleKey: "BodyStrongTextBlockStyle"));
        stats.Children.Add(new MetricBlock(NumberText.Measurement(series.AverageValue, series.Unit), LocalizedText.Get("analysis.trend.average"), valueStyleKey: "BodyStrongTextBlockStyle"));
        stats.Children.Add(new MetricBlock(NumberText.Measurement(series.MaximumValue, series.Unit), LocalizedText.Get("analysis.trend.maximum"), valueStyleKey: "BodyStrongTextBlockStyle"));
        guide.Children.Add(stats);
        body.Children.Add(new Border { Background = DesignTokens.SubtleBackground(Tone.Accent), CornerRadius = new CornerRadius(12), Padding = new Thickness(13), Child = guide });
        body.Children.Add(new LineChartView(series.Points, series.Unit) { Height = 320 });
        return AnalysisCard("analysis.trend.title", "analysis.trend.subtitle", body);
    }

    /// <summary>関係（散布図）を作ります。</summary>
    /// <remarks>責務: PID の組の選択、読み方の説明、散布図を作ります。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private UIElement Relationships(SessionLogAnalysisState state)
    {
        var relationships = state.Relationships;
        var relationship = relationships.FirstOrDefault(item => item.Id == _selectedRelationshipID) ?? relationships.FirstOrDefault();
        if (relationship is null)
        {
            return new ContentUnavailablePanel(FluentSymbol.Chart, LocalizedText.Get("analysis.relationship.empty.title"), LocalizedText.Get("analysis.relationship.empty.body"));
        }

        var body = new StackPanel { Spacing = 14 };
        var picker = new ComboBox { Header = LocalizedText.Get("analysis.relationship.pair"), MinWidth = 320 };
        foreach (var item in relationships)
        {
            picker.Items.Add(new ComboBoxItem { Content = $"{SeriesName(item.XSeries)} × {SeriesName(item.YSeries)}", Tag = item.Id });
        }

        picker.SelectedIndex = relationships.ToList().FindIndex(item => item.Id == relationship.Id);
        picker.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && picker.SelectedItem is ComboBoxItem { Tag: string id })
            {
                _selectedRelationshipID = id;
                DispatcherQueue.TryEnqueue(Render);
            }
        };
        body.Children.Add(picker);

        var guide = new StackPanel { Spacing = 7 };
        guide.Children.Add(new GlyphLabel(FluentSymbol.Info, LocalizedText.Get("analysis.relationship.guide.title"), "BodyStrongTextBlockStyle", DesignTokens.Foreground(Tone.Indigo)));
        guide.Children.Add(Typography.Text(LocalizedText.Get("analysis.relationship.guide.common"), "SecondaryCaptionTextStyle"));
        guide.Children.Add(Typography.Text(LocalizedText.Get(RelationshipGuideKey(relationship)), "SecondaryCaptionTextStyle"));
        body.Children.Add(new Border { Background = DesignTokens.SubtleBackground(Tone.Indigo), CornerRadius = new CornerRadius(12), Padding = new Thickness(13), Child = guide });
        if ((relationship.XSeries.VehicleModelCode ?? relationship.YSeries.VehicleModelCode) is { } modelCode)
        {
            body.Children.Add(new VehicleModelBadge(modelCode) { HorizontalAlignment = HorizontalAlignment.Left });
        }

        body.Children.Add(new ScatterChartView(relationship.Points, relationship.XSeries.Unit, relationship.YSeries.Unit) { Height = 320 });
        return AnalysisCard("analysis.relationship.title", "analysis.relationship.subtitle", body);
    }

    /// <summary>部品ごとの見立てを作ります。</summary>
    /// <remarks>責務: 電気・熱・燃料と吸気・排気・診断の各見立てを、統計の文字付きのカードで並べます。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>カード。</returns>
    private Border ComponentOverview(SessionLogAnalysisState state)
    {
        UIElement body;
        if (state.ComponentInsights.Count == 0)
        {
            body = Typography.Text(LocalizedText.Get("analysis.components.empty"), "SecondaryCaptionTextStyle");
        }
        else
        {
            var grid = new AdaptiveGrid { MinItemWidth = 300, Spacing = 12 };
            foreach (var insight in state.ComponentInsights)
            {
                var stack = new StackPanel { Spacing = 6 };
                stack.Children.Add(new GlyphLabel(ComponentGlyph(insight.Component), LocalizedText.Get(ComponentKey(insight.Component) + ".title"), "BodyStrongTextBlockStyle"));
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                nameRow.Children.Add(Typography.Strong(SeriesName(insight.Series), "CaptionTextBlockStyle"));
                if (insight.Series.VehicleModelCode is { } modelCode)
                {
                    nameRow.Children.Add(new VehicleModelBadge(modelCode, 28));
                }

                stack.Children.Add(nameRow);
                stack.Children.Add(Typography.Text(LocalizedText.Get(ComponentKey(insight.Component) + ".body"), "SecondaryCaptionTextStyle"));
                stack.Children.Add(Typography.Text(
                    LocalizedText.Format("analysis.components.statistics", insight.Series.LatestValue, insight.Series.MinimumValue, insight.Series.AverageValue, insight.Series.MaximumValue, insight.Series.Unit),
                    "MonospaceCaptionTextStyle"));
                grid.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), MinHeight = 132, Child = stack });
            }

            body = grid;
        }

        return AnalysisCard("analysis.components.title", "analysis.components.subtitle", body);
    }

    /// <summary>帯の順位のカードを作ります。</summary>
    /// <remarks>責務: 滞在時間の長い順に、帯の範囲・割合・時間を並べます。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="subtitleKey">補足のキー。</param>
    /// <param name="bands">帯。</param>
    /// <param name="bandKey">帯の範囲の書式のキー。</param>
    /// <returns>カード。</returns>
    private static Border DurationRanking(string titleKey, string subtitleKey, System.Collections.Generic.IReadOnlyList<SessionDurationBand> bands, string bandKey)
    {
        var list = new StackPanel { Spacing = 10 };
        if (bands.Count == 0)
        {
            list.Children.Add(Typography.Text(LocalizedText.Get("analysis.overview.ranking_empty"), "SecondaryCaptionTextStyle"));
        }

        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Typography.Text((index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture), "BodyStrongTextBlockStyle", DesignTokens.Foreground(Tone.Accent)));
            var texts = new StackPanel { Spacing = 2 };
            texts.Children.Add(Typography.Strong(LocalizedText.Format(bandKey, band.LowerBound, band.UpperBound), "CaptionTextBlockStyle"));
            texts.Children.Add(Typography.Text(NumberText.Percent(band.Proportion), "SecondaryCaptionTextStyle"));
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            var duration = Typography.Text(DurationText.Abbreviated(band.Duration), "MonospaceCaptionTextStyle", wraps: false);
            duration.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(duration, 2);
            row.Children.Add(duration);
            list.Children.Add(row);
        }

        return AnalysisCard(titleKey, subtitleKey, list);
    }

    /// <summary>解析のカードを作ります。</summary>
    /// <remarks>責務: 題・補足・中身を持つカードを作ります。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="subtitleKey">補足のキー。</param>
    /// <param name="content">中身。</param>
    /// <returns>カード。</returns>
    private static Border AnalysisCard(string titleKey, string subtitleKey, UIElement content)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Typography.Text(LocalizedText.Get(titleKey), "SubtitleTextBlockStyle"));
        stack.Children.Add(Typography.Text(LocalizedText.Get(subtitleKey), "SecondaryCaptionTextStyle"));
        stack.Children.Add(content);
        return SurfaceCard.Create(stack, padding: 20);
    }

    /// <summary>概要の数値のタイルを作ります。</summary>
    /// <remarks>責務: 記号・値・説明を縦に並べたタイルを作ります。</remarks>
    /// <param name="value">値。</param>
    /// <param name="key">説明のキー。</param>
    /// <param name="glyph">記号。</param>
    /// <returns>タイル。</returns>
    private static Border OverviewMetric(string value, string key, string glyph)
    {
        var stack = new StackPanel { Spacing = 7 };
        stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Foreground = DesignTokens.Foreground(Tone.Accent), HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(Typography.Text(value, "SubtitleTextBlockStyle", wraps: false));
        stack.Children.Add(Typography.Text(LocalizedText.Get(key), "SecondaryCaptionTextStyle"));
        return new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Child = stack };
    }

    /// <summary>セッションの時間の文字列を返します。</summary>
    /// <remarks>責務: 終了済みなら所要時間、記録中なら「記録中」の文言を返します。</remarks>
    /// <returns>文字列。</returns>
    private string SessionDurationText() => _session.EndedAt is { } endedAt
        ? DurationText.Abbreviated(Math.Max((endedAt - _session.StartedAt).TotalSeconds, 0))
        : LocalizedText.Get("analysis.value.in_progress");

    /// <summary>距離の文字列を返します。</summary>
    /// <remarks>責務: 記録した距離、なければ推定の距離（推定と分かる書式）、どちらもなければ「なし」を返します。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>文字列。</returns>
    private string DistanceValue(SessionLogAnalysisState state)
    {
        if (_session.RecordedDistanceKilometers is { } recorded)
        {
            return NumberText.Measurement(recorded, "km");
        }

        return state.PerformanceSummary.EstimatedDistanceKilometers is { } estimated
            ? LocalizedText.Format("analysis.value.estimated_distance", estimated)
            : LocalizedText.Get("analysis.value.unavailable");
    }

    /// <summary>PID の系列の名前を返します。</summary>
    /// <remarks>責務: 名前のキーがあれば訳、なければ Mode・PID の番号を返します。</remarks>
    /// <param name="series">PID の系列。</param>
    /// <returns>名前。</returns>
    private static string SeriesName(SessionPIDSeries series) =>
        series.NameKey is { } key ? LocalizedText.Get(key) : PidLabel(series.Id);

    /// <summary>Mode・PID の番号の文字列を返します。</summary>
    /// <remarks>責務: Service と PID を 16 進 2 桁の文言にします。</remarks>
    /// <param name="request">PID の要求。</param>
    /// <returns>文字列。</returns>
    private static string PidLabel(OBDPIDRequest request) => LocalizedText.Format("analysis.pid.identifier", request.Service, request.Pid);

    /// <summary>関係の読み方の説明のキーを返します（iOS 版の <c>relationshipGuideKey</c>）。</summary>
    /// <remarks>責務: PID の組を、組ごとの説明のキーへ変換します。</remarks>
    /// <param name="relationship">PID の組。</param>
    /// <returns>キー。</returns>
    private static string RelationshipGuideKey(SessionPIDRelationship relationship) =>
        (relationship.XSeries.Id.Service, relationship.XSeries.Id.Pid, relationship.YSeries.Id.Service, relationship.YSeries.Id.Pid) switch
        {
            (0x01, 0x0C, 0x01, 0x0D) => "analysis.relationship.guide.rpm_speed",
            (0x01, 0x0C, 0x01, 0x04) => "analysis.relationship.guide.rpm_load",
            (0x01, 0x0C, 0x01, 0x10) => "analysis.relationship.guide.rpm_maf",
            (0x01, 0x11, 0x01, 0x04) => "analysis.relationship.guide.throttle_load",
            (0x01, 0x06, 0x01, 0x07) => "analysis.relationship.guide.trim_banks",
            (0x01, 0x0D, 0x21, 0x17) => "analysis.relationship.guide.speed_zd8_atf",
            _ => "analysis.relationship.guide.common",
        };

    /// <summary>部品の見立てのキーの前半を返します。</summary>
    /// <remarks>責務: 部品を xcstrings のキーの前半へ変換します。</remarks>
    /// <param name="component">部品。</param>
    /// <returns>キーの前半。</returns>
    private static string ComponentKey(SessionComponentInsightComponent component) => component switch
    {
        SessionComponentInsightComponent.Electrical => "analysis.components.electrical",
        SessionComponentInsightComponent.Thermal => "analysis.components.thermal",
        SessionComponentInsightComponent.FuelAndIntake => "analysis.components.fuel_intake",
        SessionComponentInsightComponent.Exhaust => "analysis.components.exhaust",
        _ => "analysis.components.diagnostics",
    };

    /// <summary>部品の記号を返します。</summary>
    /// <remarks>責務: 部品を記号の文字へ変換します。</remarks>
    /// <param name="component">部品。</param>
    /// <returns>記号の文字。</returns>
    private static string ComponentGlyph(SessionComponentInsightComponent component) => component switch
    {
        SessionComponentInsightComponent.Electrical => FluentSymbol.Lightning,
        SessionComponentInsightComponent.Thermal => FluentSymbol.Temperature,
        SessionComponentInsightComponent.FuelAndIntake => FluentSymbol.Wind,
        SessionComponentInsightComponent.Exhaust => FluentSymbol.Wind,
        _ => FluentSymbol.Repair,
    };
}
