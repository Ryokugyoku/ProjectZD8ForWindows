using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
/// Windows 11 の「設定」アプリと同じく、グループ（全般・接続・データと同期・診断・アカウント）ごとに、
/// 左に記号・題・説明、右に操作を置いた 1 行のカードを縦に並べます。
/// 行は最初に 1 回だけ組み、モデルが変わったら <see cref="UiUpdateThrottle"/> で束ねて値だけを差し替えます（押している最中の行が消えない）。
/// Expanded 以上ではグループの目次を左に出し、それより狭いときは 1 列にします。
/// </remarks>
public sealed partial class WindowsSettingsView : UserControl
{
    /// <summary>目次の幅です。</summary>
    private const double TableOfContentsWidth = 200;

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

    /// <summary>更新を 1 フレームに 1 回へ束ねる仕組みです。</summary>
    private readonly UiUpdateThrottle _throttle;

    /// <summary>値を差し替えている最中かどうかです（差し替えで起きるイベントを無視するため）。</summary>
    private bool _isUpdating;

    /// <summary>診断ログを消してよいか尋ねている最中かどうかです。</summary>
    private bool _isConfirmingClearLogs;

    /// <summary>画面が処理したアダプターへの注意の通し番号です。</summary>
    private uint _handledAttentionSequence;

    /// <summary>目次と内容を左右に置く格子です。</summary>
    private readonly Grid _layout = new();

    /// <summary>グループの目次です。</summary>
    private readonly StackPanel _tableOfContents = new() { Spacing = Space.XS, VerticalAlignment = VerticalAlignment.Top };

    /// <summary>グループを縦に積むパネルです。</summary>
    private readonly StackPanel _groups = new() { Spacing = Space.XXL };

    /// <summary>言語の選択肢です。</summary>
    private readonly ComboBox _language = new() { MinWidth = 200 };

    /// <summary>外観の選択肢です。</summary>
    private readonly ComboBox _appearance = new() { MinWidth = 200 };

    /// <summary>アダプターの行のまとまりです（ホームから来たときに揺らす対象）。</summary>
    private readonly StackPanel _adapterGroup = new() { Spacing = Space.S, RenderTransform = new TranslateTransform() };

    /// <summary>役割ごとのアダプターの行です。</summary>
    private readonly List<(AdapterConnectionRole Role, SettingsItem Item)> _adapterRows = [];

    /// <summary>自動アップロードのスイッチです。</summary>
    private readonly ToggleSwitch _upload = new() { MinWidth = 0 };

    /// <summary>iCloud の行です。</summary>
    private SettingsItem? _cloudRow;

    /// <summary>iCloud のサインイン中の表示です。</summary>
    private readonly StackPanel _cloudSigningIn = new() { Orientation = Orientation.Horizontal, Spacing = Space.M };

    /// <summary>サインイン中の進行表示です。</summary>
    private readonly ProgressRing _cloudRing = new() { Width = Space.XL - Space.XXS, Height = Space.XL - Space.XXS };

    /// <summary>iCloud の「近日対応」の印とボタンです。</summary>
    private readonly StackPanel _cloudComingSoon = new() { Orientation = Orientation.Horizontal, Spacing = Space.M };

    /// <summary>iCloud のサインインのボタンです。</summary>
    private readonly Button _cloudSignIn = new() { Content = LocalizedText.Get("windows.cloud.sign_in"), Style = DesignTokens.Style("AccentButtonStyle") };

    /// <summary>iCloud のサインアウトのボタンです。</summary>
    private readonly Button _cloudSignOut = new() { Content = LocalizedText.Get("windows.cloud.sign_out") };

    /// <summary>iCloud の操作を入れる枠です。</summary>
    private readonly StackPanel _cloudTrailing = new() { Orientation = Orientation.Horizontal, Spacing = Space.M };

    /// <summary>詳細ログのスイッチです。</summary>
    private readonly ToggleSwitch _verbose = new() { MinWidth = 0 };

    /// <summary>詳細ログの行です。</summary>
    private SettingsItem? _verboseRow;

    /// <summary>診断ログを消す行です。</summary>
    private SettingsItem? _clearRow;

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

        BuildPage();
        _throttle = new UiUpdateThrottle(this, Update);
        ResponsiveLayout.Bind(this, ApplyLayout);
        UpdateValues();
        UpdateDiagnostics();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>表示されたときの処理です。</summary>
    /// <remarks>責務: モデルの監視を始めて、値とダイアログを今の状態に合わせます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings.PropertyChanged += OnModelChanged;
        _accountSettings.PropertyChanged += OnModelChanged;
        _authentication.PropertyChanged += OnModelChanged;
        _cloudSync.PropertyChanged += OnModelChanged;
        UpdateDiagnostics();
        _throttle.FlushNow();
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
    /// <remarks>責務: 次のフレームでの更新を頼みます（何度来ても 1 回にまとまる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => _throttle.Request();

    /// <summary>画面を今の状態に合わせます。</summary>
    /// <remarks>責務: 行の値を差し替え、ダイアログとアダプターへの注意の表示を状態に合わせます。</remarks>
    private void Update()
    {
        UpdateValues();
        var state = _settings.State;
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

            UpdateDiagnostics();
        });
        PresentAdapterAttentionIfNeeded(state);
    }

    /// <summary>
    /// 幅クラスに合わせて配置を切り替えます。
    /// </summary>
    /// <remarks>責務: Expanded 以上では目次を左に出し、それより狭いときは隠して 1 列にします（木は作り直さない）。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        var showsContents = widthClass >= WidthClass.Expanded;
        _layout.ColumnDefinitions[0].Width = showsContents ? new GridLength(TableOfContentsWidth) : new GridLength(0);
        _layout.ColumnSpacing = showsContents ? Space.XXL : 0;
        Retained.SetVisible(_tableOfContents, showsContents);
    }

    /// <summary>ページ全体を組みます（1 回だけ）。</summary>
    /// <remarks>責務: 見出し・目次・グループ（全般・接続・データと同期・診断・アカウント）の行を組んで並べます。</remarks>
    private void BuildPage()
    {
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TableOfContentsWidth) });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_groups, 1);
        _layout.Children.Add(_tableOfContents);
        _layout.Children.Add(_groups);

        AddGroup(FluentSymbol.Sliders, "windows.settings.group.general", LanguageRow(), AppearanceRow());
        BuildAdapterGroup();
        AddGroup(FluentSymbol.Plug, "windows.settings.group.connection", _adapterGroup);
        AddGroup(FluentSymbol.Cloud, "windows.settings.group.data", UploadRow(), CloudSyncRow());
        if (_diagnostics.IsAvailable)
        {
            AddGroup(FluentSymbol.Diagnostics, "windows.settings.group.diagnostics", VerboseLoggingRow(), ExportReportRow(), ClearLogsRow());
        }

        AddGroup(FluentSymbol.Shield, "windows.settings.group.account", AccountDeletionRow());

        var page = new PageScaffold(1000);
        page.Body.Children.Add(new ScreenHeader(
            FluentSymbol.Sliders,
            null,
            LocalizedText.Get("settings.title"),
            LocalizedText.Get("settings.subtitle")));
        page.Body.Children.Add(_layout);
        Content = page;
    }

    /// <summary>グループを 1 つ足します。</summary>
    /// <remarks>責務: 見出しと行を縦に並べたまとまりを作り、目次に項目を足します。</remarks>
    /// <param name="glyph">目次の記号。</param>
    /// <param name="titleKey">グループの題のキー。</param>
    /// <param name="rows">行（要素または <see cref="SettingsItem"/>）。</param>
    private void AddGroup(string glyph, string titleKey, params object[] rows)
    {
        var title = LocalizedText.Get(titleKey);
        var group = new StackPanel { Spacing = Space.S };
        group.Children.Add(Typography.Text(title, TextStyles.SectionTitle));
        foreach (var row in rows)
        {
            group.Children.Add(row switch
            {
                SettingsItem item => item.Element,
                UIElement element => element,
                _ => throw new ArgumentException("行の型が違います。", nameof(rows)),
            });
        }

        _groups.Children.Add(group);

        var entry = new Button
        {
            Content = new GlyphLabel(glyph, title, TextStyles.Body),
            Style = DesignTokens.Style("SubtleButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(entry, title);
        entry.Click += (_, _) => group.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = Motion.IsEnabled });
        _tableOfContents.Children.Add(entry);
    }

    /// <summary>値を今のモデルに合わせます。</summary>
    /// <remarks>責務: 言語・外観・アダプター・アップロード・iCloud の行の値を、違うときだけ差し替えます。</remarks>
    private void UpdateValues()
    {
        var state = _settings.State;
        var account = _accountSettings.Settings;
        _isUpdating = true;
        try
        {
            SelectIndex(_language, IndexOf(SettingsLanguageRawValue.AllCases, account.Language));
            SelectIndex(_appearance, IndexOf(SettingsAppearanceRawValue.AllCases, account.Appearance));
            if (_upload.IsOn != account.AutomaticSessionUploadEnabled)
            {
                _upload.IsOn = account.AutomaticSessionUploadEnabled;
            }
        }
        finally
        {
            _isUpdating = false;
        }

        foreach (var (role, item) in _adapterRows)
        {
            item.SetDescription(state.SelectedAdapters.TryGetValue(role, out var adapter)
                ? adapter.DisplayName
                : LocalizedText.Get("settings.adapter.not_selected"));
        }

        UpdateCloudSync();
    }

    /// <summary>選択の位置を差し替えます。</summary>
    /// <param name="box">選択肢。</param>
    /// <param name="index">位置。</param>
    private static void SelectIndex(ComboBox box, int index)
    {
        if (box.SelectedIndex != index)
        {
            box.SelectedIndex = index;
        }
    }

    /// <summary>診断の行の値を今の状態に合わせます。</summary>
    /// <remarks>責務: 詳細ログのスイッチと期限、診断ログの合計サイズを差し替えます。スイッチやボタンを押したあと・表示したときに呼びます。</remarks>
    private void UpdateDiagnostics()
    {
        if (_verboseRow is not { } verboseRow || _clearRow is not { } clearRow)
        {
            return;
        }

        var byChannel = _diagnostics.IsVerboseByChannel;
        _isUpdating = true;
        try
        {
            if (_verbose.IsOn != _diagnostics.VerboseLogging)
            {
                _verbose.IsOn = _diagnostics.VerboseLogging;
            }
        }
        finally
        {
            _isUpdating = false;
        }

        Retained.SetEnabled(_verbose, !byChannel);
        verboseRow.SetDescription(byChannel
            ? LocalizedText.Get("windows.diagnostics.verbose.always")
            : _diagnostics.VerboseExpiresAt is { } expiresAt
                ? LocalizedText.Format("windows.diagnostics.verbose.on", DateText.DateAndMinute(expiresAt))
                : LocalizedText.Get("windows.diagnostics.verbose.description"));
        clearRow.SetDescription(LocalizedText.Format("windows.diagnostics.clear.description", ByteCountText.Format(_diagnostics.TotalLogBytes())));
    }

    /// <summary>「詳細ログ」の行を作ります。</summary>
    /// <remarks>責務: スイッチで詳細ログを切り替えます。入れると 7 日で自動で切れます。Dev / Beta では最初から詳細なので、その旨を出してスイッチは無効にします。</remarks>
    /// <returns>行。</returns>
    private SettingsItem VerboseLoggingRow()
    {
        _verbose.Toggled += (_, _) =>
        {
            if (_isUpdating || _diagnostics.IsVerboseByChannel)
            {
                return;
            }

            _diagnostics.SetVerboseLogging(_verbose.IsOn);
            UpdateDiagnostics();
        };
        AutomationProperties.SetAutomationId(_verbose, "windows-settings-verbose-logging");
        AutomationProperties.SetName(_verbose, LocalizedText.Get("windows.diagnostics.verbose.title"));
        _verboseRow = new SettingsItem(
            FluentSymbol.Diagnostics,
            LocalizedText.Get("windows.diagnostics.verbose.title"),
            LocalizedText.Get("windows.diagnostics.verbose.description"),
            _verbose);
        return _verboseRow;
    }

    /// <summary>「不具合レポートを書き出す」の行を作ります。</summary>
    /// <remarks>責務: 保存先を選んでもらって zip を作り、エクスプローラーで開きます。書き出している間はボタンを押せなくします。</remarks>
    /// <returns>行。</returns>
    private SettingsItem ExportReportRow()
    {
        var button = new Button { Content = LocalizedText.Get("windows.diagnostics.report.button") };
        AutomationProperties.SetAutomationId(button, "windows-settings-export-report");
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
        return new SettingsItem(
            FluentSymbol.Document,
            LocalizedText.Get("windows.diagnostics.report.title"),
            LocalizedText.Get("windows.diagnostics.report.description"),
            button);
    }

    /// <summary>「診断ログを消す」の行を作ります。</summary>
    /// <remarks>責務: 診断ログの合計サイズを説明に出し、確認のあとで今の実行のファイル以外を消します。</remarks>
    /// <returns>行。</returns>
    private SettingsItem ClearLogsRow()
    {
        var button = new Button { Content = LocalizedText.Get("windows.diagnostics.clear.button") };
        AutomationProperties.SetAutomationId(button, "windows-settings-clear-logs");
        button.Click += (_, _) =>
        {
            _isConfirmingClearLogs = true;
            _throttle.FlushNow();
        };
        _clearRow = new SettingsItem(
            FluentSymbol.Delete,
            LocalizedText.Get("windows.diagnostics.clear.title"),
            null,
            button,
            Tone.Neutral);
        return _clearRow;
    }

    /// <summary>診断ログを消す前の確認ダイアログを作ります。</summary>
    /// <returns>ダイアログ。</returns>
    private static ContentDialog MakeClearLogsDialog() => new()
    {
        Title = LocalizedText.Get("windows.diagnostics.clear.confirm.title"),
        Content = Typography.Text(LocalizedText.Get("windows.diagnostics.clear.confirm.message"), TextStyles.Body),
        PrimaryButtonText = LocalizedText.Get("windows.diagnostics.clear.confirm.action"),
        PrimaryButtonStyle = DesignTokens.Style("DangerButtonStyle"),
        CloseButtonText = LocalizedText.Get("windows.diagnostics.cancel"),
        DefaultButton = ContentDialogButton.Close,
    };

    /// <summary>言語の行を作ります。</summary>
    /// <returns>行。</returns>
    private SettingsItem LanguageRow()
    {
        foreach (var language in SettingsLanguageRawValue.AllCases)
        {
            _language.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(language.TitleKey()), Tag = language });
        }

        _language.SelectionChanged += (_, _) =>
        {
            if (!_isUpdating && _language.SelectedItem is ComboBoxItem { Tag: SettingsLanguage language })
            {
                _accountSettings.Send(new AccountSettingsAction.LanguageSelected(language));
            }
        };
        AutomationProperties.SetAutomationId(_language, "windows-settings-language");
        AutomationProperties.SetName(_language, LocalizedText.Get("settings.language.title"));
        return new SettingsItem(
            FluentSymbol.Language,
            LocalizedText.Get("settings.language.title"),
            LocalizedText.Get("settings.language.description"),
            _language);
    }

    /// <summary>外観の行を作ります。</summary>
    /// <returns>行。</returns>
    private SettingsItem AppearanceRow()
    {
        foreach (var appearance in SettingsAppearanceRawValue.AllCases)
        {
            _appearance.Items.Add(new ComboBoxItem
            {
                Content = new GlyphLabel(appearance.Glyph(), LocalizedText.Get(appearance.TitleKey())),
                Tag = appearance,
            });
        }

        _appearance.SelectionChanged += (_, _) =>
        {
            if (!_isUpdating && _appearance.SelectedItem is ComboBoxItem { Tag: SettingsAppearance appearance })
            {
                _accountSettings.Send(new AccountSettingsAction.AppearanceSelected(appearance));
            }
        };
        AutomationProperties.SetAutomationId(_appearance, "windows-settings-appearance");
        AutomationProperties.SetName(_appearance, LocalizedText.Get("settings.appearance.title"));
        return new SettingsItem(
            FluentSymbol.Sliders,
            LocalizedText.Get("settings.appearance.title"),
            LocalizedText.Get("settings.appearance.description"),
            _appearance);
    }

    /// <summary>アダプターの行のまとまりを組みます。</summary>
    /// <remarks>責務: 役割（主・副）ごとの行と注記を 1 回だけ作ります。探索で状態が変わっても行は作り直しません。</remarks>
    private void BuildAdapterGroup()
    {
        foreach (var role in AdapterConnectionRoleRawValue.AllCases)
        {
            var item = AdapterRow(role);
            _adapterRows.Add((role, item));
            _adapterGroup.Children.Add(item.Element);
        }

        var note = Typography.Text(LocalizedText.Get("settings.adapter.description"), TextStyles.SecondaryCaption);
        note.Margin = new Thickness(Space.XS, Space.XXS, 0, 0);
        _adapterGroup.Children.Add(note);
        AutomationProperties.SetAutomationId(_adapterGroup, "windows-settings-adapter-card");
    }

    /// <summary>アダプターの役割（主・副）の行を作ります。</summary>
    /// <param name="role">役割。</param>
    /// <returns>行。</returns>
    private SettingsItem AdapterRow(AdapterConnectionRole role)
    {
        var tone = role == AdapterConnectionRole.Primary ? Tone.Accent : Tone.Neutral;
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M };
        trailing.Children.Add(StatusPill.Create(null, LocalizedText.Get(role.BadgeKey()), tone));
        var select = new Button { Content = LocalizedText.Get("settings.adapter.select"), MinWidth = 96 };
        AutomationProperties.SetName(select, LocalizedText.Get(role.TitleKey()));
        AutomationProperties.SetAutomationId(select, $"windows-settings-adapter-{role.ToRawValue()}-select");
        select.Click += (_, _) => _settings.Send(new WindowsSettingsAction.AdapterSelectionRequested(role));
        trailing.Children.Add(select);
        return new SettingsItem(role.Glyph(), LocalizedText.Get(role.TitleKey()), null, trailing, tone);
    }

    /// <summary>自動アップロードの行を作ります。</summary>
    /// <returns>行。</returns>
    private SettingsItem UploadRow()
    {
        _upload.Toggled += (_, _) =>
        {
            if (!_isUpdating)
            {
                _accountSettings.Send(new AccountSettingsAction.AutomaticSessionUploadChanged(_upload.IsOn));
            }
        };
        AutomationProperties.SetAutomationId(_upload, "windows-settings-automatic-upload");
        AutomationProperties.SetName(_upload, LocalizedText.Get("settings.upload.automatic"));
        return new SettingsItem(
            FluentSymbol.Upload,
            LocalizedText.Get("settings.upload.automatic"),
            LocalizedText.Get("settings.upload.description"),
            _upload);
    }

    /// <summary>iCloud 同期の行を作ります。</summary>
    /// <remarks>責務: 状態ごとの操作（サインイン中・近日対応・サインイン・サインアウト）を 1 回だけ組み、状態に合わせて出し分けます（<see cref="UpdateCloudSync"/>）。</remarks>
    /// <returns>行。</returns>
    private SettingsItem CloudSyncRow()
    {
        _cloudSigningIn.Children.Add(_cloudRing);
        _cloudSigningIn.Children.Add(Typography.Text(LocalizedText.Get("windows.cloud.signing_in"), TextStyles.SecondaryCaption));

        // Coming Soon の間は、入口があることだけを示す押せないボタンを出す（機能フラグ AppleSignIn:Enabled で有効になる）。
        _cloudComingSoon.Children.Add(StatusPill.Create(null, LocalizedText.Get("windows.login.apple_coming_soon"), Tone.Accent));
        _cloudComingSoon.Children.Add(new Button { Content = LocalizedText.Get("windows.cloud.sign_in"), IsEnabled = false });
        AutomationProperties.SetAutomationId(_cloudComingSoon, "windows-settings-cloud-coming-soon");

        _cloudSignIn.Click += (_, _) => _ = _cloudSync.SignIn();
        AutomationProperties.SetAutomationId(_cloudSignIn, "windows-settings-cloud-sign-in");
        _cloudSignOut.Click += (_, _) => _cloudSync.SignOut();
        AutomationProperties.SetAutomationId(_cloudSignOut, "windows-settings-cloud-sign-out");

        _cloudTrailing.Children.Add(_cloudSigningIn);
        _cloudTrailing.Children.Add(_cloudComingSoon);
        _cloudTrailing.Children.Add(_cloudSignIn);
        _cloudTrailing.Children.Add(_cloudSignOut);
        _cloudRow = new SettingsItem(
            FluentSymbol.Cloud,
            LocalizedText.Get("settings.windows_cloud.title"),
            null,
            _cloudTrailing,
            Tone.Neutral);
        return _cloudRow;
    }

    /// <summary>iCloud の行を今の状態に合わせます。</summary>
    /// <remarks>責務: 状態（近日対応・サインアウト・サインイン済み・サインイン中・失敗）を説明に出し、操作を出し分けます。</remarks>
    private void UpdateCloudSync()
    {
        if (_cloudRow is not { } row)
        {
            return;
        }

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

        row.SetDescription(description);
        row.SetTone(availability == WindowsCloudSyncAvailability.Available ? Tone.Accent : Tone.Neutral);
        var signingIn = _cloudSync.IsSigningIn;
        var comingSoon = !signingIn && availability == WindowsCloudSyncAvailability.NotConfigured;
        var signIn = !signingIn && availability == WindowsCloudSyncAvailability.SignedOut;
        var signOut = !signingIn && availability == WindowsCloudSyncAvailability.Available;
        if (_cloudRing.IsActive != signingIn)
        {
            _cloudRing.IsActive = signingIn;
        }

        Retained.SetVisible(_cloudSigningIn, signingIn);
        Retained.SetVisible(_cloudComingSoon, comingSoon);
        Retained.SetVisible(_cloudSignIn, signIn);
        Retained.SetVisible(_cloudSignOut, signOut);
        Retained.SetVisible(_cloudTrailing, signingIn || comingSoon || signIn || signOut);
    }

    /// <summary>アカウント削除の行を作ります。</summary>
    /// <returns>行。</returns>
    private SettingsItem AccountDeletionRow()
    {
        var button = new Button
        {
            Content = LocalizedText.Get("account.delete.start"),
            Style = DesignTokens.Style("DangerButtonStyle"),
        };
        AutomationProperties.SetAutomationId(button, "windows-account-delete-start");
        button.Click += (_, _) => _authentication.Send(AuthenticationAction.AccountDeletionRequested);
        return new SettingsItem(
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
            var card = _adapterGroup;
            card.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5 });
            if (Motion.IsEnabled && card.RenderTransform is TranslateTransform transform)
            {
                Shake(transform);
            }
        });
    }

    /// <summary>カードを左右に小さく揺らします。</summary>
    /// <remarks>責務: 0.56 秒で減衰する左右の揺れを 1 回だけ再生します（iOS 版の <c>AttentionShakeEffect</c>）。</remarks>
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
    private static int IndexOf<T>(IReadOnlyList<T> items, T value)
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
