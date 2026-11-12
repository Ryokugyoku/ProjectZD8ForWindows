using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Views;

/// <summary>
/// ホーム画面です。既定アダプターの状態と、接続・切断・アダプター設定への操作を出します（iOS 版の <c>MacOSHomeView</c>）。
/// </summary>
/// <remarks>
/// 新しい画面の作り方の見本です（docs/UI_GUIDELINES.md）：
/// - 要素はコンストラクターで 1 回だけ組み、フィールドに持つ。モデルが変わったら <see cref="Update"/> で値だけを差し替える（木を作り直さない）。
/// - モデルの知らせは <see cref="UiUpdateThrottle"/> で束ね、1 フレームに 1 回まで更新する（ライブ取得は 1 秒に何度もモデルを変える）。
/// - 幅は <see cref="ResponsiveLayout"/> の幅クラスで決める（<see cref="PageScaffold"/>・<see cref="AdaptiveSplit"/> が追従する）。
/// - ボタンは作り直さないので、押している最中に消えない（RenderGate は不要）。押したときは、その時点の状態を読み直して操作を送る。
/// 「検出済み」は探索の結果（設定画面の状態）が裏付けるときだけ出します。
/// </remarks>
public sealed partial class WindowsHomeView : UserControl
{
    /// <summary>設定画面の表示モデル（既定アダプターの検出状態）です。</summary>
    private readonly WindowsSettingsPresentationModel _settings;

    /// <summary>ライブ取得のモデル（接続中かどうか）です。</summary>
    private readonly LiveTelemetryModel _liveTelemetry;

    /// <summary>ホームの操作の送り先です。</summary>
    private readonly Action<WindowsHomeAction> _send;

    /// <summary>更新を 1 フレームに 1 回へ束ねる仕組みです。</summary>
    private readonly UiUpdateThrottle _throttle;

    /// <summary>アダプターの状態の輪（計器の外周）です。</summary>
    private readonly Ellipse _ring = new() { StrokeThickness = 3 };

    /// <summary>輪の内側の記号のタイルです。</summary>
    private readonly Border _gaugeTile;

    /// <summary>輪と記号をまとめた計器です。</summary>
    private readonly Grid _gauge = new();

    /// <summary>アダプターの名前です。</summary>
    private readonly TextBlock _adapterName = Typography.Text(string.Empty, TextStyles.PageTitle);

    /// <summary>検出状態の印です。</summary>
    private readonly Border _statusPill = StatusPill.Create(null, string.Empty, Tone.Neutral);

    /// <summary>主の操作のボタンです。</summary>
    private readonly Button _primaryButton = new()
    {
        MinWidth = Metrics.PrimaryControlMinWidth,
        MinHeight = Metrics.PrimaryControlHeight,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>主の操作のボタンの記号です。</summary>
    private readonly FontIcon _primaryIcon = new() { FontSize = 14 };

    /// <summary>主の操作のボタンの文字です。</summary>
    private readonly TextBlock _primaryText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    /// <summary>アダプターを設定するときの補足です。</summary>
    private readonly TextBlock _setupHint = Typography.Text(LocalizedText.Get("home.action.setup_hint"), TextStyles.SecondaryCaption);

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
        _gaugeTile = IconTile.Create(FluentSymbol.Plug, Tone.Neutral, Metrics.IconTileLarge);
        _gaugeTile.CornerRadius = new CornerRadius(Radius.Pill);
        _gaugeTile.HorizontalAlignment = HorizontalAlignment.Center;
        _gauge.Children.Add(_ring);
        _gauge.Children.Add(_gaugeTile);
        _gauge.VerticalAlignment = VerticalAlignment.Center;
        _primaryButton.Content = PrimaryButtonContent();
        _primaryButton.Click += OnPrimaryClicked;
        _setupHint.MaxWidth = 260;

        var page = new PageScaffold();
        page.Body.Children.Add(Header());
        page.Body.Children.Add(SurfaceCard.Hero(AdapterConsole()));
        page.Body.Children.Add(FutureRail());
        Content = page;

        _throttle = new UiUpdateThrottle(this, Update);
        ResponsiveLayout.Bind(this, ApplyLayout);
        Loaded += (_, _) =>
        {
            _settings.PropertyChanged += OnModelChanged;
            _liveTelemetry.PropertyChanged += OnModelChanged;
            _throttle.FlushNow();
        };
        Unloaded += (_, _) =>
        {
            _settings.PropertyChanged -= OnModelChanged;
            _liveTelemetry.PropertyChanged -= OnModelChanged;
        };
        Update();
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 次のフレームでの更新を頼みます（何度来ても 1 回にまとまる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => _throttle.Request();

    /// <summary>今のホームの状態です。</summary>
    /// <returns>ホームの表示状態。</returns>
    private WindowsHomeState State() => new(_settings.State, _liveTelemetry.State);

    /// <summary>
    /// 画面の値を今の状態に合わせます。
    /// </summary>
    /// <remarks>責務: アダプターの名前・検出状態の印・計器の色・主の操作の文字と色と押せるかを差し替えます。要素は作り直しません。</remarks>
    private void Update()
    {
        var state = State();
        var availability = state.DefaultAdapterAvailability;
        Retained.SetText(_adapterName, availability.DisplayName ?? LocalizedText.Get("home.adapter.not_configured"));

        Tone tone;
        string statusKey;
        if (state.IsConnectionActive)
        {
            (tone, statusKey) = (Tone.Success, state.IsDisconnecting ? "windows.shell.connection.disconnecting" : "windows.shell.connection.connected");
        }
        else if (availability.IsDetected)
        {
            (tone, statusKey) = (Tone.Success, "home.adapter.detected");
        }
        else
        {
            (tone, statusKey) = (Tone.Neutral, availability.HasDefaultAdapter ? "home.adapter.not_detected" : "home.adapter.setup_required");
        }

        StatusPill.Set(_statusPill, availability.IsDetected || state.IsConnectionActive ? FluentSymbol.Completed : FluentSymbol.Info, LocalizedText.Get(statusKey), tone);
        IconTile.SetTone(_gaugeTile, FluentSymbol.Plug, availability.IsDetected || state.IsConnectionActive ? Tone.Accent : Tone.Neutral);
        _ring.Stroke = state.IsConnectionActive
            ? DesignTokens.Brush("RevTorqueSignalBrush")
            : availability.IsDetected ? DesignTokens.Brush("RevTorqueAccentStrokeBrush") : DesignTokens.Brush("RevTorqueDividerBrush");

        string textKey;
        string glyph;
        string styleKey;
        bool enabled;
        string automationId = "windows-home-primary-action";
        if (state.IsConnectionActive)
        {
            (textKey, glyph, styleKey, enabled) = ("home.action.disconnect", FluentSymbol.Disconnected, "DangerButtonStyle", !state.IsDisconnecting);
        }
        else if (availability.IsDetected)
        {
            (textKey, glyph, styleKey, enabled) = ("home.action.connect", FluentSymbol.Plug, "AccentButtonStyle", state.ConnectionEndpoint is not null);
        }
        else
        {
            (textKey, glyph, styleKey, enabled) = ("home.action.setup_adapter", FluentSymbol.ChevronRight, "AccentButtonStyle", true);
            automationId = "windows-home-setup-adapter";
        }

        Retained.SetText(_primaryText, LocalizedText.Get(textKey));
        Retained.SetGlyph(_primaryIcon, glyph);
        Retained.SetStyle(_primaryButton, styleKey);
        Retained.SetEnabled(_primaryButton, enabled);
        Retained.SetVisible(_setupHint, !state.IsConnectionActive && !availability.IsDetected);
        if (AutomationProperties.GetAutomationId(_primaryButton) != automationId)
        {
            AutomationProperties.SetAutomationId(_primaryButton, automationId);
        }
    }

    /// <summary>主の操作を押したときの処理です。</summary>
    /// <remarks>責務: 押した時点の状態を読み直し、切断・接続・アダプター設定のどれかを送ります。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnPrimaryClicked(object sender, RoutedEventArgs e)
    {
        var state = State();
        if (state.IsConnectionActive)
        {
            if (!state.IsDisconnecting)
            {
                _send(new WindowsHomeAction.VehicleDisconnectionRequested());
            }
        }
        else if (state.DefaultAdapterAvailability.IsDetected)
        {
            if (state.ConnectionEndpoint is { } endpoint)
            {
                _send(new WindowsHomeAction.VehicleConnectionRequested(endpoint));
            }
        }
        else
        {
            _send(new WindowsHomeAction.AdapterSetupRequested());
        }
    }

    /// <summary>
    /// 幅クラスに合わせて配置を切り替えます。
    /// </summary>
    /// <remarks>責務: 計器の大きさと、題の大きさを幅クラスに合わせます（積む・並べるは <see cref="AdaptiveSplit"/> が行う）。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        var size = widthClass == WidthClass.Compact ? 72 : widthClass >= WidthClass.Expanded ? 112 : 96;
        _gauge.Width = size;
        _gauge.Height = size;
        var tile = size * 0.62;
        _gaugeTile.Width = tile;
        _gaugeTile.Height = tile;
        if (_gaugeTile.Child is FontIcon icon)
        {
            icon.FontSize = tile * 0.42;
        }
    }

    /// <summary>見出しを作ります。</summary>
    /// <returns>見出し。</returns>
    private static StackPanel Header()
    {
        var header = new StackPanel { Spacing = Space.S };
        header.Children.Add(Typography.Text(LocalizedText.Get("home.eyebrow"), TextStyles.Eyebrow));
        header.Children.Add(Typography.Text(LocalizedText.Get("home.title"), TextStyles.Display));
        var subtitle = Typography.Text(LocalizedText.Get("home.subtitle"), TextStyles.SecondaryBody);
        subtitle.MaxWidth = Metrics.ReadingMaxWidth;
        subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        header.Children.Add(subtitle);
        return header;
    }

    /// <summary>既定アダプターの操作盤（主役のカードの中身）を作ります。</summary>
    /// <remarks>責務: 左に計器とアダプターの名前・状態、右に主の操作を置き、Compact では積みます。</remarks>
    /// <returns>操作盤。</returns>
    private AdaptiveSplit AdapterConsole()
    {
        var identity = new Grid { ColumnSpacing = Space.XL, VerticalAlignment = VerticalAlignment.Center };
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identity.Children.Add(_gauge);
        var texts = new StackPanel { Spacing = Space.S, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(LocalizedText.Get("home.adapter.eyebrow"), TextStyles.Eyebrow));
        _adapterName.TextWrapping = TextWrapping.NoWrap;
        _adapterName.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(_adapterName);
        texts.Children.Add(_statusPill);
        Grid.SetColumn(texts, 1);
        identity.Children.Add(texts);

        var action = new StackPanel { Spacing = Space.S, VerticalAlignment = VerticalAlignment.Center };
        action.Children.Add(_primaryButton);
        action.Children.Add(_setupHint);
        return new AdaptiveSplit
        {
            Primary = identity,
            Secondary = action,
            SecondaryWidth = 240,
            CollapseBelow = WidthClass.Medium,
            MinHeight = 120,
        };
    }

    /// <summary>主の操作のボタンの中身を作ります。</summary>
    /// <returns>記号と文字の並び。</returns>
    private StackPanel PrimaryButtonContent()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(_primaryIcon);
        row.Children.Add(_primaryText);
        return row;
    }

    /// <summary>今後の機能の帯を作ります。</summary>
    /// <returns>帯。</returns>
    private static Border FutureRail()
    {
        var rail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M };
        rail.Children.Add(new FontIcon { Glyph = FluentSymbol.Gauge, FontSize = 16, Foreground = DesignTokens.Foreground(Tone.Accent) });
        rail.Children.Add(Typography.Text(LocalizedText.Get("home.future.caption"), TextStyles.SecondaryCaption));
        return SurfaceCard.Create(rail, padding: Space.L, variant: CardVariant.Quiet);
    }
}
