using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
/// 作りは「骨組みを 1 回だけ組み、値を差し替える」です：見出し・読込みの進み具合・件数の要約・表示の切り替えは持ち続け、
/// 読込み中の進み具合では値だけを差し替えます。中身（概要・推移・関係）は、表示の切り替えか、元になるデータが替わったときだけ組み直します。
/// 推移と関係で PID・組を選んだときは、グラフと読み方の値だけを差し替えます。
/// </remarks>
public sealed partial class WindowsSessionLogAnalysisView : UserControl
{
    /// <summary>解析するセッションです。</summary>
    private readonly ConnectionSession _session;

    /// <summary>解析のモデルです。</summary>
    private readonly SessionLogAnalysisModel _model;

    /// <summary>中身を組み直す前に、押している最中・メニューが開いている間を待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>モデルの知らせを束ね、読込み中の進み具合の更新を間引く仕組みです。</summary>
    private readonly UiUpdateThrottle _throttle;

    /// <summary>読込みの進み具合の面です。</summary>
    private readonly Border _progressCard;

    /// <summary>読込みの進み具合の記号です。</summary>
    private readonly FontIcon _progressIcon = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>読込みの進み具合の題です。</summary>
    private readonly TextBlock _progressTitle = Typography.Text(string.Empty, TextStyles.BodyStrong);

    /// <summary>読込みの進み具合の値です。</summary>
    private readonly TextBlock _progressValue = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);

    /// <summary>読込みの進み具合の棒です。</summary>
    private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 100 };

    /// <summary>時系列の件数の値です。</summary>
    private readonly MetricBlock _samples = new("0", LocalizedText.Get("analysis.summary.samples"), valueStyleKey: TextStyles.MetricLarge);

    /// <summary>復号できた件数の値です。</summary>
    private readonly MetricBlock _decoded = new("0", LocalizedText.Get("analysis.summary.decoded"), valueStyleKey: TextStyles.MetricLarge);

    /// <summary>Raw のままの件数の値です。</summary>
    private readonly MetricBlock _rawOnly = new("0", LocalizedText.Get("analysis.summary.raw"), valueStyleKey: TextStyles.MetricLarge);

    /// <summary>表示の切り替えです。</summary>
    private readonly SelectorBar _modePicker = new();

    /// <summary>中身を入れる場所です。</summary>
    private readonly ContentControl _contentHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };

    /// <summary>画面の一時的な状態：表示の切り替えです。</summary>
    private WindowsSessionLogAnalysisMode _mode = WindowsSessionLogAnalysisMode.Overview;

    /// <summary>画面の一時的な状態：推移で選んだ PID です。</summary>
    private OBDPIDRequest? _selectedSeriesID;

    /// <summary>画面の一時的な状態：関係で選んだ組です。</summary>
    private string? _selectedRelationshipID;

    /// <summary>更新の最中かどうかです（選択の変化を利用者の操作と区別するため）。</summary>
    private bool _isUpdating;

    /// <summary>今の中身を作ったときの元です（同じなら組み直さない）。</summary>
    private ContentKey? _contentKey;

    /// <summary>セッションとモデルを受け取って解析画面を作ります。</summary>
    /// <param name="session">解析するセッション。</param>
    /// <param name="model">解析のモデル。</param>
    /// <remarks>セッション詳細へ戻るのは、接続履歴の画面のパンくずとシェルの戻る操作で行います。</remarks>
    public WindowsSessionLogAnalysisView(ConnectionSession session, SessionLogAnalysisModel model)
    {
        _session = session;
        _model = model;
        _gate = new RenderGate(this);
        _progressCard = BuildProgressCard();
        BuildModePicker();

        var page = new PageScaffold();
        page.Body.Children.Add(Hero());
        page.Body.Children.Add(_progressCard);
        page.Body.Children.Add(Summary());
        page.Body.Children.Add(_modePicker);
        page.Body.Children.Add(_contentHost);
        Content = page;

        _throttle = new UiUpdateThrottle(this, Update, TimeSpan.FromMilliseconds(100));
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            if (_model.State.SessionID != _session.Id)
            {
                _model.Send(new SessionLogAnalysisAction.SessionSelected(_session));
            }

            _throttle.FlushNow();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>解析しているセッションです。</summary>
    public ConnectionSessionID SessionID => _session.Id;

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 更新を頼みます（何度来ても 1 フレームに 1 回、100ms 以上あけて 1 回にまとまる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => _throttle.Request();

    /// <summary>
    /// 画面の値を今の状態に合わせます。
    /// </summary>
    /// <remarks>責務: 進み具合と件数の要約は値だけ差し替えます。中身は、元になるデータか表示の切り替えが替わったときだけ組み直します。</remarks>
    private void Update()
    {
        var state = _model.State;
        _isUpdating = true;
        UpdateProgress(state);
        _samples.SetValue(NumberText.Integer(state.Timeline.Count));
        _decoded.SetValue(NumberText.Integer(state.DecodedSampleCount));
        _rawOnly.SetValue(NumberText.Integer(state.RawOnlySampleCount));
        _isUpdating = false;

        var key = KeyFor(state);
        if (key == _contentKey)
        {
            return;
        }

        if (_gate.TryDefer(Update))
        {
            return;
        }

        _contentKey = key;
        _isUpdating = true;
        _selectedSeriesID ??= state.PidSeries.FirstOrDefault()?.Id;
        _selectedRelationshipID ??= state.Relationships.FirstOrDefault()?.Id;
        _contentHost.Content = BuildContent(state, key.Kind);
        _isUpdating = false;
    }

    /// <summary>中身の元を求めます。</summary>
    /// <remarks>責務: 中身の種類と、その中身が使うデータの参照を返します（読込みの進み具合では変わらない）。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>中身の元。</returns>
    private ContentKey KeyFor(SessionLogAnalysisState state)
    {
        if (state.Phase == SessionLogAnalysisStatePhase.Downloading)
        {
            return new ContentKey(ContentKind.None, _mode, null, null);
        }

        if (state.Timeline.Count == 0)
        {
            return state.Phase switch
            {
                SessionLogAnalysisStatePhase.Loading => new ContentKey(ContentKind.None, _mode, null, null),
                SessionLogAnalysisStatePhase.Failed => new ContentKey(ContentKind.Failed, _mode, null, null),
                SessionLogAnalysisStatePhase.Loaded => new ContentKey(ContentKind.Empty, _mode, null, null),
                _ => new ContentKey(ContentKind.None, _mode, null, null),
            };
        }

        return _mode switch
        {
            WindowsSessionLogAnalysisMode.Overview => new ContentKey(ContentKind.Data, _mode, state.PerformanceSummary, state.ComponentInsights),
            WindowsSessionLogAnalysisMode.Trends => new ContentKey(ContentKind.Data, _mode, state.PidSeries, null),
            _ => new ContentKey(ContentKind.Data, _mode, state.Relationships, null),
        };
    }

    /// <summary>読込みの進み具合を合わせます。</summary>
    /// <remarks>責務: ダウンロード中・読み込み中だけ面を出し、題・値・棒を差し替えます。</remarks>
    /// <param name="state">解析の状態。</param>
    private void UpdateProgress(SessionLogAnalysisState state)
    {
        if (state.Phase == SessionLogAnalysisStatePhase.Downloading)
        {
            var progress = state.DownloadProgress ?? 0;
            Retained.SetVisible(_progressCard, true);
            Retained.SetGlyph(_progressIcon, FluentSymbol.Download);
            Retained.SetText(_progressTitle, LocalizedText.Get("analysis.download.progress"));
            Retained.SetText(_progressValue, NumberText.Percent(progress));
            SetProgress(false, progress * 100);
            return;
        }

        if (state.Phase == SessionLogAnalysisStatePhase.Loading)
        {
            Retained.SetVisible(_progressCard, true);
            Retained.SetGlyph(_progressIcon, FluentSymbol.Pulse);
            Retained.SetText(_progressTitle, LocalizedText.Get("analysis.loading"));
            if (state.Timeline.Count == 0)
            {
                Retained.SetText(_progressValue, string.Empty);
                SetProgress(true, 0);
                return;
            }

            var total = Math.Max(state.TotalSampleCount ?? state.Timeline.Count, 1);
            Retained.SetText(_progressValue, $"{NumberText.Integer(state.Timeline.Count)} / {NumberText.Integer(state.TotalSampleCount ?? state.Timeline.Count)}");
            SetProgress(false, 100.0 * state.Timeline.Count / total);
            return;
        }

        Retained.SetVisible(_progressCard, false);
    }

    /// <summary>棒の状態を差し替えます（同じなら触らない）。</summary>
    /// <param name="indeterminate">進み具合が分からないなら true。</param>
    /// <param name="percent">進み具合（0〜100）。</param>
    private void SetProgress(bool indeterminate, double percent)
    {
        if (_progressBar.IsIndeterminate != indeterminate)
        {
            _progressBar.IsIndeterminate = indeterminate;
        }

        if (!indeterminate && Math.Abs(_progressBar.Value - percent) > 0.05)
        {
            _progressBar.Value = percent;
        }
    }

    /// <summary>
    /// 中身を作ります。
    /// </summary>
    /// <remarks>責務: 種類に応じて、失敗・空の表示か、選んだ表示（概要・推移・関係）の中身を作ります。進み具合だけのときは何も出しません。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <param name="kind">中身の種類。</param>
    /// <returns>中身。なければ null。</returns>
    private UIElement? BuildContent(SessionLogAnalysisState state, ContentKind kind) => kind switch
    {
        ContentKind.Failed => ErrorReport.Bar(InfoBarSeverity.Error, LocalizedText.Get("analysis.error.storage")),
        ContentKind.Empty => new ContentUnavailablePanel(FluentSymbol.Pulse, LocalizedText.Get("analysis.empty.title"), LocalizedText.Get("analysis.empty.body")),
        ContentKind.Data => _mode switch
        {
            WindowsSessionLogAnalysisMode.Overview => Overview(state),
            WindowsSessionLogAnalysisMode.Trends => Trends(state),
            _ => Relationships(state),
        },
        _ => null,
    };

    /// <summary>見出しのカードを作ります。</summary>
    /// <remarks>責務: 車両名・開始日時・Raw の件数を並べます。</remarks>
    /// <returns>カード。</returns>
    private Border Hero() => SurfaceCard.Create(new ScreenHeader(
        FluentSymbol.Chart,
        LocalizedText.Get("analysis.eyebrow"),
        _session.Vehicle?.Name ?? LocalizedText.Get("history.vehicle.pending"),
        DateText.DateAndSecond(_session.StartedAt),
        new MetricBlock(NumberText.Integer(_session.RawLogSummary.RecordCount), LocalizedText.Get("analysis.summary.samples"), HorizontalAlignment.Right, valueStyleKey: TextStyles.MetricLarge)));

    /// <summary>読込みの進み具合の面を作ります（1 回だけ）。</summary>
    /// <returns>面。</returns>
    private Border BuildProgressCard()
    {
        var grid = new Grid { ColumnSpacing = Space.S, RowSpacing = Space.S };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _progressTitle.VerticalAlignment = VerticalAlignment.Center;
        _progressValue.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_progressTitle, 1);
        Grid.SetColumn(_progressValue, 2);
        Grid.SetRow(_progressBar, 1);
        Grid.SetColumnSpan(_progressBar, 3);
        grid.Children.Add(_progressIcon);
        grid.Children.Add(_progressTitle);
        grid.Children.Add(_progressValue);
        grid.Children.Add(_progressBar);
        var card = SurfaceCard.Create(grid, padding: Space.L);
        card.Visibility = Visibility.Collapsed;
        return card;
    }

    /// <summary>件数の要約を作ります（1 回だけ。値は <see cref="Update"/> が差し替える）。</summary>
    /// <remarks>責務: 時系列の件数・復号できた件数・Raw のままの件数のタイルを並べます。</remarks>
    /// <returns>表示の要素。</returns>
    private AdaptiveGrid Summary()
    {
        var grid = AdaptiveGrid.Tiles();
        foreach (var (metric, glyph, tone) in new[]
        {
            (_samples, FluentSymbol.Pulse, Tone.Accent),
            (_decoded, FluentSymbol.Accept, Tone.Success),
            (_rawOnly, FluentSymbol.Storage, Tone.Caution),
        })
        {
            var stack = new StackPanel { Spacing = Space.S };
            stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = DesignTokens.Foreground(tone), HorizontalAlignment = HorizontalAlignment.Left });
            stack.Children.Add(metric);
            grid.Children.Add(SurfaceCard.Create(stack, padding: Space.L));
        }

        return grid;
    }

    /// <summary>表示の切り替えを作ります（1 回だけ）。</summary>
    /// <remarks>責務: 概要・推移・関係を選ぶ SelectorBar を作ります。選んだら中身だけを組み直します。</remarks>
    private void BuildModePicker()
    {
        foreach (var mode in Enum.GetValues<WindowsSessionLogAnalysisMode>())
        {
            _modePicker.Items.Add(new SelectorBarItem { Text = LocalizedText.Get(mode.TitleKey()), Tag = mode, IsSelected = mode == _mode });
        }

        _modePicker.SelectionChanged += (_, _) =>
        {
            if (!_isUpdating && _modePicker.SelectedItem?.Tag is WindowsSessionLogAnalysisMode mode && mode != _mode)
            {
                _mode = mode;
                DispatcherQueue.TryEnqueue(Update);
            }
        };
    }

    /// <summary>概要を作ります。</summary>
    /// <remarks>責務: 時間・走行時間・距離、車速と回転数の帯の順位、部品ごとの見立てを並べます。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private StackPanel Overview(SessionLogAnalysisState state)
    {
        var stack = new StackPanel { Spacing = Space.L };
        var metrics = AdaptiveGrid.Tiles();
        metrics.Children.Add(OverviewMetric(SessionDurationText(), "analysis.overview.session_duration", FluentSymbol.Clock));
        metrics.Children.Add(OverviewMetric(
            state.PerformanceSummary.EstimatedMovingDuration is { } moving ? DurationText.Abbreviated(moving) : LocalizedText.Get("analysis.value.unavailable"),
            "analysis.overview.moving_duration",
            FluentSymbol.Car));
        metrics.Children.Add(OverviewMetric(
            DistanceValue(state),
            _session.RecordedDistanceKilometers is null ? "analysis.overview.estimated_distance" : "analysis.overview.distance",
            FluentSymbol.Road));
        var overviewBody = new StackPanel { Spacing = Space.M };
        overviewBody.Children.Add(metrics);
        overviewBody.Children.Add(Typography.Text(LocalizedText.Get("analysis.overview.estimate_note"), TextStyles.SecondaryCaption));
        stack.Children.Add(AnalysisCard("analysis.overview.title", "analysis.overview.subtitle", overviewBody));

        var rankings = AdaptiveGrid.WideCards();
        rankings.Children.Add(DurationRanking("analysis.overview.speed_ranking", "analysis.overview.speed_ranking_note", state.PerformanceSummary.SpeedBands, "analysis.overview.speed_band"));
        rankings.Children.Add(DurationRanking("analysis.overview.rpm_ranking", "analysis.overview.rpm_ranking_note", state.PerformanceSummary.RpmBands, "analysis.overview.rpm_band"));
        stack.Children.Add(rankings);
        stack.Children.Add(ComponentOverview(state));
        return stack;
    }

    /// <summary>推移（折れ線のグラフ）を作ります。</summary>
    /// <remarks>責務: PID の選択、折れ線のグラフ、説明と最小・平均・最大を、幅に合わせて並べます。PID を選んだときは、グラフと読み方の値だけを差し替えます。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private UIElement Trends(SessionLogAnalysisState state)
    {
        var all = state.PidSeries;
        if (all.Count == 0)
        {
            return new ContentUnavailablePanel(FluentSymbol.Chart, LocalizedText.Get("analysis.trend.empty.title"), LocalizedText.Get("analysis.trend.empty.body"));
        }

        var picker = new ComboBox { Header = LocalizedText.Get("analysis.trend.pid"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in all)
        {
            picker.Items.Add(new ComboBoxItem { Content = SeriesName(item), Tag = item.Id });
        }

        var chart = new LineChartView();
        var idText = Typography.Text(string.Empty, TextStyles.Monospace, DesignTokens.Foreground(Tone.Accent), wraps: false);
        var badgeHost = new Border { VerticalAlignment = VerticalAlignment.Center };
        var interpretation = Typography.Text(string.Empty, TextStyles.SecondaryCaption);
        var minimum = new MetricBlock(string.Empty, LocalizedText.Get("analysis.trend.minimum"), valueStyleKey: TextStyles.BodyStrong);
        var average = new MetricBlock(string.Empty, LocalizedText.Get("analysis.trend.average"), valueStyleKey: TextStyles.BodyStrong);
        var maximum = new MetricBlock(string.Empty, LocalizedText.Get("analysis.trend.maximum"), valueStyleKey: TextStyles.BodyStrong);

        void Show(SessionPIDSeries series)
        {
            Retained.SetText(idText, PidLabel(series.Id));
            badgeHost.Child = series.VehicleModelCode is { } modelCode ? new VehicleModelBadge(modelCode, 30) : null;
            Retained.SetText(interpretation, LocalizedText.Get(series.InterpretationKey));
            minimum.SetValue(NumberText.Measurement(series.MinimumValue, series.Unit));
            average.SetValue(NumberText.Measurement(series.AverageValue, series.Unit));
            maximum.SetValue(NumberText.Measurement(series.MaximumValue, series.Unit));
            chart.SetData(series.Points, series.Unit);
        }

        var selected = all.FirstOrDefault(item => item.Id == _selectedSeriesID) ?? all[0];
        picker.SelectedIndex = all.ToList().FindIndex(item => item.Id == selected.Id);
        Show(selected);
        picker.SelectionChanged += (_, _) =>
        {
            if (!_isUpdating && picker.SelectedIndex is >= 0 and var index && index < all.Count)
            {
                _selectedSeriesID = all[index].Id;
                Show(all[index]);
            }
        };

        var guide = new StackPanel { Spacing = Space.S };
        var idRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
        idRow.Children.Add(idText);
        idRow.Children.Add(badgeHost);
        guide.Children.Add(idRow);
        guide.Children.Add(interpretation);
        var stats = new AdaptiveGrid { MinItemWidth = 92, Spacing = Space.M };
        stats.Children.Add(minimum);
        stats.Children.Add(average);
        stats.Children.Add(maximum);
        guide.Children.Add(stats);

        return new ChartWorkspace(
            SurfaceCard.Create(picker, padding: Space.L),
            ChartCard("analysis.trend.title", "analysis.trend.subtitle", chart),
            chart,
            SurfaceCard.Create(guide, padding: Space.L));
    }

    /// <summary>関係（散布図）を作ります。</summary>
    /// <remarks>責務: PID の組の選択、散布図、読み方の説明を、幅に合わせて並べます。組を選んだときは、散布図と読み方の値だけを差し替えます。</remarks>
    /// <param name="state">解析の状態。</param>
    /// <returns>表示の要素。</returns>
    private UIElement Relationships(SessionLogAnalysisState state)
    {
        var all = state.Relationships;
        if (all.Count == 0)
        {
            return new ContentUnavailablePanel(FluentSymbol.Chart, LocalizedText.Get("analysis.relationship.empty.title"), LocalizedText.Get("analysis.relationship.empty.body"));
        }

        var picker = new ComboBox { Header = LocalizedText.Get("analysis.relationship.pair"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in all)
        {
            picker.Items.Add(new ComboBoxItem { Content = $"{SeriesName(item.XSeries)} × {SeriesName(item.YSeries)}", Tag = item.Id });
        }

        var chart = new ScatterChartView();
        var specific = Typography.Text(string.Empty, TextStyles.SecondaryCaption);
        var badgeHost = new Border { HorizontalAlignment = HorizontalAlignment.Left };

        void Show(SessionPIDRelationship relationship)
        {
            Retained.SetText(specific, LocalizedText.Get(RelationshipGuideKey(relationship)));
            var modelCode = relationship.XSeries.VehicleModelCode ?? relationship.YSeries.VehicleModelCode;
            badgeHost.Child = modelCode is { } code ? new VehicleModelBadge(code) { HorizontalAlignment = HorizontalAlignment.Left } : null;
            chart.SetData(relationship.Points, relationship.XSeries.Unit, relationship.YSeries.Unit);
        }

        var selected = all.FirstOrDefault(item => item.Id == _selectedRelationshipID) ?? all[0];
        picker.SelectedIndex = all.ToList().FindIndex(item => item.Id == selected.Id);
        Show(selected);
        picker.SelectionChanged += (_, _) =>
        {
            if (!_isUpdating && picker.SelectedIndex is >= 0 and var index && index < all.Count)
            {
                _selectedRelationshipID = all[index].Id;
                Show(all[index]);
            }
        };

        var guide = new StackPanel { Spacing = Space.S };
        guide.Children.Add(new GlyphLabel(FluentSymbol.Info, LocalizedText.Get("analysis.relationship.guide.title"), TextStyles.BodyStrong, DesignTokens.Foreground(Tone.Accent)));
        guide.Children.Add(Typography.Text(LocalizedText.Get("analysis.relationship.guide.common"), TextStyles.SecondaryCaption));
        guide.Children.Add(specific);
        guide.Children.Add(badgeHost);

        return new ChartWorkspace(
            SurfaceCard.Create(picker, padding: Space.L),
            ChartCard("analysis.relationship.title", "analysis.relationship.subtitle", chart),
            chart,
            SurfaceCard.Create(guide, padding: Space.L));
    }

    /// <summary>グラフの面を作ります。</summary>
    /// <remarks>責務: 題・補足・グラフを縦に並べ、グラフに余った高さを渡します。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="subtitleKey">補足のキー。</param>
    /// <param name="chart">グラフ。</param>
    /// <returns>面。</returns>
    private static Border ChartCard(string titleKey, string subtitleKey, UIElement chart)
    {
        var grid = new Grid { RowSpacing = Space.M };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = Typography.Text(LocalizedText.Get(titleKey), TextStyles.CardTitle);
        var subtitle = Typography.Text(LocalizedText.Get(subtitleKey), TextStyles.SecondaryCaption);
        Grid.SetRow(subtitle, 1);
        Grid.SetRow((FrameworkElement)chart, 2);
        grid.Children.Add(title);
        grid.Children.Add(subtitle);
        grid.Children.Add(chart);
        return SurfaceCard.Create(grid, padding: Space.L);
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
            body = Typography.Text(LocalizedText.Get("analysis.components.empty"), TextStyles.SecondaryCaption);
        }
        else
        {
            var grid = AdaptiveGrid.Cards();
            foreach (var insight in state.ComponentInsights)
            {
                var stack = new StackPanel { Spacing = Space.S };
                stack.Children.Add(new GlyphLabel(ComponentGlyph(insight.Component), LocalizedText.Get(ComponentKey(insight.Component) + ".title"), TextStyles.BodyStrong));
                var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
                nameRow.Children.Add(Typography.Strong(SeriesName(insight.Series), TextStyles.Caption));
                if (insight.Series.VehicleModelCode is { } modelCode)
                {
                    nameRow.Children.Add(new VehicleModelBadge(modelCode, 28));
                }

                stack.Children.Add(nameRow);
                stack.Children.Add(Typography.Text(LocalizedText.Get(ComponentKey(insight.Component) + ".body"), TextStyles.SecondaryCaption));
                stack.Children.Add(Typography.Text(
                    LocalizedText.Format("analysis.components.statistics", insight.Series.LatestValue, insight.Series.MinimumValue, insight.Series.AverageValue, insight.Series.MaximumValue, insight.Series.Unit),
                    TextStyles.Monospace));
                grid.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Child = stack });
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
    private static Border DurationRanking(string titleKey, string subtitleKey, IReadOnlyList<SessionDurationBand> bands, string bandKey)
    {
        var list = new StackPanel { Spacing = Space.M };
        if (bands.Count == 0)
        {
            list.Children.Add(Typography.Text(LocalizedText.Get("analysis.overview.ranking_empty"), TextStyles.SecondaryCaption));
        }

        for (var index = 0; index < bands.Count; index++)
        {
            var band = bands[index];
            var row = new Grid { ColumnSpacing = Space.M };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Typography.Text((index + 1).ToString(CultureInfo.CurrentCulture), TextStyles.BodyStrong, DesignTokens.Foreground(Tone.Accent)));
            var texts = new StackPanel { Spacing = Space.XXS };
            texts.Children.Add(Typography.Strong(LocalizedText.Format(bandKey, band.LowerBound, band.UpperBound), TextStyles.Caption));
            texts.Children.Add(Typography.Text(NumberText.Percent(band.Proportion), TextStyles.SecondaryCaption));
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            var duration = Typography.Text(DurationText.Abbreviated(band.Duration), TextStyles.Monospace, wraps: false);
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
        var stack = new StackPanel { Spacing = Space.M };
        stack.Children.Add(Typography.Text(LocalizedText.Get(titleKey), TextStyles.CardTitle));
        stack.Children.Add(Typography.Text(LocalizedText.Get(subtitleKey), TextStyles.SecondaryCaption));
        stack.Children.Add(content);
        return SurfaceCard.Create(stack);
    }

    /// <summary>概要の数値のタイルを作ります。</summary>
    /// <remarks>責務: 記号・値・説明を縦に並べたタイルを作ります。</remarks>
    /// <param name="value">値。</param>
    /// <param name="key">説明のキー。</param>
    /// <param name="glyph">記号。</param>
    /// <returns>タイル。</returns>
    private static Border OverviewMetric(string value, string key, string glyph)
    {
        var stack = new StackPanel { Spacing = Space.S };
        stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Foreground = DesignTokens.Foreground(Tone.Accent), HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(Typography.Metric(value, TextStyles.MetricValue));
        stack.Children.Add(Typography.Text(LocalizedText.Get(key), TextStyles.SecondaryCaption));
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

    /// <summary>中身の種類です。</summary>
    private enum ContentKind
    {
        /// <summary>中身なし（進み具合の面だけ）。</summary>
        None,

        /// <summary>読込みの失敗。</summary>
        Failed,

        /// <summary>時系列が空。</summary>
        Empty,

        /// <summary>表示の切り替えで選んだ中身。</summary>
        Data,
    }

    /// <summary>中身を作ったときの元です（参照が同じなら中身は組み直さない）。</summary>
    /// <param name="Kind">種類。</param>
    /// <param name="Mode">表示の切り替え。</param>
    /// <param name="A">中身が使うデータ。</param>
    /// <param name="B">中身が使うデータ。</param>
    private readonly record struct ContentKey(ContentKind Kind, WindowsSessionLogAnalysisMode Mode, object? A, object? B);
}
