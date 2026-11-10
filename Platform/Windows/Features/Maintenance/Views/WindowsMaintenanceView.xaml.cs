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

namespace ProjectZD8ForWindows.Platform.Windows.Features.Maintenance.Views;

/// <summary>
/// 整備記録の画面です。左に車両ごとの記録の時系列、右に概要か編集を出します（iOS 版の <c>MacOSMaintenanceView</c>）。
/// </summary>
public sealed partial class WindowsMaintenanceView : UserControl
{
    /// <summary>整備記録のモデルです。</summary>
    private readonly MaintenanceModel _model;

    /// <summary>表示中の編集画面です（同じ記録の編集中は作り直さない）。</summary>
    private WindowsMaintenanceEditorView? _editor;

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>整備記録のモデルを受け取って画面を作ります。</summary>
    /// <param name="model">整備記録のモデル。</param>
    public WindowsMaintenanceView(MaintenanceModel model)
    {
        _model = model;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            Render();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>画面を描きます。</summary>
    /// <remarks>責務: 時系列の列と、編集中なら編集画面、そうでなければ概要を出します。</remarks>
    private void Render()
    {
        _isRendering = true;
        var state = _model.State;
        RenderTimeline(state);
        if (state.Draft is { } draft)
        {
            if (_editor is null || !_editor.Shows(draft))
            {
                _editor = new WindowsMaintenanceEditorView(draft, _model);
                DetailHost.Child = _editor;
            }

            _editor.Update(state);
        }
        else
        {
            _editor = null;
            DetailHost.Child = Overview(state);
        }

        _isRendering = false;
    }

    /// <summary>時系列の列を描きます。</summary>
    /// <remarks>責務: 題、同期のボタン、車両と種類の絞り込み、同期の状態、記録の行を作ります。</remarks>
    /// <param name="state">整備記録の状態。</param>
    private void RenderTimeline(MaintenanceState state)
    {
        TimelineHeader.Children.Clear();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 3 };
        titles.Children.Add(Typography.Text(LocalizedText.Get("sidebar.maintenance"), "SubtitleTextBlockStyle"));
        titles.Children.Add(Typography.Text(LocalizedText.Get("maintenance.subtitle"), "SecondaryCaptionTextStyle"));
        head.Children.Add(titles);
        var sync = new Button { Content = new FontIcon { Glyph = FluentSymbol.Refresh, FontSize = 14 }, IsEnabled = state.Phase != MaintenanceStatePhase.Syncing, VerticalAlignment = VerticalAlignment.Top };
        ToolTipService.SetToolTip(sync, LocalizedText.Get("maintenance.sync"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sync, LocalizedText.Get("maintenance.sync"));
        sync.Click += (_, _) => _model.Send(new MaintenanceAction.RefreshRequested());
        Grid.SetColumn(sync, 1);
        head.Children.Add(sync);
        TimelineHeader.Children.Add(head);

        if (state.Vehicles.Count == 0)
        {
            TimelineHeader.Children.Add(new GlyphLabel(FluentSymbol.Car, LocalizedText.Get("maintenance.vehicle.empty"), "SecondaryCaptionTextStyle"));
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
            var syncing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            syncing.Children.Add(new ProgressRing { IsActive = true, Width = 16, Height = 16 });
            syncing.Children.Add(Typography.Text(LocalizedText.Get("maintenance.syncing"), "SecondaryCaptionTextStyle"));
            TimelineHeader.Children.Add(syncing);
        }
        else if (state.FailureKey is { } failureKey && state.Draft is null)
        {
            TimelineHeader.Children.Add(new GlyphLabel(FluentSymbol.Cloud, LocalizedText.Get(failureKey), "CaptionTextBlockStyle", DesignTokens.Foreground(Tone.Caution)));
        }

        RecordList.Children.Clear();
        foreach (var record in state.VisibleRecords)
        {
            RecordList.Children.Add(RecordRow(record));
        }

        if (state.VisibleRecords.Count == 0)
        {
            RecordList.Children.Add(new ContentUnavailablePanel(FluentSymbol.Repair, LocalizedText.Get("maintenance.empty.title")));
        }
    }

    /// <summary>記録の行を作ります。</summary>
    /// <remarks>責務: 種類の記号・題・実施日・作業と写真の数を出し、押すと編集を始めるボタンを作ります。</remarks>
    /// <param name="record">整備記録。</param>
    /// <returns>ボタン。</returns>
    private Button RecordRow(MaintenanceRecord record)
    {
        var grid = new Grid { ColumnSpacing = 11 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(IconTile.Create(KindGlyph(record.Kind), KindTone(record.Kind), 32));
        var texts = new StackPanel { Spacing = 3 };
        texts.Children.Add(Typography.Text(record.Title, "BodyStrongTextBlockStyle", wraps: false));
        texts.Children.Add(Typography.Text(DateText.DateOnly(record.PerformedAt), "SecondaryCaptionTextStyle"));
        texts.Children.Add(Typography.Text(LocalizedText.Format("%lld · %lld", record.WorkItems.Count, record.Photos.Count), "SecondaryCaptionTextStyle"));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var chevron = new FontIcon { Glyph = FluentSymbol.ChevronRight, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        var button = new Button { Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, record.Title);
        button.Click += (_, _) => _model.Send(new MaintenanceAction.EditRequested(record.Id));
        return button;
    }

    /// <summary>概要（編集していないとき）を作ります。</summary>
    /// <remarks>責務: 説明と、軽整備・重整備の記録を始めるボタン、最後に同期した日時を出します。</remarks>
    /// <param name="state">整備記録の状態。</param>
    /// <returns>表示の要素。</returns>
    private ScrollViewer Overview(MaintenanceState state)
    {
        var stack = new StackPanel { Spacing = 24, Padding = new Thickness(42), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(IconTile.Create(FluentSymbol.Repair, Tone.Accent, 72));
        stack.Children.Add(Typography.Text(LocalizedText.Get("maintenance.overview.title"), "TitleTextBlockStyle"));
        stack.Children.Add(Typography.Text(LocalizedText.Get("maintenance.overview.message"), "SecondaryBodyTextStyle"));
        var buttons = new AdaptiveGrid { MinItemWidth = 240, Spacing = 14 };
        foreach (var kind in MaintenanceKindRawValue.AllCases)
        {
            var content = new Grid { ColumnSpacing = 14 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(IconTile.Create(KindGlyph(kind), KindTone(kind), 40));
            var texts = new StackPanel { Spacing = 4 };
            texts.Children.Add(Typography.Text(LocalizedText.Get(kind.LocalizationKey()), "BodyStrongTextBlockStyle"));
            texts.Children.Add(Typography.Text(LocalizedText.Get(kind == MaintenanceKind.Light ? "maintenance.kind.light.hint" : "maintenance.kind.heavy.hint"), "SecondaryCaptionTextStyle"));
            Grid.SetColumn(texts, 1);
            content.Children.Add(texts);
            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(18),
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
            stack.Children.Add(new GlyphLabel(FluentSymbol.Cloud, DateText.DateAndMinute(date), "SecondaryCaptionTextStyle"));
        }

        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>整備の種類の記号を返します。</summary>
    /// <remarks>責務: 軽整備をしずく、重整備をレンチの記号へ変換します。</remarks>
    /// <param name="kind">整備の種類。</param>
    /// <returns>記号の文字。</returns>
    public static string KindGlyph(MaintenanceKind kind) => kind == MaintenanceKind.Light ? FluentSymbol.Drop : FluentSymbol.Repair;

    /// <summary>整備の種類の色の役割を返します。</summary>
    /// <remarks>責務: 軽整備を青、重整備をオレンジ（iOS 版と同じ）へ変換します。</remarks>
    /// <param name="kind">整備の種類。</param>
    /// <returns>色の役割。</returns>
    public static Tone KindTone(MaintenanceKind kind) => kind == MaintenanceKind.Light ? Tone.Accent : Tone.Caution;
}
