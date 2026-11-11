using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Views;

/// <summary>
/// アダプターの選択ダイアログです（iOS 版の <c>MacOSAdapterSelectionView</c> と <c>MacOSAdapterConnectionDetailView</c>）。
/// </summary>
/// <remarks>
/// WinUI はダイアログを重ねられないため、候補の一覧と候補の詳細を同じダイアログの中身として切り替えます。
/// 表示する内容はすべて <see cref="WindowsSettingsState"/> から作り、操作は <see cref="WindowsSettingsAction"/> として送ります。
/// </remarks>
public sealed class WindowsAdapterSelectionDialog
{
    /// <summary>ダイアログの中身の最大の幅です（狭いウインドウではダイアログに合わせて縮む）。</summary>
    private const double ContentMaxWidth = 520;

    /// <summary>候補の一覧の最大の高さです（超えたらスクロール）。</summary>
    private const double ListMaxHeight = 320;

    /// <summary>ダイアログの表示の窓口です。</summary>
    private readonly StateBoundDialog _dialog = new();

    /// <summary>操作の送り先です。</summary>
    private readonly Action<WindowsSettingsAction> _send;

    /// <summary>今の状態です（ボタンの処理で使う）。</summary>
    private WindowsSettingsState _state = new();

    /// <summary>通信方式の選択を画面の都合で変えている最中かどうかです。</summary>
    private bool _isRendering;

    /// <summary>中身を描いたダイアログです。</summary>
    private ContentDialog? _renderedDialog;

    /// <summary>最後に描いた中身の鍵です。</summary>
    private string? _renderedKey;

    /// <summary>ダイアログの中で押している最中の作り直しを待たせる仕組みです。</summary>
    private RenderGate? _gate;

    /// <summary>操作の送り先を指定して作ります。</summary>
    /// <param name="send">設定画面の操作の送り先。</param>
    public WindowsAdapterSelectionDialog(Action<WindowsSettingsAction> send)
    {
        _send = send;
    }

    /// <summary>
    /// 状態に合わせてダイアログを出し入れし、中身を描き直します。
    /// </summary>
    /// <remarks>責務: 選択中の役割があればダイアログを出し、探索の結果か候補の詳細を中身にします。</remarks>
    /// <param name="state">設定画面の状態。</param>
    /// <param name="xamlRoot">表示先。</param>
    public void Render(WindowsSettingsState state, XamlRoot? xamlRoot)
    {
        _state = state;
        _dialog.Sync(
            state.PresentedAdapterSlot is not null,
            state.PresentedAdapterSlot,
            xamlRoot,
            MakeDialog,
            _ => _send(new WindowsSettingsAction.AdapterSelectionCancelled()));
        if (_dialog.Current is { } dialog && state.PresentedAdapterSlot is { } slot)
        {
            // 中身に関係のある所が変わったときだけ、押している最中でなければ作り直す（探索の結果が届くたびに候補のボタンが消えないように）。
            var key = string.Join(
                "|",
                new object?[] { slot, state.InspectedAdapter?.SystemIdentifier, state.HasAdapterAssignmentConflict, state.AdapterDiscoveryStatus, state.AdapterTransportMode }
                    .Concat(state.DiscoveredAdapters.Select(adapter => (object)$"{adapter.SystemIdentifier}:{adapter.DisplayName}")));
            if (!ReferenceEquals(dialog, _renderedDialog))
            {
                _renderedDialog = dialog;
                _renderedKey = null;
                _gate = new RenderGate(dialog, watchesPopups: false);
            }

            if (key == _renderedKey || _gate?.TryDefer(() => Render(_state, dialog.XamlRoot)) == true)
            {
                return;
            }

            _renderedKey = key;
            _isRendering = true;
            if (state.InspectedAdapter is { } adapter)
            {
                RenderDetail(dialog, adapter, state.HasAdapterAssignmentConflict);
            }
            else
            {
                RenderSelection(dialog, slot, state);
            }

            _isRendering = false;
        }
    }

    /// <summary>ダイアログを作ります。</summary>
    /// <remarks>責務: 主・副のボタンの操作を、詳細の表示中かどうかで決めるダイアログを作ります。</remarks>
    /// <returns>ダイアログ。</returns>
    private ContentDialog MakeDialog()
    {
        var dialog = new ContentDialog { DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (_state.InspectedAdapter is not null)
            {
                _send(new WindowsSettingsAction.InspectedAdapterConfirmed());
            }
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            _send(_state.InspectedAdapter is null
                ? new WindowsSettingsAction.AdapterRefreshRequested()
                : new WindowsSettingsAction.InspectedAdapterDeclined());
        };
        return dialog;
    }

    /// <summary>候補の一覧を描きます。</summary>
    /// <remarks>責務: 通信方式の選択、探索中・空・失敗・候補の一覧の表示を作ります。</remarks>
    /// <param name="dialog">ダイアログ。</param>
    /// <param name="slot">選択中の役割。</param>
    /// <param name="state">設定画面の状態。</param>
    private void RenderSelection(ContentDialog dialog, AdapterConnectionRole slot, WindowsSettingsState state)
    {
        dialog.Title = LocalizedText.Get(slot.SelectionTitleKey());
        dialog.PrimaryButtonText = string.Empty;
        dialog.SecondaryButtonText = LocalizedText.Get("settings.adapter.selector.refresh");
        dialog.IsSecondaryButtonEnabled = state.AdapterDiscoveryStatus != WindowsAdapterDiscoveryStatus.Searching;
        dialog.CloseButtonText = LocalizedText.Get("settings.adapter.selector.cancel");
        dialog.DefaultButton = ContentDialogButton.Close;

        var body = new StackPanel { Spacing = Space.L, MaxWidth = ContentMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(Typography.Text(LocalizedText.Get("settings.adapter.selector.description"), TextStyles.SecondaryBody));
        var modes = new RadioButtons { MaxColumns = 2, Header = LocalizedText.Get("settings.adapter.selector.mode") };
        foreach (var mode in AdapterTransportModeRawValue.AllCases)
        {
            modes.Items.Add(new RadioButton { Content = new GlyphLabel(mode.Glyph(), LocalizedText.Get(mode.TitleKey())), Tag = mode });
        }

        modes.SelectedIndex = state.AdapterTransportMode == AdapterTransportMode.Usb ? 0 : 1;
        modes.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && modes.SelectedItem is RadioButton { Tag: AdapterTransportMode mode })
            {
                _send(new WindowsSettingsAction.AdapterTransportModeSelected(mode));
            }
        };
        body.Children.Add(modes);
        body.Children.Add(DiscoveryContent(state));
        dialog.Content = body;
    }

    /// <summary>探索の状態に合わせた中身を作ります。</summary>
    /// <remarks>責務: 探索中・空・失敗・候補の一覧のいずれかを作ります。</remarks>
    /// <param name="state">設定画面の状態。</param>
    /// <returns>中身。</returns>
    private UIElement DiscoveryContent(WindowsSettingsState state)
    {
        switch (state.AdapterDiscoveryStatus)
        {
            case WindowsAdapterDiscoveryStatus.Idle:
            case WindowsAdapterDiscoveryStatus.Searching:
                var searching = new StackPanel { Spacing = Space.M, Padding = new Thickness(0, Space.XXXL, 0, Space.XXXL), HorizontalAlignment = HorizontalAlignment.Center };
                searching.Children.Add(new ProgressRing { IsActive = true, Width = Metrics.IconTileMedium, Height = Metrics.IconTileMedium });
                searching.Children.Add(Typography.Text(LocalizedText.Get("settings.adapter.selector.searching"), TextStyles.SecondaryBody));
                return searching;
            case WindowsAdapterDiscoveryStatus.Failed:
                return new ContentUnavailablePanel(
                    FluentSymbol.Warning,
                    LocalizedText.Get("settings.adapter.selector.failed.title"),
                    LocalizedText.Get("settings.adapter.selector.failed.description"));
            default:
                if (state.DiscoveredAdapters.Count == 0)
                {
                    return new ContentUnavailablePanel(
                        state.AdapterTransportMode.Glyph(),
                        LocalizedText.Get("settings.adapter.selector.empty.title"),
                        LocalizedText.Get("settings.adapter.selector.empty.description"));
                }

                var list = new StackPanel { Spacing = Space.S + 2 };
                foreach (var adapter in state.DiscoveredAdapters)
                {
                    list.Children.Add(CandidateButton(adapter));
                }

                return new ScrollViewer { Content = list, MaxHeight = ListMaxHeight };
        }
    }

    /// <summary>候補のボタンを作ります。</summary>
    /// <remarks>責務: 候補の名前と識別子を出し、押すと詳細の確認を送るボタンを作ります。</remarks>
    /// <param name="adapter">候補。</param>
    /// <returns>ボタン。</returns>
    private Button CandidateButton(DiscoveredAdapter adapter)
    {
        var row = new Grid { ColumnSpacing = Space.M };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(IconTile.Create(adapter.TransportMode.Glyph(), Tone.Accent, Metrics.IconTileMedium));
        var texts = new StackPanel { Spacing = Space.XXS, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(adapter.DisplayName, TextStyles.BodyStrong, wraps: false));
        texts.Children.Add(Typography.Text(adapter.SystemIdentifier, TextStyles.Monospace, wraps: false));
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        var info = new FontIcon { Glyph = FluentSymbol.Info, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 2);
        row.Children.Add(info);
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(Space.M),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, adapter.DisplayName);
        button.Click += (_, _) => _send(new WindowsSettingsAction.AdapterCandidateSelected(adapter));
        return button;
    }

    /// <summary>候補の詳細を描きます（iOS 版の <c>MacOSAdapterConnectionDetailView</c>）。</summary>
    /// <remarks>責務: 通信方式・製造元・製品・識別子などの一覧、注意書き、割り当ての衝突の表示を作ります。</remarks>
    /// <param name="dialog">ダイアログ。</param>
    /// <param name="adapter">確認中の候補。</param>
    /// <param name="hasConflict">ほかの役割と衝突したなら true。</param>
    private static void RenderDetail(ContentDialog dialog, DiscoveredAdapter adapter, bool hasConflict)
    {
        dialog.Title = LocalizedText.Get("settings.adapter.details.title");
        dialog.PrimaryButtonText = LocalizedText.Get("settings.adapter.details.confirm");
        dialog.SecondaryButtonText = LocalizedText.Get("settings.adapter.details.decline");
        dialog.IsSecondaryButtonEnabled = true;
        dialog.CloseButtonText = LocalizedText.Get("settings.adapter.selector.cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;

        var unknown = LocalizedText.Get("settings.adapter.details.unknown");
        var body = new StackPanel { Spacing = Space.M, MaxWidth = ContentMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(Typography.Text(adapter.DisplayName, TextStyles.SecondaryBody));
        var rows = new StackPanel();
        AddRow(rows, "settings.adapter.details.transport", LocalizedText.Get(adapter.TransportMode.TitleKey()), false);
        AddRow(rows, "settings.adapter.details.manufacturer", adapter.ManufacturerName ?? unknown, false);
        AddRow(rows, "settings.adapter.details.product", adapter.ProductName ?? unknown, false);
        AddRow(rows, "settings.adapter.details.identifier", adapter.SystemIdentifier, true);
        if (adapter.SerialNumber is { } serial)
        {
            AddRow(rows, "settings.adapter.details.serial", serial, true);
        }

        if (adapter.VendorIdentifier is { } vendor)
        {
            AddRow(rows, "settings.adapter.details.vendor_id", vendor, true);
        }

        if (adapter.ProductIdentifier is { } product)
        {
            AddRow(rows, "settings.adapter.details.product_id", product, true);
        }

        body.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(0), Child = rows });
        body.Children.Add(Typography.Text(LocalizedText.Get("settings.adapter.details.notice"), TextStyles.SecondaryCaption));
        if (hasConflict)
        {
            body.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Error,
                Message = LocalizedText.Get("settings.adapter.details.assignment_conflict"),
            });
        }

        dialog.Content = body;
    }

    /// <summary>詳細の 1 行を足します。</summary>
    /// <remarks>責務: 項目名と値の 1 行を作って一覧へ足します。</remarks>
    /// <param name="rows">一覧。</param>
    /// <param name="titleKey">項目名のキー。</param>
    /// <param name="value">値。</param>
    /// <param name="monospaced">等幅で出すなら true。</param>
    private static void AddRow(StackPanel rows, string titleKey, string value, bool monospaced)
    {
        // 狭い幅（Compact）では、項目名の下に値を置く（値が切れない）。
        var stacked = ResponsiveLayout.Current == WidthClass.Compact;
        var row = new Grid { ColumnSpacing = Space.L, RowSpacing = Space.XXS, Padding = new Thickness(Space.M + 2, Space.S + 2, Space.M + 2, Space.S + 2) };
        if (stacked)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        row.Children.Add(Typography.Text(LocalizedText.Get(titleKey), TextStyles.SecondaryCaption));
        var valueText = Typography.Text(value, monospaced ? TextStyles.Monospace : TextStyles.Body);
        valueText.IsTextSelectionEnabled = true;
        Grid.SetColumn(valueText, stacked ? 0 : 1);
        Grid.SetRow(valueText, stacked ? 1 : 0);
        row.Children.Add(valueText);
        rows.Children.Add(row);
    }
}
