using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
/// 一覧と詳細は <see cref="AdaptiveSplit"/>：Expanded 以上は左に一覧・右に詳細を並べ、Compact・Medium は片方だけ（詳細を開くと一覧を隠し、戻ると一覧へ）。
/// 幅クラスが変わっても移動の積み重ね（パンくず）は保たれ、要素は作り直しません。
/// 一覧は仮想化し、行は使い回します。モデルの知らせは束ね、アーカイブ・詳細は元のデータが変わったときだけ組み直します。
/// </remarks>
public sealed partial class WindowsConnectionHistoryView : UserControl, IBackNavigable, ISessionRowHost
{
    /// <summary>一覧の幅です（Expanded 以上で、左に置く一覧の列）。</summary>
    private const double ListColumnWidth = 400;

    /// <summary>詳細の中身の最大の幅です。</summary>
    private const double DetailMaxWidth = 1000;

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

    /// <summary>経過時間の表示と開始時刻です。</summary>
    private readonly List<(MetricBlock Metric, DateTimeOffset StartedAt)> _timers = [];

    /// <summary>操作中の作り直しを待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>モデルの知らせを束ね、1 フレームに 1 回へする仕組みです。</summary>
    private readonly UiUpdateThrottle _throttle;

    /// <summary>車両別のアーカイブの面です。</summary>
    private readonly PageScaffold _archive = new();

    /// <summary>解析の面です。</summary>
    private readonly Border _analysisHost = new() { Visibility = Visibility.Collapsed };

    /// <summary>一覧（副・左）と詳細（主）の格子です。</summary>
    private readonly AdaptiveSplit _split = new()
    {
        SecondaryFirst = true,
        SecondaryWidth = ListColumnWidth,
        CollapseBelow = WidthClass.Expanded,
        CollapseMode = SplitCollapseMode.Single,
        Visibility = Visibility.Collapsed,
    };

    /// <summary>一覧の面です。</summary>
    private readonly Grid _listPane = new() { RowSpacing = Space.M };

    /// <summary>一覧の車両名です。</summary>
    private readonly TextBlock _listTitle = Typography.Text(string.Empty, TextStyles.SectionTitle);

    /// <summary>一覧の車両の識別子です。</summary>
    private readonly TextBlock _listIdentifier = Typography.Text(string.Empty, TextStyles.Monospace);

    /// <summary>一覧の車両の数値です。</summary>
    private readonly MetricsStrip _listMetrics = new();

    /// <summary>絞り込みの面です。</summary>
    private readonly Expander _filter = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>絞り込みを解除するリンクです。</summary>
    private readonly HyperlinkButton _filterReset = new() { Content = LocalizedText.Get("history.filter.reset"), Padding = new Thickness(Space.S, Space.XXS, Space.S, Space.XXS) };

    /// <summary>開始日の絞り込みのチェックです。</summary>
    private readonly CheckBox _fromCheck = new() { Content = LocalizedText.Get("history.filter.from") };

    /// <summary>開始日の絞り込みの日付です。</summary>
    private readonly CalendarDatePicker _fromPicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>終了日の絞り込みのチェックです。</summary>
    private readonly CheckBox _toCheck = new() { Content = LocalizedText.Get("history.filter.to") };

    /// <summary>終了日の絞り込みの日付です。</summary>
    private readonly CalendarDatePicker _toPicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>終わった理由の絞り込みです。</summary>
    private readonly ComboBox _reasonCombo = new() { Header = LocalizedText.Get("history.filter.reason"), HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>並べ替えです。</summary>
    private readonly ComboBox _sortCombo = new() { Header = LocalizedText.Get("history.sort.title"), HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>絞り込みで 1 件もないときの表示です。</summary>
    private readonly ContentUnavailablePanel _filterEmpty = new(FluentSymbol.Search, LocalizedText.Get("history.filter.empty"), LocalizedText.Get("history.filter.empty.body"));

    /// <summary>セッションの行を出す、仮想化した一覧です。</summary>
    private readonly VirtualList<ConnectionSession> _list;

    /// <summary>作った行です（状態が変わったときに、見えている行の値だけを合わせるため）。</summary>
    private readonly HashSet<SessionRowView> _rows = [];

    /// <summary>詳細の面（縦にスクロールする）です。</summary>
    private readonly ScrollViewer _detailScroller;

    /// <summary>詳細の中身を入れる場所です。</summary>
    private readonly ContentControl _detailHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsTabStop = false };

    /// <summary>一覧から選ぶように促す表示です。</summary>
    private readonly ContentUnavailablePanel _selectPrompt = new(FluentSymbol.Document, LocalizedText.Get("windows.history.select_session"));

    /// <summary>絞り込みの終わった理由の選択肢です。</summary>
    private readonly IReadOnlyList<ConnectionHistoryEndReasonFilter> _reasonFilters = ConnectionHistoryEndReasonFilterRawValue.AllCases.ToList();

    /// <summary>並べ替えの選択肢です。</summary>
    private readonly IReadOnlyList<ConnectionHistorySortOrder> _sortOrders = ConnectionHistorySortOrderRawValue.AllCases.ToList();

    /// <summary>描いている最中かどうかです（選択の変化を利用者の操作と区別するため）。</summary>
    private bool _isSyncing;

    /// <summary>今の幅クラスです。</summary>
    private WidthClass _widthClass = ResponsiveLayout.Current;

    /// <summary>一覧の行を広く（端末・ID・ボタン付きで）出すかどうかです。</summary>
    private bool _wideRows;

    /// <summary>表示中の解析画面です。</summary>
    private WindowsSessionLogAnalysisView? _analysisView;

    /// <summary>最後にパンくずへ出した文字です。</summary>
    private string? _breadcrumbKey;

    /// <summary>最後に詳細へ出したセッションです（切り替えたら詳細を上までスクロールし直す）。</summary>
    private ConnectionSessionID? _detailSessionID;

    /// <summary>一覧で強調しているセッションです。</summary>
    private ConnectionSessionID? _selectedID;

    /// <summary>最後に描いたときに戻れたかどうかです。</summary>
    private bool _lastCanGoBack;

    /// <summary>アーカイブを組んだときのセッションです（変わらなければ組み直さない）。</summary>
    private IReadOnlyList<ConnectionSession>? _archiveSessions;

    /// <summary>アーカイブを組んだときの読込みの段階です。</summary>
    private ConnectionHistoryStatePhase? _archivePhase;

    /// <summary>詳細を組んだときの元です（同じなら組み直さない）。</summary>
    private DetailKey? _detailKey;

    /// <summary>最後に確認の窓を合わせたときのダウンロードの確認です（変わったときだけ合わせ直す）。</summary>
    private SessionLogAnalysisStateDownloadPrompt? _lastDownloadPrompt;

    /// <summary>モデルを受け取って接続履歴の画面を作ります。</summary>
    /// <param name="model">接続履歴のモデル。</param>
    /// <param name="analysis">保存済みログの解析モデル。</param>
    public WindowsConnectionHistoryView(ConnectionHistoryModel model, SessionLogAnalysisModel analysis)
    {
        _model = model;
        _analysis = analysis;
        InitializeComponent();
        _gate = new RenderGate(this);
        _list = new VirtualList<ConnectionSession>(CreateRow, UpdateRow);
        ConfigureList();
        BuildListPane();
        _detailScroller = BuildDetailScroller();
        _split.Secondary = _listPane;
        _split.Primary = _detailScroller;
        Stage.Children.Add(_archive);
        Stage.Children.Add(_split);
        Stage.Children.Add(_analysisHost);

        _timer.Tick += (_, _) =>
        {
            foreach (var (metric, startedAt) in _timers)
            {
                metric.SetValue(DurationText.Timer(startedAt, DateTimeOffset.Now));
            }
        };
        _throttle = new UiUpdateThrottle(this, Render);
        ResponsiveLayout.Bind(this, ApplyLayout);
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            _analysis.PropertyChanged += OnAnalysisChanged;
            _timer.Start();
            Render();
            _model.Send(new ConnectionHistoryAction.RefreshRequested());
        };
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _analysis.PropertyChanged -= OnAnalysisChanged;
            _timer.Stop();
        };
    }

    /// <inheritdoc />
    public event EventHandler? CanGoBackChanged;

    /// <inheritdoc />
    public bool CanGoBack => _navigation.Path.Count > 0;

    /// <inheritdoc />
    ConnectionHistoryState ISessionRowHost.State => _model.State;

    /// <inheritdoc />
    public void GoBack() => NavigateTo(_navigation.Path.Count - 1);

    /// <inheritdoc />
    void ISessionRowHost.OpenDetail(ConnectionSessionID sessionID) => OpenDetail(sessionID);

    /// <inheritdoc />
    void ISessionRowHost.OpenAnalysis(ConnectionSession session) => OpenAnalysis(session);

    /// <inheritdoc />
    void ISessionRowHost.RequestUpload(ConnectionSessionID sessionID)
    {
        UiLog.SessionUploadRequested(UiLog.Logger);
        _model.Send(new ConnectionHistoryAction.SessionUploadRequested(sessionID));
    }

    /// <inheritdoc />
    void ISessionRowHost.Send(ConnectionHistoryAction action) => _model.Send(action);

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 描き直しを頼みます（何度来ても 1 フレームに 1 回にまとまる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => _throttle.Request();

    /// <summary>解析のモデルが変わったときの処理です。</summary>
    /// <remarks>責務: 読込みの進み具合では何もしません（解析の画面が自分で値を差し替える）。ダウンロードの確認が変わったときだけ、確認の窓を合わせ直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnAnalysisChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Equals(_analysis.State.DownloadPrompt, _lastDownloadPrompt))
        {
            _throttle.Request();
        }
    }

    /// <summary>
    /// 幅クラスに合わせて余白と行の見せ方を切り替えます。
    /// </summary>
    /// <remarks>責務: パンくず・一覧と詳細の余白を差し替え、一覧の行を広く見せるか（Medium の 1 列のとき）を切り替えます。一覧と詳細の畳み方は <see cref="AdaptiveSplit"/> が受け持ちます。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        _widthClass = widthClass;
        var gutter = Space.PageGutter(widthClass);
        Breadcrumb.Margin = new Thickness(gutter, Space.S, gutter, 0);
        _split.Padding = new Thickness(gutter, Space.S, gutter, 0);
        _wideRows = widthClass == WidthClass.Medium;
        RefreshRows();
    }

    /// <summary>
    /// 今の行き先の画面を合わせます。
    /// </summary>
    /// <remarks>責務: 行き先に応じて、アーカイブ・一覧と詳細・解析のどれかを出し、変わった部分だけ差し替えます。パンくずとダイアログも合わせます。押している最中は待ちます。</remarks>
    private void Render()
    {
        if (_gate.TryDefer(Render))
        {
            return;
        }

        var state = _model.State;
        var sessions = state.Sessions;
        _navigation.ReturnFromDeletedSession(sessions.Select(session => session.Id).ToHashSet());
        _isSyncing = true;
        var groups = state.VehicleGroups;
        switch (_navigation.Current)
        {
            case WindowsConnectionHistoryRoute.SessionAnalysis analysisRoute
                when sessions.FirstOrDefault(session => session.Id == analysisRoute.SessionID) is { } analyzed:
                ShowStage(HistoryStage.Analysis);
                if (_analysisView?.SessionID != analyzed.Id)
                {
                    _analysisView = new WindowsSessionLogAnalysisView(analyzed, _analysis);
                }

                if (!ReferenceEquals(_analysisHost.Child, _analysisView))
                {
                    _analysisHost.Child = _analysisView;
                }

                break;
            case WindowsConnectionHistoryRoute.SessionDetail detailRoute
                when sessions.FirstOrDefault(session => session.Id == detailRoute.SessionID) is { } session:
                LeaveAnalysis();
                ShowStage(HistoryStage.Split);
                SyncList(state, groups, GroupOf(groups, session.Id), session.Id);
                SyncDetail(session, state);
                SetShowsList(false);
                break;
            case WindowsConnectionHistoryRoute.VehicleSessions vehicleRoute:
                LeaveAnalysis();
                _detailSessionID = null;
                ShowStage(HistoryStage.Split);
                SyncList(state, groups, vehicleRoute.GroupID, null);
                _detailKey = null;
                _detailHost.Content = _selectPrompt;
                SetShowsList(true);
                break;
            default:
                LeaveAnalysis();
                _detailSessionID = null;
                ShowStage(HistoryStage.Archive);
                SyncArchive(state, groups);
                break;
        }

        RenderBreadcrumb(state, groups);
        _isSyncing = false;
        SyncDialogs(state, _analysis.State);
        if (_lastCanGoBack != CanGoBack)
        {
            _lastCanGoBack = CanGoBack;
            CanGoBackChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>解析の画面を外します（外すと解析の画面はモデルの購読をやめる）。</summary>
    private void LeaveAnalysis()
    {
        _analysisView = null;
        if (_analysisHost.Child is not null)
        {
            _analysisHost.Child = null;
        }
    }

    /// <summary>3 つの面のうち 1 つだけを出します。</summary>
    /// <param name="stage">出す面。</param>
    private void ShowStage(HistoryStage stage)
    {
        Retained.SetVisible(_archive, stage == HistoryStage.Archive);
        Retained.SetVisible(_split, stage == HistoryStage.Split);
        Retained.SetVisible(_analysisHost, stage == HistoryStage.Analysis);
    }

    /// <summary>畳んでいるときに、一覧と詳細のどちらを出すかを合わせます（変わったときだけ）。</summary>
    /// <param name="showsList">一覧を出すなら true。</param>
    private void SetShowsList(bool showsList)
    {
        if (_split.ShowsSecondary != showsList)
        {
            _split.ShowsSecondary = showsList;
        }
    }

    /// <summary>セッションを含む車両のまとまりの識別子を返します。</summary>
    /// <param name="groups">車両のまとまり。</param>
    /// <param name="sessionID">セッション。</param>
    /// <returns>識別子。移動の積み重ねにあればそれを優先する。</returns>
    private ConnectionHistoryVehicleGroupID GroupOf(IReadOnlyList<ConnectionHistoryVehicleGroup> groups, ConnectionSessionID sessionID) =>
        _navigation.Path.OfType<WindowsConnectionHistoryRoute.VehicleSessions>().LastOrDefault()?.GroupID
        ?? groups.FirstOrDefault(group => group.Sessions.Any(session => session.Id == sessionID))?.Id
        ?? new ConnectionHistoryVehicleGroupID.Unassigned();

    /// <summary>
    /// パンくずを描きます。
    /// </summary>
    /// <remarks>責務: 移動の積み重ねを「接続履歴 › 車両 › セッション › 解析」の文字に変えます。変わったときだけ入れ替えます。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="groups">車両のまとまり。</param>
    private void RenderBreadcrumb(ConnectionHistoryState state, IReadOnlyList<ConnectionHistoryVehicleGroup> groups)
    {
        var labels = new List<string> { LocalizedText.Get("history.title") };
        foreach (var route in _navigation.Path)
        {
            labels.Add(route switch
            {
                WindowsConnectionHistoryRoute.VehicleSessions vehicle =>
                    groups.FirstOrDefault(group => group.Id == vehicle.GroupID) is { } group
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
        Retained.SetVisible(Breadcrumb, labels.Count > 1);
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
    /// 車両別のアーカイブ（最初の画面）を合わせます。
    /// </summary>
    /// <remarks>責務: セッションか読込みの段階が変わったときだけ、見出しと合計、失敗・空の表示、進行中のセッション、車両ごとのカードを組み直します。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="groups">車両のまとまり。</param>
    private void SyncArchive(ConnectionHistoryState state, IReadOnlyList<ConnectionHistoryVehicleGroup> groups)
    {
        var sessions = state.Sessions;
        if (_archivePhase == state.Phase
            && _archiveSessions is { } previous
            && (ReferenceEquals(previous, sessions) || previous.SequenceEqual(sessions)))
        {
            return;
        }

        _archivePhase = state.Phase;
        _archiveSessions = sessions;
        _timers.Clear();
        var body = _archive.Body.Children;
        body.Clear();
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.XL };
        summary.Children.Add(new MetricBlock(NumberText.Integer(sessions.Count), LocalizedText.Get("history.summary.total"), HorizontalAlignment.Left, valueStyleKey: TextStyles.MetricLarge));
        summary.Children.Add(new MetricBlock(
            DurationText.HoursAndMinutes(state.TotalRecordedDuration),
            LocalizedText.Get("history.summary.duration"),
            HorizontalAlignment.Left,
            DesignTokens.Foreground(Tone.Accent),
            TextStyles.MetricLarge));
        body.Add(SurfaceCard.Hero(new ScreenHeader(
            FluentSymbol.History,
            LocalizedText.Get("history.eyebrow"),
            LocalizedText.Get("history.title"),
            LocalizedText.Get("history.subtitle"),
            summary)));

        if (state.Phase == ConnectionHistoryStatePhase.Failed)
        {
            var failure = new StackPanel { Spacing = Space.M, HorizontalAlignment = HorizontalAlignment.Center };
            failure.Children.Add(new FontIcon { Glyph = FluentSymbol.Storage, FontSize = 34, Foreground = DesignTokens.Foreground(Tone.Caution), HorizontalAlignment = HorizontalAlignment.Center });
            failure.Children.Add(Typography.Strong(LocalizedText.Get("history.error.storage")));
            var retry = new Button { Content = LocalizedText.Get("history.error.retry"), Style = DesignTokens.Style("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Center };
            retry.Click += (_, _) => _model.Send(new ConnectionHistoryAction.RefreshRequested());
            failure.Children.Add(retry);
            body.Add(SurfaceCard.Create(failure, Tone.Caution, Space.XXXL, tinted: true));
            return;
        }

        if (sessions.Count == 0)
        {
            body.Add(SurfaceCard.Create(new ContentUnavailablePanel(
                FluentSymbol.Road,
                LocalizedText.Get("history.empty.title"),
                LocalizedText.Get("history.empty.body"))));
            return;
        }

        var active = state.ActiveSessions;
        if (active.Count > 0)
        {
            body.Add(new GlyphLabel(FluentSymbol.Signal, LocalizedText.Get("history.active.title"), TextStyles.SectionTitle));
            foreach (var session in active)
            {
                body.Add(ActiveSessionCard(session));
            }
        }

        body.Add(new GlyphLabel(FluentSymbol.Car, LocalizedText.Get("history.archive.title"), TextStyles.SectionTitle));
        if (groups.Count == 0)
        {
            var empty = Typography.Text(LocalizedText.Get("history.archive.empty"), TextStyles.SecondaryBody);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            body.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(Space.XXL), Child = empty });
            return;
        }

        var grid = AdaptiveGrid.Cards();
        foreach (var group in groups)
        {
            grid.Children.Add(VehicleGroupButton(group));
        }

        body.Add(grid);
    }

    /// <summary>進行中のセッションのカードを作ります。</summary>
    /// <remarks>責務: 記録中の車両と経過時間・開始日時を横長のカードにします。Compact では経過時間と開始日時を下の段へ回します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>カード。</returns>
    private Border ActiveSessionCard(ConnectionSession session)
    {
        var grid = new Grid { ColumnSpacing = Space.L };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = IconTile.Create(FluentSymbol.Pulse, Tone.Success, Metrics.IconTileLarge);
        grid.Children.Add(tile);
        var texts = new StackPanel { Spacing = Space.XS, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(VehicleName(session), TextStyles.CardTitle));
        texts.Children.Add(Typography.Strong(LocalizedText.Get("history.active.recording"), TextStyles.Caption, DesignTokens.Foreground(Tone.Success)));
        grid.Children.Add(texts);
        var elapsed = new MetricBlock(
            DurationText.Timer(session.StartedAt, DateTimeOffset.Now),
            LocalizedText.Get("history.active.elapsed"),
            HorizontalAlignment.Right,
            DesignTokens.Foreground(Tone.Success),
            TextStyles.MetricLarge);
        _timers.Add((elapsed, session.StartedAt));
        grid.Children.Add(elapsed);
        var started = new MetricBlock(DateText.DateAndMinute(session.StartedAt), LocalizedText.Get("history.started"), HorizontalAlignment.Right, valueStyleKey: TextStyles.BodyStrong);
        grid.Children.Add(started);
        ResponsiveLayout.Bind(grid, widthClass =>
        {
            var compact = widthClass == WidthClass.Compact;
            grid.RowDefinitions.Clear();
            grid.RowSpacing = compact ? Space.M : 0;
            if (compact)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Place(tile, 0, 0, 1);
            Place(texts, 0, 1, compact ? 3 : 1);
            Place(elapsed, compact ? 1 : 0, compact ? 0 : 2, compact ? 2 : 1);
            Place(started, compact ? 1 : 0, compact ? 2 : 3, compact ? 2 : 1);
            elapsed.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        });
        return SurfaceCard.Create(grid, Tone.Success, Space.CardPadding(ResponsiveLayout.Current), tinted: true);
    }

    /// <summary>格子に要素を置きます。</summary>
    /// <param name="element">要素。</param>
    /// <param name="row">行。</param>
    /// <param name="column">列。</param>
    /// <param name="columnSpan">またぐ列の数。</param>
    private static void Place(FrameworkElement element, int row, int column, int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    /// <summary>車両のカード（押すとその車両のセッション一覧へ進む）を作ります。</summary>
    /// <param name="group">車両のまとまり。</param>
    /// <returns>ボタン。</returns>
    private Button VehicleGroupButton(ConnectionHistoryVehicleGroup group)
    {
        var tone = group.InterruptedCount > 0 ? Tone.Caution : Tone.Accent;
        var stack = new StackPanel { Spacing = Space.L };
        var head = new Grid { ColumnSpacing = Space.M };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(IconTile.Create(FluentSymbol.Car, tone, Metrics.IconTileMedium));
        var texts = new StackPanel { Spacing = Space.XXS, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(GroupName(group), TextStyles.CardTitle, wraps: false));
        if (group.Vehicle?.DisplayIdentifier is { Length: > 0 } identifier)
        {
            texts.Children.Add(Typography.Text(identifier, TextStyles.Monospace, wraps: false));
        }

        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        var chevron = new FontIcon { Glyph = FluentSymbol.ChevronRight, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        head.Children.Add(chevron);
        stack.Children.Add(head);
        var metrics = new MetricsStrip();
        metrics.Set(group);
        stack.Children.Add(metrics);
        var button = new Button
        {
            Content = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(Space.CardPadding(ResponsiveLayout.Current)),
            CornerRadius = new CornerRadius(Radius.L),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, GroupName(group));
        button.Click += (_, _) =>
        {
            _navigation.Push(new WindowsConnectionHistoryRoute.VehicleSessions(group.Id));
            Render();
        };
        return button;
    }

    /// <summary>
    /// 一覧の面を 1 回だけ組みます。
    /// </summary>
    /// <remarks>責務: 車両の見出しと数値、絞り込み、仮想化した一覧、絞り込みで 0 件のときの表示を、縦に並べます。値は <see cref="SyncList"/> が差し替えます。</remarks>
    private void BuildListPane()
    {
        _listPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _listPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _listPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Spacing = Space.XS };
        header.Children.Add(Typography.Text(LocalizedText.Get("history.archive.title"), TextStyles.Eyebrow));
        header.Children.Add(_listTitle);
        header.Children.Add(_listIdentifier);
        header.Children.Add(_listMetrics);
        _listPane.Children.Add(header);

        BuildFilter();
        Grid.SetRow(_filter, 1);
        _listPane.Children.Add(_filter);

        var results = new Grid();
        results.Children.Add(_list.View);
        results.Children.Add(_filterEmpty);
        Grid.SetRow(results, 2);
        _listPane.Children.Add(results);
    }

    /// <summary>詳細の面（縦にスクロール）を組みます。</summary>
    /// <remarks>責務: 詳細の中身を最大幅で置き、スクロールする枠に入れます（ScrollViewer の中で MaxWidth と Stretch を直接組み合わせるとはみ出すので、外側の格子に入れる）。</remarks>
    /// <returns>スクロールする枠。</returns>
    private ScrollViewer BuildDetailScroller()
    {
        _detailHost.MaxWidth = DetailMaxWidth;
        _detailHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        var frame = new Grid { Padding = new Thickness(0, 0, 0, Space.XL), Children = { _detailHost } };
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = frame,
        };
    }

    /// <summary>一覧の見た目と押したときの動きを設定します。</summary>
    /// <remarks>責務: 行の器を詰め、押すと詳細を開き、選択を使わず、器の背景を静かな色にします。</remarks>
    private void ConfigureList()
    {
        var view = _list.View;
        view.IsItemClickEnabled = true;
        view.Padding = new Thickness(0, 0, 0, Space.L);
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, Space.XS)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Radius.M)));
        view.ItemContainerStyle = style;
        var hover = DesignTokens.Brush("RevTorqueQuietFillBrush");
        view.Resources["ListViewItemBackgroundPointerOver"] = hover;
        view.Resources["ListViewItemBackgroundPressed"] = hover;
        view.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is ConnectionSession session)
            {
                OpenDetail(session.Id);
            }
        };
    }

    /// <summary>一覧の行を 1 つ作ります（器ごとに 1 回だけ）。</summary>
    /// <returns>行。</returns>
    private FrameworkElement CreateRow()
    {
        var row = new SessionRowView(this);
        _rows.Add(row);
        return row;
    }

    /// <summary>一覧の行にセッションを入れます。</summary>
    /// <param name="element">行。</param>
    /// <param name="session">セッション。</param>
    private void UpdateRow(FrameworkElement element, ConnectionSession session)
    {
        if (element is SessionRowView row)
        {
            row.Bind(session, session.Id == _selectedID, _wideRows);
        }
    }

    /// <summary>見えている行の、状態に左右される部分（選択・ボタン・メニュー）を合わせます。</summary>
    private void RefreshRows()
    {
        foreach (var row in _rows)
        {
            row.Refresh(row.Session?.Id == _selectedID, _wideRows);
        }
    }

    /// <summary>
    /// 車両のセッション一覧を合わせます。
    /// </summary>
    /// <remarks>責務: 車両の見出しと数値、絞り込みの値を差し替え、一覧の項目を差分だけ反映し、詳細を出しているセッションを強調します。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="groups">車両のまとまり。</param>
    /// <param name="groupID">車両のまとまり。</param>
    /// <param name="selectedID">詳細を出しているセッション。なければ null。</param>
    private void SyncList(ConnectionHistoryState state, IReadOnlyList<ConnectionHistoryVehicleGroup> groups, ConnectionHistoryVehicleGroupID groupID, ConnectionSessionID? selectedID)
    {
        var group = groups.FirstOrDefault(item => item.Id.Equals(groupID));
        Retained.SetText(_listTitle, group is null ? LocalizedText.Get("history.vehicle.unassigned") : GroupName(group));
        var identifier = group?.Vehicle?.DisplayIdentifier;
        Retained.SetText(_listIdentifier, identifier);
        Retained.SetVisible(_listIdentifier, identifier is { Length: > 0 });
        _listMetrics.Set(group);
        SyncFilter(state);

        var sessions = state.FilteredSessions(groupID);
        _list.Sync(sessions, session => session.Id);
        Retained.SetVisible(_list.View, sessions.Count > 0);
        Retained.SetVisible(_filterEmpty, sessions.Count == 0);
        var selectionChanged = _selectedID != selectedID;
        _selectedID = selectedID;
        RefreshRows();
        if (selectionChanged && selectedID is { } id && sessions.FirstOrDefault(session => session.Id == id) is { } item)
        {
            _list.View.ScrollIntoView(item);
        }
    }

    /// <summary>
    /// 詳細を合わせます。
    /// </summary>
    /// <remarks>責務: 詳細の元（セッション・アップロードの進み具合・削除中）が変わったときだけ詳細を組み直します。別のセッションに替わったときは上までスクロールし直します。</remarks>
    /// <param name="session">セッション。</param>
    /// <param name="state">接続履歴の状態。</param>
    private void SyncDetail(ConnectionSession session, ConnectionHistoryState state)
    {
        var uploading = state.UploadingSessionID == session.Id;
        var key = new DetailKey(session, uploading, uploading ? state.UploadProgress : null, state.UploadingSessionID is not null, state.DeletingSessionID == session.Id, state.AutomaticUploadEnabled);
        if (key != _detailKey)
        {
            _detailKey = key;
            _detailHost.Content = new WindowsConnectionSessionDetailView(session, state, _model.Send, OpenAnalysis);
        }

        if (_detailSessionID != session.Id)
        {
            _detailSessionID = session.Id;
            _detailScroller.ChangeView(null, 0, null, true);
        }
    }

    /// <summary>絞り込みと並べ替えの面を 1 回だけ組みます。</summary>
    /// <remarks>責務: 開閉できる面（見出しに絞り込みの解除）に、日付・終わった理由・並べ替えの入力を並べます。値は <see cref="SyncFilter"/> が差し替えます。</remarks>
    private void BuildFilter()
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new GlyphLabel(FluentSymbol.Filter, LocalizedText.Get("history.filter.title"), TextStyles.BodyStrong));
        _filterReset.Visibility = Visibility.Collapsed;
        _filterReset.Click += (_, _) => _model.Send(new ConnectionHistoryAction.FiltersReset());
        Grid.SetColumn(_filterReset, 1);
        head.Children.Add(_filterReset);
        _filter.Header = head;

        _fromCheck.Click += (_, _) => _model.Send(new ConnectionHistoryAction.FilterStartDateChanged(_fromCheck.IsChecked == true ? DateFallback(earliest: true) : null));
        _fromPicker.DateChanged += (_, args) =>
        {
            if (!_isSyncing && args.NewDate is { } date)
            {
                _model.Send(new ConnectionHistoryAction.FilterStartDateChanged(date));
            }
        };
        _toCheck.Click += (_, _) => _model.Send(new ConnectionHistoryAction.FilterEndDateChanged(_toCheck.IsChecked == true ? DateFallback(earliest: false) : null));
        _toPicker.DateChanged += (_, args) =>
        {
            if (!_isSyncing && args.NewDate is { } date)
            {
                _model.Send(new ConnectionHistoryAction.FilterEndDateChanged(date));
            }
        };
        foreach (var filter in _reasonFilters)
        {
            _reasonCombo.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(filter.HistoryLocalizationKey()) });
        }

        _reasonCombo.SelectionChanged += (_, _) =>
        {
            if (!_isSyncing && _reasonCombo.SelectedIndex is >= 0 and var index && index < _reasonFilters.Count)
            {
                _model.Send(new ConnectionHistoryAction.EndReasonFilterChanged(_reasonFilters[index]));
            }
        };
        foreach (var order in _sortOrders)
        {
            _sortCombo.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(order.HistoryLocalizationKey()) });
        }

        _sortCombo.SelectionChanged += (_, _) =>
        {
            if (!_isSyncing && _sortCombo.SelectedIndex is >= 0 and var index && index < _sortOrders.Count)
            {
                _model.Send(new ConnectionHistoryAction.SortOrderChanged(_sortOrders[index]));
            }
        };

        var from = new StackPanel { Spacing = Space.XS, Children = { _fromCheck, _fromPicker } };
        var to = new StackPanel { Spacing = Space.XS, Children = { _toCheck, _toPicker } };
        var controls = new AdaptiveGrid { MinItemWidth = 160, Spacing = Space.M };
        controls.Children.Add(from);
        controls.Children.Add(to);
        controls.Children.Add(_reasonCombo);
        controls.Children.Add(_sortCombo);
        _filter.Content = controls;
    }

    /// <summary>絞り込みの入力を今の状態に合わせます。</summary>
    /// <remarks>責務: 値が違うものだけ設定します。設定による変化は利用者の操作として扱いません。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    private void SyncFilter(ConnectionHistoryState state)
    {
        SyncDateFilter(_fromCheck, _fromPicker, state.FilterStartDate);
        SyncDateFilter(_toCheck, _toPicker, state.FilterEndDate);
        var reason = _reasonFilters.ToList().IndexOf(state.EndReasonFilter);
        if (_reasonCombo.SelectedIndex != reason)
        {
            _reasonCombo.SelectedIndex = reason;
        }

        var sort = _sortOrders.ToList().IndexOf(state.SortOrder);
        if (_sortCombo.SelectedIndex != sort)
        {
            _sortCombo.SelectedIndex = sort;
        }

        Retained.SetVisible(_filterReset, state.HasActiveFilters);
    }

    /// <summary>日付の絞り込みの入力を合わせます。</summary>
    /// <param name="check">チェック。</param>
    /// <param name="picker">日付。</param>
    /// <param name="date">今の日付。なければ絞り込まない。</param>
    private static void SyncDateFilter(CheckBox check, CalendarDatePicker picker, DateTimeOffset? date)
    {
        var enabled = date is not null;
        if (check.IsChecked != enabled)
        {
            check.IsChecked = enabled;
        }

        Retained.SetVisible(picker, enabled);
        if (date is { } value && picker.Date != value)
        {
            picker.Date = value;
        }
    }

    /// <summary>日付の絞り込みを有効にしたときの最初の日付を返します（終了したセッションの最も古い／新しい開始日）。</summary>
    /// <param name="earliest">最も古い日付なら true、新しい日付なら false。</param>
    /// <returns>日付。セッションがなければ今。</returns>
    private DateTimeOffset DateFallback(bool earliest)
    {
        var starts = _model.State.ClosedSessions.Select(session => session.StartedAt).ToList();
        if (starts.Count == 0)
        {
            return DateTimeOffset.Now;
        }

        return earliest ? starts.Min() : starts.Max();
    }

    /// <summary>状態に合わせて確認のダイアログを出し入れします。</summary>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="analysis">解析の状態。</param>
    private void SyncDialogs(ConnectionHistoryState state, SessionLogAnalysisState analysis)
    {
        _lastDownloadPrompt = analysis.DownloadPrompt;
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
            Content = Typography.Text(message, TextStyles.Body),
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
            var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S, MinWidth = 112 };
            progress.Children.Add(new ProgressBar { Value = (state.UploadProgress ?? 0) * 100, Width = 72, VerticalAlignment = VerticalAlignment.Center });
            progress.Children.Add(Typography.Text(NumberText.Percent(state.UploadProgress ?? 0), TextStyles.Caption, wraps: false));
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

    /// <summary>画面の 3 つの面です。</summary>
    private enum HistoryStage
    {
        /// <summary>車両別のアーカイブ。</summary>
        Archive,

        /// <summary>一覧と詳細。</summary>
        Split,

        /// <summary>解析。</summary>
        Analysis,
    }

    /// <summary>詳細を組んだときの元です（同じなら組み直さない）。</summary>
    /// <param name="Session">セッション。</param>
    /// <param name="Uploading">このセッションをアップロード中か。</param>
    /// <param name="Progress">アップロードの進み具合。</param>
    /// <param name="UploadBusy">どれかをアップロード中か（アップロードのボタンを押せるか）。</param>
    /// <param name="Deleting">このセッションを削除中か。</param>
    /// <param name="AutomaticUpload">自動アップロードが有効か。</param>
    private readonly record struct DetailKey(ConnectionSession Session, bool Uploading, double? Progress, bool UploadBusy, bool Deleting, bool AutomaticUpload);

    /// <summary>車両の数値（件数・時間・距離・中断）を並べた帯です。値は <see cref="Set"/> で差し替えます。</summary>
    private sealed class MetricsStrip : Grid
    {
        /// <summary>件数です。</summary>
        private readonly MetricBlock _count = new(string.Empty, LocalizedText.Get("history.vehicle.sessions"), valueStyleKey: TextStyles.BodyStrong);

        /// <summary>時間です。</summary>
        private readonly MetricBlock _duration = new(string.Empty, LocalizedText.Get("history.summary.duration"), valueStyleKey: TextStyles.BodyStrong);

        /// <summary>距離です。</summary>
        private readonly MetricBlock _distance = new(string.Empty, LocalizedText.Get("history.summary.distance"), valueStyleKey: TextStyles.BodyStrong);

        /// <summary>中断の件数の印です。</summary>
        private readonly Border _interrupted = StatusPill.Create(FluentSymbol.Warning, string.Empty, Tone.Caution);

        /// <summary>帯を作ります。</summary>
        public MetricsStrip()
        {
            ColumnSpacing = Space.M;
            for (var index = 0; index < 3; index++)
            {
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetColumn(_duration, 1);
            SetColumn(_distance, 2);
            SetColumn(_interrupted, 3);
            _interrupted.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(_interrupted, LocalizedText.Get("history.warning.interrupted"));
            Children.Add(_count);
            Children.Add(_duration);
            Children.Add(_distance);
            Children.Add(_interrupted);
        }

        /// <summary>車両のまとまりの値を入れます。</summary>
        /// <param name="group">車両のまとまり。なければ 0 を出す。</param>
        public void Set(ConnectionHistoryVehicleGroup? group)
        {
            _count.SetValue(NumberText.Integer(group?.SessionCount ?? 0));
            _duration.SetValue(DurationText.HoursAndMinutes(group?.TotalDuration ?? 0));
            _distance.SetValue(DistanceText.Format(group?.TotalDistanceKilometers));
            var interrupted = group?.InterruptedCount ?? 0;
            Retained.SetVisible(_interrupted, interrupted > 0);
            if (interrupted > 0)
            {
                StatusPill.Set(_interrupted, FluentSymbol.Warning, NumberText.Integer(interrupted), Tone.Caution);
            }
        }
    }
}
