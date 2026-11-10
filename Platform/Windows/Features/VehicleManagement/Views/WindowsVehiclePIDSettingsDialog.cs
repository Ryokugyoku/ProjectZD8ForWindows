using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Views;

/// <summary>
/// 車両ごとの PID の収集設定のダイアログです（iOS 版の <c>MacOSVehiclePIDSettingsView</c>）。
/// </summary>
/// <remarks>検索の文字と絞り込みは画面の一時的な状態です。入力中の検索欄が消えないよう、一覧の部分だけを描き直します。</remarks>
public sealed class WindowsVehiclePIDSettingsDialog
{
    /// <summary>ダイアログの表示の窓口です。</summary>
    private readonly StateBoundDialog _dialog = new();

    /// <summary>操作の送り先です。</summary>
    private readonly Action<VehicleManagementAction> _send;

    /// <summary>一覧の置き場です。</summary>
    private StackPanel? _listHost;

    /// <summary>件数の要約の文字です。</summary>
    private TextBlock? _summary;

    /// <summary>今の項目です。</summary>
    private IReadOnlyList<VehiclePIDSelectionItem> _items = [];

    /// <summary>画面の一時的な状態：検索の文字です。</summary>
    private string _query = string.Empty;

    /// <summary>画面の一時的な状態：絞り込みです。</summary>
    private WindowsPIDCollectionFilter _filter = WindowsPIDCollectionFilter.All;

    /// <summary>出している行の記号とスイッチです（作り直さずに値だけを合わせるため）。</summary>
    private readonly Dictionary<OBDPIDRequest, (FontIcon Icon, ToggleSwitch Toggle)> _rows = [];

    /// <summary>最後に作った一覧の並びの鍵です。</summary>
    private string? _listKey;

    /// <summary>スイッチを画面の都合で動かしている最中かどうかです。</summary>
    private bool _isUpdating;

    /// <summary>操作の送り先を指定して作ります。</summary>
    /// <param name="send">車両管理の操作の送り先。</param>
    public WindowsVehiclePIDSettingsDialog(Action<VehicleManagementAction> send)
    {
        _send = send;
    }

    /// <summary>
    /// 状態に合わせてダイアログを出し入れし、一覧を描き直します。
    /// </summary>
    /// <remarks>責務: 収集設定の対象の車両があればダイアログを出し、項目の一覧を更新します。</remarks>
    /// <param name="state">車両管理の状態。</param>
    /// <param name="xamlRoot">表示先。</param>
    public void Render(VehicleManagementState state, XamlRoot? xamlRoot)
    {
        _items = state.PidSelectionItems;
        _dialog.Sync(
            state.PidSettingsVehicleID is not null,
            state.PidSettingsVehicleID,
            xamlRoot,
            MakeDialog,
            _ => _send(new VehicleManagementAction.PidSettingsClosed()));
        RenderList();
    }

    /// <summary>ダイアログを作ります。</summary>
    /// <remarks>責務: 要約・絞り込み・検索欄と一覧の置き場を持つダイアログを作ります。</remarks>
    /// <returns>ダイアログ。</returns>
    private ContentDialog MakeDialog()
    {
        _query = string.Empty;
        _filter = WindowsPIDCollectionFilter.All;
        _listKey = null;
        _rows.Clear();
        var body = new StackPanel { Spacing = 14, Width = 600 };
        _summary = Typography.Text(string.Empty, "SecondaryCaptionTextStyle");
        body.Children.Add(_summary);
        var filter = new SelectorBar();
        foreach (var value in Enum.GetValues<WindowsPIDCollectionFilter>())
        {
            filter.Items.Add(new SelectorBarItem { Text = LocalizedText.Get(value.TitleKey()), Tag = value, IsSelected = value == _filter });
        }

        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedItem?.Tag is WindowsPIDCollectionFilter selected)
            {
                _filter = selected;
                RenderList();
            }
        };
        body.Children.Add(filter);
        var search = new AutoSuggestBox { PlaceholderText = LocalizedText.Get("garage.pid_settings.search"), QueryIcon = new SymbolIcon(Symbol.Find) };
        search.TextChanged += (_, _) =>
        {
            _query = search.Text;
            RenderList();
        };
        body.Children.Add(search);
        _listHost = new StackPanel { Spacing = 6 };
        body.Children.Add(new ScrollViewer { Content = _listHost, Height = 380 });
        return new ContentDialog
        {
            Title = LocalizedText.Get("garage.pid_settings.title"),
            Content = body,
            CloseButtonText = LocalizedText.Get("common.close"),
            DefaultButton = ContentDialogButton.Close,
        };
    }

    /// <summary>一覧を描き直します。</summary>
    /// <remarks>責務: 要約の件数と、絞り込み・検索に合う項目の切り替えスイッチを作ります。</remarks>
    private void RenderList()
    {
        if (_listHost is null || _summary is null || _dialog.Current is null)
        {
            return;
        }

        _summary.Text = LocalizedText.Format("garage.pid_settings.summary %lld %lld", _items.Count(item => item.IsEnabled), _items.Count);
        var filtered = _items.Where(item => _filter.Matches(item) && MatchesQuery(item)).ToList();
        var key = string.Join(",", filtered.Select(item => ServicePIDText(item.Id))) + "|" + _query;
        if (key == _listKey && filtered.Count > 0)
        {
            // 並びが同じなら作り直さず、スイッチと記号だけを合わせる（スクロールの位置と、切り替えたスイッチを保つ）。
            _isUpdating = true;
            foreach (var item in filtered)
            {
                if (_rows.TryGetValue(item.Id, out var row))
                {
                    if (row.Toggle.IsOn != item.IsEnabled)
                    {
                        row.Toggle.IsOn = item.IsEnabled;
                    }

                    row.Icon.Glyph = item.IsEnabled ? FluentSymbol.Pulse : FluentSymbol.Pause;
                    row.Icon.Foreground = DesignTokens.Foreground(item.IsEnabled ? Tone.Success : Tone.Neutral);
                }
            }

            _isUpdating = false;
            return;
        }

        _listKey = key;
        _rows.Clear();
        _listHost.Children.Clear();
        if (filtered.Count == 0)
        {
            _listHost.Children.Add(new ContentUnavailablePanel(
                _query.Length == 0 ? FluentSymbol.Pause : FluentSymbol.Search,
                LocalizedText.Get(_query.Length == 0 ? "garage.pid_settings.empty" : "garage.pid_settings.no_results")));
            return;
        }

        foreach (var item in filtered)
        {
            _listHost.Children.Add(Row(item));
        }
    }

    /// <summary>項目の行を作ります。</summary>
    /// <remarks>責務: 収集の状態の記号・名前・番号・車種のバッジ・状態の文言と切り替えスイッチを並べます。</remarks>
    /// <param name="item">PID の項目。</param>
    /// <returns>行。</returns>
    private Grid Row(VehiclePIDSelectionItem item)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 6, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new FontIcon
        {
            Glyph = item.IsEnabled ? FluentSymbol.Pulse : FluentSymbol.Pause,
            FontSize = 16,
            Foreground = DesignTokens.Foreground(item.IsEnabled ? Tone.Success : Tone.Neutral),
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(icon);
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(Typography.Text(Name(item), "BodyStrongTextBlockStyle"));
        texts.Children.Add(Typography.Text(ServicePIDText(item.Id), "MonospaceCaptionTextStyle", wraps: false));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (item.VehicleModelCode is { } modelCode)
        {
            var badge = new VehicleModelBadge(modelCode, 30);
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var toggle = new ToggleSwitch
        {
            IsOn = item.IsEnabled,
            OnContent = LocalizedText.Get("garage.pid_settings.collecting"),
            OffContent = LocalizedText.Get("garage.pid_settings.paused"),
            MinWidth = 0,
        };
        var request = item.Id;
        toggle.Toggled += (_, _) =>
        {
            if (!_isUpdating)
            {
                _send(new VehicleManagementAction.PidCollectionChanged(request, toggle.IsOn));
            }
        };
        Grid.SetColumn(toggle, 3);
        grid.Children.Add(toggle);
        _rows[request] = (icon, toggle);
        return grid;
    }

    /// <summary>検索の文字に合うかどうかを返します。</summary>
    /// <remarks>責務: 名前・Mode と PID の番号・PID の 16 進のどれかに検索の文字が含まれるかを、大文字小文字を区別せずに調べます。</remarks>
    /// <param name="item">PID の項目。</param>
    /// <returns>合うなら true。</returns>
    private bool MatchesQuery(VehiclePIDSelectionItem item) =>
        _query.Length == 0
        || Name(item).Contains(_query, StringComparison.CurrentCultureIgnoreCase)
        || ServicePIDText(item.Id).Contains(_query, StringComparison.CurrentCultureIgnoreCase)
        || item.Id.Pid.ToString("X2", CultureInfo.InvariantCulture).Contains(_query, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>項目の名前を返します。</summary>
    /// <remarks>責務: 名前のキーがあれば訳、なければ「未確認」の文言を返します。</remarks>
    /// <param name="item">PID の項目。</param>
    /// <returns>名前。</returns>
    private static string Name(VehiclePIDSelectionItem item) =>
        LocalizedText.Get(item.NameKey ?? "garage.pid_settings.unconfirmed");

    /// <summary>Mode と PID の番号の文字列を返します。</summary>
    /// <remarks>責務: iOS 版と同じ「MODE xx · PID xx」の形にします。</remarks>
    /// <param name="request">PID の要求。</param>
    /// <returns>文字列。</returns>
    private static string ServicePIDText(OBDPIDRequest request) =>
        string.Create(CultureInfo.InvariantCulture, $"MODE {request.Service:X2} · PID {request.Pid:X2}");
}
