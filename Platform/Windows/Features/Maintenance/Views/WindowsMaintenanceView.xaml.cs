using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.Maintenance.Actions;
using ProjectZD8.Application.Maintenance.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Maintenance.Views;

/// <summary>
/// 整備記録の画面です。左に車両ごとの記録の時系列、右に概要か編集を出します（iOS 版の <c>MacOSMaintenanceView</c>）。
/// </summary>
/// <remarks>
/// 左の一覧は Windows の ListView なので、選んでいる記録が強調され、↑↓ キーでも選べます。右クリックで編集・削除ができます。
/// 「新しい記録」のメニューは一覧の上にいつも出しておき、編集中でもすぐに次の記録を作れます。
/// </remarks>
public sealed partial class WindowsMaintenanceView : UserControl
{
    /// <summary>整備記録のモデルです。</summary>
    private readonly MaintenanceModel _model;

    /// <summary>操作中の作り直しを待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>削除の確認です。</summary>
    private readonly StateBoundDialog _deletionDialog = new();

    /// <summary>表示中の編集画面です（同じ記録の編集中は作り直さない）。</summary>
    private WindowsMaintenanceEditorView? _editor;

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>最後に描いた時系列の鍵です（編集中の入力のたびに一覧を作り直さないため）。</summary>
    private string? _timelineKey;

    /// <summary>一覧（副）と編集・概要（主）を左右または片方だけで出す面です。</summary>
    private readonly AdaptiveSplit _split;

    /// <summary>削除を確かめている記録です。</summary>
    private MaintenanceRecord? _deletionCandidate;

    /// <summary>整備記録のモデルを受け取って画面を作ります。</summary>
    /// <param name="model">整備記録のモデル。</param>
    public WindowsMaintenanceView(MaintenanceModel model)
    {
        _model = model;
        InitializeComponent();
        _gate = new RenderGate(this);
        Root.Children.Clear();
        _split = new AdaptiveSplit
        {
            Primary = DetailHost,
            Secondary = TimelinePane,
            SecondaryFirst = true,
            SecondaryWidth = 340,
            CollapseBelow = WidthClass.Expanded,
            CollapseMode = SplitCollapseMode.Single,
            ShowsSecondary = true,
        };
        Root.Children.Add(_split);

        // ほかの画面（PageScaffold）と同じ外周の余白にそろえる。一覧の面がナビの縁や窓の縁に貼り付かないように。
        ResponsiveLayout.Bind(this, widthClass =>
        {
            var gutter = Space.PageGutter(widthClass);
            Root.Padding = new Thickness(gutter, gutter * 0.75, gutter, gutter);
        });
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            _timelineKey = null;
            Render();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>
    /// 整備記録を削除してよいか尋ねます。
    /// </summary>
    /// <remarks>責務: 取り消せない削除の前に、記録の題を入れた確認を出します（モデルは確かめずに消すため、画面で確かめる）。</remarks>
    /// <param name="dialog">確認を出し入れする仕組み。</param>
    /// <param name="xamlRoot">出す先。</param>
    /// <param name="title">記録の題。</param>
    /// <param name="key">確認の鍵（記録）。</param>
    /// <param name="closed">閉じたときの処理。削除してよいなら true を受け取る。</param>
    public static void SyncDeletionDialog(StateBoundDialog dialog, XamlRoot? xamlRoot, string? title, object? key, Action<bool> closed)
    {
        dialog.Sync(key is not null, key, xamlRoot, () => new ContentDialog
        {
            Title = LocalizedText.Get("windows.maintenance.delete.title"),
            Content = Typography.Text(LocalizedText.Format("windows.maintenance.delete.message", title ?? string.Empty), TextStyles.Body),
            PrimaryButtonText = LocalizedText.Get("maintenance.delete"),
            PrimaryButtonStyle = DesignTokens.Style("DangerButtonStyle"),
            CloseButtonText = LocalizedText.Get("maintenance.cancel"),
            DefaultButton = ContentDialogButton.Close,
        }, result => closed(result == ContentDialogResult.Primary));
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>画面を描きます。</summary>
    /// <remarks>責務: 時系列（変わったときだけ）と、右側の概要か編集を描き、削除の確認を合わせます。</remarks>
    private void Render()
    {
        var state = _model.State;
        _isRendering = true;
        if (TimelineKey(state) != _timelineKey && !_gate.TryDefer(Render))
        {
            _timelineKey = TimelineKey(state);
            RenderTimeline(state);
        }

        if (state.Draft is { } draft)
        {
            if (_editor is null || !_editor.Shows(draft))
            {
                _editor = new WindowsMaintenanceEditorView(draft, _model, RequestDeletion);
                DetailHost.Child = _editor;
            }

            _editor.Update(state);
        }
        else
        {
            _editor = null;
            DetailHost.Child = Overview(state);
        }

        _split.ShowsSecondary = state.Draft is null;
        SelectCurrentRecord(state);
        _isRendering = false;
        SyncDeletionDialog(_deletionDialog, XamlRoot, _deletionCandidate?.Title, _deletionCandidate?.Id, confirmed =>
        {
            var candidate = _deletionCandidate;
            _deletionCandidate = null;
            if (confirmed && candidate is not null)
            {
                UiLog.MaintenanceDeleteConfirmed(UiLog.Logger);
                _model.Send(new MaintenanceAction.DeleteRequested(candidate.Id));
            }
        });
    }

    /// <summary>記録の削除を確かめます。</summary>
    /// <param name="id">記録。</param>
    private void RequestDeletion(MaintenanceRecordID id)
    {
        _deletionCandidate = _model.State.Records.FirstOrDefault(record => record.Id == id)
            ?? _model.State.VisibleRecords.FirstOrDefault(record => record.Id == id);
        if (_deletionCandidate is null)
        {
            UiLog.MaintenanceDeleteConfirmed(UiLog.Logger);
            _model.Send(new MaintenanceAction.DeleteRequested(id));
            return;
        }

        Render();
    }

    /// <summary>時系列を作り直すかを見分ける鍵を作ります。</summary>
    /// <param name="state">整備記録の状態。</param>
    /// <returns>鍵。</returns>
    private static string TimelineKey(MaintenanceState state) => string.Join(
        "|",
        new object?[]
        {
            state.Phase == MaintenanceStatePhase.Syncing,
            state.Draft is null ? state.FailureKey : null,
            state.SelectedVehicleID,
            state.KindFilter,
            state.Draft is null,
        }
        .Concat(state.Vehicles.Select(vehicle => (object)$"{vehicle.Id}:{vehicle.Name}"))
        .Concat(state.VisibleRecords.Select(record => (object)$"{record.Id}:{record.Title}:{record.PerformedAt.UtcTicks}:{record.Kind}:{record.WorkItems.Count}:{record.Photos.Count}")));

    /// <summary>時系列を描きます。</summary>
    /// <remarks>責務: 見出しと「新しい記録」・同期のボタン、車両の選択、種類の絞り込み、同期の状態、記録の一覧を作ります。</remarks>
    /// <param name="state">整備記録の状態。</param>
    private void RenderTimeline(MaintenanceState state)
    {
        TimelineHeader.Children.Clear();
        var head = new Grid { ColumnSpacing = Space.S };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = Space.XXS };
        titles.Children.Add(Typography.Text(LocalizedText.Get("sidebar.maintenance"), TextStyles.CardTitle));
        titles.Children.Add(Typography.Text(LocalizedText.Get("maintenance.subtitle"), TextStyles.SecondaryCaption));
        head.Children.Add(titles);
        var create = NewRecordButton(state);
        Grid.SetColumn(create, 1);
        head.Children.Add(create);
        var sync = new Button { Content = new FontIcon { Glyph = FluentSymbol.Refresh, FontSize = 14 }, IsEnabled = state.Phase != MaintenanceStatePhase.Syncing, VerticalAlignment = VerticalAlignment.Top };
        ToolTipService.SetToolTip(sync, LocalizedText.Get("maintenance.sync") + " (F5)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sync, LocalizedText.Get("maintenance.sync"));
        sync.Click += (_, _) => _model.Send(new MaintenanceAction.RefreshRequested());
        sync.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.F5 });
        sync.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        Grid.SetColumn(sync, 2);
        head.Children.Add(sync);
        TimelineHeader.Children.Add(head);

        if (state.Vehicles.Count == 0)
        {
            TimelineHeader.Children.Add(new GlyphLabel(FluentSymbol.Car, LocalizedText.Get("maintenance.vehicle.empty"), TextStyles.SecondaryCaption));
        }
        else
        {
            var vehicles = new ComboBox { Header = LocalizedText.Get("maintenance.vehicle"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var vehicle in state.Vehicles)
            {
                vehicles.Items.Add(new ComboBoxItem { Content = vehicle.Name.Length == 0 ? vehicle.DisplayIdentifier : vehicle.Name, Tag = vehicle.Id });
            }

            var selectedID = state.SelectedVehicleID ?? state.Vehicles[0].Id;
            vehicles.SelectedIndex = state.Vehicles.ToList().FindIndex(vehicle => vehicle.Id == selectedID);
            vehicles.SelectionChanged += (_, _) =>
            {
                if (!_isRendering && vehicles.SelectedItem is ComboBoxItem { Tag: VehicleID id })
                {
                    _model.Send(new MaintenanceAction.VehicleSelected(id));
                }
            };
            TimelineHeader.Children.Add(vehicles);
        }

        var filter = new SelectorBar();
        filter.Items.Add(new SelectorBarItem { Text = LocalizedText.Get("maintenance.filter.all"), Tag = null, IsSelected = state.KindFilter is null });
        foreach (var kind in MaintenanceKindRawValue.AllCases)
        {
            filter.Items.Add(new SelectorBarItem { Text = LocalizedText.Get(kind.LocalizationKey()), Tag = kind, IsSelected = state.KindFilter == kind });
        }

        filter.SelectionChanged += (_, _) =>
        {
            if (!_isRendering)
            {
                var kind = filter.SelectedItem?.Tag as MaintenanceKind?;
                DispatcherQueue.TryEnqueue(() => _model.Send(new MaintenanceAction.KindFilterChanged(kind)));
            }
        };
        TimelineHeader.Children.Add(filter);

        if (state.Phase == MaintenanceStatePhase.Syncing)
        {
            var syncing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S };
            syncing.Children.Add(new ProgressRing { IsActive = true, Width = Space.L, Height = Space.L });
            syncing.Children.Add(Typography.Text(LocalizedText.Get("maintenance.syncing"), TextStyles.SecondaryCaption));
            TimelineHeader.Children.Add(syncing);
        }
        else if (state.FailureKey is { } failureKey && state.Draft is null)
        {
            TimelineHeader.Children.Add(new GlyphLabel(FluentSymbol.Cloud, LocalizedText.Get(failureKey), TextStyles.Caption, DesignTokens.Foreground(Tone.Caution)));
        }

        RecordList.Items.Clear();
        foreach (var record in state.VisibleRecords)
        {
            RecordList.Items.Add(RecordRow(record));
        }

        EmptyHost.Content = state.VisibleRecords.Count == 0
            ? new ContentUnavailablePanel(FluentSymbol.Repair, LocalizedText.Get("maintenance.empty.title"))
            : null;
    }

    /// <summary>「新しい記録」のボタン（軽整備・重整備を選ぶメニュー）を作ります。</summary>
    /// <param name="state">整備記録の状態。</param>
    /// <returns>ボタン。</returns>
    private DropDownButton NewRecordButton(MaintenanceState state)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var kind in MaintenanceKindRawValue.AllCases)
        {
            var item = new MenuFlyoutItem
            {
                Text = LocalizedText.Get(kind.LocalizationKey()),
                Icon = new FontIcon { Glyph = KindGlyph(kind), Foreground = DesignTokens.Foreground(KindTone(kind)) },
            };
            var selected = kind;
            item.Click += (_, _) => _model.Send(new MaintenanceAction.CreateRequested(selected));
            menu.Items.Add(item);
        }

        var button = new DropDownButton
        {
            Content = new FontIcon { Glyph = FluentSymbol.Add, FontSize = 14 },
            Flyout = menu,
            IsEnabled = state.SelectedVehicleID is not null,
            VerticalAlignment = VerticalAlignment.Top,
            Style = DesignTokens.Style("AccentButtonStyle"),
        };
        ToolTipService.SetToolTip(button, LocalizedText.Get("windows.maintenance.new"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, LocalizedText.Get("windows.maintenance.new"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-maintenance-new");
        return button;
    }

    /// <summary>記録の 1 行を作ります。</summary>
    /// <param name="record">記録。</param>
    /// <returns>一覧の項目。</returns>
    private ListViewItem RecordRow(MaintenanceRecord record)
    {
        var grid = new Grid { ColumnSpacing = Space.M, Padding = new Thickness(0, Space.S, 0, Space.S) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(IconTile.Create(KindGlyph(record.Kind), KindTone(record.Kind), Metrics.IconTileSmall));
        var texts = new StackPanel { Spacing = Space.XXS };
        texts.Children.Add(Typography.Text(record.Title, TextStyles.BodyStrong, wraps: false));
        texts.Children.Add(Typography.Text(DateText.DateOnly(record.PerformedAt), TextStyles.SecondaryCaption));
        texts.Children.Add(Typography.Text(LocalizedText.Format("%lld · %lld", record.WorkItems.Count, record.Photos.Count), TextStyles.SecondaryCaption));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var id = record.Id;
        grid.Tag = id;
        var item = new ListViewItem
        {
            Content = grid,
            Tag = id,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ContextFlyout = new ContextMenu()
                .Item(FluentSymbol.Edit, LocalizedText.Get("garage.edit"), () => _model.Send(new MaintenanceAction.EditRequested(id)))
                .Separator()
                .Item(FluentSymbol.Delete, LocalizedText.Get("maintenance.delete"), () => RequestDeletion(id), Tone.Danger)
                .Build(),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, record.Title);
        return item;
    }

    /// <summary>一覧の選択を、編集中の記録に合わせます。</summary>
    /// <param name="state">整備記録の状態。</param>
    private void SelectCurrentRecord(MaintenanceState state)
    {
        var editing = state.Draft?.RecordID;
        var target = RecordList.Items.OfType<ListViewItem>().FirstOrDefault(item => editing is { } id && item.Tag is MaintenanceRecordID tag && tag == id);
        if (!ReferenceEquals(RecordList.SelectedItem, target))
        {
            RecordList.SelectedItem = target;
        }
    }

    /// <summary>記録を押したときの処理です（選び直しでなくても、押せば編集を開く）。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">押した項目。</param>
    private void OnRecordClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FrameworkElement { Tag: MaintenanceRecordID id } && _model.State.Draft?.RecordID != id)
        {
            _model.Send(new MaintenanceAction.EditRequested(id));
        }
    }

    /// <summary>キーボード（↑↓）で選び直したときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">選択の変化。</param>
    private void OnRecordSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isRendering && RecordList.SelectedItem is ListViewItem { Tag: MaintenanceRecordID id } && _model.State.Draft?.RecordID != id)
        {
            _model.Send(new MaintenanceAction.EditRequested(id));
        }
    }

    /// <summary>右側の概要（記録を選んでいないとき）を作ります。</summary>
    /// <param name="state">整備記録の状態。</param>
    /// <returns>概要。</returns>
    private PageScaffold Overview(MaintenanceState state)
    {
        var scaffold = new PageScaffold(Metrics.ReadingMaxWidth);
        var stack = scaffold.Body;
        stack.Children.Add(IconTile.Create(FluentSymbol.Repair, Tone.Accent, Metrics.IconTileHero));
        stack.Children.Add(Typography.Text(LocalizedText.Get("maintenance.overview.title"), TextStyles.PageTitle));
        stack.Children.Add(Typography.Text(LocalizedText.Get("maintenance.overview.message"), TextStyles.SecondaryBody));
        var buttons = new AdaptiveGrid { MinItemWidth = 240 };
        foreach (var kind in MaintenanceKindRawValue.AllCases)
        {
            var content = new Grid { ColumnSpacing = Space.M };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(IconTile.Create(KindGlyph(kind), KindTone(kind), Metrics.IconTileMedium));
            var texts = new StackPanel { Spacing = Space.XS };
            texts.Children.Add(Typography.Text(LocalizedText.Get(kind.LocalizationKey()), TextStyles.BodyStrong));
            texts.Children.Add(Typography.Text(LocalizedText.Get(kind == MaintenanceKind.Light ? "maintenance.kind.light.hint" : "maintenance.kind.heavy.hint"), TextStyles.SecondaryCaption));
            Grid.SetColumn(texts, 1);
            content.Children.Add(texts);
            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(Space.L),
                IsEnabled = state.SelectedVehicleID is not null,
            };
            var selected = kind;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, LocalizedText.Get(kind.LocalizationKey()));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "windows-maintenance-create-" + kind.ToRawValue());
            button.Click += (_, _) => _model.Send(new MaintenanceAction.CreateRequested(selected));
            buttons.Children.Add(button);
        }

        stack.Children.Add(buttons);
        if (state.LastSynchronizedAt is { } date)
        {
            stack.Children.Add(new GlyphLabel(FluentSymbol.Cloud, DateText.DateAndMinute(date), TextStyles.SecondaryCaption));
        }

        return scaffold;
    }

    /// <summary>整備の種類の記号です。</summary>
    /// <param name="kind">種類。</param>
    /// <returns>記号。</returns>
    public static string KindGlyph(MaintenanceKind kind) => kind == MaintenanceKind.Light ? FluentSymbol.Drop : FluentSymbol.Repair;

    /// <summary>整備の種類の色です。</summary>
    /// <param name="kind">種類。</param>
    /// <returns>色。</returns>
    public static Tone KindTone(MaintenanceKind kind) => kind == MaintenanceKind.Light ? Tone.Accent : Tone.Caution;
}
