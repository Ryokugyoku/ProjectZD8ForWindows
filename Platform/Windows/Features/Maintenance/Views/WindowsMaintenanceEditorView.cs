using System;
using System.Collections.Generic;
using System.Globalization;
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
/// 整備記録の編集画面です（iOS 版の <c>MacOSMaintenanceEditorView</c>）。
/// </summary>
/// <remarks>
/// 入力のたびに下書きの変更（<see cref="MaintenanceAction.DraftChanged"/>）を送ります。
/// 入力中の欄が消えないよう、項目の増減や確認の記録など「作り」が変わったときだけ作り直します。
/// </remarks>
public sealed partial class WindowsMaintenanceEditorView : UserControl
{
    /// <summary>整備記録のモデルです。</summary>
    private readonly MaintenanceModel _model;

    /// <summary>作ったときの下書きの作りの鍵です。</summary>
    private readonly string _structureKey;

    /// <summary>失敗の表示の置き場です。</summary>
    private readonly StackPanel _failureHost = new();

    /// <summary>保存のボタンです。</summary>
    private readonly Button _saveButton;

    /// <summary>記録の削除を確かめる処理です。</summary>
    private readonly Action<MaintenanceRecordID> _requestDeletion;

    /// <summary>下書きとモデルを受け取って編集画面を作ります。</summary>
    /// <param name="draft">下書き。</param>
    /// <param name="model">整備記録のモデル。</param>
    /// <param name="requestDeletion">記録の削除を確かめる処理（確かめてから削除を送る）。</param>
    public WindowsMaintenanceEditorView(MaintenanceEditorDraft draft, MaintenanceModel model, Action<MaintenanceRecordID> requestDeletion)
    {
        _model = model;
        _requestDeletion = requestDeletion;
        _structureKey = StructureKey(draft);
        _saveButton = new Button
        {
            Content = new GlyphLabel(FluentSymbol.Accept, LocalizedText.Get("maintenance.save")),
            Style = DesignTokens.Style("AccentButtonStyle"),
            MinWidth = 96,
        };
        _saveButton.Click += (_, _) => _model.Send(new MaintenanceAction.SaveRequested());
        // Ctrl+S で保存（ボタンのツールチップに近道が出る）。
        _saveButton.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.S,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control,
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_saveButton, LocalizedText.Get("maintenance.save"));

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Toolbar(draft));
        var body = new StackPanel { Spacing = 22, Padding = new Thickness(28), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(OverviewSection(draft));
        body.Children.Add(WorkItemsSection(draft));
        body.Children.Add(PhotosSection(draft));
        if (draft.Kind == MaintenanceKind.Heavy)
        {
            body.Children.Add(FastenersSection(draft));
        }

        body.Children.Add(_failureHost);
        var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        Content = root;
    }

    /// <summary>
    /// この画面が下書きをそのまま出せるかどうかを返します。
    /// </summary>
    /// <remarks>責務: 下書きの作り（記録・種類・項目の ID・確認の記録）が作ったときと同じかを比べます。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>同じなら true。</returns>
    public bool Shows(MaintenanceEditorDraft draft) => StructureKey(draft) == _structureKey;

    /// <summary>
    /// 作りを変えずに、保存できるかどうかと失敗の表示を更新します。
    /// </summary>
    /// <remarks>責務: 題が空なら保存できないようにし、失敗のキーがあれば注意の表示を出します。</remarks>
    /// <param name="state">整備記録の状態。</param>
    public void Update(MaintenanceState state)
    {
        _saveButton.IsEnabled = state.Draft is { } draft
            && draft.Title.Trim().Length > 0
            && state.Phase != MaintenanceStatePhase.Saving;
        _failureHost.Children.Clear();
        if (state.FailureKey is { } failureKey)
        {
            _failureHost.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Message = LocalizedText.Get(failureKey) });
        }
    }

    /// <summary>上の操作の帯を作ります。</summary>
    /// <remarks>責務: 種類の記号と名前、削除・取り消し・保存のボタンを並べます。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>帯。</returns>
    private Grid Toolbar(MaintenanceEditorDraft draft)
    {
        var bar = new Grid { ColumnSpacing = 10, Padding = new Thickness(24, 12, 24, 12) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(IconTile.Create(WindowsMaintenanceView.KindGlyph(draft.Kind), WindowsMaintenanceView.KindTone(draft.Kind), 34));
        var title = Typography.Text(LocalizedText.Get(draft.Kind.LocalizationKey()), "SubtitleTextBlockStyle");
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (draft.RecordID is { } recordID)
        {
            var delete = new Button { Content = new FontIcon { Glyph = FluentSymbol.Delete, FontSize = 14, Foreground = DesignTokens.Foreground(Tone.Danger) } };
            ToolTipService.SetToolTip(delete, LocalizedText.Get("maintenance.delete"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(delete, LocalizedText.Get("maintenance.delete"));
            delete.Click += (_, _) => _requestDeletion(recordID);
            actions.Children.Add(delete);
        }

        var cancel = new Button { Content = LocalizedText.Get("maintenance.cancel") };
        cancel.Click += (_, _) => _model.Send(new MaintenanceAction.EditingCancelled());
        actions.Children.Add(cancel);
        actions.Children.Add(_saveButton);
        Grid.SetColumn(actions, 2);
        bar.Children.Add(actions);
        return bar;
    }

    /// <summary>概要の欄を作ります。</summary>
    /// <remarks>責務: 題・実施日時・走行距離・メモの入力を作り、入力のたびに下書きの変更を送ります。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>区画。</returns>
    private Border OverviewSection(MaintenanceEditorDraft draft)
    {
        var grid = new AdaptiveGrid { MinItemWidth = 260, Spacing = 14 };
        grid.Children.Add(TextInput("maintenance.field.title", draft.Title, value => Change(current => current with { Title = value })));
        var date = new CalendarDatePicker { Date = draft.PerformedAt, Width = 200 };
        var time = new TimePicker { Time = draft.PerformedAt.ToLocalTime().TimeOfDay, MinWidth = 0 };
        void SendDate()
        {
            if (date.Date is { } day)
            {
                var local = day.ToLocalTime().Date + time.Time;
                Change(current => current with { PerformedAt = new DateTimeOffset(local) });
            }
        }

        date.DateChanged += (_, _) => SendDate();
        time.TimeChanged += (_, _) => SendDate();
        var performedAt = new StackPanel { Spacing = 8 };
        performedAt.Children.Add(Typography.Text(LocalizedText.Get("maintenance.field.performed_at"), "BodyTextBlockStyle"));
        var dateTime = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dateTime.Children.Add(date);
        dateTime.Children.Add(time);
        performedAt.Children.Add(dateTime);
        grid.Children.Add(NumberInput("maintenance.field.odometer", draft.OdometerKilometers, value => Change(current => current with { OdometerKilometers = value })));
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(grid);
        stack.Children.Add(performedAt);
        stack.Children.Add(TextInput("maintenance.field.notes", draft.Notes, value => Change(current => current with { Notes = value }), multiline: true));
        return Section("maintenance.section.overview", stack);
    }

    /// <summary>作業の欄を作ります。</summary>
    /// <remarks>責務: 作業ごとの部位・作業・部品・製品・数量などの入力と、作業の追加のボタンを作ります。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>区画。</returns>
    private Border WorkItemsSection(MaintenanceEditorDraft draft)
    {
        var stack = new StackPanel { Spacing = 14 };
        foreach (var item in draft.WorkItems)
        {
            stack.Children.Add(WorkItem(item));
        }

        var add = new Button { Content = new GlyphLabel(FluentSymbol.Add, LocalizedText.Get("maintenance.add.work_item")) };
        add.Click += (_, _) => _model.Send(new MaintenanceAction.WorkItemAdded());
        stack.Children.Add(add);
        return Section("maintenance.section.work_items", stack);
    }

    /// <summary>作業 1 件の入力を作ります。</summary>
    /// <remarks>責務: 1 件の作業の各欄の入力を作り、入力のたびにその作業だけを置き換えた下書きを送ります。</remarks>
    /// <param name="item">作業。</param>
    /// <returns>表示の要素。</returns>
    private Border WorkItem(MaintenanceWorkItem item)
    {
        var id = item.Id;
        void ChangeItem(Func<MaintenanceWorkItem, MaintenanceWorkItem> transform) =>
            Change(current => current with { WorkItems = current.WorkItems.Select(work => work.Id == id ? transform(work) : work).ToList() });

        var grid = new AdaptiveGrid { MinItemWidth = 200, Spacing = 10 };
        grid.Children.Add(Picker("maintenance.field.component", MaintenanceComponentRawValue.AllCases, item.Component, value => value.LocalizationKey(), value => ChangeItem(work => work with { Component = value })));
        grid.Children.Add(Picker("maintenance.field.operation", MaintenanceOperationRawValue.AllCases, item.Operation, value => value.LocalizationKey(), value => ChangeItem(work => work with { Operation = value })));
        grid.Children.Add(TextInput("maintenance.field.part_name", item.PartName, value => ChangeItem(work => work with { PartName = value })));
        grid.Children.Add(TextInput("maintenance.field.product_name", item.ProductName, value => ChangeItem(work => work with { ProductName = value })));
        grid.Children.Add(TextInput("maintenance.field.manufacturer", item.Manufacturer, value => ChangeItem(work => work with { Manufacturer = value })));
        grid.Children.Add(TextInput("maintenance.field.specification", item.Specification, value => ChangeItem(work => work with { Specification = value })));
        grid.Children.Add(NumberInput("maintenance.field.quantity", item.Quantity, value => ChangeItem(work => work with { Quantity = value })));
        grid.Children.Add(TextInput("maintenance.field.unit", item.Unit, value => ChangeItem(work => work with { Unit = value })));
        var stack = new StackPanel { Spacing = 10 };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var remove = RemoveButton(() => _model.Send(new MaintenanceAction.WorkItemRemoved(id)));
        Grid.SetColumn(remove, 1);
        head.Children.Add(remove);
        stack.Children.Add(head);
        stack.Children.Add(grid);
        stack.Children.Add(TextInput("maintenance.field.item_notes", item.Notes, value => ChangeItem(work => work with { Notes = value }), multiline: true));
        return new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Child = stack };
    }

    /// <summary>写真の欄を作ります。</summary>
    /// <remarks>責務: 写真の一覧（消すボタン付き）と、写真を足すボタンを作ります。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>区画。</returns>
    private Border PhotosSection(MaintenanceEditorDraft draft)
    {
        var stack = new StackPanel { Spacing = 12 };
        var grid = new AdaptiveGrid { MinItemWidth = 140, Spacing = 12 };
        foreach (var photo in draft.Photos)
        {
            var cell = new Grid { Height = 105, CornerRadius = new CornerRadius(12) };
            cell.Children.Add(BytesImage.Create(photo.Data, 320));
            var remove = RemoveButton(() => _model.Send(new MaintenanceAction.PhotoRemoved(photo.Id)));
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Margin = new Thickness(6);
            cell.Children.Add(remove);
            grid.Children.Add(cell);
        }

        stack.Children.Add(grid);
        var add = new Button { Content = new GlyphLabel(FluentSymbol.Photo, LocalizedText.Get("maintenance.add.photo")) };
        add.Click += async (_, _) =>
        {
            if (await ImageFilePicker.PickAsync(XamlRoot) is { } url)
            {
                _model.Send(new MaintenanceAction.PhotoSelected(url));
            }
        };
        stack.Children.Add(add);
        return Section("maintenance.section.photos", stack);
    }

    /// <summary>締結（重整備）の欄を作ります。</summary>
    /// <remarks>責務: 説明、締結のまとまりごとの入力、まとまりの追加のボタンを作ります。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>区画。</returns>
    private Border FastenersSection(MaintenanceEditorDraft draft)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Typography.Text(LocalizedText.Get("maintenance.fastener.guidance"), "SecondaryCaptionTextStyle"));
        foreach (var group in draft.FastenerGroups)
        {
            stack.Children.Add(FastenerGroup(group, draft.Photos.Count > 0));
        }

        var add = new Button { Content = new GlyphLabel(FluentSymbol.Add, LocalizedText.Get("maintenance.add.fastener_group")) };
        add.Click += (_, _) => _model.Send(new MaintenanceAction.FastenerGroupAdded());
        stack.Children.Add(add);
        return Section("maintenance.section.fasteners", stack);
    }

    /// <summary>締結のまとまり 1 件の入力を作ります。</summary>
    /// <remarks>責務: まとまりの名前・場所・本数・取り外しのメモと、締結ごとの記録の入力を作ります。</remarks>
    /// <param name="group">締結のまとまり。</param>
    /// <param name="hasPhotos">写真があれば true（写真の紐付けを出す）。</param>
    /// <returns>表示の要素。</returns>
    private Border FastenerGroup(MaintenanceFastenerGroup group, bool hasPhotos)
    {
        var id = group.Id;
        void ChangeGroup(Func<MaintenanceFastenerGroup, MaintenanceFastenerGroup> transform) =>
            Change(current => current with { FastenerGroups = current.FastenerGroups.Select(item => item.Id == id ? transform(item) : item).ToList() });

        var grid = new AdaptiveGrid { MinItemWidth = 180, Spacing = 10 };
        grid.Children.Add(TextInput("maintenance.field.fastener_group", group.Name, value => ChangeGroup(item => item with { Name = value })));
        grid.Children.Add(TextInput("maintenance.field.location", group.Location, value => ChangeGroup(item => item with { Location = value })));
        grid.Children.Add(NumberInput("maintenance.field.expected_count", group.ExpectedCount, value => ChangeGroup(item => item with { ExpectedCount = value is { } count ? (int)count : null })));
        grid.Children.Add(NumberInput("maintenance.field.removed_count", group.RemovedCount, value => ChangeGroup(item => item with { RemovedCount = (int)(value ?? 0) })));
        var stack = new StackPanel { Spacing = 10 };
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var remove = RemoveButton(() => _model.Send(new MaintenanceAction.FastenerGroupRemoved(id)));
        Grid.SetColumn(remove, 1);
        head.Children.Add(remove);
        stack.Children.Add(head);
        stack.Children.Add(grid);
        stack.Children.Add(TextInput("maintenance.field.removal_notes", group.RemovalNotes, value => ChangeGroup(item => item with { RemovalNotes = value }), multiline: true));
        foreach (var evidence in group.Installations)
        {
            stack.Children.Add(FastenerEvidence(group.Id, evidence, hasPhotos, ChangeGroup));
        }

        var add = new Button { Content = new GlyphLabel(FluentSymbol.Add, LocalizedText.Get("maintenance.add.fastener")) };
        add.Click += (_, _) => _model.Send(new MaintenanceAction.FastenerAdded(id));
        stack.Children.Add(add);
        return new Border { Background = DesignTokens.SubtleBackground(Tone.Caution), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = stack };
    }

    /// <summary>締結 1 件の記録の入力を作ります。</summary>
    /// <remarks>責務: 位置・トルク・工具・作業者の入力と、締結の確認・写真の紐付けのボタン、確認日時を作ります。</remarks>
    /// <returns>表示の要素。</returns>
    private Border FastenerEvidence(
        Guid groupID,
        FastenerInstallationEvidence evidence,
        bool hasPhotos,
        Action<Func<MaintenanceFastenerGroup, MaintenanceFastenerGroup>> changeGroup)
    {
        var id = evidence.Id;
        void ChangeEvidence(Func<FastenerInstallationEvidence, FastenerInstallationEvidence> transform) =>
            changeGroup(group => group with { Installations = group.Installations.Select(item => item.Id == id ? transform(item) : item).ToList() });

        var grid = new AdaptiveGrid { MinItemWidth = 160, Spacing = 10 };
        grid.Children.Add(TextInput("maintenance.field.fastener_position", evidence.Position, value => ChangeEvidence(item => item with { Position = value })));
        grid.Children.Add(NumberInput("maintenance.field.torque", evidence.TorqueNewtonMeters, value => ChangeEvidence(item => item with { TorqueNewtonMeters = value })));
        grid.Children.Add(TextInput("maintenance.field.tool", evidence.Tool, value => ChangeEvidence(item => item with { Tool = value })));
        grid.Children.Add(TextInput("maintenance.field.technician", evidence.TightenedBy, value => ChangeEvidence(item => item with { TightenedBy = value })));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var confirm = new Button { Content = LocalizedText.Get("maintenance.fastener.confirm") };
        confirm.Click += (_, _) => _model.Send(new MaintenanceAction.FastenerConfirmed(groupID, id));
        actions.Children.Add(confirm);
        if (hasPhotos)
        {
            var link = new Button { Content = LocalizedText.Get("maintenance.fastener.link_photo") };
            link.Click += (_, _) => _model.Send(new MaintenanceAction.LatestPhotoLinked(groupID, id));
            actions.Children.Add(link);
        }

        if (evidence.TightenedAt is { } tightenedAt)
        {
            actions.Children.Add(new GlyphLabel(FluentSymbol.Completed, DateText.DateAndMinute(tightenedAt), "CaptionTextBlockStyle", DesignTokens.Foreground(Tone.Success)));
        }

        if (evidence.PhotoIDs.Count > 0)
        {
            actions.Children.Add(new GlyphLabel(FluentSymbol.Photo, evidence.PhotoIDs.Count.ToString(CultureInfo.CurrentCulture), "SecondaryCaptionTextStyle"));
        }

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(grid);
        stack.Children.Add(actions);
        return new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(10), Child = stack };
    }

    /// <summary>下書きの変更を送ります。</summary>
    /// <remarks>責務: いまの状態の下書きへ変換を当て、下書きの変更として送ります。</remarks>
    /// <param name="transform">下書きの変換。</param>
    private void Change(Func<MaintenanceEditorDraft, MaintenanceEditorDraft> transform)
    {
        if (_model.State.Draft is { } current)
        {
            _model.Send(new MaintenanceAction.DraftChanged(transform(current)));
        }
    }

    /// <summary>見出し付きの区画を作ります。</summary>
    /// <remarks>責務: 題の下に中身を置いたカードを作ります。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="content">中身。</param>
    /// <returns>カード。</returns>
    private static Border Section(string titleKey, UIElement content)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Typography.Text(LocalizedText.Get(titleKey), "CardTitleTextStyle"));
        stack.Children.Add(content);
        return SurfaceCard.Create(stack, padding: 18);
    }

    /// <summary>文字の入力欄を作ります。</summary>
    /// <remarks>責務: 見出し付きの入力欄を作り、変更のたびに値を渡します。</remarks>
    /// <param name="titleKey">見出しのキー（空のときの案内にも使う）。</param>
    /// <param name="value">初期値。</param>
    /// <param name="changed">値の変更を受け取る処理。</param>
    /// <param name="multiline">複数行なら true。</param>
    /// <returns>入力欄。</returns>
    private static TextBox TextInput(string titleKey, string value, Action<string> changed, bool multiline = false)
    {
        var box = new TextBox
        {
            Header = LocalizedText.Get(titleKey),
            PlaceholderText = LocalizedText.Get(titleKey),
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 80 : 0,
        };
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    /// <summary>数の入力欄を作ります。</summary>
    /// <remarks>責務: 見出し付きの数の入力欄を作り、数として読めた値（空なら null）を渡します。</remarks>
    /// <param name="titleKey">見出しのキー。</param>
    /// <param name="value">初期値。</param>
    /// <param name="changed">値の変更を受け取る処理。</param>
    /// <returns>入力欄。</returns>
    private static NumberBox NumberInput(string titleKey, double? value, Action<double?> changed)
    {
        var box = new NumberBox
        {
            Header = LocalizedText.Get(titleKey),
            Value = value ?? double.NaN,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.ValueChanged += (_, args) => changed(double.IsNaN(args.NewValue) ? null : args.NewValue);
        return box;
    }

    /// <summary>選択肢の入力欄を作ります。</summary>
    /// <remarks>責務: 選択肢の一覧から 1 つを選ぶ入力欄を作り、選んだ値を渡します。</remarks>
    /// <typeparam name="T">選択肢の型。</typeparam>
    /// <returns>入力欄。</returns>
    private static ComboBox Picker<T>(string titleKey, IReadOnlyList<T> cases, T value, Func<T, string> key, Action<T> changed)
        where T : struct, Enum
    {
        var box = new ComboBox { Header = LocalizedText.Get(titleKey), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in cases)
        {
            box.Items.Add(new ComboBoxItem { Content = LocalizedText.Get(key(item)), Tag = item });
        }

        box.SelectedIndex = cases.ToList().IndexOf(value);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: T selected })
            {
                changed(selected);
            }
        };
        return box;
    }

    /// <summary>消すボタンを作ります。</summary>
    /// <remarks>責務: 赤のごみ箱の記号のボタンを作ります。</remarks>
    /// <param name="remove">押したときの処理。</param>
    /// <returns>ボタン。</returns>
    private static Button RemoveButton(Action remove)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = FluentSymbol.Delete, FontSize = 13, Foreground = DesignTokens.Foreground(Tone.Danger) },
            Padding = new Thickness(8),
        };
        button.Click += (_, _) => remove();
        return button;
    }

    /// <summary>下書きの作りの鍵を返します。</summary>
    /// <remarks>責務: 作り直しが必要な違い（記録・種類・項目の ID・確認の記録・写真の紐付け）を 1 つの文字列にします。</remarks>
    /// <param name="draft">下書き。</param>
    /// <returns>鍵。</returns>
    private static string StructureKey(MaintenanceEditorDraft draft) => string.Join(
        "|",
        new[] { draft.RecordID?.ToString() ?? "new", draft.Kind.ToString(), draft.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture), draft.VehicleID.ToString() }
            .Concat(draft.WorkItems.Select(item => item.Id.ToString()))
            .Concat(draft.Photos.Select(photo => photo.Id.ToString()))
            .Concat(draft.FastenerGroups.SelectMany(group => new[] { group.Id.ToString() }
                .Concat(group.Installations.Select(item => $"{item.Id}:{item.TightenedAt?.UtcTicks}:{item.PhotoIDs.Count}")))));
}
