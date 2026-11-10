using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ProjectZD8.Application.LiveTelemetry.Actions;
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
/// <remarks>行き先の選択と「接続の結果を待っているか」だけを画面の一時的な状態として持ちます。</remarks>
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

    /// <summary>アプリの流れと最初の行き先を指定して作ります。</summary>
    /// <param name="flow">アプリの流れ。</param>
    /// <param name="initialDestination">最初に開く行き先。</param>
    public WindowsAppShellView(WindowsApplicationFlow flow, WindowsSidebarDestination initialDestination)
    {
        _flow = flow;
        InitializeComponent();
        BrandTagline.Text = LocalizedText.Get("sidebar.brand.tagline");
        FooterCaption.Text = LocalizedText.Get("sidebar.footer.caption");
        Navigation.MenuItems.Add(new NavigationViewItemHeader { Content = LocalizedText.Get("sidebar.section.navigation") });
        foreach (var destination in WindowsSidebarDestinationPresentation.AllCases)
        {
            Navigation.MenuItems.Add(MakeItem(destination));
        }

        AddKeyboardShortcuts();
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
        Navigation.SelectedItem = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .First(item => (WindowsSidebarDestination)item.Tag == destination);
        _isSelectingProgrammatically = false;
        ShowDestination(destination);
    }

    /// <summary>表示されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視を始め、履歴の警告の数を出します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _flow.Root.ConnectionHistory.PropertyChanged += OnHistoryChanged;
        _flow.Root.VehicleManagement.PropertyChanged += OnVehicleManagementChanged;
        _flow.Root.AdapterArrivalPrompt.PropertyChanged += OnAdapterArrivalPromptChanged;
        RenderAdapterArrivalPrompt();
        UpdateHistoryBadge();
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

    /// <summary>車両管理の段階が変わったときの処理です（iOS 版の <c>handleVehicleManagementPhase</c>）。</summary>
    /// <remarks>責務: ホームから接続したあと、確認・登録・失敗になったらガレージへ移ります。</remarks>
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
    /// ホーム画面の操作を処理します（iOS 版の <c>handleHomeAction</c>）。
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

    /// <summary>行き先の画面を出します（iOS 版の <c>MacOSDestinationView</c>）。</summary>
    /// <remarks>責務: 行き先ごとの画面を、必要なモデルを渡して作ります。</remarks>
    /// <param name="destination">行き先。</param>
    private void ShowDestination(WindowsSidebarDestination destination)
    {
        SelectedDestination = destination;
        var root = _flow.Root;
        DestinationHost.Child = destination switch
        {
            WindowsSidebarDestination.Home => new WindowsHomeView(_flow.SettingsModel, root.LiveTelemetry, HandleHomeAction),
            WindowsSidebarDestination.LiveLog => new WindowsLiveTelemetryView(root.LiveTelemetry),
            WindowsSidebarDestination.History => new WindowsConnectionHistoryView(root.ConnectionHistory, root.SessionLogAnalysis),
            WindowsSidebarDestination.Maintenance => new WindowsMaintenanceView(root.Maintenance),
            WindowsSidebarDestination.Garage => new WindowsGarageView(root.VehicleManagement),
            _ => new WindowsSettingsView(_flow.SettingsModel, root.AccountSettings, root.Authentication, root.CloudSync),
        };
    }

    /// <summary>行き先の項目を作ります。</summary>
    /// <remarks>責務: 記号・題・補足を持つナビゲーションの項目を作ります。</remarks>
    /// <param name="destination">行き先。</param>
    /// <returns>ナビゲーションの項目。</returns>
    private static NavigationViewItem MakeItem(WindowsSidebarDestination destination)
    {
        var texts = new StackPanel { Spacing = 0, Padding = new Thickness(0, 6, 0, 6) };
        texts.Children.Add(Typography.Text(LocalizedText.Get(destination.TitleKey()), "BodyStrongTextBlockStyle", wraps: false));
        texts.Children.Add(Typography.Text(LocalizedText.Get(destination.SubtitleKey()), "SecondaryCaptionTextStyle", wraps: false));
        var item = new NavigationViewItem
        {
            Tag = destination,
            Content = texts,
            Icon = new FontIcon { Glyph = destination.Glyph() },
        };
        ToolTipService.SetToolTip(item, LocalizedText.Get(destination.TitleKey()));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, LocalizedText.Get(destination.TitleKey()));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "windows-sidebar-" + destination.ToString().ToLowerInvariant());
        return item;
    }

    /// <summary>Ctrl+1〜6 で行き先を選べるようにします。</summary>
    /// <remarks>責務: 表示順の行き先へキーボードの近道を割り当てます（macOS の ⌘1〜6）。</remarks>
    private void AddKeyboardShortcuts()
    {
        var keys = new[] { VirtualKey.Number1, VirtualKey.Number2, VirtualKey.Number3, VirtualKey.Number4, VirtualKey.Number5, VirtualKey.Number6 };
        for (var index = 0; index < keys.Length; index++)
        {
            var destination = WindowsSidebarDestinationPresentation.AllCases[index];
            var accelerator = new KeyboardAccelerator { Key = keys[index], Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                Select(destination);
            };
            KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>履歴の項目のバッジを更新します。</summary>
    /// <remarks>責務: 中断したセッションがあればオレンジの数のバッジを出し、なければ消します。</remarks>
    private void UpdateHistoryBadge()
    {
        var item = Navigation.MenuItems.OfType<NavigationViewItem>()
            .First(menu => (WindowsSidebarDestination)menu.Tag == WindowsSidebarDestination.History);
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
