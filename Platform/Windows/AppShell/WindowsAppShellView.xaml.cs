using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.AppRoot.Flow;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.LiveTelemetry.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Maintenance.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;
using Windows.Graphics;
using Windows.System;

namespace ProjectZD8ForWindows.Platform.Windows.AppShell;

/// <summary>
/// サインイン後のアプリのシェルです。左のナビゲーションと、選んだ行き先の画面を出します（iOS 版の <c>MacOSAppShellView</c>）。
/// </summary>
/// <remarks>
/// 行き先の選択と「接続の結果を待っているか」だけを画面の一時的な状態として持ちます。
/// どの画面からでも接続・切断できるように、タイトルバーに接続コンソールを置きます（ホームでは状態だけ）。ナビの形は幅クラスで決めます。
/// </remarks>
public sealed partial class WindowsAppShellView : UserControl
{
    /// <summary>アプリの流れです。</summary>
    private readonly WindowsApplicationFlow _flow;

    /// <summary>ホームの接続要求の結果（ガレージへの移動）を待っているかどうかです。</summary>
    private bool _awaitsVehicleConnectionOutcome;

    /// <summary>選択を画面の都合で変えている最中かどうかです。</summary>
    private bool _isSelectingProgrammatically;

    /// <summary>直前の車両管理の段階です。</summary>
    private VehicleManagementStatePhase _lastVehiclePhase;

    /// <summary>今の画面のうち、戻る操作を受け付ける画面です。</summary>
    private IBackNavigable? _backTarget;

    /// <summary>診断（詳細ログ・不具合レポート・前回の異常終了の知らせ）の表示モデルです。</summary>
    private readonly WindowsDiagnosticsModel _diagnostics;

    /// <summary>前回の異常終了の知らせを出したことを、ログに書いたかです。</summary>
    private bool _loggedPreviousRunNotice;

    /// <summary>タイトルバーの押せる領域を OS へ伝えるための、ウインドウです（なければ伝えない）。</summary>
    private readonly AppWindow? _appWindow;

    /// <summary>最後に OS へ伝えた、タイトルバーの押せる領域です。</summary>
    private RectInt32[] _passthroughRects = [];

    /// <summary>今の幅クラスです。</summary>
    private WidthClass _widthClass = ResponsiveLayout.Current;

    /// <summary>下のタブ帯のタブです（行き先ごとに 1 回だけ作って使い回す）。</summary>
    private readonly Dictionary<WindowsSidebarDestination, BottomTab> _tabs = [];

    /// <summary>タイトルバーの押せる領域の登録を、配置が落ち着くまで待つ時計です。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _titleBarRegionsTimer;

    /// <summary>ナビの形を最後に合わせた幅クラスです（まだなら null）。</summary>
    private WidthClass? _navigationClass;

    /// <summary>幅を続けて変えている間、待たせているナビの幅クラスです（なければ null）。</summary>
    private WidthClass? _pendingNavigationClass;

    /// <summary>待たせているナビの切り替えの時計です。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _navigationTimer;

    /// <summary>下のタブ帯の 1 つ分の要素です。</summary>
    /// <param name="Button">押せる面。</param>
    /// <param name="Indicator">選択中の印（シアンの線）。</param>
    /// <param name="Glyph">記号。</param>
    /// <param name="Label">短い題。</param>
    /// <param name="Badge">オレンジの数のバッジ。</param>
    private sealed record BottomTab(Button Button, Border Indicator, FontIcon Glyph, TextBlock Label, InfoBadge Badge);

    /// <summary>アプリの流れと最初の行き先を指定して作ります。</summary>
    /// <param name="flow">アプリの流れ。</param>
    /// <param name="initialDestination">最初に開く行き先。</param>
    /// <param name="appWindow">シェルを置くウインドウ（タイトルバーの押せる領域とキャプションボタンの幅に使う）。</param>
    public WindowsAppShellView(WindowsApplicationFlow flow, WindowsSidebarDestination initialDestination, AppWindow? appWindow = null)
    {
        _flow = flow;
        _appWindow = appWindow;
        _diagnostics = new WindowsDiagnosticsModel(flow.Root.Diagnostics, DiagnosticsEnvironment);
        InitializeComponent();
        BrandTagline.Text = LocalizedText.Get("sidebar.brand.tagline").ToUpperInvariant();
        OpenLiveText.Text = LocalizedText.Get("windows.shell.open_live");
        ToolTipService.SetToolTip(OpenLiveButton, LocalizedText.Get("sidebar.live_log"));
        var backLabel = LocalizedText.Get("windows.shell.back") + " (Alt+←)";
        ToolTipService.SetToolTip(BackButton, backLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BackButton, LocalizedText.Get("windows.shell.back"));
        ToolTipService.SetToolTip(PaneToggleButton, LocalizedText.Get("windows.shell.navigation"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PaneToggleButton, LocalizedText.Get("windows.shell.navigation"));
        if (Motion.IsEnabled)
        {
            // ページの切り替えは、GPU で動く ThemeTransition（少し下から現れる）だけにする。
            DestinationHost.ChildTransitions = [new EntranceThemeTransition { FromVerticalOffset = 16, IsStaggeringEnabled = false }];
        }

        // 押せる領域は、位置や大きさが変わりうるときだけ数え直す（LayoutUpdated は毎回の配置で起きるので使わない）。
        // 幅を変えている間は 3 つの SizeChanged が同じ配置で続けて来るので、1 回の配置につき 1 回だけ数え直す。
        TitleBar.SizeChanged += (_, _) => ScheduleTitleBarRegions();
        ConnectionConsole.SizeChanged += (_, _) => ScheduleTitleBarRegions();
        BackButton.SizeChanged += (_, _) => ScheduleTitleBarRegions();

        // 下のタブ帯は Compact になったときに初めて作る（広い幅で起動したときに要素を作らない）。
        ActualThemeChanged += (_, _) => RefreshTabTheme();
        ResponsiveLayout.Bind(this, ApplyWidthClass);
        foreach (var destination in WindowsSidebarDestinationPresentation.AllCases)
        {
            var item = MakeItem(destination);
            if (destination == WindowsSidebarDestination.Settings)
            {
                Navigation.FooterMenuItems.Add(item);
            }
            else
            {
                Navigation.MenuItems.Add(item);
            }
        }

        AddKeyboardShortcuts();
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressedForBack), true);
        _lastVehiclePhase = flow.Root.VehicleManagement.State.Phase;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Select(initialDestination);
    }

    /// <summary>選択中の行き先です。</summary>
    public WindowsSidebarDestination SelectedDestination { get; private set; } = WindowsSidebarDestination.Home;

    /// <summary>タイトルバーの要素です（MainWindow が Window.SetTitleBar に渡す。押せる部分はシェルが OS へ伝える）。</summary>
    public UIElement TitleBarElement => TitleBar;

    /// <summary>
    /// 行き先を選びます。
    /// </summary>
    /// <remarks>責務: ナビゲーションの選択を行き先に合わせ、その画面を出します。</remarks>
    /// <param name="destination">行き先。</param>
    public void Select(WindowsSidebarDestination destination)
    {
        _isSelectingProgrammatically = true;
        Navigation.SelectedItem = Item(destination);
        _isSelectingProgrammatically = false;
        ShowDestination(destination);
    }

    /// <summary>表示されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視を始め、履歴の警告の数と接続コンソールを出します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _flow.Root.ConnectionHistory.PropertyChanged += OnHistoryChanged;
        _flow.Root.VehicleManagement.PropertyChanged += OnVehicleManagementChanged;
        _flow.Root.AdapterArrivalPrompt.PropertyChanged += OnAdapterArrivalPromptChanged;
        _flow.Root.LiveTelemetry.PropertyChanged += OnConnectionChanged;
        _flow.SettingsModel.PropertyChanged += OnConnectionChanged;
        _diagnostics.PropertyChanged += OnDiagnosticsChanged;
        RenderAdapterArrivalPrompt();
        RenderPreviousRunNotice();
        UpdateHistoryBadge();
        UpdateConnectionBar();
    }

    /// <summary>外されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視をやめます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _flow.Root.ConnectionHistory.PropertyChanged -= OnHistoryChanged;
        _flow.Root.VehicleManagement.PropertyChanged -= OnVehicleManagementChanged;
        _flow.Root.AdapterArrivalPrompt.PropertyChanged -= OnAdapterArrivalPromptChanged;
        _flow.Root.LiveTelemetry.PropertyChanged -= OnConnectionChanged;
        _flow.SettingsModel.PropertyChanged -= OnConnectionChanged;
        _diagnostics.PropertyChanged -= OnDiagnosticsChanged;
        AttachBackTarget(null);
    }

    /// <summary>不具合レポートに添える、既定アダプターの種類と接続セッションの数を集めます。</summary>
    /// <returns>状態の情報。</returns>
    private WindowsDiagnosticsEnvironment DiagnosticsEnvironment()
    {
        var adapter = _flow.SettingsModel.State.SelectedAdapters.TryGetValue(AdapterConnectionRole.Primary, out var primary)
            ? primary.ConnectionTransport.ToString()
            : null;
        return new WindowsDiagnosticsEnvironment(adapter, _flow.Root.ConnectionHistory.State.Sessions.Count);
    }

    /// <summary>診断の表示モデルが変わったときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnDiagnosticsChanged(object? sender, PropertyChangedEventArgs e) => RenderPreviousRunNotice();

    /// <summary>前回の異常終了の知らせを描きます。</summary>
    /// <remarks>責務: 前回の実行が正常に終了しなかったときだけ、書き出しを尋ねる InfoBar を開きます（閉じたらこの実行では出さない）。</remarks>
    private void RenderPreviousRunNotice()
    {
        if (!_diagnostics.ShowsPreviousRunNotice)
        {
            PreviousRunBar.IsOpen = false;
            return;
        }

        PreviousRunBar.Message = LocalizedText.Get("windows.diagnostics.previous_run.message");
        PreviousRunExportButton.Content = LocalizedText.Get("windows.diagnostics.report.button");
        PreviousRunBar.IsOpen = true;
        if (!_loggedPreviousRunNotice)
        {
            _loggedPreviousRunNotice = true;
            UiLog.CrashNoticeShown(UiLog.Logger);
        }
    }

    /// <summary>前回の異常終了の知らせの「不具合レポートを書き出す」を押したときの処理です。</summary>
    /// <remarks>責務: 設定画面と同じ流れで書き出し、書き出せたらこの実行では知らせを出さなくします。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private async void OnPreviousRunExportClicked(object sender, RoutedEventArgs e)
    {
        PreviousRunExportButton.IsEnabled = false;
        try
        {
            if (await WindowsDiagnosticsReportFlow.ExportAsync(_diagnostics, XamlRoot))
            {
                _diagnostics.HandlePreviousRunNotice();
            }
        }
        finally
        {
            PreviousRunExportButton.IsEnabled = true;
        }
    }

    /// <summary>前回の異常終了の知らせを閉じたときの処理です。</summary>
    /// <remarks>責務: この実行では、もう知らせを出さなくします。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnPreviousRunDismissed(InfoBar sender, object args)
    {
        UiLog.CrashNoticeDismissed(UiLog.Logger);
        _diagnostics.HandlePreviousRunNotice();
    }

    /// <summary>到来したアダプターについて尋ねる表示モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 尋ねている終端があれば InfoBar を開き、なければ閉じます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnAdapterArrivalPromptChanged(object? sender, PropertyChangedEventArgs e) => RenderAdapterArrivalPrompt();

    /// <summary>到来したアダプターについての InfoBar を描きます。</summary>
    /// <remarks>責務: 表示モデルの終端を InfoBar の題・本文・ボタンへ写します。</remarks>
    private void RenderAdapterArrivalPrompt()
    {
        var endpoint = _flow.Root.AdapterArrivalPrompt.PendingEndpoint;
        if (endpoint is null)
        {
            AdapterArrivalBar.IsOpen = false;
            return;
        }

        AdapterArrivalBar.Title = LocalizedText.Get("windows.adapter_prompt.title");
        AdapterArrivalBar.Message = LocalizedText.Format("windows.adapter_prompt.message", endpoint.DisplayName);
        AdapterArrivalConnectButton.Content = LocalizedText.Get("windows.adapter_prompt.connect");
        AdapterArrivalBar.IsOpen = true;
    }

    /// <summary>InfoBar の「接続」を押したときの処理です。</summary>
    /// <remarks>責務: 接続を受け入れたことを表示モデルへ送ります。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnAdapterArrivalAccepted(object sender, RoutedEventArgs e) => _flow.Root.AdapterArrivalPrompt.Accept();

    /// <summary>InfoBar を閉じたときの処理です。</summary>
    /// <remarks>責務: 今は接続しないことを表示モデルへ送ります。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnAdapterArrivalDismissed(InfoBar sender, object args) => _flow.Root.AdapterArrivalPrompt.Decline();

    /// <summary>ナビゲーションで選んだときの処理です。</summary>
    /// <remarks>責務: 利用者が選んだ行き先の画面を出します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSelectingProgrammatically || args.SelectedItem is not NavigationViewItem { Tag: WindowsSidebarDestination destination })
        {
            return;
        }

        ShowDestination(destination);
    }

    /// <summary>接続履歴が変わったときの処理です。</summary>
    /// <remarks>責務: 中断したセッションの数を履歴の項目のバッジへ反映します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnHistoryChanged(object? sender, PropertyChangedEventArgs e) => UpdateHistoryBadge();

    /// <summary>接続の状態（設定のアダプター・ライブ取得）が変わったときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e) => UpdateConnectionBar();

    /// <summary>車両管理の段階が変わったときの処理です（iOS 版の <c>handleVehicleManagementPhase</c>）。</summary>
    /// <remarks>責務: ホームや接続コンソールから接続したあと、確認・登録・失敗になったらガレージへ移ります。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnVehicleManagementChanged(object? sender, PropertyChangedEventArgs e)
    {
        var phase = _flow.Root.VehicleManagement.State.Phase;
        if (phase == _lastVehiclePhase)
        {
            return;
        }

        _lastVehiclePhase = phase;
        if (!_awaitsVehicleConnectionOutcome)
        {
            return;
        }

        if (phase == VehicleManagementStatePhase.ReadyToConnect)
        {
            _awaitsVehicleConnectionOutcome = false;
            return;
        }

        if (WindowsSidebarDestinationPresentation.Destination(phase) is { } destination)
        {
            _awaitsVehicleConnectionOutcome = false;
            Select(destination);
        }
    }

    /// <summary>
    /// ホーム画面（と接続コンソール）の操作を処理します（iOS 版の <c>handleHomeAction</c>）。
    /// </summary>
    /// <remarks>責務: ホームの操作を、設定画面への移動・車両接続の開始・切断へ変換します。</remarks>
    /// <param name="action">ホームの操作。</param>
    private void HandleHomeAction(WindowsHomeAction action)
    {
        switch (action)
        {
            case WindowsHomeAction.AdapterSetupRequested:
                UiLog.AdapterSetupRequested(UiLog.Logger);
                Select(WindowsSidebarDestination.Settings);
                _flow.SettingsModel.Send(new WindowsSettingsAction.AdapterAttentionRequested());
                break;
            case WindowsHomeAction.VehicleConnectionRequested request:
                UiLog.ConnectRequested(UiLog.Logger, request.Endpoint.Transport.ToString());
                _awaitsVehicleConnectionOutcome = true;
                _flow.Root.StartVehicleConnection.Execute(request.Endpoint);
                break;
            case WindowsHomeAction.VehicleDisconnectionRequested:
                UiLog.DisconnectRequested(UiLog.Logger);
                _flow.Root.LiveTelemetry.Send(new LiveTelemetryAction.StopRequested());
                break;
        }
    }

    /// <summary>今の接続の状態です。</summary>
    /// <returns>ホームと同じ接続の状態。</returns>
    private WindowsHomeState ConnectionState() => new(_flow.SettingsModel.State, _flow.Root.LiveTelemetry.State);

    /// <summary>
    /// 接続コンソールを描きます。
    /// </summary>
    /// <remarks>責務: 既定のアダプターと接続の状態を、点の色・名前・状態の文字・ボタンへ写します。作り直さずに値だけを変えるので、押している最中のボタンが消えません。</remarks>
    private void UpdateConnectionBar()
    {
        var state = ConnectionState();
        var live = _flow.Root.LiveTelemetry.State;
        var availability = state.DefaultAdapterAvailability;
        var onHome = SelectedDestination == WindowsSidebarDestination.Home;
        Retained.SetText(ConnectionName, availability.DisplayName ?? LocalizedText.Get("home.adapter.not_configured"));
        string statusKey;
        Tone dotTone;
        if (state.IsConnectionActive)
        {
            dotTone = Tone.Success;
            statusKey = state.IsDisconnecting
                ? "windows.shell.connection.disconnecting"
                : live.Phase == LiveTelemetryStatePhase.Reading ? "telemetry.status.reading" : "windows.shell.connection.connected";
        }
        else
        {
            dotTone = availability.IsDetected ? Tone.Accent : Tone.Neutral;
            statusKey = availability.IsDetected
                ? "home.adapter.detected"
                : availability.HasDefaultAdapter ? "home.adapter.not_detected" : "home.adapter.setup_required";
        }

        var dotBrush = DesignTokens.Foreground(dotTone);
        if (!ReferenceEquals(ConnectionDot.Fill, dotBrush))
        {
            ConnectionDot.Fill = dotBrush;
            ConnectionHalo.Fill = dotBrush;
        }

        // ライブ取得中はモデルの知らせが 1 秒に何度も来るので、変わったものだけを設定する。
        Retained.SetText(ConnectionStatus, LocalizedText.Get(statusKey));
        Retained.SetToolTip(ConnectionConsole, $"{ConnectionName.Text}\n{ConnectionStatus.Text}");

        // ホームには大きな操作のカードがあるので、ここでは状態だけを出す。
        Retained.SetVisible(OpenLiveButton, state.IsConnectionActive && SelectedDestination != WindowsSidebarDestination.LiveLog);
        Retained.SetVisible(ConnectionButton, !onHome);
        var consolePadding = new Thickness(12, 0, onHome && !state.IsConnectionActive ? 14 : 3, 0);
        if (ConnectionConsole.Padding != consolePadding)
        {
            ConnectionConsole.Padding = consolePadding;
        }

        string buttonKey;
        string glyph;
        string styleKey;
        bool enabled;
        if (state.IsConnectionActive)
        {
            (buttonKey, glyph, styleKey, enabled) = ("home.action.disconnect", FluentSymbol.Disconnected, "DangerButtonStyle", !state.IsDisconnecting);
        }
        else if (availability.IsDetected)
        {
            (buttonKey, glyph, styleKey, enabled) = ("home.action.connect", FluentSymbol.Plug, "AccentButtonStyle", state.ConnectionEndpoint is not null);
        }
        else
        {
            (buttonKey, glyph, styleKey, enabled) = ("home.action.setup_adapter", FluentSymbol.Sliders, "DefaultButtonStyle", true);
        }

        Retained.SetText(ConnectionButtonText, LocalizedText.Get(buttonKey));
        Retained.SetGlyph(ConnectionButtonIcon, glyph);
        Retained.SetStyle(ConnectionButton, styleKey);
        Retained.SetEnabled(ConnectionButton, enabled);
        Retained.SetToolTip(ConnectionButton, ConnectionButtonText.Text);
        Retained.SetAutomationName(ConnectionButton, ConnectionButtonText.Text);
    }

    /// <summary>
    /// 幅クラスに合わせて、ナビの形とタイトルバーの中身を切り替えます。
    /// </summary>
    /// <remarks>
    /// 責務: Wide/Expanded は題つきのペイン、Medium は記号だけのレール、Compact は画面の下のタブ帯にします（重ねて開くペインは使わない）。
    /// 狭いほど、タイトルバーのブランドの文字・接続の状態の文字・ボタンの文字を省きます（ツールチップには残す）。
    /// </remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyWidthClass(WidthClass widthClass)
    {
        _widthClass = widthClass;
        var compact = widthClass == WidthClass.Compact;

        // ナビの形の切り替え（NavigationView の表示の形・ペインの幅・下のタブ帯）は 1 回で 7〜12ms かかる。
        // 縁を引っぱって幅を続けて変えている間は、しきい値をまたぐたびに切り替えず、止まってから 1 回だけ切り替える。
        // 最大化・スナップ・起動のような 1 回きりの変化では、待たずにすぐ切り替える。
        if (_navigationClass is not null && ResponsiveLayout.IsLiveResizing)
        {
            _pendingNavigationClass = widthClass;
            RestartNavigationTimer();
        }
        else
        {
            ApplyNavigation(widthClass);
        }

        ScheduleTitleBarRegions();
        Retained.SetVisible(BrandTexts, !compact);
        Retained.SetVisible(BrandTagline, widthClass >= WidthClass.Expanded);
        Retained.SetVisible(ConnectionStatus, !compact);
        Retained.SetVisible(OpenLiveText, widthClass >= WidthClass.Expanded);
        Retained.SetVisible(ConnectionButtonText, !compact);
        ConnectionName.MaxWidth = compact ? 120 : widthClass == WidthClass.Medium ? 160 : 240;
        var gutter = Space.PageGutter(widthClass);
        NoticeArea.Padding = new Thickness(gutter, 0, gutter, 0);
        NoticeArea.MaxWidth = Metrics.ContentMaxWidth + (gutter * 2);
        NoticeArea.Spacing = Space.S;
        foreach (var bar in new[] { AdapterArrivalBar, PreviousRunBar })
        {
            bar.Margin = new Thickness(0, Space.M, 0, 0);
        }
    }

    /// <summary>
    /// ナビの形を幅クラスに合わせます。
    /// </summary>
    /// <remarks>
    /// 責務: Wide/Expanded は題つきのペイン、Medium は記号だけのレール、Compact は画面の下のタブ帯にします（重ねて開くペインは使わない）。
    /// 同じ幅クラスで 2 回目は何もしません。待たせていた切り替えは取り消します。
    /// </remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyNavigation(WidthClass widthClass)
    {
        _pendingNavigationClass = null;
        _navigationTimer?.Stop();
        if (_navigationClass == widthClass)
        {
            return;
        }

        _navigationClass = widthClass;
        (Navigation.PaneDisplayMode, Navigation.OpenPaneLength) = widthClass switch
        {
            WidthClass.Compact => (NavigationViewPaneDisplayMode.LeftMinimal, 280d),
            WidthClass.Medium => (NavigationViewPaneDisplayMode.LeftCompact, 248d),
            WidthClass.Expanded => (NavigationViewPaneDisplayMode.Left, 232d),
            _ => (NavigationViewPaneDisplayMode.Left, 264d),
        };
        Navigation.IsPaneOpen = widthClass >= WidthClass.Expanded;
        var compact = widthClass == WidthClass.Compact;
        if (compact)
        {
            EnsureBottomTabs();
        }

        // Compact のナビは下のタブ帯。タイトルバーの ≡ は出さず、重ねて開くペインは使わない。
        Retained.SetVisible(BottomBar, compact);
        Retained.SetVisible(PaneToggleButton, !compact);
        ScheduleTitleBarRegions();
    }

    /// <summary>待たせているナビの切り替えを、幅の変化が止まってから行うよう時計を掛け直します。</summary>
    /// <remarks>責務: 幅が続けて変わっている間は 150ms ごとに見直して待ち続け、止まったら <see cref="ApplyNavigation"/> を 1 回だけ呼びます。</remarks>
    private void RestartNavigationTimer()
    {
        if (_navigationTimer is null)
        {
            _navigationTimer = DispatcherQueue.CreateTimer();
            _navigationTimer.Interval = ResponsiveLayout.SettleDelay;
            _navigationTimer.IsRepeating = false;
            _navigationTimer.Tick += (_, _) =>
            {
                if (_pendingNavigationClass is not { } pending)
                {
                    return;
                }

                if (ResponsiveLayout.IsLiveResizing)
                {
                    RestartNavigationTimer();
                    return;
                }

                ApplyNavigation(pending);
            };
        }

        _navigationTimer.Stop();
        _navigationTimer.Start();
    }

    /// <summary>
    /// タイトルバーの押せる領域（戻る・ナビの開閉・接続コンソール）を OS へ伝えます。
    /// </summary>
    /// <remarks>責務: 押せる要素の位置を物理ピクセルの矩形にし、前回と違うときだけ伝えます。キャプションボタンの幅も合わせます。</remarks>
    /// <summary>タイトルバーの押せる領域の数え直しを、配置が落ち着いたあと 1 回にまとめて頼みます。</summary>
    private void ScheduleTitleBarRegions()
    {
        // 押せる領域の登録は OS（DWM）への呼び出しで重い。幅を変えている間や起動直後の配置が続く間は待ち、
        // 配置が落ち着いてから 1 回だけ登録する（待つ間はタイトルバーのボタンが押せないが、幅を変えている最中だけ）。
        if (_titleBarRegionsTimer is null)
        {
            _titleBarRegionsTimer = DispatcherQueue.CreateTimer();
            _titleBarRegionsTimer.Interval = TimeSpan.FromMilliseconds(250);
            _titleBarRegionsTimer.IsRepeating = false;
            _titleBarRegionsTimer.Tick += (_, _) => UpdateTitleBarRegions();
        }

        _titleBarRegionsTimer.Stop();
        _titleBarRegionsTimer.Start();
    }

    private void UpdateTitleBarRegions()
    {
        if (_appWindow is null || XamlRoot is null)
        {
            return;
        }

        var scale = XamlRoot.RasterizationScale;
        var inset = _appWindow.TitleBar.RightInset / scale;
        if (inset > 0 && Math.Abs(CaptionInsetColumn.Width.Value - inset) > 0.5)
        {
            CaptionInsetColumn.Width = new GridLength(inset);
        }

        var rects = new[] { BackButton, (FrameworkElement)PaneToggleButton, ConnectionConsole }
            .Where(element => element.Visibility == Visibility.Visible && element.ActualWidth > 0)
            .Select(element =>
            {
                var bounds = element.TransformToVisual(null).TransformBounds(new global::Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                return new RectInt32(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale),
                    (int)Math.Round(bounds.Height * scale));
            })
            .ToArray();
        if (rects.SequenceEqual(_passthroughRects))
        {
            return;
        }

        _passthroughRects = rects;
        InputNonClientPointerSource.GetForWindowId(_appWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects);
    }

    /// <summary>タイトルバーの戻るを押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnBackButtonClicked(object sender, RoutedEventArgs e) => TryGoBack();

    /// <summary>タイトルバーのナビの開閉を押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnPaneToggleClicked(object sender, RoutedEventArgs e) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    /// <summary>接続コンソールのボタンを押したときの処理です。</summary>
    /// <remarks>責務: 押した時点の状態で、切断・接続・アダプターの設定のどれかをホームと同じ操作として送ります。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnConnectionButtonClicked(object sender, RoutedEventArgs e)
    {
        var state = ConnectionState();
        if (state.IsConnectionActive)
        {
            if (!state.IsDisconnecting)
            {
                HandleHomeAction(new WindowsHomeAction.VehicleDisconnectionRequested());
            }
        }
        else if (state.DefaultAdapterAvailability.IsDetected)
        {
            if (state.ConnectionEndpoint is { } endpoint)
            {
                HandleHomeAction(new WindowsHomeAction.VehicleConnectionRequested(endpoint));
            }
        }
        else
        {
            HandleHomeAction(new WindowsHomeAction.AdapterSetupRequested());
        }
    }

    /// <summary>接続コンソールの「ライブを表示」を押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnOpenLiveClicked(object sender, RoutedEventArgs e) => Select(WindowsSidebarDestination.LiveLog);

    /// <summary>行き先の画面を出します（iOS 版の <c>MacOSDestinationView</c>）。</summary>
    /// <remarks>責務: 行き先ごとの画面を、必要なモデルを渡して作り、戻る操作の受け先を付け替えます。</remarks>
    /// <param name="destination">行き先。</param>
    private void ShowDestination(WindowsSidebarDestination destination)
    {
        SelectedDestination = destination;
        UiLog.Navigated(UiLog.Logger, destination.ToString());
        var root = _flow.Root;
        UIElement view = destination switch
        {
            WindowsSidebarDestination.Home => new WindowsHomeView(_flow.SettingsModel, root.LiveTelemetry, HandleHomeAction),
            WindowsSidebarDestination.LiveLog => new WindowsLiveTelemetryView(root.LiveTelemetry),
            WindowsSidebarDestination.History => new WindowsConnectionHistoryView(root.ConnectionHistory, root.SessionLogAnalysis),
            WindowsSidebarDestination.Maintenance => new WindowsMaintenanceView(root.Maintenance),
            WindowsSidebarDestination.Garage => new WindowsGarageView(root.VehicleManagement),
            _ => new WindowsSettingsView(_flow.SettingsModel, root.AccountSettings, root.Authentication, root.CloudSync, _diagnostics),
        };
        DestinationHost.Child = view;
        UpdateTabSelection(destination);
        AttachBackTarget(view as IBackNavigable);
        if (IsLoaded)
        {
            UpdateConnectionBar();
        }
    }

    /// <summary>戻る操作の受け先を付け替えます。</summary>
    /// <param name="target">新しい受け先。なければ null。</param>
    private void AttachBackTarget(IBackNavigable? target)
    {
        if (_backTarget is not null)
        {
            _backTarget.CanGoBackChanged -= OnCanGoBackChanged;
        }

        _backTarget = target;
        if (_backTarget is not null)
        {
            _backTarget.CanGoBackChanged += OnCanGoBackChanged;
        }

        UpdateBackButton();
    }

    /// <summary>今の画面で戻れるかどうかが変わったときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnCanGoBackChanged(object? sender, EventArgs e) => UpdateBackButton();

    /// <summary>タイトルバーの戻るボタンを、戻れるときだけ出します。</summary>
    private void UpdateBackButton()
    {
        var canGoBack = _backTarget?.CanGoBack == true;
        Retained.SetVisible(BackButton, canGoBack);
        ScheduleTitleBarRegions();
    }

    /// <summary>マウスの戻るボタン（XButton1）を押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnPointerPressedForBack(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsXButton1Pressed && TryGoBack())
        {
            e.Handled = true;
        }
    }

    /// <summary>今の画面で 1 段戻ります。</summary>
    /// <returns>戻ったなら true。</returns>
    private bool TryGoBack()
    {
        if (_backTarget is not { CanGoBack: true } target)
        {
            return false;
        }

        target.GoBack();
        return true;
    }

    /// <summary>ナビゲーションの項目を探します。</summary>
    /// <param name="destination">行き先。</param>
    /// <returns>項目。</returns>
    private NavigationViewItem Item(WindowsSidebarDestination destination) => Navigation.MenuItems
        .Concat(Navigation.FooterMenuItems)
        .OfType<NavigationViewItem>()
        .First(item => (WindowsSidebarDestination)item.Tag == destination);

    /// <summary>行き先の項目を作ります。</summary>
    /// <remarks>責務: 記号と 1 行の題を持つナビゲーションの項目を作り、補足をツールチップに入れます。</remarks>
    /// <param name="destination">行き先。</param>
    /// <returns>ナビゲーションの項目。</returns>
    private static NavigationViewItem MakeItem(WindowsSidebarDestination destination)
    {
        var title = LocalizedText.Get(destination.TitleKey());
        var item = new NavigationViewItem
        {
            Tag = destination,

            // 長い訳（スペイン語など）がペインの幅で途中から切れて見えないよう、末尾を「…」にする（全文はツールチップ）。
            Content = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap },
            Icon = new FontIcon { Glyph = destination.Glyph() },
        };
        var shortcut = "Ctrl+" + (WindowsSidebarDestinationPresentation.AllCases.ToList().IndexOf(destination) + 1);
        ToolTipService.SetToolTip(item, $"{title} ({shortcut})\n{LocalizedText.Get(destination.SubtitleKey())}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "windows-sidebar-" + destination.ToString().ToLowerInvariant());
        return item;
    }

    /// <summary>キーボードの近道を割り当てます。</summary>
    /// <remarks>責務: Ctrl+1〜6 で行き先を選び（macOS の ⌘1〜6）、Ctrl+, で設定を開き、Alt+← で 1 段戻れるようにします。</remarks>
    private void AddKeyboardShortcuts()
    {
        var keys = new[] { VirtualKey.Number1, VirtualKey.Number2, VirtualKey.Number3, VirtualKey.Number4, VirtualKey.Number5, VirtualKey.Number6 };
        for (var index = 0; index < keys.Length; index++)
        {
            AddAccelerator(keys[index], VirtualKeyModifiers.Control, WindowsSidebarDestinationPresentation.AllCases[index]);
        }

        AddAccelerator((VirtualKey)188, VirtualKeyModifiers.Control, WindowsSidebarDestination.Settings);
        var back = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        back.Invoked += (_, args) => args.Handled = TryGoBack();
        KeyboardAccelerators.Add(back);
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    /// <summary>行き先へ移るキーボードの近道を 1 つ足します。</summary>
    /// <param name="key">キー。</param>
    /// <param name="modifiers">修飾キー。</param>
    /// <param name="destination">行き先。</param>
    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, WindowsSidebarDestination destination)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            Select(destination);
        };
        KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>履歴の項目のバッジを更新します。</summary>
    /// <remarks>責務: 中断したセッションがあればオレンジの数のバッジを出し、なければ消します。</remarks>
    private void UpdateHistoryBadge()
    {
        var item = Item(WindowsSidebarDestination.History);
        var count = _flow.Root.ConnectionHistory.State.InterruptedCount;
        item.InfoBadge = count > 0
            ? new InfoBadge { Value = count, Background = DesignTokens.Foreground(Tone.Caution) }
            : null;
        if (count > 0)
        {
            ToolTipService.SetToolTip(item.InfoBadge, LocalizedText.Get("history.warning.interrupted"));
        }

        if (_tabs.TryGetValue(WindowsSidebarDestination.History, out var tab))
        {
            tab.Badge.Value = count;
            Retained.SetVisible(tab.Badge, count > 0);
        }
    }

    /// <summary>下のタブ帯がまだなければ作り、今の選択・バッジに合わせます。</summary>
    private void EnsureBottomTabs()
    {
        if (_tabs.Count > 0)
        {
            return;
        }

        BuildBottomTabs();

        // ナビの項目がまだない（コンストラクターの途中）こともあるので、タブのバッジだけを合わせる。
        var count = _flow.Root.ConnectionHistory.State.InterruptedCount;
        var badge = _tabs[WindowsSidebarDestination.History].Badge;
        badge.Value = count;
        Retained.SetVisible(badge, count > 0);
    }

    /// <summary>下のタブ帯を作ります（行き先ごとに 1 回だけ）。</summary>
    /// <remarks>責務: 記号と短い題のタブを等間隔に並べ、押したら行き先へ移るようにします。題が切れるほど狭いときは記号だけにし、題はツールチップに残します。</remarks>
    private void BuildBottomTabs()
    {
        var destinations = WindowsSidebarDestinationPresentation.AllCases;
        for (var index = 0; index < destinations.Count; index++)
        {
            var destination = destinations[index];
            var title = LocalizedText.Get(destination.TitleKey());
            BottomTabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });

            var indicator = new Border
            {
                Width = 28,
                Height = 3,
                CornerRadius = new CornerRadius(0, 0, Radius.S, Radius.S),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Background = DesignTokens.Foreground(Tone.Accent),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            var glyph = new FontIcon { Glyph = destination.Glyph(), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
            var badge = new InfoBadge
            {
                Background = DesignTokens.Foreground(Tone.Caution),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -10, 0),
                Visibility = Visibility.Collapsed,
            };
            var iconHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Children = { glyph, badge } };
            var label = new TextBlock
            {
                // 見える文字は短い名前（狭い幅で切れないように）。読み上げとツールチップは正式な名前のまま。
                Text = LocalizedText.Get(destination.TabTitleKey()),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var content = new Grid { RowSpacing = 2, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(Space.XS, 0, Space.XS, 0) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(iconHost);
            Grid.SetRow(label, 1);
            content.Children.Add(label);

            var button = new Button
            {
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["ChromeButtonStyle"],
                Width = double.NaN,
                Height = double.NaN,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(0),
                Content = new Grid { Children = { content, indicator } },
                Tag = destination,
            };
            var shortcut = "Ctrl+" + (index + 1);
            ToolTipService.SetToolTip(button, $"{title} ({shortcut})");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-tab-" + destination.ToString().ToLowerInvariant());
            button.Click += (_, _) =>
            {
                if (SelectedDestination != destination)
                {
                    Select(destination);
                }
            };
            Grid.SetColumn(button, index);
            BottomTabs.Children.Add(button);

            var visual = ElementCompositionPreview.GetElementVisual(indicator);
            visual.CenterPoint = new Vector3(14, 1.5f, 0);
            visual.Scale = new Vector3(0.2f, 1, 1);
            _tabs[destination] = new BottomTab(button, indicator, glyph, label, badge);
        }

        UpdateTabSelection(SelectedDestination);
    }

    /// <summary>テーマ（明・暗）が変わったときに、下のタブ帯の色を引き直します。</summary>
    /// <remarks>責務: ブラシは作った時点のテーマで引くので、テーマが変わったら印・バッジ・選択の色を引き直します。</remarks>
    private void RefreshTabTheme()
    {
        foreach (var tab in _tabs.Values)
        {
            tab.Indicator.Background = DesignTokens.Foreground(Tone.Accent);
            tab.Badge.Background = DesignTokens.Foreground(Tone.Caution);
        }

        UpdateTabSelection(SelectedDestination);
    }

    /// <summary>下のタブ帯の選択中の印を、行き先に合わせます。</summary>
    /// <remarks>責務: 選択中のタブだけシアンの印と色にし、印は GPU の Composition で少し伸びて現れます（動きが無効なら即時）。</remarks>
    /// <param name="destination">選択中の行き先。</param>
    private void UpdateTabSelection(WindowsSidebarDestination destination)
    {
        foreach (var (key, tab) in _tabs)
        {
            var selected = key == destination;
            var foreground = selected ? DesignTokens.Foreground(Tone.Accent) : DesignTokens.Foreground(Tone.Neutral);
            tab.Glyph.Foreground = foreground;
            Retained.SetForeground(tab.Label, foreground);
            tab.Label.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

            var visual = ElementCompositionPreview.GetElementVisual(tab.Indicator);
            var scale = selected ? Vector3.One : new Vector3(0.2f, 1, 1);
            var opacity = selected ? 1f : 0f;
            if (Motion.IsEnabled && IsLoaded && tab.Button.IsLoaded && BottomBar.Visibility == Visibility.Visible)
            {
                var compositor = visual.Compositor;
                var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
                scaleAnimation.InsertKeyFrame(1f, scale);
                scaleAnimation.Duration = Motion.Normal;
                var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
                opacityAnimation.InsertKeyFrame(1f, opacity);
                opacityAnimation.Duration = Motion.Fast;
                visual.StartAnimation("Scale", scaleAnimation);
                visual.StartAnimation("Opacity", opacityAnimation);
            }
            else
            {
                visual.Scale = scale;
                visual.Opacity = opacity;
            }
        }
    }
}
