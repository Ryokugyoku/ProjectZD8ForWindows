using System;
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
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Views;

/// <summary>
/// 接続履歴の画面です。記録中のセッション、車両ごとの保存済みログ、セッション詳細、解析へ移ります（iOS 版の <c>MacOSConnectionHistoryView</c>）。
/// </summary>
/// <remarks>画面の一時的な状態は移動の積み重ねだけです。確認のダイアログはすべて履歴の状態から出します。</remarks>
public sealed partial class WindowsConnectionHistoryView : UserControl
{
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

    /// <summary>停止の確認です。</summary>
    private readonly StateBoundDialog _stopReviewDialog = new();

    /// <summary>停止の確認の失敗です。</summary>
    private readonly StateBoundDialog _stopReviewFailureDialog = new();

    /// <summary>アップロードの失敗です。</summary>
    private readonly StateBoundDialog _uploadFailureDialog = new();

    /// <summary>解析のための Raw のダウンロードの確認です。</summary>
    private readonly StateBoundDialog _downloadDialog = new();

    /// <summary>記録中の経過時間を更新するタイマーです。</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>表示中の解析画面です（同じセッションの間は作り直さない）。</summary>
    private WindowsSessionLogAnalysisView? _analysisView;

    /// <summary>経過時間の文字と開始時刻です。</summary>
    private readonly System.Collections.Generic.List<(TextBlock Text, DateTimeOffset StartedAt)> _timers = [];

    /// <summary>モデルを受け取って接続履歴の画面を作ります。</summary>
    /// <param name="model">接続履歴のモデル。</param>
    /// <param name="analysis">保存済みログの解析モデル。</param>
    public WindowsConnectionHistoryView(ConnectionHistoryModel model, SessionLogAnalysisModel analysis)
    {
        _model = model;
        _analysis = analysis;
        InitializeComponent();
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
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>今の行き先の画面を描き、確認のダイアログを状態に合わせます。</summary>
    /// <remarks>責務: 消えたセッションの画面から戻り、行き先ごとの画面を作ります。</remarks>
    private void Render()
    {
        var state = _model.State;
        _navigation.ReturnFromDeletedSession(state.Sessions.Select(session => session.Id).ToHashSet());
        _isRendering = true;
        _timers.Clear();
        switch (_navigation.Current)
        {
            case WindowsConnectionHistoryRoute.SessionAnalysis analysisRoute
                when state.Sessions.FirstOrDefault(session => session.Id == analysisRoute.SessionID) is { } analyzed:
                if (_analysisView?.SessionID != analyzed.Id)
                {
                    _analysisView = new WindowsSessionLogAnalysisView(analyzed, _analysis, CloseAnalysis);
                    Page.Children.Clear();
                    Page.Children.Add(_analysisView);
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
                Page.Children.Clear();
                Page.Children.Add(new WindowsConnectionSessionDetailView(
                    session,
                    state.DeletingSessionID == session.Id,
                    _model.Send,
                    OpenAnalysis,
                    () =>
                    {
                        _navigation.ReturnToVehicleSessionList();
                        Render();
                    }));
                break;
            case WindowsConnectionHistoryRoute.VehicleSessions vehicleRoute:
                _analysisView = null;
                Page.Children.Clear();
                RenderVehicleSessions(state, vehicleRoute.GroupID);
                break;
            default:
                _analysisView = null;
                Page.Children.Clear();
                RenderArchive(state);
                break;
        }

        _isRendering = false;
        SyncDialogs(state, _analysis.State);
    }

    /// <summary>解析を開きます（セッション詳細の解析のカード）。</summary>
    /// <remarks>責務: 解析を始め、端末に Raw があれば解析画面へ進みます（なければダウンロードの確認を待つ）。</remarks>
    /// <param name="session">セッション。</param>
    private void OpenAnalysis(ConnectionSession session)
    {
        _analysis.Send(new SessionLogAnalysisAction.SessionSelected(session));
        if (session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Removed)
        {
            _navigation.Push(new WindowsConnectionHistoryRoute.SessionAnalysis(session.Id));
            Render();
        }
    }

    /// <summary>解析を閉じてセッション詳細へ戻ります。</summary>
    /// <remarks>責務: 解析の終了を送り、詳細へ戻って履歴の再読込を求めます（iOS 版の <c>onChange(of: isAnalysisPresented)</c>）。</remarks>
    private void CloseAnalysis()
    {
        _analysis.Send(new SessionLogAnalysisAction.Dismissed());
        _navigation.ReturnFromAnalysis();
        _analysisView = null;
        _model.Send(new ConnectionHistoryAction.LocalDataChanged());
        Render();
    }

    /// <summary>最上位（記録中と車両ごとの保存済みログ）を描きます。</summary>
    /// <remarks>責務: 見出し、失敗・空の表示、記録中のセッション、車両ごとのまとまりを作ります。</remarks>
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

    /// <summary>記録中のセッションのカードを作ります。</summary>
    /// <remarks>責務: 車両名、「記録中」、経過時間、開始日時を並べます（緑は記録中の状態が裏付ける）。</remarks>
    /// <param name="session">記録中のセッション。</param>
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

    /// <summary>車両のまとまりのボタンを作ります。</summary>
    /// <remarks>責務: 車両名・識別子・セッション数・記録時間・距離・中断の数を出し、押すとその車両の一覧へ進みます。</remarks>
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

    /// <summary>車両のまとまりの数値の行を作ります。</summary>
    /// <remarks>責務: セッション数・記録時間・距離と、中断があれば警告のバッジを並べます。</remarks>
    /// <param name="group">車両のまとまり。null なら 0 として出す。</param>
    /// <returns>行。</returns>
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

    /// <summary>車両のセッション一覧を描きます。</summary>
    /// <remarks>責務: 戻るボタン付きの見出し、絞り込み、絞り込んだセッションの行を作ります。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="groupID">車両のまとまり。</param>
    private void RenderVehicleSessions(ConnectionHistoryState state, ConnectionHistoryVehicleGroupID groupID)
    {
        var group = state.VehicleGroups.FirstOrDefault(item => item.Id == groupID);
        var sessions = state.FilteredSessions(groupID);
        var header = new Grid { ColumnSpacing = 18 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var back = BackButton(LocalizedText.Get("history.navigation.back"), () =>
        {
            _navigation.ReturnToVehicleArchive();
            Render();
        });
        header.Children.Add(back);
        var identityTexts = new StackPanel { Spacing = 4 };
        identityTexts.Children.Add(Typography.Text(LocalizedText.Get("history.archive.title"), "EyebrowTextStyle"));
        identityTexts.Children.Add(Typography.Text(group is null ? LocalizedText.Get("history.vehicle.unassigned") : GroupName(group), "PageTitleTextStyle"));
        if (group?.Vehicle?.DisplayIdentifier is { Length: > 0 } identifier)
        {
            identityTexts.Children.Add(Typography.Text(identifier, "MonospaceCaptionTextStyle"));
        }

        identityTexts.Children.Add(GroupMetrics(group));
        Grid.SetColumn(identityTexts, 1);
        header.Children.Add(identityTexts);
        Page.Children.Add(SurfaceCard.Create(header, padding: 20));
        Page.Children.Add(FilterPanel(state));
        if (sessions.Count == 0)
        {
            Page.Children.Add(new ContentUnavailablePanel(FluentSymbol.Search, LocalizedText.Get("history.filter.empty"), LocalizedText.Get("history.filter.empty.body")));
            return;
        }

        var list = new StackPanel { Spacing = 10 };
        foreach (var session in sessions)
        {
            list.Children.Add(SessionListItem(state, session));
        }

        Page.Children.Add(list);
    }

    /// <summary>絞り込みの部分を作ります。</summary>
    /// <remarks>責務: 開始日・終了日・終了理由・並び順の絞り込みと、解除のボタンを作ります。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <returns>表示の要素。</returns>
    private Border FilterPanel(ConnectionHistoryState state)
    {
        var stack = new StackPanel { Spacing = 13 };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new GlyphLabel(FluentSymbol.Filter, LocalizedText.Get("history.filter.title"), "BodyStrongTextBlockStyle"));
        if (state.HasActiveFilters)
        {
            var reset = new HyperlinkButton { Content = LocalizedText.Get("history.filter.reset") };
            reset.Click += (_, _) => _model.Send(new ConnectionHistoryAction.FiltersReset());
            Grid.SetColumn(reset, 1);
            head.Children.Add(reset);
        }

        stack.Children.Add(head);
        var controls = new AdaptiveGrid { MinItemWidth = 220, Spacing = 14 };
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
        return new Border { Background = DesignTokens.SubtleBackground(Tone.Accent), CornerRadius = new CornerRadius(14), Padding = new Thickness(17), Child = stack };
    }

    /// <summary>日付の絞り込みを作ります。</summary>
    /// <remarks>責務: 使うかどうかのチェックと日付の選択を作り、変化を送ります。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="date">今の日付。null なら使わない。</param>
    /// <param name="fallback">チェックしたときの初期の日付。</param>
    /// <param name="update">変化を送る処理。</param>
    /// <returns>表示の要素。</returns>
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

    /// <summary>セッションの行（と手動アップロード・Raw の解放のボタン）を作ります。</summary>
    /// <remarks>責務: 行を押すと詳細へ進み、条件を満たすときだけアップロードと端末の Raw の解放のボタンを出します。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <returns>表示の要素。</returns>
    private Grid SessionListItem(ConnectionHistoryState state, ConnectionSession session)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new Button
        {
            Content = SessionRow(session),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(12),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row, DateText.DateAndMinute(session.StartedAt));
        row.Click += (_, _) =>
        {
            _navigation.Push(new WindowsConnectionHistoryRoute.SessionDetail(session.Id));
            Render();
        };
        grid.Children.Add(row);
        if (CanUpload(state, session))
        {
            var upload = new Button { Style = DesignTokens.Style("AccentButtonStyle"), IsEnabled = state.UploadingSessionID is null, VerticalAlignment = VerticalAlignment.Center };
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

            upload.Click += (_, _) => _model.Send(new ConnectionHistoryAction.SessionUploadRequested(session.Id));
            Grid.SetColumn(upload, 1);
            grid.Children.Add(upload);
        }

        if (CanReleaseRawCache(session))
        {
            var release = new Button
            {
                Content = new GlyphLabel(FluentSymbol.Storage, LocalizedText.Get("history.raw.remove.button"), foreground: DesignTokens.Foreground(Tone.Caution)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            release.Click += (_, _) => _model.Send(new ConnectionHistoryAction.LocalRawRemovalRequested(session.Id));
            Grid.SetColumn(release, 2);
            grid.Children.Add(release);
        }

        return grid;
    }

    /// <summary>セッションの行の中身を作ります。</summary>
    /// <remarks>責務: 完了・中断の記号、開始日時、取得元、保存の状態、終了理由、時間・距離、短い ID を並べます。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>表示の要素。</returns>
    private static Grid SessionRow(ConnectionSession session)
    {
        var grid = new Grid { ColumnSpacing = 14 };
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
        texts.Children.Add(new GlyphLabel(FluentSymbol.Person, AcquisitionDeviceText(session), "SecondaryCaptionTextStyle"));
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
        var id = Typography.Text(session.Id.RawValue.ToString("N")[..8].ToUpperInvariant(), "MonospaceCaptionTextStyle", wraps: false);
        id.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(id, 3);
        grid.Children.Add(id);
        return grid;
    }

    /// <summary>クラウドの保存状態の表示を作ります。</summary>
    /// <remarks>責務: アップロードの状態を記号・文言・色の組へ変換します。</remarks>
    /// <param name="state">クラウドの保存状態。</param>
    /// <returns>表示の要素。</returns>
    private static Border CloudStatusPill(ConnectionSessionCloudSyncState state) => state switch
    {
        ConnectionSessionCloudSyncState.Pending => StatusPill.Create(FluentSymbol.Upload, LocalizedText.Get("history.raw.cloud.pending"), Tone.Caution),
        ConnectionSessionCloudSyncState.Uploaded => StatusPill.Create(FluentSymbol.Cloud, LocalizedText.Get("history.raw.cloud.uploaded"), Tone.Success),
        ConnectionSessionCloudSyncState.Failed => StatusPill.Create(FluentSymbol.Error, LocalizedText.Get("history.raw.cloud.failed"), Tone.Danger),
        _ => StatusPill.Create(FluentSymbol.Cloud, LocalizedText.Get("history.raw.cloud.not_uploaded"), Tone.Neutral),
    };

    /// <summary>端末の保存状態の表示を作ります。</summary>
    /// <remarks>責務: 端末の Raw の状態を記号・文言・色の組へ変換します。</remarks>
    /// <param name="state">端末の保存状態。</param>
    /// <returns>表示の要素。</returns>
    private static Border LocalStatusPill(ConnectionSessionLocalRawState state) => state switch
    {
        ConnectionSessionLocalRawState.Empty => StatusPill.Create(null, LocalizedText.Get("history.raw.local.empty"), Tone.Neutral),
        ConnectionSessionLocalRawState.Available => StatusPill.Create(FluentSymbol.Completed, LocalizedText.Get("history.raw.local.available"), Tone.Accent),
        _ => StatusPill.Create(FluentSymbol.Download, LocalizedText.Get("history.raw.local.removed"), Tone.Neutral),
    };

    /// <summary>戻るボタンを作ります。</summary>
    /// <remarks>責務: 記号だけの戻るボタンに、読み上げとツールチップの文言を付けます。</remarks>
    /// <param name="label">文言。</param>
    /// <param name="back">押したときの処理。</param>
    /// <returns>ボタン。</returns>
    public static Button BackButton(string label, Action back)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = FluentSymbol.Back, FontSize = 14 },
            Width = 44,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => back();
        return button;
    }

    /// <summary>確認のダイアログを状態に合わせます。</summary>
    /// <remarks>責務: Raw の解放・削除・停止の確認・アップロードの失敗・ダウンロードの確認を、状態があるときだけ出します。</remarks>
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
        }, result => _model.Send(result == ContentDialogResult.Primary
            ? new ConnectionHistoryAction.LocalRawRemovalConfirmed()
            : new ConnectionHistoryAction.LocalRawRemovalCancelled()));

        _deletionDialog.Sync(state.SessionDeletionPrompt is not null, state.SessionDeletionPrompt, root, () =>
        {
            var prompt = state.SessionDeletionPrompt!;
            return Alert(
                "history.delete.warning.title",
                LocalizedText.Format("history.delete.warning.message", prompt.RecordCount, ByteCountText.Format(prompt.ByteCount)),
                "history.delete.confirm",
                "history.delete.cancel",
                destructive: true);
        }, result => _model.Send(result == ContentDialogResult.Primary
            ? new ConnectionHistoryAction.SessionDeletionConfirmed()
            : new ConnectionHistoryAction.SessionDeletionCancelled()));

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

    /// <summary>確認のダイアログを作ります。</summary>
    /// <remarks>責務: 題・本文・決定と取り消しのボタンを持つダイアログを作ります。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="message">本文。</param>
    /// <param name="primaryKey">決定のボタンのキー。null なら決定なし。</param>
    /// <param name="closeKey">閉じるボタンのキー。</param>
    /// <param name="destructive">取り消せない操作なら true（決定を赤にする）。</param>
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

    /// <summary>手動アップロードできるかどうかを返します（iOS 版の <c>canUpload</c>）。</summary>
    /// <remarks>責務: 自動アップロードが無効で、終了済みかつ端末に Raw があり未アップロードのときだけ true にします。</remarks>
    /// <param name="state">接続履歴の状態。</param>
    /// <param name="session">セッション。</param>
    /// <returns>アップロードできるなら true。</returns>
    private static bool CanUpload(ConnectionHistoryState state, ConnectionSession session) =>
        !state.AutomaticUploadEnabled
        && session.EndedAt is not null
        && session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Removed
        && session.RawLogSummary.CloudState != ConnectionSessionCloudSyncState.Uploaded;

    /// <summary>端末の Raw を解放できるかどうかを返します（iOS 版の <c>canReleaseRawCache</c>）。</summary>
    /// <remarks>責務: クラウドへの保存が確認できた Raw だけを解放の対象にします。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>解放できるなら true。</returns>
    private static bool CanReleaseRawCache(ConnectionSession session) =>
        session.EndedAt is not null
        && session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Available
        && session.RawLogSummary.RecordCount > 0
        && session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded
        && !string.IsNullOrEmpty(session.RawLogSummary.ManifestDigest);

    /// <summary>車両のまとまりの名前を返します。</summary>
    /// <remarks>責務: 車両名、なければ「未割り当て」の文言を返します。</remarks>
    /// <param name="group">車両のまとまり。</param>
    /// <returns>名前。</returns>
    private static string GroupName(ConnectionHistoryVehicleGroup group) =>
        group.Vehicle?.Name is { Length: > 0 } name ? name : LocalizedText.Get("history.vehicle.unassigned");

    /// <summary>セッションの車両名を返します。</summary>
    /// <remarks>責務: 車両名、なければ「確認中」の文言を返します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>名前。</returns>
    public static string VehicleName(ConnectionSession session) =>
        session.Vehicle?.Name is { Length: > 0 } name ? name : LocalizedText.Get("history.vehicle.pending");

    /// <summary>取得元の端末の表示を返します。</summary>
    /// <remarks>責務: 端末名と種類、記録がなければ「不明」の文言を返します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>表示文字列。</returns>
    public static string AcquisitionDeviceText(ConnectionSession session) =>
        session.AcquisitionDevice is { } device
            ? $"{device.Name} ({device.Platform.ToRawValue()})"
            : LocalizedText.Get("history.device.unknown");

    /// <summary>セッションの記録時間（秒）を返します。</summary>
    /// <remarks>責務: 終了済みなら開始から終了まで、記録中なら 0 を返します。</remarks>
    /// <param name="session">セッション。</param>
    /// <returns>秒数。</returns>
    private static double SessionDuration(ConnectionSession session) =>
        Math.Max(0, ((session.EndedAt ?? session.StartedAt) - session.StartedAt).TotalSeconds);
}
