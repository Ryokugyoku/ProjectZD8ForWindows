using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.Analysis.Actions;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.AppShell;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Views;

/// <summary>
/// 接続履歴の画面です（iOS 版の <c>MacOSConnectionHistoryView</c>）。車両別のアーカイブ → 車両のセッション一覧 → セッションの詳細 → 解析と進みます。
/// </summary>
/// <remarks>
/// 上のパンくずで、どの段にも 1 クリックで戻れます。シェルの戻るボタン・Alt+←・マウスの戻るボタンでも 1 段戻れます。
/// ウインドウが広いときは、セッション一覧と詳細を左右に並べ、一覧から選ぶだけで詳細を切り替えられます。
/// </remarks>
public sealed partial class WindowsConnectionHistoryView : UserControl, IBackNavigable
{
    /// <summary>一覧と詳細を左右に並べる幅です。</summary>
    private const double SplitWidth = 1100;

    /// <summary>接続履歴のモデルです。</summary>
    private readonly ConnectionHistoryModel _model;

    /// <summary>保存済みログの解析モデルです。</summary>
    private readonly SessionLogAnalysisModel _analysis;

    /// <summary>画面の一時的な状態：移動の積み重ねです。</summary>
    private readonly WindowsConnectionHistoryNavigationState _navigation = new();

    /// <summary>端末の Raw を消す確認です。</summary>
    private readonly StateBoundDialog _rawRemovalDialog = new();

    /// <summary>セッション削除の確認です。</summary>
    private readonly StateBoundDialog _deletionDialog = new();

    /// <summary>セッション削除の失敗です。</summary>
    private readonly StateBoundDialog _deletionFailureDialog = new();

    /// <summary>停止内容の確認です。</summary>
    private readonly StateBoundDialog _stopReviewDialog = new();

    /// <summary>停止内容の確認の失敗です。</summary>
    private readonly StateBoundDialog _stopReviewFailureDialog = new();

    /// <summary>アップロードの失敗です。</summary>
    private readonly StateBoundDialog _uploadFailureDialog = new();

    /// <summary>解析のための Raw のダウンロードの確認です。</summary>
    private readonly StateBoundDialog _downloadDialog = new();

    /// <summary>進行中のセッションの経過時間を進めるタイマーです。</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>経過時間の文字と開始時刻です。</summary>
    private readonly List<(TextBlock Text, DateTimeOffset StartedAt)> _timers = [];

    /// <summary>操作中の作り直しを待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>描いている最中かどうかです（選択の変化を利用者の操作と区別するため）。</summary>
    private bool _isRendering;

    /// <summary>表示中の解析画面です。</summary>
    private WindowsSessionLogAnalysisView? _analysisView;

    /// <summary>最後にパンくずへ出した文字です。</summary>
    private string? _breadcrumbKey;

    /// <summary>最後に詳細へ出したセッションです（切り替えたら詳細を上までスクロールし直す）。</summary>
    private ConnectionSessionID? _detailSessionID;

    /// <summary>最後に描いたときに戻れたかどうかです。</summary>
    private bool _lastCanGoBack;

    /// <summary>モデルを受け取って接続履歴の画面を作ります。</summary>
    /// <param name="model">接続履歴のモデル。</param>
    /// <param name="analysis">保存済みログの解析モデル。</param>
    public WindowsConnectionHistoryView(ConnectionHistoryModel model, SessionLogAnalysisModel analysis)
    {
        _model = model;
        _analysis = analysis;
        InitializeComponent();
        _gate = new RenderGate(this);
        _timer.Tick += (_, _) =>
        {
            foreach (var (text, startedAt) in _timers)
            {
                text.Text = DurationText.Timer(startedAt, DateTimeOffset.Now);
            }
        };
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            _analysis.PropertyChanged += OnModelChanged;
            _timer.Start();
            Render();
            _model.Send(new ConnectionHistoryAction.RefreshRequested());
        };
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _analysis.PropertyChanged -= OnModelChanged;
            _timer.Stop();
        };
        SizeChanged += (_, args) =>
        {
            if ((args.PreviousSize.Width >= SplitWidth) != (args.NewSize.Width >= SplitWidth))
            {
                Render();
            }
        };
    }

    /// <inheritdoc />
    public event EventHandler? CanGoBackChanged;

    /// <inheritdoc />
    public bool CanGoBack => _navigation.Path.Count > 0;

    /// <summary>一覧と詳細を左右に並べるかどうかです。</summary>
    private bool IsWide => ActualWidth >= SplitWidth;

    /// <inheritdoc />
    public void GoBack() => NavigateTo(_navigation.Path.Count - 1);

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>
    /// 今の行き先の画面を描きます。
    /// </summary>
    /// <remarks>責務: 行き先に応じて、アーカイブ・セッション一覧・詳細・解析を 1 列か左右 2 列で描き、パンくずとダイアログを合わせます。押している最中は待ちます。</remarks>
    private void Render()
    {
        if (_gate.TryDefer(Render))
        {
            return;
        }

        var state = _model.State;
        _navigation.ReturnFromDeletedSession(state.Sessions.Select(session => session.Id).ToHashSet());
        _isRendering = true;
        _timers.Clear();
        var groupID = _navigation.Path.OfType<WindowsConnectionHistoryRoute.VehicleSessions>().LastOrDefault()?.GroupID;
        switch (_navigation.Current)
        {
            case WindowsConnectionHistoryRoute.SessionAnalysis analysisRoute
                when state.Sessions.FirstOrDefault(session => session.Id == analysisRoute.SessionID) is { } analyzed:
                ShowSingleColumn();
                if (_analysisView?.SessionID != analyzed.Id)
                {
                    _analysisView = new WindowsSessionLogAnalysisView(analyzed, _analysis);
                    Page.Children.Clear();
                    Page.Children.Add(_analysisView);
                    Scroller.ChangeView(null, 0, null, true);
                }
                else if (!Page.Children.Contains(_analysisView))
                {
                    Page.Children.Clear();
                    Page.Children.Add(_analysisView);
                }

                break;
            case WindowsConnectionHistoryRoute.SessionDetail detailRoute
                when state.Sessions.FirstOrDefault(session => session.Id == detailRoute.SessionID) is { } session:
                _analysisView = null;
                var detail = new WindowsConnectionSessionDetailView(session, state, _model.Send, OpenAnalysis);
                if (IsWide && groupID is { } detailGroup)
                {
                    ShowSplit();
                    RenderSessionList(ListPane, state, detailGroup, session.Id);
                    DetailPane.Children.Clear();
                    DetailPane.Children.Add(detail);
                }
                else
                {
                    ShowSingleColumn();
                    Page.Children.Clear();
                    Page.Children.Add(detail);
                }

                if (_detailSessionID != session.Id)
                {
                    _detailSessionID = session.Id;
                    DetailScroller.ChangeView(null, 0, null, true);
                    Scroller.ChangeView(null, 0, null, true);
                }

                break;
            case WindowsConnectionHistoryRoute.VehicleSessions vehicleRoute:
                _analysisView = null;
                _detailSessionID = null;
                if (IsWide)
                {
                    ShowSplit();
                    RenderSessionList(ListPane, state, vehicleRoute.GroupID, null);
                    DetailPane.Children.Clear();
                    DetailPane.Children.Add(new ContentUnavailablePanel(FluentSymbol.Document, LocalizedText.Get("windows.history.select_session")));
                }
                else
                {
                    ShowSingleColumn();
                    Page.Children.Clear();
                    RenderSessionList(Page, state, vehicleRoute.GroupID, null);
                }

                break;
            default:
                _analysisView = null;
                _detailSessionID = null;
                ShowSingleColumn();
                Page.Children.Clear();
                RenderArchive(state);
                break;
        }

        RenderBreadcrumb(state);
        _isRendering = false;
        SyncDialogs(state, _analysis.State);
        if (_lastCanGoBack != CanGoBack)
        {
            _lastCanGoBack = CanGoBack;
            CanGoBackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>1 列の表示に切り替えます。</summary>
    private void ShowSingleColumn()
    {
        Scroller.Visibility = Visibility.Visible;
        Split.Visibility = Visibility.Collapsed;
        ListPane.Children.Clear();
        DetailPane.Children.Clear();
    }

    /// <summary>左右 2 列の表示に切り替えます。</summary>
    private void ShowSplit()
    {
        Scroller.Visibility = Visibility.Collapsed;
        Split.Visibility = Visibility.Visible;
        Page.Children.Clear();
    }

    /// <summary>
    /// パンくずを描きます。
    /// </summary>
    /// <remarks>責務: 移動の積み重ねを「接続履歴 › 車両 › セッション › 解析」の文字に変えます。変わったときだけ入れ替えます。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    private void RenderBreadcrumb(ConnectionHistoryState state)
    {
        var labels = new List<string> { LocalizedText.Get("history.title") };
        foreach (var route in _navigation.Path)
        {
            labels.Add(route switch
            {
                WindowsConnectionHistoryRoute.VehicleSessions vehicle =>
                    state.VehicleGroups.FirstOrDefault(group => group.Id == vehicle.GroupID) is { } group
                        ? GroupName(group)
                        : LocalizedText.Get("history.vehicle.unassigned"),
                WindowsConnectionHistoryRoute.SessionDetail detail =>
                    state.Sessions.FirstOrDefault(session => session.Id == detail.SessionID) is { } session
                        ? DateText.DateAndMinute(session.StartedAt)
                        : "—",
                _ => LocalizedText.Get("analysis.open"),
            });
        }

        var key = string.Join("\u001F", labels);
        Breadcrumb.Visibility = labels.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (key != _breadcrumbKey)
        {
            _breadcrumbKey = key;
            Breadcrumb.ItemsSource = labels;
        }
    }

    /// <summary>パンくずの項目を押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="args">押した項目。</param>
    private void OnBreadcrumbItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args) => NavigateTo(args.Index);

    /// <summary>
    /// 移動の積み重ねを指定した深さまで戻します。
    /// </summary>
    /// <remarks>責務: 解析から離れるときは解析を閉じたことをモデルへ伝え、端末のデータの変化を履歴へ反映させます。</remarks>
    /// <param name="depth">残す深さ（0 で車両のアーカイブ）。</param>
    private void NavigateTo(int depth)
    {
        if (depth < 0 || depth >= _navigation.Path.Count)
        {
            return;
        }

        var leavesAnalysis = _navigation.Path.Skip(depth).Any(route => route is WindowsConnectionHistoryRoute.SessionAnalysis);
        if (leavesAnalysis)
        {
            _analysis.Send(new SessionLogAnalysisAction.Dismissed());
            _analysisView = null;
        }

        _navigation.Truncate(depth);
        if (leavesAnalysis)
        {
            _model.Send(new ConnectionHistoryAction.LocalDataChanged());
        }

        Render();
    }

    /// <summary>セッションの詳細を開きます。</summary>
    /// <param name="sessionID">セッション。</param>
    private void OpenDetail(ConnectionSessionID sessionID)
    {
        if (_navigation.Current is WindowsConnectionHistoryRoute.SessionAnalysis)
        {
            NavigateTo(_navigation.Path.Count - 1);
        }

        _navigation.ShowDetail(sessionID);
        Render();
    }

    /// <summary>解析を開きます（iOS 版と同じく、Raw が端末になければダウンロードの確認から）。</summary>
    /// <param name="session">セッション。</param>
    private void OpenAnalysis(ConnectionSession session)
    {
        if (_navigation.Current is not WindowsConnectionHistoryRoute.SessionDetail detail || detail.SessionID != session.Id)
        {
            OpenDetail(session.Id);
        }

        _analysis.Send(new SessionLogAnalysisAction.SessionSelected(session));
        if (session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Removed)
        {
            _navigation.Push(new WindowsConnectionHistoryRoute.SessionAnalysis(session.Id));
            Render();
        }
    }

    /// <summary>
    /// 車両別のアーカイブ（最初の画面）を描きます。
    /// </summary>
    /// <remarks>責務: 見出しと合計、失敗・空の表示、進行中のセッション、車両ごとのカードを作ります。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    private void RenderArchive(ConnectionHistoryState state)
    {
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        summary.Children.Add(new MetricBlock(NumberText.Integer(state.Sessions.Count), LocalizedText.Get("history.summary.total"), HorizontalAlignment.Right));
        summary.Children.Add(new MetricBlock(
            DurationText.HoursAndMinutes(state.TotalRecordedDuration),
            LocalizedText.Get("history.summary.duration"),
            HorizontalAlignment.Right,
            DesignTokens.Foreground(Tone.Accent)));
        Page.Children.Add(SurfaceCard.Create(new ScreenHeader(
            FluentSymbol.History,
            LocalizedText.Get("history.eyebrow"),
            LocalizedText.Get("history.title"),
            LocalizedText.Get("history.subtitle"),
            summary), padding: 24));

        if (state.Phase == ConnectionHistoryStatePhase.Failed)
        {
            var failure = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
            failure.Children.Add(new FontIcon { Glyph = FluentSymbol.Storage, FontSize = 34, Foreground = DesignTokens.Foreground(Tone.Caution) });
            failure.Children.Add(Typography.Strong(LocalizedText.Get("history.error.storage")));
            var retry = new Button { Content = LocalizedText.Get("history.error.retry"), Style = DesignTokens.Style("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Center };
            retry.Click += (_, _) => _model.Send(new ConnectionHistoryAction.RefreshRequested());
            failure.Children.Add(retry);
            Page.Children.Add(SurfaceCard.Create(failure, Tone.Caution, 34, tinted: true));
            return;
        }

        if (state.Sessions.Count == 0)
        {
            Page.Children.Add(SurfaceCard.Create(new ContentUnavailablePanel(
                FluentSymbol.Road,
                LocalizedText.Get("history.empty.title"),
                LocalizedText.Get("history.empty.body"))));
            return;
        }

        if (state.ActiveSessions.Count > 0)
        {
            Page.Children.Add(new GlyphLabel(FluentSymbol.Signal, LocalizedText.Get("history.active.title"), "SectionTitleTextStyle"));
            foreach (var session in state.ActiveSessions)
            {
                Page.Children.Add(ActiveSessionCard(session));
            }
        }

        Page.Children.Add(new GlyphLabel(FluentSymbol.Car, LocalizedText.Get("history.archive.title"), "SectionTitleTextStyle"));
        if (state.VehicleGroups.Count == 0)
        {
            var empty = Typography.Text(LocalizedText.Get("history.archive.empty"), "SecondaryBodyTextStyle");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            Page.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(24), Child = empty });
            return;
        }

        var grid = new AdaptiveGrid { MinItemWidth = 300, Spacing = 14 };
        foreach (var group in state.VehicleGroups)
        {
            grid.Children.Add(VehicleGroupButton(group));
        }

        Page.Children.Add(grid);
    }

    /// <summary>進行中のセッションのカードを作ります。</summary>
    /// <param name="session">セッション。</param>
    /// <returns>カード。</returns>
    private Border ActiveSessionCard(ConnectionSession session)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconTile.Create(FluentSymbol.Pulse, Tone.Success, 58));
        var texts = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(VehicleName(session), "CardTitleTextStyle"));
        texts.Children.Add(Typography.Strong(LocalizedText.Get("history.active.recording"), "CaptionTextBlockStyle", DesignTokens.Foreground(Tone.Success)));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var elapsed = new MetricBlock(
            DurationText.Timer(session.StartedAt, DateTimeOffset.Now),
            LocalizedText.Get("history.active.elapsed"),
            HorizontalAlignment.Right,
            DesignTokens.Foreground(Tone.Success),
            "TitleTextBlockStyle");
        _timers.Add(((TextBlock)elapsed.Children[0], session.StartedAt));
        Grid.SetColumn(elapsed, 2);
        grid.Children.Add(elapsed);
        var started = new MetricBlock(DateText.DateAndMinute(session.StartedAt), LocalizedText.Get("history.started"), HorizontalAlignment.Right, valueStyleKey: "BodyStrongTextBlockStyle");
        Grid.SetColumn(started, 3);
        grid.Children.Add(started);
        return SurfaceCard.Create(grid, Tone.Success, 20, tinted: true);
    }

    /// <summary>車両のカード（押すとその車両のセッション一覧へ進む）を作ります。</summary>
    /// <param name="group">車両のまとまり。</param>
    /// <returns>ボタン。</returns>
    private Button VehicleGroupButton(ConnectionHistoryVehicleGroup group)
    {
        var tone = group.InterruptedCount > 0 ? Tone.Caution : Tone.Accent;
        var stack = new StackPanel { Spacing = 14 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(IconTile.Create(FluentSymbol.Car, tone, 48));
        var texts = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(GroupName(group), "CardTitleTextStyle", wraps: false));
        if (group.Vehicle?.DisplayIdentifier is { Length: > 0 } identifier)
        {
            texts.Children.Add(Typography.Text(identifier, "MonospaceCaptionTextStyle", wraps: false));
        }

        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        var chevron = new FontIcon { Glyph = FluentSymbol.ChevronRight, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        head.Children.Add(chevron);
        stack.Children.Add(head);
        stack.Children.Add(GroupMetrics(group));
        var button = new Button
        {
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(14),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, GroupName(group));
        button.Click += (_, _) =>
        {
            _navigation.Push(new WindowsConnectionHistoryRoute.VehicleSessions(group.Id));
            Render();
        };
        return button;
    }

    /// <summary>車両のまとまりの数値（件数・時間・距離・中断）を並べます。</summary>
    /// <param name="group">車両のまとまり。なければ 0 を出す。</param>
    /// <returns>数値の行。</returns>
    private static StackPanel GroupMetrics(ConnectionHistoryVehicleGroup? group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        row.Children.Add(new MetricBlock(NumberText.Integer(group?.SessionCount ?? 0), LocalizedText.Get("history.vehicle.sessions"), valueStyleKey: "BodyStrongTextBlockStyle"));
        row.Children.Add(new MetricBlock(DurationText.HoursAndMinutes(group?.TotalDuration ?? 0), LocalizedText.Get("history.summary.duration"), valueStyleKey: "BodyStrongTextBlockStyle"));
        row.Children.Add(new MetricBlock(DistanceText.Format(group?.TotalDistanceKilometers), LocalizedText.Get("history.summary.distance"), valueStyleKey: "BodyStrongTextBlockStyle"));
        if (group is { InterruptedCount: > 0 } warned)
        {
            var badge = StatusPill.Create(FluentSymbol.Warning, NumberText.Integer(warned.InterruptedCount), Tone.Caution);
            ToolTipService.SetToolTip(badge, LocalizedText.Get("history.warning.interrupted"));
            row.Children.Add(badge);
        }

        return row;
    }

    /// <summary>
    /// 車両のセッション一覧を描きます。
    /// </summary>
    /// <remarks>責務: 車両の見出し、絞り込み、セッションの行を作ります。左右 2 列のとき（<paramref name="selectedID"/> の行を強調）は狭い幅に合わせて詰めます。</remarks>
    /// <param name="host">描く先。</param>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="groupID">車両のまとまり。</param>
    /// <param name="selectedID">詳細を出しているセッション。なければ null。</param>
    private void RenderSessionList(StackPanel host, ConnectionHistoryState state, ConnectionHistoryVehicleGroupID groupID, ConnectionSessionID? selectedID)
    {
        var compact = !ReferenceEquals(host, Page);
        host.Children.Clear();
        var group = state.VehicleGroups.FirstOrDefault(item => item.Id == groupID);
        var sessions = state.FilteredSessions(groupID);
        var identityTexts = new StackPanel { Spacing = 4 };
        identityTexts.Children.Add(Typography.Text(LocalizedText.Get("history.archive.title"), "EyebrowTextStyle"));
        identityTexts.Children.Add(Typography.Text(group is null ? LocalizedText.Get("history.vehicle.unassigned") : GroupName(group), compact ? "SubtitleTextBlockStyle" : "PageTitleTextStyle"));
        if (group?.Vehicle?.DisplayIdentifier is { Length: > 0 } identifier)
        {
            identityTexts.Children.Add(Typography.Text(identifier, "MonospaceCaptionTextStyle"));
        }

        identityTexts.Children.Add(GroupMetrics(group));
        host.Children.Add(compact ? identityTexts : SurfaceCard.Create(identityTexts, padding: 20));
        host.Children.Add(FilterPanel(state, compact));
        if (sessions.Count == 0)
        {
            host.Children.Add(new ContentUnavailablePanel(FluentSymbol.Search, LocalizedText.Get("history.filter.empty"), LocalizedText.Get("history.filter.empty.body")));
            return;
        }

        var list = new StackPanel { Spacing = compact ? 6 : 10 };
        foreach (var session in sessions)
        {
            list.Children.Add(SessionListItem(state, session, compact, session.Id == selectedID));
        }

        host.Children.Add(list);
    }

    /// <summary>絞り込みと並べ替えの面を作ります。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="compact">狭い一覧の中に置くなら true。</param>
    /// <returns>絞り込みの面。</returns>
    private Border FilterPanel(ConnectionHistoryState state, bool compact)
    {
        var stack = new StackPanel { Spacing = 13 };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new GlyphLabel(FluentSymbol.Filter, LocalizedText.Get("history.filter.title"), "BodyStrongTextBlockStyle"));
        if (state.HasActiveFilters)
        {
            var reset = new HyperlinkButton { Content = LocalizedText.Get("history.filter.reset"), Padding = new Thickness(6, 2, 6, 2) };
            reset.Click += (_, _) => _model.Send(new ConnectionHistoryAction.FiltersReset());
            Grid.SetColumn(reset, 1);
            head.Children.Add(reset);
        }

        stack.Children.Add(head);
        var controls = new AdaptiveGrid { MinItemWidth = compact ? 160 : 220, Spacing = compact ? 10 : 14 };
        var closedStarts = state.ClosedSessions.Select(session => session.StartedAt).ToList();
        controls.Children.Add(DateFilter(
            "history.filter.from",
            state.FilterStartDate,
            closedStarts.Count > 0 ? closedStarts.Min() : DateTimeOffset.Now,
            date => _model.Send(new ConnectionHistoryAction.FilterStartDateChanged(date))));
        controls.Children.Add(DateFilter(
            "history.filter.to",
            state.FilterEndDate,
            closedStarts.Count > 0 ? closedStarts.Max() : DateTimeOffset.Now,
            date => _model.Send(new ConnectionHistoryAction.FilterEndDateChanged(date))));
        var reason = new ComboBox { Header = LocalizedText.Get("history.filter.reason"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var filter in ConnectionHistoryEndReasonFilterRawValue.AllCases)
        {
            reason.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(filter.HistoryLocalizationKey()), Tag = filter });
        }

        reason.SelectedIndex = ConnectionHistoryEndReasonFilterRawValue.AllCases.ToList().IndexOf(state.EndReasonFilter);
        reason.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && reason.SelectedItem is ComboBoxItem { Tag: ConnectionHistoryEndReasonFilter filter })
            {
                _model.Send(new ConnectionHistoryAction.EndReasonFilterChanged(filter));
            }
        };
        controls.Children.Add(reason);
        var sort = new ComboBox { Header = LocalizedText.Get("history.sort.title"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var order in ConnectionHistorySortOrderRawValue.AllCases)
        {
            sort.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(order.HistoryLocalizationKey()), Tag = order });
        }

        sort.SelectedIndex = ConnectionHistorySortOrderRawValue.AllCases.ToList().IndexOf(state.SortOrder);
        sort.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && sort.SelectedItem is ComboBoxItem { Tag: ConnectionHistorySortOrder order })
            {
                _model.Send(new ConnectionHistoryAction.SortOrderChanged(order));
            }
        };
        controls.Children.Add(sort);
        stack.Children.Add(controls);
        return new Border
        {
            Background = DesignTokens.SubtleBackground(Tone.Accent),
            CornerRadius = new CornerRadius(compact ? 10 : 14),
            Padding = new Thickness(compact ? 13 : 17),
            Child = stack,
        };
    }

    /// <summary>日付の絞り込み（チェックで有効にし、カレンダーで選ぶ）を作ります。</summary>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="date">今の日付。なければ絞り込まない。</param>
    /// <param name="fallback">有効にしたときの最初の日付。</param>
    /// <param name="update">変わったときの送り先。</param>
    /// <returns>日付の絞り込み。</returns>
    private StackPanel DateFilter(string titleKey, DateTimeOffset? date, DateTimeOffset fallback, Action<DateTimeOffset?> update)
    {
        var stack = new StackPanel { Spacing = 6 };
        var check = new CheckBox { Content = LocalizedText.Get(titleKey), IsChecked = date is not null };
        check.Click += (_, _) => update(check.IsChecked == true ? fallback : null);
        stack.Children.Add(check);
        if (date is { } value)
        {
            var picker = new CalendarDatePicker { Date = value, HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.DateChanged += (_, args) =>
            {
                if (!_isRendering && args.NewDate is { } newDate)
                {
                    update(newDate);
                }
            };
            stack.Children.Add(picker);
        }

        return stack;
    }

    /// <summary>
    /// セッションの 1 行を作ります。
    /// </summary>
    /// <remarks>責務: 押すと詳細を開く行を作り、右クリックで開く操作のメニューを付けます。1 列のときはアップロードと Raw の解放のボタンも横に並べます。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <param name="compact">狭い一覧の中に置くなら true。</param>
    /// <param name="selected">詳細を出しているなら true。</param>
    /// <returns>行。</returns>
    private Grid SessionListItem(ConnectionHistoryState state, ConnectionSession session, bool compact, bool selected)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new Button
        {
            Content = SessionRow(session, compact),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(compact ? 12 : 15),
            CornerRadius = new CornerRadius(compact ? 8 : 12),
            ContextFlyout = SessionMenu(state, session),
        };
        if (selected)
        {
            row.Background = DesignTokens.SubtleBackground(Tone.Accent);
            row.BorderBrush = DesignTokens.Brush("RevTorqueAccentBrush");
            row.BorderThickness = new Thickness(1.5);
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, DateText.DateAndMinute(session.StartedAt));
        row.Click += (_, _) => OpenDetail(session.Id);
        grid.Children.Add(row);
        if (compact)
        {
            return grid;
        }

        if (CanUpload(state, session))
        {
            var upload = UploadButton(state, session);
            upload.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(upload, 1);
            grid.Children.Add(upload);
        }

        if (CanReleaseRawCache(session))
        {
            var release = ReleaseRawButton(session, _model.Send);
            release.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(release, 2);
            grid.Children.Add(release);
        }

        return grid;
    }

    /// <summary>セッションの右クリックメニューを作ります。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <returns>メニュー。</returns>
    private MenuFlyout SessionMenu(ConnectionHistoryState state, ConnectionSession session)
    {
        var menu = new ContextMenu()
            .Item(FluentSymbol.Document, LocalizedText.Get("windows.history.open_detail"), () => OpenDetail(session.Id))
            .Item(FluentSymbol.Chart, LocalizedText.Get("analysis.open"), () => OpenAnalysis(session));
        if (CanUpload(state, session) && state.UploadingSessionID is null)
        {
            menu.Item(FluentSymbol.Upload, LocalizedText.Get("history.upload.button"), () =>
            {
                UiLog.SessionUploadRequested(UiLog.Logger);
                _model.Send(new ConnectionHistoryAction.SessionUploadRequested(session.Id));
            });
        }

        if (CanReleaseRawCache(session))
        {
            menu.Item(FluentSymbol.Storage, LocalizedText.Get("history.raw.remove.button"), () => _model.Send(new ConnectionHistoryAction.LocalRawRemovalRequested(session.Id)), Tone.Caution);
        }

        if (session.EndedAt is not null && state.DeletingSessionID != session.Id)
        {
            menu.Separator().Item(FluentSymbol.Delete, LocalizedText.Get("history.delete.button"), () => _model.Send(new ConnectionHistoryAction.SessionDeletionRequested(session.Id)), Tone.Danger);
        }

        return menu.Build();
    }

    /// <summary>手動アップロードのボタンを作ります（アップロード中は進み具合を出す）。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <returns>ボタン。</returns>
    internal Button UploadButton(ConnectionHistoryState state, ConnectionSession session) => MakeUploadButton(state, session, _model.Send);

    /// <summary>手動アップロードのボタンを作ります（アップロード中は進み具合を出す）。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <param name="send">操作の送り先。</param>
    /// <returns>ボタン。</returns>
    internal static Button MakeUploadButton(ConnectionHistoryState state, ConnectionSession session, Action<ConnectionHistoryAction> send)
    {
        var upload = new Button { Style = DesignTokens.Style("AccentButtonStyle"), IsEnabled = state.UploadingSessionID is null };
        if (state.UploadingSessionID == session.Id)
        {
            var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, MinWidth = 112 };
            progress.Children.Add(new ProgressBar { Value = (state.UploadProgress ?? 0) * 100, Width = 72, VerticalAlignment = VerticalAlignment.Center });
            progress.Children.Add(Typography.Text(NumberText.Percent(state.UploadProgress ?? 0), "CaptionTextBlockStyle", wraps: false));
            upload.Content = progress;
        }
        else
        {
            upload.Content = new GlyphLabel(FluentSymbol.Upload, LocalizedText.Get("history.upload.button"));
        }

        upload.Click += (_, _) => send(new ConnectionHistoryAction.SessionUploadRequested(session.Id));
        return upload;
    }

    /// <summary>端末の Raw を解放するボタンを作ります。</summary>
    /// <param name="session">セッション。</param>
    /// <param name="send">操作の送り先。</param>
    /// <returns>ボタン。</returns>
    internal static Button ReleaseRawButton(ConnectionSession session, Action<ConnectionHistoryAction> send)
    {
        var release = new Button
        {
            Content = new GlyphLabel(FluentSymbol.Storage, LocalizedText.Get("history.raw.remove.button"), foreground: DesignTokens.Foreground(Tone.Caution)),
        };
        release.Click += (_, _) => send(new ConnectionHistoryAction.LocalRawRemovalRequested(session.Id));
        return release;
    }

    /// <summary>セッションの行の中身を作ります。</summary>
    /// <param name="session">セッション。</param>
    /// <param name="compact">狭い一覧の中に置くなら true（ID を省き、時間と距離を下の段へ回す）。</param>
    /// <returns>行の中身。</returns>
    private static Grid SessionRow(ConnectionSession session, bool compact)
    {
        var grid = new Grid { ColumnSpacing = compact ? 11 : 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        FrameworkElement status = session.Status == ConnectionSessionStatus.Completed
            ? new FontIcon { Glyph = FluentSymbol.Completed, FontSize = 18, Foreground = DesignTokens.Brush("RevTorqueBlueBrush"), VerticalAlignment = VerticalAlignment.Center }
            : new WarningTriangleIcon(18);
        grid.Children.Add(status);
        var texts = new StackPanel { Spacing = 4 };
        texts.Children.Add(Typography.Strong(DateText.DateAndMinute(session.StartedAt)));
        if (!compact)
        {
            texts.Children.Add(new GlyphLabel(FluentSymbol.Person, AcquisitionDeviceText(session), "SecondaryCaptionTextStyle"));
        }

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        badges.Children.Add(CloudStatusPill(session.RawLogSummary.CloudState));
        badges.Children.Add(LocalStatusPill(session.RawLogSummary.LocalState));
        texts.Children.Add(badges);
        var reason = Typography.Strong(
            LocalizedText.Get(session.EndReason?.HistoryLocalizationKey() ?? ConnectionHistoryEndReasonFilter.All.HistoryLocalizationKey()),
            "CaptionTextBlockStyle",
            session.Status == ConnectionSessionStatus.Completed ? null : DesignTokens.Foreground(Tone.Caution));
        texts.Children.Add(reason);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var measures = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        measures.Children.Add(new GlyphLabel(FluentSymbol.Clock, DurationText.HoursAndMinutes(SessionDuration(session)), "MonospaceCaptionTextStyle"));
        measures.Children.Add(new GlyphLabel(FluentSymbol.Road, DistanceText.Format(session.RecordedDistanceKilometers), "MonospaceCaptionTextStyle"));
        Grid.SetColumn(measures, 2);
        grid.Children.Add(measures);
        if (!compact)
        {
            var id = Typography.Text(session.Id.RawValue.ToString("N")[..8].ToUpperInvariant(), "MonospaceCaptionTextStyle", wraps: false);
            id.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(id, 3);
            grid.Children.Add(id);
        }

        return grid;
    }

    /// <summary>iCloud の状態の印を作ります。</summary>
    /// <param name="state">iCloud の状態。</param>
    /// <returns>印。</returns>
    private static Border CloudStatusPill(ConnectionSessionCloudSyncState state) => state switch
    {
        ConnectionSessionCloudSyncState.Pending => StatusPill.Create(FluentSymbol.Upload, LocalizedText.Get("history.raw.cloud.pending"), Tone.Caution),
        ConnectionSessionCloudSyncState.Uploaded => StatusPill.Create(FluentSymbol.Cloud, LocalizedText.Get("history.raw.cloud.uploaded"), Tone.Success),
        ConnectionSessionCloudSyncState.Failed => StatusPill.Create(FluentSymbol.Error, LocalizedText.Get("history.raw.cloud.failed"), Tone.Danger),
        _ => StatusPill.Create(FluentSymbol.Cloud, LocalizedText.Get("history.raw.cloud.not_uploaded"), Tone.Neutral),
    };

    /// <summary>端末の Raw の状態の印を作ります。</summary>
    /// <param name="state">端末の Raw の状態。</param>
    /// <returns>印。</returns>
    private static Border LocalStatusPill(ConnectionSessionLocalRawState state) => state switch
    {
        ConnectionSessionLocalRawState.Empty => StatusPill.Create(null, LocalizedText.Get("history.raw.local.empty"), Tone.Neutral),
        ConnectionSessionLocalRawState.Available => StatusPill.Create(FluentSymbol.Completed, LocalizedText.Get("history.raw.local.available"), Tone.Accent),
        _ => StatusPill.Create(FluentSymbol.Download, LocalizedText.Get("history.raw.local.removed"), Tone.Neutral),
    };

    /// <summary>状態に合わせて確認のダイアログを出し入れします。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="analysis">解析の状態。</param>
    private void SyncDialogs(ConnectionHistoryState state, SessionLogAnalysisState analysis)
    {
        var root = XamlRoot;
        _rawRemovalDialog.Sync(state.RawRemovalPrompt is not null, state.RawRemovalPrompt, root, () =>
        {
            var prompt = state.RawRemovalPrompt!;
            var safe = prompt.Decision == ConnectionSessionLocalRemovalDecision.Safe;
            return Alert(
                safe ? "history.raw.remove.safe.title" : "history.raw.remove.warning.title",
                LocalizedText.Format(safe ? "history.raw.remove.safe.message" : "history.raw.remove.warning.message", prompt.RecordCount, ByteCountText.Format(prompt.ByteCount)),
                "history.raw.remove.confirm",
                "history.raw.remove.cancel",
                destructive: true);
        }, result =>
        {
            if (result == ContentDialogResult.Primary)
            {
                UiLog.RawRemovalConfirmed(UiLog.Logger);
            }

            _model.Send(result == ContentDialogResult.Primary
                ? new ConnectionHistoryAction.LocalRawRemovalConfirmed()
                : new ConnectionHistoryAction.LocalRawRemovalCancelled());
        });

        _deletionDialog.Sync(state.SessionDeletionPrompt is not null, state.SessionDeletionPrompt, root, () =>
        {
            var prompt = state.SessionDeletionPrompt!;
            return Alert(
                "history.delete.warning.title",
                LocalizedText.Format("history.delete.warning.message", prompt.RecordCount, ByteCountText.Format(prompt.ByteCount)),
                "history.delete.confirm",
                "history.delete.cancel",
                destructive: true);
        }, result =>
        {
            if (result == ContentDialogResult.Primary)
            {
                UiLog.SessionDeleteConfirmed(UiLog.Logger);
            }

            _model.Send(result == ContentDialogResult.Primary
                ? new ConnectionHistoryAction.SessionDeletionConfirmed()
                : new ConnectionHistoryAction.SessionDeletionCancelled());
        });

        _deletionFailureDialog.Sync(state.SessionDeletionFailureKey is not null, state.SessionDeletionFailureKey, root,
            () => Alert("history.delete.error.title", LocalizedText.Get("history.delete.error"), null, "history.delete.error.dismiss"),
            _ => _model.Send(new ConnectionHistoryAction.SessionDeletionFailureDismissed()));

        _stopReviewDialog.Sync(state.StopReviewPrompt is not null, state.StopReviewPrompt, root, () =>
            Alert(
                "history.stop_review.title",
                LocalizedText.Get(state.StopReviewPrompt!.ObservedReason == ConnectionSessionEndReason.VehicleNoResponse
                    ? "history.stop_review.message.no_response"
                    : "history.stop_review.message.connection_lost"),
                "history.stop_review.confirm",
                "history.stop_review.cancel"),
            result => _model.Send(result == ContentDialogResult.Primary
                ? new ConnectionHistoryAction.StopReviewConfirmed()
                : new ConnectionHistoryAction.StopReviewCancelled()));

        _stopReviewFailureDialog.Sync(state.StopReviewFailureKey is not null, state.StopReviewFailureKey, root,
            () => Alert("history.stop_review.error.title", LocalizedText.Get("history.stop_review.error"), null, "history.stop_review.error.dismiss"),
            _ => _model.Send(new ConnectionHistoryAction.StopReviewFailureDismissed()));

        _uploadFailureDialog.Sync(state.UploadFailureSessionID is not null, state.UploadFailureSessionID, root,
            () => Alert("history.upload.error.title", LocalizedText.Get("history.upload.error.message"), null, "history.upload.error.dismiss"),
            _ => _model.Send(new ConnectionHistoryAction.SessionUploadFailureDismissed()));

        var detailSessionID = (_navigation.Current as WindowsConnectionHistoryRoute.SessionDetail)?.SessionID;
        var showsDownload = analysis.DownloadPrompt is { } download && download.SessionID == detailSessionID;
        _downloadDialog.Sync(showsDownload, analysis.DownloadPrompt, root,
            () => Alert(
                "analysis.download.confirm.title",
                LocalizedText.Format("analysis.download.confirm.message", ByteCountText.Format(analysis.DownloadPrompt!.ByteCount)),
                "analysis.download.confirm",
                "analysis.download.cancel"),
            result =>
            {
                if (result == ContentDialogResult.Primary)
                {
                    _analysis.Send(new SessionLogAnalysisAction.DownloadConfirmed());
                    if (detailSessionID is { } id)
                    {
                        _navigation.Push(new WindowsConnectionHistoryRoute.SessionAnalysis(id));
                        Render();
                    }
                }
                else
                {
                    _analysis.Send(new SessionLogAnalysisAction.DownloadCancelled());
                }
            });
    }

    /// <summary>確認・知らせのダイアログを作ります。</summary>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="message">本文。</param>
    /// <param name="primaryKey">主のボタンのキー。なければ null。</param>
    /// <param name="closeKey">閉じるボタンのキー。</param>
    /// <param name="destructive">取り消せない操作なら true（主のボタンを赤にする）。</param>
    /// <returns>ダイアログ。</returns>
    private static ContentDialog Alert(string titleKey, string message, string? primaryKey, string closeKey, bool destructive = false)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizedText.Get(titleKey),
            Content = Typography.Text(message, "BodyTextBlockStyle"),
            CloseButtonText = LocalizedText.Get(closeKey),
            DefaultButton = ContentDialogButton.Close,
        };
        if (primaryKey is not null)
        {
            dialog.PrimaryButtonText = LocalizedText.Get(primaryKey);
            dialog.PrimaryButtonStyle = DesignTokens.Style(destructive ? "DangerButtonStyle" : "AccentButtonStyle");
        }

        return dialog;
    }

    /// <summary>手動アップロードできるかどうかを返します（自動アップロードが無効で、終わっていて、端末に Raw があり、まだ上げていない）。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <returns>できるなら true。</returns>
    internal static bool CanUpload(ConnectionHistoryState state, ConnectionSession session) =>
        !state.AutomaticUploadEnabled
        && session.EndedAt is not null
        && session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Removed
        && session.RawLogSummary.CloudState != ConnectionSessionCloudSyncState.Uploaded;

    /// <summary>端末の Raw を解放できるかどうかを返します（iCloud に上がっていて、目録の要約がある）。</summary>
    /// <param name="session">セッション。</param>
    /// <returns>できるなら true。</returns>
    internal static bool CanReleaseRawCache(ConnectionSession session) =>
        session.EndedAt is not null
        && session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Available
        && session.RawLogSummary.RecordCount > 0
        && session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded
        && !string.IsNullOrEmpty(session.RawLogSummary.ManifestDigest);

    /// <summary>車両のまとまりの名前です。</summary>
    /// <param name="group">車両のまとまり。</param>
    /// <returns>名前。</returns>
    private static string GroupName(ConnectionHistoryVehicleGroup group) =>
        group.Vehicle?.Name is { Length: > 0 } name ? name : LocalizedText.Get("history.vehicle.unassigned");

    /// <summary>セッションの車両の名前です。</summary>
    /// <param name="session">セッション。</param>
    /// <returns>名前。</returns>
    public static string VehicleName(ConnectionSession session) =>
        session.Vehicle?.Name is { Length: > 0 } name ? name : LocalizedText.Get("history.vehicle.pending");

    /// <summary>取得した端末の文字です。</summary>
    /// <param name="session">セッション。</param>
    /// <returns>文字。</returns>
    public static string AcquisitionDeviceText(ConnectionSession session) =>
        session.AcquisitionDevice is { } device
            ? $"{device.Name} ({device.Platform.ToRawValue()})"
            : LocalizedText.Get("history.device.unknown");

    /// <summary>セッションの長さ（秒）です。</summary>
    /// <param name="session">セッション。</param>
    /// <returns>秒。</returns>
    private static double SessionDuration(ConnectionSession session) =>
        Math.Max(0, ((session.EndedAt ?? session.StartedAt) - session.StartedAt).TotalSeconds);
}
