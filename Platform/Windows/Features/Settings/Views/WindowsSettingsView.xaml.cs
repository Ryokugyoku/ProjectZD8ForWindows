using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Views;

/// <summary>
/// 設定画面です。言語・外観・アダプター・自動アップロード・アカウント削除を扱います（iOS 版の <c>MacOSSettingsView</c>）。
/// </summary>
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

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>最後にカードを描いたときの入力です（同じなら描き直さない）。</summary>
    private object? _pageKey;

    /// <summary>最後に描いたアダプターのカードです。</summary>
    private FrameworkElement? _adapterCard;

    /// <summary>画面が処理したアダプターへの注意の通し番号です。</summary>
    private uint _handledAttentionSequence;

    /// <summary>モデルを受け取って設定画面を作ります。</summary>
    /// <param name="settings">アダプター選択の表示モデル。</param>
    /// <param name="accountSettings">アカウント設定のモデル。</param>
    /// <param name="authentication">認証のモデル。</param>
    public WindowsSettingsView(
        WindowsSettingsPresentationModel settings,
        AccountSettingsModel accountSettings,
        AuthenticationSessionModel authentication)
    {
        _settings = settings;
        _accountSettings = accountSettings;
        _authentication = authentication;
        _adapterSelection = new WindowsAdapterSelectionDialog(settings.Send);
        _accountDeletion = new WindowsAccountDeletionFlow(authentication.Send);
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, args) =>
        {
            if ((args.PreviousSize.Width >= 760) != (args.NewSize.Width >= 760))
            {
                _pageKey = null;
                Render();
            }
        };
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
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>設定画面を描きます。</summary>
    /// <remarks>責務: 状態から各カードを作り、ダイアログとアダプターへの注意の表示を状態に合わせます。</remarks>
    private void Render()
    {
        var state = _settings.State;
        var account = _accountSettings.Settings;
        var pageKey = (state.SelectedAdapters, account, ActualWidth >= 760);
        if (!Equals(pageKey, _pageKey))
        {
            _pageKey = pageKey;
            RenderPage(state, account);
        }

        _adapterSelection.Render(state, XamlRoot);
        var authentication = _authentication.State;
        _accountDeletion.Render(authentication.AccountDeletionPhase, authentication.AccountDeletionFailure, XamlRoot);
        PresentAdapterAttentionIfNeeded(state);
    }

    /// <summary>設定のカードを描き直します。</summary>
    /// <remarks>責務: 見出し・言語・外観・アダプター・アップロード・アカウント削除のカードを幅に合わせて並べます。</remarks>
    /// <param name="state">設定画面の状態。</param>
    /// <param name="account">アカウント設定。</param>
    private void RenderPage(WindowsSettingsState state, AccountSettings account)
    {
        _isRendering = true;
        Page.Children.Clear();
        Page.Children.Add(new ScreenHeader(
            FluentSymbol.Sliders,
            null,
            LocalizedText.Get("settings.title"),
            LocalizedText.Get("settings.subtitle")));

        var adapterCard = AdapterCard(state);
        _adapterCard = adapterCard;
        var cards = new UIElement[] { LanguageCard(account), AppearanceCard(account), adapterCard, UploadCard(account) };
        if (ActualWidth >= 760)
        {
            var grid = new Grid { ColumnSpacing = 18 };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Spacing = 18 };
            left.Children.Add(cards[0]);
            left.Children.Add(cards[1]);
            var right = new StackPanel { Spacing = 18 };
            right.Children.Add(cards[2]);
            right.Children.Add(cards[3]);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            Page.Children.Add(grid);
        }
        else
        {
            foreach (var card in cards)
            {
                Page.Children.Add(card);
            }
        }

        Page.Children.Add(AccountDeletionCard());
        _isRendering = false;
    }

    /// <summary>設定のカードを作ります。</summary>
    /// <remarks>責務: 記号・小見出し・題・説明と中身を持つカードを作ります。</remarks>
    /// <param name="prefix">キーの前半（例 settings.language）。</param>
    /// <param name="glyph">記号。</param>
    /// <param name="content">中身。</param>
    /// <returns>カード。</returns>
    private static Border SettingsCard(string prefix, string glyph, UIElement content)
    {
        var stack = new StackPanel { Spacing = 16 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(IconTile.Create(glyph, Tone.Accent, 34));
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(Typography.Text(LocalizedText.Get(prefix + ".eyebrow"), "EyebrowTextStyle"));
        texts.Children.Add(Typography.Text(LocalizedText.Get(prefix + ".title"), "CardTitleTextStyle"));
        texts.Children.Add(Typography.Text(LocalizedText.Get(prefix + ".description"), "SecondaryCaptionTextStyle"));
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        stack.Children.Add(head);
        stack.Children.Add(content);
        return SurfaceCard.Create(stack, padding: 18);
    }

    /// <summary>言語のカードを作ります。</summary>
    /// <remarks>責務: 3 つの言語から 1 つを選ぶ選択肢を作ります。</remarks>
    /// <param name="account">アカウント設定。</param>
    /// <returns>カード。</returns>
    private Border LanguageCard(AccountSettings account)
    {
        var choices = new RadioButtons { MaxColumns = 3 };
        foreach (var language in SettingsLanguageRawValue.AllCases)
        {
            choices.Items.Add(new RadioButton { Content = LocalizedText.Get(language.TitleKey()), Tag = language });
        }

        choices.SelectedIndex = IndexOf(SettingsLanguageRawValue.AllCases, account.Language);
        choices.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && choices.SelectedItem is RadioButton { Tag: SettingsLanguage language })
            {
                _accountSettings.Send(new AccountSettingsAction.LanguageSelected(language));
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(choices, "windows-settings-language");
        return SettingsCard("settings.language", FluentSymbol.Language, choices);
    }

    /// <summary>外観のカードを作ります。</summary>
    /// <remarks>責務: システム・ライト・ダークの 3 つの切り替えボタンを作ります。</remarks>
    /// <param name="account">アカウント設定。</param>
    /// <returns>カード。</returns>
    private Border AppearanceCard(AccountSettings account)
    {
        var row = new Grid { ColumnSpacing = 8 };
        var index = 0;
        foreach (var appearance in SettingsAppearanceRawValue.AllCases)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var content = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new FontIcon { Glyph = appearance.Glyph(), FontSize = 17 });
            content.Children.Add(Typography.Text(LocalizedText.Get(appearance.TitleKey()), "CaptionTextBlockStyle", wraps: false));
            var button = new ToggleButton
            {
                Content = content,
                IsChecked = account.Appearance == appearance,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 60,
            };
            var selected = appearance;
            button.Click += (_, _) => _accountSettings.Send(new AccountSettingsAction.AppearanceSelected(selected));
            Grid.SetColumn(button, index++);
            row.Children.Add(button);
        }

        return SettingsCard("settings.appearance", FluentSymbol.Sliders, row);
    }

    /// <summary>アダプターのカードを作ります。</summary>
    /// <remarks>責務: 主・副のアダプターの行（選択済みの名前と「選択」ボタン）を作ります。</remarks>
    /// <param name="state">設定画面の状態。</param>
    /// <returns>カード。</returns>
    private Border AdapterCard(WindowsSettingsState state)
    {
        var rows = new StackPanel { Spacing = 10 };
        foreach (var role in AdapterConnectionRoleRawValue.AllCases)
        {
            rows.Children.Add(AdapterRow(role, state));
        }

        var card = SettingsCard("settings.adapter", FluentSymbol.Plug, rows);
        card.RenderTransform = new TranslateTransform();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, "windows-settings-adapter-card");
        return card;
    }

    /// <summary>アダプターの 1 行を作ります。</summary>
    /// <remarks>責務: 役割の名前・バッジ・選択済みのアダプター名と「選択」ボタンを並べます。</remarks>
    /// <param name="role">役割。</param>
    /// <param name="state">設定画面の状態。</param>
    /// <returns>行。</returns>
    private Border AdapterRow(AdapterConnectionRole role, WindowsSettingsState state)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconTile.Create(role.Glyph(), role == AdapterConnectionRole.Primary ? Tone.Accent : Tone.Neutral, 34));
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(Typography.Text(LocalizedText.Get(role.TitleKey()), "BodyStrongTextBlockStyle", wraps: false));
        titleRow.Children.Add(StatusPill.Create(null, LocalizedText.Get(role.BadgeKey()), role == AdapterConnectionRole.Primary ? Tone.Accent : Tone.Neutral));
        texts.Children.Add(titleRow);
        var description = state.SelectedAdapters.TryGetValue(role, out var adapter)
            ? adapter.DisplayName
            : LocalizedText.Get("settings.adapter.not_selected");
        texts.Children.Add(Typography.Text(description, "SecondaryCaptionTextStyle", wraps: false));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var select = new Button { Content = LocalizedText.Get("settings.adapter.select"), VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(select, LocalizedText.Get(role.TitleKey()));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(select, $"windows-settings-adapter-{role.ToRawValue()}-select");
        select.Click += (_, _) => _settings.Send(new WindowsSettingsAction.AdapterSelectionRequested(role));
        Grid.SetColumn(select, 2);
        grid.Children.Add(select);
        return new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(11), Child = grid };
    }

    /// <summary>自動アップロードのカードを作ります。</summary>
    /// <remarks>責務: 自動アップロードの切り替えスイッチを作ります。</remarks>
    /// <param name="account">アカウント設定。</param>
    /// <returns>カード。</returns>
    private Border UploadCard(AccountSettings account)
    {
        var toggle = new ToggleSwitch
        {
            Header = LocalizedText.Get("settings.upload.automatic"),
            IsOn = account.AutomaticSessionUploadEnabled,
            OnContent = string.Empty,
            OffContent = string.Empty,
        };
        toggle.Toggled += (_, _) =>
        {
            if (!_isRendering)
            {
                _accountSettings.Send(new AccountSettingsAction.AutomaticSessionUploadChanged(toggle.IsOn));
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "windows-settings-automatic-upload");
        return SettingsCard("settings.upload", FluentSymbol.Upload, toggle);
    }

    /// <summary>アカウント削除のカードを作ります。</summary>
    /// <remarks>責務: アカウント削除の説明と開始のボタンを、赤の控えめなカードで作ります。</remarks>
    /// <returns>カード。</returns>
    private Border AccountDeletionCard()
    {
        var grid = new Grid { ColumnSpacing = 15 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconTile.Create(FluentSymbol.Shield, Tone.Danger, 40));
        var texts = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(LocalizedText.Get("account.delete.card.title"), "CardTitleTextStyle"));
        texts.Children.Add(Typography.Text(LocalizedText.Get("account.delete.card.description"), "SecondaryCaptionTextStyle"));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var button = new Button
        {
            Content = LocalizedText.Get("account.delete.start"),
            Style = DesignTokens.Style("DangerButtonStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-account-delete-start");
        button.Click += (_, _) => _authentication.Send(AuthenticationAction.AccountDeletionRequested);
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return SurfaceCard.Create(grid, Tone.Danger, 18, tinted: true);
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
