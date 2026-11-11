using System;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Settings.State;
using ProjectZD8ForWindows.AppRoot.Flow;
using ProjectZD8ForWindows.Platform.Windows.AppShell;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.Authentication.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows;

/// <summary>
/// アプリのメインウインドウです。サインイン前はログイン画面、サインイン後はアプリのシェルを出します（iOS 版の <c>WindowGroup</c> の中身）。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>モデル同士をつなぐアプリの流れです。</summary>
    private readonly WindowsApplicationFlow _flow;

    /// <summary>今表示しているシェルです。ログイン画面なら null。</summary>
    private WindowsAppShellView? _shell;

    /// <summary>今表示しているのがシェルかどうかです（未表示なら null）。</summary>
    private bool? _showsShell;

    /// <summary>適用中の言語タグです。</summary>
    private string? _appliedLanguage;

    /// <summary>適用中の外観です。</summary>
    private SettingsAppearance? _appliedAppearance;

    /// <summary>ハイコントラストの変化を知るための、このウインドウのテーマの設定です（イベントを受け続けるためにフィールドで持つ）。</summary>
    private Microsoft.UI.System.ThemeSettings? _themeSettings;

    /// <summary>アプリの流れを受け取ってウインドウを作ります。</summary>
    /// <param name="flow">アプリの流れ。</param>
    public MainWindow(WindowsApplicationFlow flow)
    {
        _flow = flow;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SetTitleBar(AppTitleBar);
        ResponsiveLayout.Observe(RootGrid);
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1280, 840));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 420;
        }

        ApplyEffectiveTheme();
        RootGrid.ActualThemeChanged += OnActualThemeChanged;
        _themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(AppWindow.Id);
        DesignTokens.IsHighContrast = _themeSettings.HighContrast;
        _themeSettings.Changed += OnThemeSettingsChanged;
        flow.Root.Authentication.PropertyChanged += OnAuthenticationChanged;
        flow.Root.AccountSettings.PropertyChanged += OnAccountSettingsChanged;
        flow.SettingsModelReplaced += OnSettingsModelReplaced;
        Activated += OnWindowActivated;
        UpdateContent();
    }

    /// <summary>
    /// ウインドウを前面に出します（2 つ目の起動が転送されてきたとき）。
    /// </summary>
    /// <remarks>責務: 最小化されていれば元に戻し、ウインドウを前面に出します。</remarks>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        AppWindow.Show();
        Activate();
    }

    /// <summary>認証の状態が変わったときの処理です。</summary>
    /// <remarks>責務: サインインの段階に合わせて、ログイン画面とシェルを切り替えます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnAuthenticationChanged(object? sender, PropertyChangedEventArgs e) => UpdateContent();

    /// <summary>アカウント設定が変わったときの処理です。</summary>
    /// <remarks>責務: 言語・外観の変更をシェルへ反映します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnAccountSettingsChanged(object? sender, PropertyChangedEventArgs e) => UpdateContent();

    /// <summary>設定画面の表示モデルが作り直されたときの処理です。</summary>
    /// <remarks>責務: ログアウト後の新しい表示モデルでシェルを作り直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnSettingsModelReplaced(object? sender, EventArgs e)
    {
        if (_showsShell == true)
        {
            ShowShell(_shell?.SelectedDestination ?? WindowsSidebarDestination.Home);
        }
    }

    /// <summary>ウインドウの前面・背面が変わったときの処理です。</summary>
    /// <remarks>責務: 前面に戻ったことを同期のきっかけとしてアプリの流れへ伝えます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _flow.WindowActivated();
        }
    }

    /// <summary>実際のテーマが変わったときの処理です。</summary>
    /// <remarks>責務: コードで組んだ画面の色を新しいテーマで作り直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyEffectiveTheme();
        if (_showsShell == true)
        {
            ShowShell(_shell?.SelectedDestination ?? WindowsSidebarDestination.Home);
        }
        else if (_showsShell == false)
        {
            ShowLogin();
        }
    }

    /// <summary>
    /// 認証の段階と設定に合わせて、表示する画面・言語・外観を決めます。
    /// </summary>
    /// <remarks>責務: サインイン済みならシェル（アカウントの言語・外観）、そうでなければログイン画面（OS の言語）を出します。</remarks>
    private void UpdateContent()
    {
        var signedIn = _flow.Root.Authentication.State.Phase == AuthenticationPhase.SignedIn;
        var settings = _flow.Root.AccountSettings.Settings;
        var language = signedIn ? settings.Language.LanguageTag() : null;
        var appearance = signedIn ? settings.Appearance : SettingsAppearance.System;
        var languageChanged = !string.Equals(language, _appliedLanguage, StringComparison.Ordinal);
        if (languageChanged)
        {
            _appliedLanguage = language;
            LocalizedText.SetLanguage(language);
        }

        if (appearance != _appliedAppearance)
        {
            _appliedAppearance = appearance;
            RootGrid.RequestedTheme = appearance switch
            {
                SettingsAppearance.Light => ElementTheme.Light,
                SettingsAppearance.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
            ApplyEffectiveTheme();
        }

        if (signedIn && (_showsShell != true || languageChanged))
        {
            ShowShell(_shell?.SelectedDestination ?? WindowsSidebarDestination.Home);
        }
        else if (!signedIn && (_showsShell != false || languageChanged))
        {
            ShowLogin();
        }
    }

    /// <summary>実際のテーマを、コードで組む画面の色とタイトルバーのボタンへ反映します。</summary>
    /// <remarks>責務: 外観の設定で決まったテーマを、デザインの約束とタイトルバーへ伝えます。</remarks>
    private void ApplyEffectiveTheme()
    {
        DesignTokens.EffectiveTheme = RootGrid.ActualTheme;
        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;

        // 外観をアプリで OS と逆にしたとき、キャプションボタンの記号が背景に溶けないよう色を明示する。
        var titleBar = AppWindow.TitleBar;
        var ink = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = ink;
        titleBar.ButtonHoverForegroundColor = ink;
        titleBar.ButtonPressedForegroundColor = ink;
        titleBar.ButtonInactiveForegroundColor = global::Windows.UI.Color.FromArgb(0x80, ink.R, ink.G, ink.B);
        titleBar.ButtonHoverBackgroundColor = global::Windows.UI.Color.FromArgb(0x18, ink.R, ink.G, ink.B);
        titleBar.ButtonPressedBackgroundColor = global::Windows.UI.Color.FromArgb(0x10, ink.R, ink.G, ink.B);
    }

    /// <summary>シェルを表示します。</summary>
    /// <remarks>責務: アプリのシェルを作り、指定の行き先を選んだ状態で表示します。</remarks>
    /// <param name="destination">最初に開く行き先。</param>
    private void ShowShell(WindowsSidebarDestination destination)
    {
        var activates = _showsShell != true;
        _shell = new WindowsAppShellView(_flow, destination, AppWindow);
        AppTitleBar.Visibility = Visibility.Collapsed;
        Grid.SetRow(ContentHost, 0);
        Grid.SetRowSpan(ContentHost, 2);
        ContentHost.Child = _shell;
        SetTitleBar(_shell.TitleBarElement);
        _showsShell = true;
        if (activates)
        {
            _flow.ActivateAuthenticatedScope();
        }
    }

    /// <summary>ログイン画面を表示します。</summary>
    /// <remarks>責務: 認証モデルを渡したログイン画面を表示します。</remarks>
    private void ShowLogin()
    {
        _shell = null;
        AppTitleBar.Visibility = Visibility.Visible;
        Grid.SetRow(ContentHost, 1);
        Grid.SetRowSpan(ContentHost, 1);
        ContentHost.Child = new WindowsLoginView(_flow.Root.Authentication, _flow.Root.IsLocalMode);
        SetTitleBar(AppTitleBar);
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).ClearRegionRects(NonClientRegionKind.Passthrough);
        _showsShell = false;
    }

    /// <summary>ハイコントラストの有無が変わったときの処理です。</summary>
    /// <remarks>責務: コードで組んだ画面の色を、UI スレッドで作り直します（テーマの変化と同じ扱い）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnThemeSettingsChanged(Microsoft.UI.System.ThemeSettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (DesignTokens.IsHighContrast != sender.HighContrast)
            {
                DesignTokens.IsHighContrast = sender.HighContrast;
                OnActualThemeChanged(RootGrid, args);
            }
        });
}
