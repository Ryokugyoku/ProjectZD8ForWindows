using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using ProjectZD8.Application.Authentication.Actions;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Settings.Actions;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.Authentication.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Views;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Views;

/// <summary>
/// 設定画面です。言語・外観・アダプター・自動アップロード・アカウント削除を扱います（iOS 版の <c>MacOSSettingsView</c>）。
/// </summary>
/// <remarks>
/// Windows 11 の「設定」アプリと同じく、見出し（全般・接続・データと同期・アカウント）ごとに、
/// 左に記号・題・説明、右に操作を置いた 1 行のカードを縦に並べます。
/// </remarks>
public sealed partial class WindowsSettingsView : UserControl
{
    /// <summary>アダプター選択の表示モデルです。</summary>
    private readonly WindowsSettingsPresentationModel _settings;

    /// <summary>アカウント設定のモデルです。</summary>
    private readonly AccountSettingsModel _accountSettings;

    /// <summary>認証のモデルです（アカウント削除）。</summary>
    private readonly AuthenticationSessionModel _authentication;

    /// <summary>アダプターの選択ダイアログです。</summary>
    private readonly WindowsAdapterSelectionDialog _adapterSelection;

    /// <summary>アカウント削除のダイアログです。</summary>
    private readonly WindowsAccountDeletionFlow _accountDeletion;

    /// <summary>Windows 版だけの iCloud サインインの表示モデルです。</summary>
    private readonly WindowsCloudSyncModel _cloudSync;

    /// <summary>診断（詳細ログ・不具合レポート・診断ログの削除）の表示モデルです。</summary>
    private readonly WindowsDiagnosticsModel _diagnostics;

    /// <summary>診断ログを消す前の確認ダイアログです。</summary>
    private readonly StateBoundDialog _clearLogsDialog = new();

    /// <summary>診断ログを消してよいか尋ねている最中かどうかです。</summary>
    private bool _isConfirmingClearLogs;

    /// <summary>診断の行のまとまりです（スイッチを押したあとなどに、ここだけ作り直す）。</summary>
    private StackPanel? _diagnosticsGroup;

    /// <summary>操作中の作り直しを待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>最後にカードを描いたときの入力です（同じなら描き直さない）。</summary>
    private object? _pageKey;

    /// <summary>最後に描いたアダプターのまとまりです。</summary>
    private FrameworkElement? _adapterCard;

    /// <summary>画面が処理したアダプターへの注意の通し番号です。</summary>
    private uint _handledAttentionSequence;

    /// <summary>モデルを受け取って設定画面を作ります。</summary>
    /// <param name="settings">アダプター選択の表示モデル。</param>
    /// <param name="accountSettings">アカウント設定のモデル。</param>
    /// <param name="authentication">認証のモデル。</param>
    /// <param name="cloudSync">iCloud サインインの表示モデル。</param>
    /// <param name="diagnostics">診断（詳細ログ・不具合レポート・診断ログの削除）の表示モデル。</param>
    public WindowsSettingsView(
        WindowsSettingsPresentationModel settings,
        AccountSettingsModel accountSettings,
        AuthenticationSessionModel authentication,
        WindowsCloudSyncModel cloudSync,
        WindowsDiagnosticsModel diagnostics)
    {
        _settings = settings;
        _accountSettings = accountSettings;
        _authentication = authentication;
        _cloudSync = cloudSync;
        _diagnostics = diagnostics;
        _adapterSelection = new WindowsAdapterSelectionDialog(settings.Send);
        _accountDeletion = new WindowsAccountDeletionFlow(authentication.Send);
        InitializeComponent();
        _gate = new RenderGate(this);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>表示されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視を始めて描きます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings.PropertyChanged += OnModelChanged;
        _accountSettings.PropertyChanged += OnModelChanged;
        _authentication.PropertyChanged += OnModelChanged;
        _cloudSync.PropertyChanged += OnModelChanged;
        _pageKey = null;
        Render();
    }

    /// <summary>外されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視をやめます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _settings.PropertyChanged -= OnModelChanged;
        _accountSettings.PropertyChanged -= OnModelChanged;
        _authentication.PropertyChanged -= OnModelChanged;
        _cloudSync.PropertyChanged -= OnModelChanged;
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>設定画面を描きます。</summary>
    /// <remarks>責務: 入力が変わったときだけ（押している最中でなければ）行を作り直し、ダイアログとアダプターへの注意の表示を状態に合わせます。</remarks>
    private void Render()
    {
        var state = _settings.State;
        var account = _accountSettings.Settings;
        var pageKey = (state.SelectedAdapters, account, _cloudSync.Availability, _cloudSync.IsSigningIn, _cloudSync.DidFail);
        if (!Equals(pageKey, _pageKey) && !_gate.TryDefer(Render))
        {
            _pageKey = pageKey;
            RenderPage(state, account);
        }

        _adapterSelection.Render(state, XamlRoot);
        var authentication = _authentication.State;
        _accountDeletion.Render(authentication.AccountDeletionPhase, authentication.AccountDeletionFailure, XamlRoot);
        _clearLogsDialog.Sync(_isConfirmingClearLogs, "clear-logs", XamlRoot, MakeClearLogsDialog, result =>
        {
            _isConfirmingClearLogs = false;
            if (result == ContentDialogResult.Primary)
            {
                _diagnostics.ClearLogs();
            }

            RefreshDiagnostics();
        });
        PresentAdapterAttentionIfNeeded(state);
    }

    /// <summary>設定の行を描き直します。</summary>
    /// <remarks>責務: 見出しと、全般（言語・外観）・接続（アダプター）・データと同期（アップロード・iCloud）・アカウント（削除）の行を並べます。</remarks>
    /// <param name="state">設定画面の状態。</param>
    /// <param name="account">アカウント設定。</param>
    private void RenderPage(WindowsSettingsState state, AccountSettings account)
    {
        _isRendering = true;
        Page.Children.Clear();
        var header = new ScreenHeader(
            FluentSymbol.Sliders,
            null,
            LocalizedText.Get("settings.title"),
            LocalizedText.Get("settings.subtitle"));
        header.Margin = new Thickness(0, 0, 0, 8);
        Page.Children.Add(header);

        Page.Children.Add(SettingsRow.Header(LocalizedText.Get("windows.settings.group.general")));
        Page.Children.Add(LanguageRow(account));
        Page.Children.Add(AppearanceRow(account));

        Page.Children.Add(SettingsRow.Header(LocalizedText.Get("windows.settings.group.connection")));
        var adapters = AdapterGroup(state);
        _adapterCard = adapters;
        Page.Children.Add(adapters);

        Page.Children.Add(SettingsRow.Header(LocalizedText.Get("windows.settings.group.data")));
        Page.Children.Add(UploadRow(account));
        Page.Children.Add(CloudSyncRow());

        _diagnosticsGroup = null;
        if (_diagnostics.IsAvailable)
        {
            Page.Children.Add(SettingsRow.Header(LocalizedText.Get("windows.settings.group.diagnostics")));
            _diagnosticsGroup = new StackPanel { Spacing = 4 };
            FillDiagnostics(_diagnosticsGroup);
            Page.Children.Add(_diagnosticsGroup);
        }

        Page.Children.Add(SettingsRow.Header(LocalizedText.Get("windows.settings.group.account")));
        Page.Children.Add(AccountDeletionRow());
        _isRendering = false;
    }

    /// <summary>診断の行だけを作り直します。</summary>
    /// <remarks>責務: スイッチやボタンを押した結果（期限・合計サイズ）を、押している最中でなければすぐ、そうでなければ操作が終わってから反映します。</remarks>
    private void RefreshDiagnostics()
    {
        if (_diagnosticsGroup is not { } group || _gate.TryDefer(RefreshDiagnostics))
        {
            return;
        }

        _isRendering = true;
        group.Children.Clear();
        FillDiagnostics(group);
        _isRendering = false;
    }

    /// <summary>診断の行（詳細ログ・不具合レポート・診断ログを消す）を並べます。</summary>
    /// <param name="group">行を入れるまとまり。</param>
    private void FillDiagnostics(StackPanel group)
    {
        group.Children.Add(VerboseLoggingRow());
        group.Children.Add(ExportReportRow());
        group.Children.Add(ClearLogsRow());
    }

    /// <summary>「詳細ログ」の行を作ります。</summary>
    /// <remarks>責務: スイッチで詳細ログを切り替えます。入れると 7 日で自動で切れます。Dev / Beta では最初から詳細なので、その旨を出してスイッチは無効にします。</remarks>
    /// <returns>行。</returns>
    private Border VerboseLoggingRow()
    {
        var byChannel = _diagnostics.IsVerboseByChannel;
        var toggle = new ToggleSwitch
        {
            IsOn = _diagnostics.VerboseLogging,
            IsEnabled = !byChannel,
            MinWidth = 0,
        };
        toggle.Toggled += (_, _) =>
        {
            if (_isRendering || byChannel)
            {
                return;
            }

            _diagnostics.SetVerboseLogging(toggle.IsOn);
            RefreshDiagnostics();
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "windows-settings-verbose-logging");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, LocalizedText.Get("windows.diagnostics.verbose.title"));
        var description = byChannel
            ? LocalizedText.Get("windows.diagnostics.verbose.always")
            : _diagnostics.VerboseExpiresAt is { } expiresAt
                ? LocalizedText.Format("windows.diagnostics.verbose.on", DateText.DateAndMinute(expiresAt))
                : LocalizedText.Get("windows.diagnostics.verbose.description");
        return SettingsRow.Create(
            FluentSymbol.Diagnostics,
            LocalizedText.Get("windows.diagnostics.verbose.title"),
            description,
            toggle);
    }

    /// <summary>「不具合レポートを書き出す」の行を作ります。</summary>
    /// <remarks>責務: 保存先を選んでもらって zip を作り、エクスプローラーで開きます。書き出している間はボタンを押せなくします。</remarks>
    /// <returns>行。</returns>
    private Border ExportReportRow()
    {
        var button = new Button { Content = LocalizedText.Get("windows.diagnostics.report.button") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-settings-export-report");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await WindowsDiagnosticsReportFlow.ExportAsync(_diagnostics, XamlRoot);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return SettingsRow.Create(
            FluentSymbol.Document,
            LocalizedText.Get("windows.diagnostics.report.title"),
            LocalizedText.Get("windows.diagnostics.report.description"),
            button);
    }

    /// <summary>「診断ログを消す」の行を作ります。</summary>
    /// <remarks>責務: 診断ログの合計サイズを説明に出し、確認のあとで今の実行のファイル以外を消します。</remarks>
    /// <returns>行。</returns>
    private Border ClearLogsRow()
    {
        var button = new Button { Content = LocalizedText.Get("windows.diagnostics.clear.button") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-settings-clear-logs");
        button.Click += (_, _) =>
        {
            _isConfirmingClearLogs = true;
            Render();
        };
        return SettingsRow.Create(
            FluentSymbol.Delete,
            LocalizedText.Get("windows.diagnostics.clear.title"),
            LocalizedText.Format("windows.diagnostics.clear.description", ByteCountText.Format(_diagnostics.TotalLogBytes())),
            button,
            Tone.Neutral);
    }

    /// <summary>診断ログを消す前の確認ダイアログを作ります。</summary>
    /// <returns>ダイアログ。</returns>
    private static ContentDialog MakeClearLogsDialog() => new()
    {
        Title = LocalizedText.Get("windows.diagnostics.clear.confirm.title"),
        Content = Typography.Text(LocalizedText.Get("windows.diagnostics.clear.confirm.message"), "BodyTextBlockStyle"),
        PrimaryButtonText = LocalizedText.Get("windows.diagnostics.clear.confirm.action"),
        PrimaryButtonStyle = DesignTokens.Style("DangerButtonStyle"),
        CloseButtonText = LocalizedText.Get("windows.diagnostics.cancel"),
        DefaultButton = ContentDialogButton.Close,
    };

    /// <summary>言語の行を作ります。</summary>
    /// <param name="account">アカウント設定。</param>
    /// <returns>行。</returns>
    private Border LanguageRow(AccountSettings account)
    {
        var choices = new ComboBox { MinWidth = 200 };
        foreach (var language in SettingsLanguageRawValue.AllCases)
        {
            choices.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(language.TitleKey()), Tag = language });
        }

        choices.SelectedIndex = IndexOf(SettingsLanguageRawValue.AllCases, account.Language);
        choices.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && choices.SelectedItem is ComboBoxItem { Tag: SettingsLanguage language })
            {
                _accountSettings.Send(new AccountSettingsAction.LanguageSelected(language));
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(choices, "windows-settings-language");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choices, LocalizedText.Get("settings.language.title"));
        return SettingsRow.Create(
            FluentSymbol.Language,
            LocalizedText.Get("settings.language.title"),
            LocalizedText.Get("settings.language.description"),
            choices);
    }

    /// <summary>外観の行を作ります。</summary>
    /// <param name="account">アカウント設定。</param>
    /// <returns>行。</returns>
    private Border AppearanceRow(AccountSettings account)
    {
        var choices = new ComboBox { MinWidth = 200 };
        foreach (var appearance in SettingsAppearanceRawValue.AllCases)
        {
            choices.Items.Add(new ComboBoxItem
            {
                Content = new GlyphLabel(appearance.Glyph(), LocalizedText.Get(appearance.TitleKey())),
                Tag = appearance,
            });
        }

        choices.SelectedIndex = IndexOf(SettingsAppearanceRawValue.AllCases, account.Appearance);
        choices.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && choices.SelectedItem is ComboBoxItem { Tag: SettingsAppearance appearance })
            {
                _accountSettings.Send(new AccountSettingsAction.AppearanceSelected(appearance));
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(choices, "windows-settings-appearance");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choices, LocalizedText.Get("settings.appearance.title"));
        return SettingsRow.Create(
            FluentSymbol.Sliders,
            LocalizedText.Get("settings.appearance.title"),
            LocalizedText.Get("settings.appearance.description"),
            choices);
    }

    /// <summary>アダプターの行のまとまりを作ります。</summary>
    /// <param name="state">設定画面の状態。</param>
    /// <returns>まとまり（ホームから来たときに揺らす対象）。</returns>
    private StackPanel AdapterGroup(WindowsSettingsState state)
    {
        var group = new StackPanel { Spacing = 4, RenderTransform = new TranslateTransform() };
        foreach (var role in AdapterConnectionRoleRawValue.AllCases)
        {
            group.Children.Add(AdapterRow(role, state));
        }

        var note = Typography.Text(LocalizedText.Get("settings.adapter.description"), "SecondaryCaptionTextStyle");
        note.Margin = new Thickness(4, 2, 0, 0);
        group.Children.Add(note);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(group, "windows-settings-adapter-card");
        return group;
    }

    /// <summary>アダプターの役割（主・副）の行を作ります。</summary>
    /// <param name="role">役割。</param>
    /// <param name="state">設定画面の状態。</param>
    /// <returns>行。</returns>
    private Border AdapterRow(AdapterConnectionRole role, WindowsSettingsState state)
    {
        var description = state.SelectedAdapters.TryGetValue(role, out var adapter)
            ? adapter.DisplayName
            : LocalizedText.Get("settings.adapter.not_selected");
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        trailing.Children.Add(StatusPill.Create(null, LocalizedText.Get(role.BadgeKey()), role == AdapterConnectionRole.Primary ? Tone.Accent : Tone.Neutral));
        var select = new Button { Content = LocalizedText.Get("settings.adapter.select"), MinWidth = 96 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(select, LocalizedText.Get(role.TitleKey()));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(select, $"windows-settings-adapter-{role.ToRawValue()}-select");
        select.Click += (_, _) => _settings.Send(new WindowsSettingsAction.AdapterSelectionRequested(role));
        trailing.Children.Add(select);
        return SettingsRow.Create(
            role.Glyph(),
            LocalizedText.Get(role.TitleKey()),
            description,
            trailing,
            role == AdapterConnectionRole.Primary ? Tone.Accent : Tone.Neutral);
    }

    /// <summary>自動アップロードの行を作ります。</summary>
    /// <param name="account">アカウント設定。</param>
    /// <returns>行。</returns>
    private Border UploadRow(AccountSettings account)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = account.AutomaticSessionUploadEnabled,
            MinWidth = 0,
        };
        toggle.Toggled += (_, _) =>
        {
            if (!_isRendering)
            {
                _accountSettings.Send(new AccountSettingsAction.AutomaticSessionUploadChanged(toggle.IsOn));
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "windows-settings-automatic-upload");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, LocalizedText.Get("settings.upload.automatic"));
        return SettingsRow.Create(
            FluentSymbol.Upload,
            LocalizedText.Get("settings.upload.automatic"),
            LocalizedText.Get("settings.upload.description"),
            toggle);
    }

    /// <summary>iCloud 同期の行を作ります。</summary>
    /// <remarks>責務: 状態（近日対応・サインアウト・サインイン済み・サインイン中・失敗）を説明に、サインイン・サインアウトのボタンを右に置きます。</remarks>
    /// <returns>行。</returns>
    private Border CloudSyncRow()
    {
        var availability = _cloudSync.Availability;
        var statusKey = availability switch
        {
            WindowsCloudSyncAvailability.Available => "windows.cloud.signed_in",
            WindowsCloudSyncAvailability.SignedOut => "windows.cloud.signed_out",
            _ => "windows.cloud.coming_soon",
        };
        var description = LocalizedText.Get(statusKey);
        if (_cloudSync.DidFail && !_cloudSync.IsSigningIn)
        {
            description += "\n" + LocalizedText.Get("windows.cloud.sign_in_failed");
        }

        UIElement? trailing = null;
        if (_cloudSync.IsSigningIn)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new ProgressRing { IsActive = true, Width = 18, Height = 18 });
            row.Children.Add(Typography.Text(LocalizedText.Get("windows.cloud.signing_in"), "SecondaryCaptionTextStyle"));
            trailing = row;
        }
        else if (availability == WindowsCloudSyncAvailability.NotConfigured)
        {
            // Coming Soon の間は、入口があることだけを示す押せないボタンを出す（機能フラグ AppleSignIn:Enabled で有効になる）。
            var comingSoon = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            comingSoon.Children.Add(StatusPill.Create(null, LocalizedText.Get("windows.login.apple_coming_soon"), Tone.Accent));
            comingSoon.Children.Add(new Button
            {
                Content = LocalizedText.Get("windows.cloud.sign_in"),
                IsEnabled = false,
            });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(comingSoon, "windows-settings-cloud-coming-soon");
            trailing = comingSoon;
        }
        else if (availability == WindowsCloudSyncAvailability.SignedOut)
        {
            var signIn = new Button
            {
                Content = LocalizedText.Get("windows.cloud.sign_in"),
                Style = DesignTokens.Style("AccentButtonStyle"),
            };
            signIn.Click += (_, _) => _ = _cloudSync.SignIn();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(signIn, "windows-settings-cloud-sign-in");
            trailing = signIn;
        }
        else if (availability == WindowsCloudSyncAvailability.Available)
        {
            var signOut = new Button { Content = LocalizedText.Get("windows.cloud.sign_out") };
            signOut.Click += (_, _) => _cloudSync.SignOut();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(signOut, "windows-settings-cloud-sign-out");
            trailing = signOut;
        }

        return SettingsRow.Create(
            FluentSymbol.Cloud,
            LocalizedText.Get("settings.windows_cloud.title"),
            description,
            trailing,
            availability == WindowsCloudSyncAvailability.Available ? Tone.Accent : Tone.Neutral);
    }

    /// <summary>アカウント削除の行を作ります。</summary>
    /// <returns>行。</returns>
    private Border AccountDeletionRow()
    {
        var button = new Button
        {
            Content = LocalizedText.Get("account.delete.start"),
            Style = DesignTokens.Style("DangerButtonStyle"),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-account-delete-start");
        button.Click += (_, _) => _authentication.Send(AuthenticationAction.AccountDeletionRequested);
        return SettingsRow.Create(
            FluentSymbol.Shield,
            LocalizedText.Get("account.delete.card.title"),
            LocalizedText.Get("account.delete.card.description"),
            button,
            Tone.Danger);
    }

    /// <summary>
    /// ホームの「アダプターを設定」から来たとき、アダプターのカードを見せて小さく揺らします。
    /// </summary>
    /// <remarks>責務: 未処理の注意要求を 1 回だけ処理し、カードを表示範囲へ入れます（視差効果を減らす設定なら揺らさない）。</remarks>
    /// <param name="state">設定画面の状態。</param>
    private void PresentAdapterAttentionIfNeeded(WindowsSettingsState state)
    {
        if (!state.HasPendingAdapterAttention || state.AdapterAttentionSequence == _handledAttentionSequence)
        {
            return;
        }

        var sequence = state.AdapterAttentionSequence;
        _handledAttentionSequence = sequence;
        DispatcherQueue.TryEnqueue(() =>
        {
            _settings.Send(new WindowsSettingsAction.AdapterAttentionConsumed(sequence));
            if (_adapterCard is not { } card)
            {
                return;
            }

            card.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
            if (new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled && card.RenderTransform is TranslateTransform transform)
            {
                Shake(transform);
            }
        });
    }

    /// <summary>カードを左右に小さく揺らします。</summary>
    /// <remarks>責務: 0.56 秒で減衰する左右の揺れを再生します（iOS 版の <c>AttentionShakeEffect</c>）。</remarks>
    /// <param name="transform">カードの移動。</param>
    private static void Shake(TranslateTransform transform)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.56) };
        double[] offsets = [0, 9, -9, 6, -6, 3, 0];
        for (var index = 0; index < offsets.Length; index++)
        {
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                Value = offsets[index],
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.56 * index / (offsets.Length - 1))),
            });
        }

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, "X");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    /// <summary>一覧の中の位置を返します。</summary>
    /// <remarks>責務: 選択肢の一覧から値の位置を探します。</remarks>
    /// <typeparam name="T">値の型。</typeparam>
    /// <param name="items">一覧。</param>
    /// <param name="value">値。</param>
    /// <returns>位置。なければ -1。</returns>
    private static int IndexOf<T>(System.Collections.Generic.IReadOnlyList<T> items, T value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (Equals(items[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
