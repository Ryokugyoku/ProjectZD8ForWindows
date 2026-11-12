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
/// <remarks>
/// 検索の文字と絞り込みは画面の一時的な状態です。入力中の検索欄が消えないよう、一覧の部分だけを差し替えます。
/// 一覧は仮想化した <see cref="VirtualList{TItem}"/> で、スイッチを切り替えても差分だけを反映するので先頭へ戻りません。
/// ダイアログの幅は <see cref="StateBoundDialog"/> の最大幅に合わせて伸縮し、一覧の高さはウインドウの高さに合わせます。
/// </remarks>
public sealed class WindowsVehiclePIDSettingsDialog
{
    /// <summary>ダイアログの表示の窓口です。</summary>
    private readonly StateBoundDialog _dialog = new();

    /// <summary>操作の送り先です。</summary>
    private readonly Action<VehicleManagementAction> _send;

    /// <summary>項目の仮想化した一覧です。</summary>
    private VirtualList<VehiclePIDSelectionItem>? _list;

    /// <summary>項目がないときの表示です。</summary>
    private ContentUnavailablePanel? _emptyPanel;

    /// <summary>項目がないときの表示を置く場所です（文言の差し替えのため）。</summary>
    private Border? _emptyHost;

    /// <summary>件数の要約の文字です。</summary>
    private TextBlock? _summary;

    /// <summary>今の項目です。</summary>
    private IReadOnlyList<VehiclePIDSelectionItem> _items = [];

    /// <summary>画面の一時的な状態：検索の文字です。</summary>
    private string _query = string.Empty;

    /// <summary>画面の一時的な状態：絞り込みです。</summary>
    private WindowsPIDCollectionFilter _filter = WindowsPIDCollectionFilter.All;

    /// <summary>最後に空の表示を作ったときの「検索中か」です。</summary>
    private bool? _emptyForQuery;

    /// <summary>スイッチを画面の都合で動かしている最中かどうかです。</summary>
    private bool _isUpdating;

    /// <summary>操作の送り先を指定して作ります。</summary>
    /// <param name="send">車両管理の操作の送り先。</param>
    public WindowsVehiclePIDSettingsDialog(Action<VehicleManagementAction> send)
    {
        _send = send;
    }

    /// <summary>
    /// 状態に合わせてダイアログを出し入れし、一覧を更新します。
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
    /// <remarks>責務: 要約・絞り込み・検索欄と、仮想化した一覧を持つダイアログを作ります。</remarks>
    /// <returns>ダイアログ。</returns>
    private ContentDialog MakeDialog()
    {
        _query = string.Empty;
        _filter = WindowsPIDCollectionFilter.All;
        _emptyForQuery = null;
        var body = new Grid { RowSpacing = Space.M, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _summary = Typography.Text(string.Empty, TextStyles.SecondaryCaption);
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
        Grid.SetRow(filter, 1);
        body.Children.Add(filter);
        var search = new AutoSuggestBox { PlaceholderText = LocalizedText.Get("garage.pid_settings.search"), QueryIcon = new SymbolIcon(Symbol.Find) };
        search.TextChanged += (_, _) =>
        {
            _query = search.Text;
            RenderList();
        };
        Grid.SetRow(search, 2);
        body.Children.Add(search);

        _list = new VirtualList<VehiclePIDSelectionItem>(CreateRow, UpdateRow);
        _list.View.Padding = new Thickness(0);
        _emptyHost = new Border { Visibility = Visibility.Collapsed };
        var listArea = new Grid();
        listArea.Children.Add(_list.View);
        listArea.Children.Add(_emptyHost);
        Grid.SetRow(listArea, 3);
        body.Children.Add(listArea);

        var dialog = new ContentDialog
        {
            Title = LocalizedText.Get("garage.pid_settings.title"),
            Content = body,
            CloseButtonText = LocalizedText.Get("common.close"),
            DefaultButton = ContentDialogButton.Close,
        };

        // 一覧の高さはウインドウの高さに合わせる（見出し・ボタン・余白の分を除く）。ダイアログは画面の外側に出るので、表示先の大きさを見る。
        void FitHeight()
        {
            if (dialog.XamlRoot is { } root)
            {
                body.Height = Math.Clamp(root.Size.Height - 300, 220, 560);
            }
        }

        dialog.Opened += (_, _) =>
        {
            FitHeight();
            if (dialog.XamlRoot is { } root)
            {
                root.Changed += OnRootChanged;
            }
        };
        dialog.Closed += (_, _) =>
        {
            if (dialog.XamlRoot is { } root)
            {
                root.Changed -= OnRootChanged;
            }
        };
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => FitHeight();
        return dialog;
    }

    /// <summary>一覧を更新します。</summary>
    /// <remarks>責務: 要約の件数を直し、絞り込み・検索に合う項目へ差分だけを反映します（スクロールの位置とスイッチを保つ）。</remarks>
    private void RenderList()
    {
        if (_list is null || _summary is null || _emptyHost is null || _dialog.Current is null)
        {
            return;
        }

        Retained.SetText(_summary, LocalizedText.Format("garage.pid_settings.summary %lld %lld", _items.Count(item => item.IsEnabled), _items.Count));
        var filtered = _items.Where(item => _filter.Matches(item) && MatchesQuery(item)).ToList();
        _isUpdating = true;
        try
        {
            _list.Sync(filtered, item => item.Id);
        }
        finally
        {
            _isUpdating = false;
        }

        var empty = filtered.Count == 0;
        Retained.SetVisible(_emptyHost, empty);
        Retained.SetVisible(_list.View, !empty);
        if (empty && _emptyForQuery != (_query.Length != 0))
        {
            _emptyForQuery = _query.Length != 0;
            _emptyPanel = new ContentUnavailablePanel(
                _query.Length == 0 ? FluentSymbol.Pause : FluentSymbol.Search,
                LocalizedText.Get(_query.Length == 0 ? "garage.pid_settings.empty" : "garage.pid_settings.no_results"));
            _emptyHost.Child = _emptyPanel;
        }
    }

    /// <summary>項目の行の要素を作ります（一覧が 1 つの器につき 1 回だけ呼ぶ）。</summary>
    /// <returns>行。</returns>
    private FrameworkElement CreateRow()
    {
        var row = new PidRow();
        row.Toggle.Toggled += (_, _) =>
        {
            if (!_isUpdating && !row.IsBinding && row.Request is { } request)
            {
                _send(new VehicleManagementAction.PidCollectionChanged(request, row.Toggle.IsOn));
            }
        };
        return row;
    }

    /// <summary>項目の行へ値を入れます。</summary>
    /// <remarks>責務: 収集の状態の記号・名前・番号・車種のバッジ・スイッチを、違うときだけ差し替えます。</remarks>
    /// <param name="element">行。</param>
    /// <param name="item">PID の項目。</param>
    private static void UpdateRow(FrameworkElement element, VehiclePIDSelectionItem item) => ((PidRow)element).Bind(item, Name(item), ServicePIDText(item.Id));

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

    /// <summary>PID の項目の 1 行です（使い回して値だけを差し替える）。</summary>
    private sealed class PidRow : Grid
    {
        /// <summary>収集の状態の記号です。</summary>
        private readonly FontIcon _icon = new() { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };

        /// <summary>名前の文字です。</summary>
        private readonly TextBlock _name = Typography.Text(string.Empty, TextStyles.BodyStrong, wraps: false);

        /// <summary>番号の文字です。</summary>
        private readonly TextBlock _code = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);

        /// <summary>車種のバッジの置き場です。</summary>
        private readonly Border _badgeHost = new() { VerticalAlignment = VerticalAlignment.Center };

        /// <summary>今のバッジの車種です。</summary>
        private string? _badgeModel;

        /// <summary>今の収集の状態の記号の色の元です。</summary>
        private bool? _enabled;

        /// <summary>行を組みます。</summary>
        public PidRow()
        {
            ColumnSpacing = Space.M;
            Padding = new Thickness(Space.S, Space.XS, Space.S, Space.XS);
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Children.Add(_icon);
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            var texts = new StackPanel { Spacing = Space.XXS, VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(_name);
            texts.Children.Add(_code);
            SetColumn(texts, 1);
            Children.Add(texts);
            SetColumn(_badgeHost, 2);
            Children.Add(_badgeHost);
            Toggle = new ToggleSwitch
            {
                OnContent = LocalizedText.Get("garage.pid_settings.collecting"),
                OffContent = LocalizedText.Get("garage.pid_settings.paused"),
                MinWidth = 0,
            };
            SetColumn(Toggle, 3);
            Children.Add(Toggle);
        }

        /// <summary>切り替えスイッチです。</summary>
        public ToggleSwitch Toggle { get; }

        /// <summary>今の行の PID の要求です。</summary>
        public OBDPIDRequest? Request { get; private set; }

        /// <summary>値を入れている最中かどうかです（そのあいだのスイッチの変化は利用者の操作ではない）。</summary>
        public bool IsBinding { get; private set; }

        /// <summary>項目の値を入れます。</summary>
        /// <remarks>責務: 違う値だけを差し替えます。</remarks>
        /// <param name="item">PID の項目。</param>
        /// <param name="name">名前。</param>
        /// <param name="code">番号の文字列。</param>
        public void Bind(VehiclePIDSelectionItem item, string name, string code)
        {
            IsBinding = true;
            try
            {
                Request = item.Id;
                Retained.SetText(_name, name);
                Retained.SetText(_code, code);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"{name} {code}");
                if (_enabled != item.IsEnabled)
                {
                    _enabled = item.IsEnabled;
                    _icon.Glyph = item.IsEnabled ? FluentSymbol.Pulse : FluentSymbol.Pause;
                    _icon.Foreground = DesignTokens.Foreground(item.IsEnabled ? Tone.Success : Tone.Neutral);
                }

                if (Toggle.IsOn != item.IsEnabled)
                {
                    Toggle.IsOn = item.IsEnabled;
                }

                if (_badgeModel != item.VehicleModelCode)
                {
                    _badgeModel = item.VehicleModelCode;
                    _badgeHost.Child = item.VehicleModelCode is { } modelCode ? new VehicleModelBadge(modelCode, Metrics.IconTileSmall) : null;
                }
            }
            finally
            {
                IsBinding = false;
            }
        }
    }
}
