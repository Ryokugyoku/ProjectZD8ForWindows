using System;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProjectZD8.Application.Authentication.Actions;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using Windows.Foundation;
using Windows.UI;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Authentication.Views;

/// <summary>
/// サインイン前のログイン画面です（iOS 版の <c>MacOSLoginView</c>）。
/// </summary>
/// <remarks>認証の状態を表示し、操作を <see cref="AuthenticationSessionModel"/> へ送ります。</remarks>
public sealed partial class WindowsLoginView : UserControl
{
    /// <summary>認証セッションのモデルです。</summary>
    private readonly AuthenticationSessionModel _model;

    /// <summary>免責事項のダイアログです。</summary>
    private readonly StateBoundDialog _disclaimer = new();

    /// <summary>Apple のサインインの設定がなく、ローカルモードで動いているなら true。</summary>
    private readonly bool _isLocalMode;

    /// <summary>認証モデルを受け取ってログイン画面を作ります。</summary>
    /// <param name="model">認証セッションのモデル。</param>
    /// <param name="isLocalMode">ローカルモード（Apple のサインインを設定していない）なら true。ボタンの文言と説明を変える。</param>
    public WindowsLoginView(AuthenticationSessionModel model, bool isLocalMode = false)
    {
        _model = model;
        _isLocalMode = isLocalMode;
        InitializeComponent();
        ApplyStaticTexts();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
        SizeChanged += (_, args) => ApplyLayout(args.NewSize);
    }

    /// <summary>表示されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視を始め、「表示された」ことを送ります（保存済みのセッションの確認が始まる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _model.PropertyChanged += OnModelChanged;
        Render();
        _model.Send(AuthenticationAction.Appeared);
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 認証の状態を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>状態に依存しない文言を入れます。</summary>
    /// <remarks>責務: ログイン画面の見出し・紹介・機能の一覧の文言を設定します。</remarks>
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
        foreach (var (glyph, key) in new[]
        {
            (FluentSymbol.Pulse, "auth.capability.telemetry"),
            (FluentSymbol.Document, "auth.capability.logs"),
            (FluentSymbol.Chart, "auth.capability.analysis"),
        })
        {
            Capabilities.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(13, 10, 13, 10),
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(17, 255, 255, 255)),
                Child = new GlyphLabel(glyph, LocalizedText.Get(key), "CaptionTextBlockStyle", new SolidColorBrush(Color.FromArgb(184, 255, 255, 255))),
            });
        }
    }

    /// <summary>
    /// 認証の段階に合わせて操作の部分を描き、免責事項のダイアログを出し入れします。
    /// </summary>
    /// <remarks>責務: 確認中・サインイン中・未サインイン（失敗の表示を含む）の表示と、免責事項の表示を状態に合わせます。</remarks>
    private void Render()
    {
        var state = _model.State;
        foreach (var child in PhaseArea.Children)
        {
            if (child is StackPanel { Children: [ProgressRing ring, ..] })
            {
                ring.IsActive = false;
            }
        }

        PhaseArea.Children.Clear();
        switch (state.Phase)
        {
            case AuthenticationPhase.CheckingSession:
                PhaseArea.Children.Add(Progress(LocalizedText.Get("auth.checking")));
                break;
            case AuthenticationPhase.SigningIn:
                PhaseArea.Children.Add(Progress(LocalizedText.Get("auth.signing_in")));
                break;
            case AuthenticationPhase.SignedOut:
                if (state.Failure is { } failure)
                {
                    PhaseArea.Children.Add(new GlyphLabel(
                        FluentSymbol.Warning,
                        LocalizedText.Get(FailureKey(failure)),
                        "CaptionTextBlockStyle",
                        new SolidColorBrush(Color.FromArgb(255, 255, 159, 46))));
                }

                PhaseArea.Children.Add(ContinueButton(state.Failure is null));
                if (_isLocalMode)
                {
                    PhaseArea.Children.Add(Typography.Text(LocalizedText.Get("windows.login.local_mode_notice"), "CaptionTextBlockStyle", new SolidColorBrush(Color.FromArgb(204, 255, 255, 255))));
                }
                break;
        }

        _disclaimer.Sync(
            state.IsDisclaimerPresented,
            "disclaimer",
            XamlRoot,
            MakeDisclaimerDialog,
            result => _model.Send(result == ContentDialogResult.Primary
                ? AuthenticationAction.DisclaimerAccepted
                : AuthenticationAction.DisclaimerDismissed));
    }

    /// <summary>進行中の表示を作ります。</summary>
    /// <remarks>責務: 回転する進行表示と文言を並べます。</remarks>
    /// <param name="text">文言。</param>
    /// <returns>表示の要素。</returns>
    private static StackPanel Progress(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, Foreground = new SolidColorBrush(Colors.White) });
        row.Children.Add(Typography.Text(text, "BodyTextBlockStyle", new SolidColorBrush(Color.FromArgb(204, 255, 255, 255))));
        return row;
    }

    /// <summary>「Apple アカウントで続ける」ボタンを作ります。</summary>
    /// <remarks>責務: 初回はログイン、失敗後は再試行の操作を送るボタンを作ります。</remarks>
    /// <param name="isFirstAttempt">失敗の表示がなければ true。</param>
    /// <returns>ボタン。</returns>
    private Button ContinueButton(bool isFirstAttempt)
    {
        var button = new Button
        {
            Content = Typography.Strong(LocalizedText.Get(_isLocalMode ? "windows.login.continue_local" : "auth.continue_with_apple"), "BodyStrongTextBlockStyle", new SolidColorBrush(Colors.Black)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 46,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
        };
        button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromArgb(255, 232, 240, 244));
        button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(255, 214, 226, 232));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-login-continue");
        button.Click += (_, _) => _model.Send(isFirstAttempt ? AuthenticationAction.LoginTapped : AuthenticationAction.RetryTapped);
        return button;
    }

    /// <summary>免責事項のダイアログを作ります（iOS 版の <c>MacOSLoginDisclaimerView</c>）。</summary>
    /// <remarks>責務: 運転のリスク・無保証・責任・安全の 4 項目と法的な注記を並べたダイアログを作ります。</remarks>
    /// <returns>ダイアログ。</returns>
    private ContentDialog MakeDisclaimerDialog()
    {
        var body = new StackPanel { Spacing = 16, MaxWidth = 620 };
        body.Children.Add(Typography.Text(LocalizedText.Get("auth.disclaimer.instruction"), "SecondaryBodyTextStyle"));
        body.Children.Add(Typography.Text(LocalizedText.Get("auth.disclaimer.introduction"), "BodyStrongTextBlockStyle"));
        foreach (var (glyph, title, text) in new[]
        {
            (FluentSymbol.Steering, "auth.disclaimer.risk.title", "auth.disclaimer.risk.body"),
            (FluentSymbol.Repair, "auth.disclaimer.no_warranty.title", "auth.disclaimer.no_warranty.body"),
            (FluentSymbol.Shield, "auth.disclaimer.liability.title", "auth.disclaimer.liability.body"),
            (FluentSymbol.Hand, "auth.disclaimer.safety.title", "auth.disclaimer.safety.body"),
        })
        {
            var row = new Grid { ColumnSpacing = 13 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(IconTile.Create(glyph, Tone.Accent, 36));
            var texts = new StackPanel { Spacing = 4 };
            texts.Children.Add(Typography.Text(LocalizedText.Get(title), "BodyStrongTextBlockStyle"));
            texts.Children.Add(Typography.Text(LocalizedText.Get(text), "SecondaryBodyTextStyle"));
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);
            body.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Child = row });
        }

        body.Children.Add(Typography.Text(LocalizedText.Get("auth.disclaimer.legal_note"), "SecondaryCaptionTextStyle"));
        return new ContentDialog
        {
            Title = LocalizedText.Get("auth.disclaimer.title"),
            Content = new ScrollViewer { Content = body, MaxHeight = 460 },
            PrimaryButtonText = LocalizedText.Get("auth.disclaimer.accept"),
            CloseButtonText = LocalizedText.Get("auth.disclaimer.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ElementTheme.Dark,
        };
    }

    /// <summary>
    /// 画面の大きさに合わせて、横並びと縦積みを切り替えます（iOS 版の <c>MacOSLoginMetrics</c>）。
    /// </summary>
    /// <remarks>責務: 幅 820 未満か高さ 560 未満なら縦積みにし、回転計の大きさを合わせます。</remarks>
    /// <param name="size">画面の大きさ。</param>
    private void ApplyLayout(Size size)
    {
        var scale = Math.Clamp(Math.Min(size.Width / 1200, size.Height / 800), 0.82, 1.35);
        var stacked = size.Width < 820 || size.Height < 560;
        Layout.Padding = new Thickness(Math.Max(18, 34 * scale));
        Grid.SetColumn(AuthPanel, stacked ? 0 : 1);
        Grid.SetRow(AuthPanel, stacked ? 1 : 0);
        PanelColumn.Width = stacked ? new GridLength(0) : GridLength.Auto;
        AuthPanel.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        AuthPanel.Width = stacked ? Math.Min(Math.Max(size.Width * 0.84, 340), 520) : Math.Min(Math.Max(size.Width * 0.36, 360), 460);
        IntroArea.MinHeight = stacked ? 0 : Math.Max(0, size.Height - Layout.Padding.Top * 2);
        var heroSize = (stacked ? 150 : 250) * scale;
        Hero.Width = heroSize;
        Hero.Height = heroSize;
        HeroLetter.FontSize = heroSize * 0.30;
        HeroPulse.FontSize = heroSize * 0.23;
        HeroDot.Margin = new Thickness(heroSize * 0.38, 0, 0, heroSize * 0.06);
        Title.FontSize = 38 * scale;
        Subtitle.FontSize = 15 * scale;
        var narrow = stacked && size.Width < 560;
        Grid.SetColumn(IntroTexts, narrow ? 0 : 1);
        Grid.SetRow(IntroTexts, narrow ? 1 : 0);
        HeroArc.Data = ArcGeometry(heroSize);
    }

    /// <summary>回転計の弧の形を作ります。</summary>
    /// <remarks>責務: 円周の 8%〜72% を 135° 回した位置に置いた弧を作ります（iOS 版の <c>trim</c> と <c>rotationEffect</c>）。</remarks>
    /// <param name="diameter">直径。</param>
    /// <returns>弧の形。</returns>
    private static PathGeometry ArcGeometry(double diameter)
    {
        var radius = diameter / 2 - 8;
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
