using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8ForWindows.AppRoot.Flow;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.LiveTelemetry.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Maintenance.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Views;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using Windows.System;

namespace ProjectZD8ForWindows.Platform.Windows.AppShell;

/// <summary>
/// サインイン後のアプリのシェルです。左のナビゲーションと、選んだ行き先の画面を出します（iOS 版の <c>MacOSAppShellView</c>）。
/// </summary>
/// <remarks>
/// 行き先の選択と「接続の結果を待っているか」だけを画面の一時的な状態として持ちます。
/// どの画面からでも接続・切断できるように、上に接続バーを置きます（ホームを除く）。
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

    /// <summary>アプリの流れと最初の行き先を指定して作ります。</summary>
    /// <param name="flow">アプリの流れ。</param>
    /// <param name="initialDestination">最初に開く行き先。</param>
    public WindowsAppShellView(WindowsApplicationFlow flow, WindowsSidebarDestination initialDestination)
    {
        _flow = flow;
        InitializeComponent();
        BrandTagline.Text = LocalizedText.Get("sidebar.brand.tagline");
        OpenLiveText.Text = LocalizedText.Get("windows.shell.open_live");
        ToolTipService.SetToolTip(OpenLiveButton, LocalizedText.Get("sidebar.live_log"));
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
    /// <remarks>責務: モデルの監視を始め、履歴の警告の数と接続バーを出します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _flow.Root.ConnectionHistory.PropertyChanged += OnHistoryChanged;
        _flow.Root.VehicleManagement.PropertyChanged += OnVehicleManagementChanged;
        _flow.Root.AdapterArrivalPrompt.PropertyChanged += OnAdapterArrivalPromptChanged;
        _flow.Root.LiveTelemetry.PropertyChanged += OnConnectionChanged;
        _flow.SettingsModel.PropertyChanged += OnConnectionChanged;
        RenderAdapterArrivalPrompt();
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
        AttachBackTarget(null);
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
    /// <remarks>責務: ホームや接続バーから接続したあと、確認・登録・失敗になったらガレージへ移ります。</remarks>
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
    /// ホーム画面（と接続バー）の操作を処理します（iOS 版の <c>handleHomeAction</c>）。
    /// </summary>
    /// <remarks>責務: ホームの操作を、設定画面への移動・車両接続の開始・切断へ変換します。</remarks>
    /// <param name="action">ホームの操作。</param>
    private void HandleHomeAction(WindowsHomeAction action)
    {
        switch (action)
        {
            case WindowsHomeAction.AdapterSetupRequested:
                Select(WindowsSidebarDestination.Settings);
                _flow.SettingsModel.Send(new WindowsSettingsAction.AdapterAttentionRequested());
                break;
            case WindowsHomeAction.VehicleConnectionRequested request:
                _awaitsVehicleConnectionOutcome = true;
                _flow.Root.StartVehicleConnection.Execute(request.Endpoint);
                break;
            case WindowsHomeAction.VehicleDisconnectionRequested:
                _flow.Root.LiveTelemetry.Send(new LiveTelemetryAction.StopRequested());
                break;
        }
    }

    /// <summary>今の接続の状態です。</summary>
    /// <returns>ホームと同じ接続の状態。</returns>
    private WindowsHomeState ConnectionState() => new(_flow.SettingsModel.State, _flow.Root.LiveTelemetry.State);

    /// <summary>
    /// 接続バーを描きます。
    /// </summary>
    /// <remarks>責務: 既定のアダプターと接続の状態を、点の色・名前・状態の文字・ボタンへ写します。作り直さずに値だけを変えるので、押している最中のボタンが消えません。</remarks>
    private void UpdateConnectionBar()
    {
        var state = ConnectionState();
        var live = _flow.Root.LiveTelemetry.State;
        var availability = state.DefaultAdapterAvailability;
        ConnectionBar.Visibility = SelectedDestination == WindowsSidebarDestination.Home ? Visibility.Collapsed : Visibility.Visible;
        ConnectionName.Text = availability.DisplayName ?? LocalizedText.Get("home.adapter.not_configured");
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

        ConnectionDot.Fill = DesignTokens.Foreground(dotTone);
        ConnectionStatus.Text = LocalizedText.Get(statusKey);
        ToolTipService.SetToolTip(ConnectionStatus, ConnectionStatus.Text);
        OpenLiveButton.Visibility = state.IsConnectionActive && SelectedDestination != WindowsSidebarDestination.LiveLog
            ? Visibility.Visible
            : Visibility.Collapsed;

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

        ConnectionButtonText.Text = LocalizedText.Get(buttonKey);
        ConnectionButtonIcon.Glyph = glyph;
        var style = DesignTokens.Style(styleKey);
        if (!ReferenceEquals(ConnectionButton.Style, style))
        {
            ConnectionButton.Style = style;
        }

        ConnectionButton.IsEnabled = enabled;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ConnectionButton, ConnectionButtonText.Text);
    }

    /// <summary>接続バーのボタンを押したときの処理です。</summary>
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

    /// <summary>接続バーの「ライブを表示」を押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnOpenLiveClicked(object sender, RoutedEventArgs e) => Select(WindowsSidebarDestination.LiveLog);

    /// <summary>行き先の画面を出します（iOS 版の <c>MacOSDestinationView</c>）。</summary>
    /// <remarks>責務: 行き先ごとの画面を、必要なモデルを渡して作り、戻る操作の受け先を付け替えます。</remarks>
    /// <param name="destination">行き先。</param>
    private void ShowDestination(WindowsSidebarDestination destination)
    {
        SelectedDestination = destination;
        var root = _flow.Root;
        UIElement view = destination switch
        {
            WindowsSidebarDestination.Home => new WindowsHomeView(_flow.SettingsModel, root.LiveTelemetry, HandleHomeAction),
            WindowsSidebarDestination.LiveLog => new WindowsLiveTelemetryView(root.LiveTelemetry),
            WindowsSidebarDestination.History => new WindowsConnectionHistoryView(root.ConnectionHistory, root.SessionLogAnalysis),
            WindowsSidebarDestination.Maintenance => new WindowsMaintenanceView(root.Maintenance),
            WindowsSidebarDestination.Garage => new WindowsGarageView(root.VehicleManagement),
            _ => new WindowsSettingsView(_flow.SettingsModel, root.AccountSettings, root.Authentication, root.CloudSync),
        };
        DestinationHost.Child = view;
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

    /// <summary>ナビゲーションの戻るボタンを、戻れるときだけ出します。</summary>
    private void UpdateBackButton()
    {
        var canGoBack = _backTarget?.CanGoBack == true;
        Navigation.IsBackEnabled = canGoBack;
        Navigation.IsBackButtonVisible = canGoBack ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed;
    }

    /// <summary>ナビゲーションの戻るボタンを押したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => TryGoBack();

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
            Content = title,
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
    }
}
