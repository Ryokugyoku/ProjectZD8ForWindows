using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using ProjectZD8.Application.Authentication.Actions;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using Windows.Foundation;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Authentication.Views;

/// <summary>
/// サインイン前のログイン画面です（iOS 版の <c>MacOSLoginView</c>）。
/// </summary>
/// <remarks>
/// 認証の状態を表示し、操作を <see cref="AuthenticationSessionModel"/> へ送ります。常に暗い外観です。
/// 操作の要素は 1 回だけ組み、状態が変わったら見せる・隠す・文字を差し替えるだけにします（押している最中のボタンが消えない）。
/// 配置は幅クラスで決めます：Expanded 以上は左右、Medium は高さに余裕があれば左右、そうでなければ積みます
/// （Compact は常に積む。高さが足りないときはサインインの操作を先に出す）。
/// </remarks>
public sealed partial class WindowsLoginView : UserControl
{
    /// <summary>左右に並べるのに必要な高さです（これより低いと積む）。</summary>
    private const double SideBySideMinHeight = 560;

    /// <summary>認証セッションのモデルです。</summary>
    private readonly AuthenticationSessionModel _model;

    /// <summary>免責事項のダイアログです。</summary>
    private readonly StateBoundDialog _disclaimer = new();

    /// <summary>Apple のサインインの設定がなく、ローカルモードで動いているなら true。</summary>
    private readonly bool _isLocalMode;

    /// <summary>確認中・サインイン中の進行表示の行です。</summary>
    private readonly StackPanel _progressRow = new() { Orientation = Orientation.Horizontal, Spacing = Space.M };

    /// <summary>進行表示の回転です。</summary>
    private readonly ProgressRing _progressRing = new() { Width = Space.XL, Height = Space.XL };

    /// <summary>進行表示の文言です。</summary>
    private readonly TextBlock _progressText = Typography.Text(string.Empty, TextStyles.SecondaryBody);

    /// <summary>失敗の表示の行です。</summary>
    private readonly StackPanel _failureRow = new() { Orientation = Orientation.Horizontal, Spacing = Space.S };

    /// <summary>失敗の文言です。</summary>
    private readonly TextBlock _failureText = Typography.Text(string.Empty, TextStyles.Caption);

    /// <summary>押せない「Apple アカウントで続ける」ボタン（近日対応）です。</summary>
    private readonly Button _appleComingSoon;

    /// <summary>「Apple アカウントで続ける」（ローカルモードでは「この PC で続ける」）ボタンです。</summary>
    private readonly Button _continue;

    /// <summary>ローカルモードの説明です。</summary>
    private readonly TextBlock _localNotice = Typography.Text(LocalizedText.Get("windows.login.local_mode_notice"), TextStyles.SecondaryCaption);

    /// <summary>今の幅クラスです。</summary>
    private WidthClass _widthClass = ResponsiveLayout.Current;

    /// <summary>高さに余裕がある（左右に並べられる）かどうかです。</summary>
    private bool _isTall = true;

    /// <summary>回転計の今の直径です（変わったときだけ弧を引き直す）。</summary>
    private double _heroSize;

    /// <summary>入場の動きを再生したかどうかです。</summary>
    private bool _hasPlayedEntrance;

    /// <summary>認証モデルを受け取ってログイン画面を作ります。</summary>
    /// <param name="model">認証セッションのモデル。</param>
    /// <param name="isLocalMode">ローカルモード（Apple のサインインを設定していない）なら true。ボタンの文言と説明を変える。</param>
    public WindowsLoginView(AuthenticationSessionModel model, bool isLocalMode = false)
    {
        _model = model;
        _isLocalMode = isLocalMode;
        InitializeComponent();
        _appleComingSoon = AppleComingSoonButton();
        _continue = ContinueButton();
        BuildPhaseArea();
        ApplyStaticTexts();
        BuildCapabilities();
        ResponsiveLayout.Bind(this, widthClass =>
        {
            _widthClass = widthClass;
            ApplyLayout();
        });
        // 高さは「左右に並べられるか」の境目をまたいだときだけ見る（木は作り直さない）。
        LayoutRoot.SizeChanged += (_, args) =>
        {
            var tall = args.NewSize.Height >= SideBySideMinHeight;
            if (tall != _isTall)
            {
                _isTall = tall;
                ApplyLayout();
            }
        };
        Loaded += OnLoaded;
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>表示されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視を始め、入場の動きを再生し、「表示された」ことを送ります（保存済みのセッションの確認が始まる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _model.PropertyChanged -= OnModelChanged;
        _model.PropertyChanged += OnModelChanged;
        Render();
        PlayEntrance();
        _model.Send(AuthenticationAction.Appeared);
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 認証の状態を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>状態に依存しない文言を入れます。</summary>
    /// <remarks>責務: ログイン画面の見出し・紹介・パネルの文言を設定します。</remarks>
    private void ApplyStaticTexts()
    {
        Eyebrow.Text = LocalizedText.Get("auth.eyebrow");
        Title.Text = LocalizedText.Get("auth.title");
        Subtitle.Text = LocalizedText.Get("auth.subtitle");
        PanelEyebrow.Text = LocalizedText.Get("auth.panel.eyebrow");
        PanelTitle.Text = LocalizedText.Get("auth.panel.title");
        PanelSubtitle.Text = LocalizedText.Get("auth.panel.subtitle");
        TrustText.Text = LocalizedText.Get("auth.trust.apple_only");
        ConsentHint.Text = LocalizedText.Get("auth.consent_hint");
        if (_isLocalMode)
        {
            // ローカルモードでは Apple のサインインを使わないので、「Apple アカウントのみ」の説明を出さない。
            PanelSubtitle.Visibility = Visibility.Collapsed;
            TrustRow.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>機能の紹介のチップを組みます（1 回だけ）。</summary>
    private void BuildCapabilities()
    {
        var grid = new AdaptiveGrid { MinItemWidth = Metrics.TileMinWidth, Spacing = Space.M };
        grid.SetMaxColumns(3, 3, 3, 3);
        foreach (var (glyph, key) in new[]
        {
            (FluentSymbol.Pulse, "auth.capability.telemetry"),
            (FluentSymbol.Document, "auth.capability.logs"),
            (FluentSymbol.Chart, "auth.capability.analysis"),
        })
        {
            var icon = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Dark("RevTorqueAccentBrush"), VerticalAlignment = VerticalAlignment.Center };
            var label = Typography.Text(LocalizedText.Get(key), TextStyles.Caption);
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
            row.Children.Add(icon);
            row.Children.Add(label);
            grid.Children.Add(new Border
            {
                Style = DesignTokens.Style("QuietSurfaceStyle"),
                Padding = new Thickness(Space.M, Space.S, Space.M, Space.S),
                Child = row,
            });
        }

        CapabilitiesHost.Children.Add(grid);
    }

    /// <summary>サインインの操作の要素を組みます（1 回だけ）。</summary>
    /// <remarks>責務: 進行表示・失敗の表示・押せない Apple ボタン・続けるボタン・ローカルモードの説明を、<c>PhaseArea</c> に並べます。表示の出し分けは <see cref="Render"/> が行う。</remarks>
    private void BuildPhaseArea()
    {
        _progressRow.Children.Add(_progressRing);
        _progressRow.Children.Add(_progressText);
        _progressText.VerticalAlignment = VerticalAlignment.Center;

        _failureRow.Children.Add(new FontIcon
        {
            Glyph = FluentSymbol.Warning,
            FontSize = 14,
            Foreground = Dark("RevTorqueCautionBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Space.XXS, 0, 0),
        });
        _failureRow.Children.Add(_failureText);
        _failureText.Foreground = Dark("RevTorqueCautionBrush");

        PhaseArea.Children.Add(_progressRow);
        PhaseArea.Children.Add(_failureRow);
        PhaseArea.Children.Add(_appleComingSoon);
        PhaseArea.Children.Add(_continue);
        PhaseArea.Children.Add(_localNotice);
        foreach (UIElement child in PhaseArea.Children)
        {
            child.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 認証の段階に合わせて操作の部分を整え、免責事項のダイアログを出し入れします。
    /// </summary>
    /// <remarks>責務: 確認中・サインイン中・未サインイン（失敗の表示を含む）の表示と、免責事項の表示を状態に合わせます。要素は作り直しません。</remarks>
    private void Render()
    {
        var state = _model.State;
        var isBusy = state.Phase is AuthenticationPhase.CheckingSession or AuthenticationPhase.SigningIn;
        var isSignedOut = state.Phase == AuthenticationPhase.SignedOut;
        Retained.SetText(_progressText, LocalizedText.Get(state.Phase == AuthenticationPhase.SigningIn ? "auth.signing_in" : "auth.checking"));
        if (_progressRing.IsActive != isBusy)
        {
            _progressRing.IsActive = isBusy;
        }

        Retained.SetVisible(_progressRow, isBusy);
        if (isSignedOut && state.Failure is { } failure)
        {
            Retained.SetText(_failureText, LocalizedText.Get(FailureKey(failure)));
        }

        Retained.SetVisible(_failureRow, isSignedOut && state.Failure is not null);
        Retained.SetVisible(_appleComingSoon, isSignedOut && _isLocalMode);
        Retained.SetVisible(_continue, isSignedOut);
        Retained.SetVisible(_localNotice, isSignedOut && _isLocalMode);

        _disclaimer.Sync(
            state.IsDisclaimerPresented,
            "disclaimer",
            XamlRoot,
            MakeDisclaimerDialog,
            result => _model.Send(result == ContentDialogResult.Primary
                ? AuthenticationAction.DisclaimerAccepted
                : AuthenticationAction.DisclaimerDismissed));
    }

    /// <summary>押せない「Apple アカウントで続ける」ボタン（近日対応）を作ります。</summary>
    /// <remarks>
    /// 責務: Sign in with Apple が Coming Soon の間、入口があることだけを示し、押せないボタンと「近日対応」の印を作ります。
    /// 機能フラグ（AppleSignIn:Enabled）を有効にすると、このボタンは出ず、ふつうの「Apple アカウントで続ける」になります。
    /// </remarks>
    /// <returns>押せないボタン。</returns>
    private static Button AppleComingSoonButton()
    {
        var content = new Grid { ColumnSpacing = Space.M };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Typography.Strong(LocalizedText.Get("auth.continue_with_apple"), TextStyles.BodyStrong);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(label);
        var badge = new Border
        {
            Padding = new Thickness(Space.S, Space.XXS, Space.S, Space.XXS),
            CornerRadius = new CornerRadius(Radius.Pill),
            Background = Dark("RevTorqueAccentSubtleBrush"),
            BorderBrush = Dark("RevTorqueAccentStrokeBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Typography.Text(LocalizedText.Get("windows.login.apple_coming_soon"), TextStyles.Caption, Dark("RevTorqueAccentBrush"), wraps: false),
        };
        Grid.SetColumn(badge, 1);
        content.Children.Add(badge);
        var button = new Button
        {
            Content = content,
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = Metrics.PrimaryControlHeight,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-login-apple-coming-soon");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            button,
            LocalizedText.Get("auth.continue_with_apple") + " — " + LocalizedText.Get("windows.login.apple_coming_soon"));
        return button;
    }

    /// <summary>「Apple アカウントで続ける」（ローカルモードでは「この PC で続ける」）ボタンを作ります。</summary>
    /// <remarks>責務: 初回はログイン、失敗後は再試行の操作を送るボタンを作ります（どちらかは押した時点の状態で決める）。主の操作はこのボタンだけです。</remarks>
    /// <returns>ボタン。</returns>
    private Button ContinueButton()
    {
        var button = new Button
        {
            Content = Typography.Strong(LocalizedText.Get(_isLocalMode ? "windows.login.continue_local" : "auth.continue_with_apple"), TextStyles.BodyStrong),
            Style = DesignTokens.Style("AccentButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = Metrics.PrimaryControlHeight,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-login-continue");
        button.Click += (_, _) => _model.Send(_model.State.Failure is null ? AuthenticationAction.LoginTapped : AuthenticationAction.RetryTapped);
        return button;
    }

    /// <summary>免責事項のダイアログを作ります（iOS 版の <c>MacOSLoginDisclaimerView</c>）。</summary>
    /// <remarks>責務: 運転のリスク・無保証・責任・安全の 4 項目と法的な注記を並べたダイアログを作ります。</remarks>
    /// <returns>ダイアログ。</returns>
    private ContentDialog MakeDisclaimerDialog()
    {
        var body = new StackPanel { Spacing = Space.L, MaxWidth = 620 };
        body.Children.Add(Typography.Text(LocalizedText.Get("auth.disclaimer.instruction"), TextStyles.SecondaryBody));
        body.Children.Add(Typography.Text(LocalizedText.Get("auth.disclaimer.introduction"), TextStyles.BodyStrong));
        foreach (var (glyph, title, text) in new[]
        {
            (FluentSymbol.Steering, "auth.disclaimer.risk.title", "auth.disclaimer.risk.body"),
            (FluentSymbol.Repair, "auth.disclaimer.no_warranty.title", "auth.disclaimer.no_warranty.body"),
            (FluentSymbol.Shield, "auth.disclaimer.liability.title", "auth.disclaimer.liability.body"),
            (FluentSymbol.Hand, "auth.disclaimer.safety.title", "auth.disclaimer.safety.body"),
        })
        {
            var row = new Grid { ColumnSpacing = Space.M };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(IconTile.Create(glyph, Tone.Accent, Metrics.IconTileSmall));
            var texts = new StackPanel { Spacing = Space.XS };
            texts.Children.Add(Typography.Text(LocalizedText.Get(title), TextStyles.BodyStrong));
            texts.Children.Add(Typography.Text(LocalizedText.Get(text), TextStyles.SecondaryBody));
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            body.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Child = row });
        }

        body.Children.Add(Typography.Text(LocalizedText.Get("auth.disclaimer.legal_note"), TextStyles.SecondaryCaption));
        return new ContentDialog
        {
            Title = LocalizedText.Get("auth.disclaimer.title"),
            Content = new ScrollViewer { Content = body, MaxHeight = 460 },
            PrimaryButtonText = LocalizedText.Get(_isLocalMode ? "windows.login.disclaimer.accept_local" : "auth.disclaimer.accept"),
            CloseButtonText = LocalizedText.Get("auth.disclaimer.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ElementTheme.Dark,
        };
    }

    /// <summary>
    /// 幅クラスと高さに合わせて配置を切り替えます。
    /// </summary>
    /// <remarks>
    /// 責務: Expanded 以上は左右、Medium は高さ 560 以上なら左右、それ以外は積みます。積むときは、高さが足りなければサインインの操作を先に出し、
    /// 回転計と紹介を省いて最小の 640×420 でも続けるボタンが見えるようにします。木は作り直さず、行・列・余白・表示だけを差し替えます。
    /// </remarks>
    private void ApplyLayout()
    {
        var sideBySide = _widthClass >= WidthClass.Expanded || (_widthClass == WidthClass.Medium && _isTall);
        var panelFirst = !sideBySide && !_isTall;
        var showsHero = sideBySide || _isTall;

        Layout.Padding = new Thickness(Space.PageGutter(_widthClass));
        Layout.ColumnSpacing = sideBySide ? Space.XXXL + Space.XXL : 0;
        Layout.RowSpacing = sideBySide ? 0 : Space.XL;
        PanelColumn.Width = sideBySide ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(PanelHost, sideBySide ? 1 : 0);
        Grid.SetRow(PanelHost, sideBySide || panelFirst ? 0 : 1);
        Grid.SetRow(IntroArea, panelFirst ? 1 : 0);
        AuthPanel.Width = sideBySide ? 420 : double.NaN;
        AuthPanel.Padding = new Thickness(_widthClass == WidthClass.Compact ? Space.XL : Space.XXL + Space.XS);

        // 積むときは回転計を小さくして題の横に置く。左右のときは回転計の下に題を置く。
        Retained.SetVisible(HeroRow, showsHero);
        Retained.SetVisible(Subtitle, showsHero);
        Retained.SetVisible(CapabilitiesHost, sideBySide);
        Retained.SetStyle(Title, sideBySide ? TextStyles.Display : TextStyles.PageTitle);
        Grid.SetColumn(IntroTexts, sideBySide ? 0 : 1);
        Grid.SetRow(IntroTexts, sideBySide ? 1 : 0);
        Grid.SetColumnSpan(IntroTexts, sideBySide ? 2 : 1);
        Grid.SetColumnSpan(Hero, sideBySide ? 2 : 1);
        HeroRow.ColumnSpacing = sideBySide ? 0 : Space.XL;
        SetHeroSize(sideBySide ? _widthClass switch
        {
            WidthClass.Wide => 232,
            WidthClass.Expanded => 200,
            _ => 160,
        } : 96);
    }

    /// <summary>回転計の大きさを合わせます。</summary>
    /// <remarks>責務: 直径に比例して、内周・中央のタイル・文字の大きさと弧を合わせます（直径が変わったときだけ）。</remarks>
    /// <param name="diameter">直径。</param>
    private void SetHeroSize(double diameter)
    {
        if (Math.Abs(_heroSize - diameter) < 0.5)
        {
            return;
        }

        _heroSize = diameter;
        Hero.Width = diameter;
        Hero.Height = diameter;
        HeroInner.Margin = new Thickness(diameter * 0.14);
        var tile = diameter * 0.46;
        HeroTile.Width = tile;
        HeroTile.Height = tile;
        HeroLetter.FontSize = tile * 0.42;
        HeroPulse.FontSize = tile * 0.32;
        HeroDot.Margin = new Thickness(0, tile * 0.12, tile * 0.12, 0);
        HeroArc.Data = ArcGeometry(diameter);
    }

    /// <summary>入場の動きを 1 回だけ再生します。</summary>
    /// <remarks>責務: 回転計・紹介・サインインのパネルを、少しずつ遅らせて下から浮かび上がらせます（Composition。OS のアニメーション効果が切れていれば何もしない）。</remarks>
    private void PlayEntrance()
    {
        if (_hasPlayedEntrance || !Motion.IsEnabled)
        {
            return;
        }

        _hasPlayedEntrance = true;
        Rise(BrandRow, 0);
        Rise(HeroRow, 60);
        Rise(CapabilitiesHost, 120);
        Rise(AuthPanel, 90);
    }

    /// <summary>要素を下から浮かび上がらせます。</summary>
    /// <param name="element">要素。</param>
    /// <param name="delayMilliseconds">始めるまでの遅れ。</param>
    private static void Rise(UIElement element, int delayMilliseconds)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
        var duration = Motion.Slow;
        var delay = TimeSpan.FromMilliseconds(delayMilliseconds);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = duration;
        fade.DelayTime = delay;
        fade.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;

        var rise = compositor.CreateScalarKeyFrameAnimation();
        rise.InsertKeyFrame(0, (float)Space.XXL);
        rise.InsertKeyFrame(1, 0, easing);
        rise.Duration = duration;
        rise.DelayTime = delay;
        rise.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation.Y", rise);
    }

    /// <summary>回転計の弧の形を作ります。</summary>
    /// <remarks>責務: 円周の 8%〜72% を 135° 回した位置に置いた弧を作ります（iOS 版の <c>trim</c> と <c>rotationEffect</c>）。</remarks>
    /// <param name="diameter">直径。</param>
    /// <returns>弧の形。</returns>
    private static PathGeometry ArcGeometry(double diameter)
    {
        var radius = diameter / 2 - 6;
        var center = diameter / 2;
        var start = (135 + 0.08 * 360) * Math.PI / 180;
        var end = (135 + 0.72 * 360) * Math.PI / 180;
        var figure = new PathFigure { StartPoint = new Point(center + radius * Math.Cos(start), center + radius * Math.Sin(start)) };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(center + radius * Math.Cos(end), center + radius * Math.Sin(end)),
            Size = new Size(radius, radius),
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>暗い外観のブラシを返します。</summary>
    /// <remarks>責務: ログインは常に暗い外観なので、アプリ全体のテーマに関わらず、自前のテーマ辞書の Dark（ハイコントラストなら HighContrast）から引きます。</remarks>
    /// <param name="key">ブラシのキー。</param>
    /// <returns>ブラシ。</returns>
    private static Brush Dark(string key)
    {
        var theme = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries
            .First(dictionary => dictionary.Source?.OriginalString.EndsWith("RevTorqueTheme.xaml", StringComparison.Ordinal) == true);
        var dictionary = (ResourceDictionary)theme.ThemeDictionaries[DesignTokens.IsHighContrast ? "HighContrast" : "Dark"];
        return (Brush)dictionary[key];
    }

    /// <summary>失敗の種類の文言のキーを返します。</summary>
    /// <remarks>責務: 認証の失敗の種類を xcstrings のキーへ変換します。</remarks>
    /// <param name="failure">失敗の種類。</param>
    /// <returns>キー。</returns>
    private static string FailureKey(AuthenticationFailure failure) => failure switch
    {
        AuthenticationFailure.RemoteLogoutPersistenceFailed => "auth.error.remote_logout_persistence",
        AuthenticationFailure.SessionCheckFailed => "auth.error.session_check",
        AuthenticationFailure.ServiceUnavailable => "auth.error.unavailable",
        _ => "auth.error.sign_in",
    };
}
