using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Views;

/// <summary>
/// ホーム画面です。既定アダプターの状態と、接続・切断・アダプター設定への操作を出します（iOS 版の <c>MacOSHomeView</c>）。
/// </summary>
/// <remarks>「検出済み」は探索の結果（設定画面の状態）が裏付けるときだけ出します。</remarks>
public sealed partial class WindowsHomeView : UserControl
{
    /// <summary>設定画面の表示モデル（既定アダプターの検出状態）です。</summary>
    private readonly WindowsSettingsPresentationModel _settings;

    /// <summary>ライブ取得のモデル（接続中かどうか）です。</summary>
    private readonly LiveTelemetryModel _liveTelemetry;

    /// <summary>ホームの操作の送り先です。</summary>
    private readonly Action<WindowsHomeAction> _send;

    /// <summary>モデルと操作の送り先を受け取ってホーム画面を作ります。</summary>
    /// <param name="settings">設定画面の表示モデル。</param>
    /// <param name="liveTelemetry">ライブ取得のモデル。</param>
    /// <param name="send">ホームの操作の送り先。</param>
    public WindowsHomeView(WindowsSettingsPresentationModel settings, LiveTelemetryModel liveTelemetry, Action<WindowsHomeAction> send)
    {
        _settings = settings;
        _liveTelemetry = liveTelemetry;
        _send = send;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _settings.PropertyChanged += OnModelChanged;
            _liveTelemetry.PropertyChanged += OnModelChanged;
            Render();
        };
        Unloaded += (_, _) =>
        {
            _settings.PropertyChanged -= OnModelChanged;
            _liveTelemetry.PropertyChanged -= OnModelChanged;
        };
        SizeChanged += (_, args) =>
        {
            if ((args.PreviousSize.Width < 720) != (args.NewSize.Width < 720))
            {
                Render();
            }
        };
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>ホーム画面を描きます。</summary>
    /// <remarks>責務: 見出し、既定アダプターのカード、今後の機能の帯を作ります。</remarks>
    private void Render()
    {
        var state = new WindowsHomeState(_settings.State, _liveTelemetry.State);
        Page.Children.Clear();
        var header = new StackPanel { Spacing = 7 };
        header.Children.Add(Typography.Text(LocalizedText.Get("home.eyebrow"), "EyebrowTextStyle"));
        header.Children.Add(Typography.Text(LocalizedText.Get("home.title"), "TitleLargeTextBlockStyle"));
        header.Children.Add(Typography.Text(LocalizedText.Get("home.subtitle"), "SecondaryBodyTextStyle"));
        Page.Children.Add(header);
        Page.Children.Add(AdapterHero(state, ActualWidth > 0 && ActualWidth < 720));

        var rail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        rail.Children.Add(new FontIcon { Glyph = FluentSymbol.Gauge, FontSize = 16, Foreground = DesignTokens.Foreground(Tone.Accent) });
        rail.Children.Add(Typography.Text(LocalizedText.Get("home.future.caption"), "SecondaryCaptionTextStyle"));
        Page.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(16), Child = rail });
    }

    /// <summary>既定アダプターのカードを作ります。</summary>
    /// <remarks>責務: アダプターの名前・検出状態と、主の操作を、幅に合わせて横並びか縦積みにします。</remarks>
    /// <param name="state">ホームの表示状態。</param>
    /// <param name="isCompact">狭い幅なら true。</param>
    /// <returns>カード。</returns>
    private Border AdapterHero(WindowsHomeState state, bool isCompact)
    {
        var identity = AdapterIdentity(state);
        var action = PrimaryAction(state);
        var layout = new Grid { ColumnSpacing = 34, RowSpacing = 24 };
        if (isCompact)
        {
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(action, 1);
        }
        else
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(action, 1);
        }

        layout.Children.Add(identity);
        layout.Children.Add(action);
        layout.MinHeight = 170;
        var card = SurfaceCard.Create(layout, padding: 28);
        card.CornerRadius = new CornerRadius(20);
        return card;
    }

    /// <summary>アダプターの名前と状態の部分を作ります。</summary>
    /// <remarks>責務: 記号、小見出し、アダプター名（未設定なら案内）、検出状態のラベルを並べます。</remarks>
    /// <param name="state">ホームの表示状態。</param>
    /// <returns>表示の要素。</returns>
    private static Grid AdapterIdentity(WindowsHomeState state)
    {
        var availability = state.DefaultAdapterAvailability;
        var grid = new Grid { ColumnSpacing = 20, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = IconTile.Create(availability.IsDetected ? FluentSymbol.Plug : FluentSymbol.Disconnected, Tone.Accent, 82);
        tile.CornerRadius = new CornerRadius(41);
        grid.Children.Add(tile);
        var texts = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(LocalizedText.Get("home.adapter.eyebrow"), "EyebrowTextStyle"));
        texts.Children.Add(Typography.Text(
            availability.DisplayName ?? LocalizedText.Get("home.adapter.not_configured"),
            "SubtitleTextBlockStyle"));
        var statusKey = availability.IsDetected
            ? "home.adapter.detected"
            : availability.HasDefaultAdapter ? "home.adapter.not_detected" : "home.adapter.setup_required";
        texts.Children.Add(StatusPill.Create(
            availability.IsDetected ? FluentSymbol.Completed : FluentSymbol.Info,
            LocalizedText.Get(statusKey),
            availability.IsDetected ? Tone.Success : Tone.Neutral));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return grid;
    }

    /// <summary>主の操作を作ります。</summary>
    /// <remarks>責務: 接続中なら切断、検出済みなら接続、それ以外はアダプター設定へのボタンを作ります。</remarks>
    /// <param name="state">ホームの表示状態。</param>
    /// <returns>表示の要素。</returns>
    private StackPanel PrimaryAction(WindowsHomeState state)
    {
        var panel = new StackPanel { Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        Button button;
        if (state.IsConnectionActive)
        {
            button = new Button
            {
                Content = LocalizedText.Get("home.action.disconnect"),
                Style = DesignTokens.Style("DangerButtonStyle"),
                IsEnabled = !state.IsDisconnecting,
            };
            button.Click += (_, _) => _send(new WindowsHomeAction.VehicleDisconnectionRequested());
        }
        else if (state.DefaultAdapterAvailability.IsDetected)
        {
            button = new Button
            {
                Content = LocalizedText.Get("home.action.connect"),
                Style = DesignTokens.Style("AccentButtonStyle"),
                IsEnabled = state.ConnectionEndpoint is not null,
            };
            button.Click += (_, _) =>
            {
                if (state.ConnectionEndpoint is { } endpoint)
                {
                    _send(new WindowsHomeAction.VehicleConnectionRequested(endpoint));
                }
            };
        }
        else
        {
            button = new Button
            {
                Content = new GlyphLabel(FluentSymbol.ChevronRight, LocalizedText.Get("home.action.setup_adapter"), "BodyStrongTextBlockStyle"),
                Style = DesignTokens.Style("AccentButtonStyle"),
            };
            button.Click += (_, _) => _send(new WindowsHomeAction.AdapterSetupRequested());
            panel.Children.Add(button);
            panel.Children.Add(Typography.Text(LocalizedText.Get("home.action.setup_hint"), "SecondaryCaptionTextStyle"));
            button.MinWidth = 210;
            button.MinHeight = 40;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-home-setup-adapter");
            return panel;
        }

        button.MinWidth = 190;
        button.MinHeight = 40;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-home-primary-action");
        panel.Children.Add(button);
        return panel;
    }
}
