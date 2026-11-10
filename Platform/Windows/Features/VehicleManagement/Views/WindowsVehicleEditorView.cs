using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using Windows.UI;

namespace ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Views;

/// <summary>
/// 車両の登録・編集の画面です（iOS 版の <c>MacOSVehicleEditorView</c>）。
/// </summary>
/// <remarks>
/// 入力中の値は画面の一時的な下書きとして持ち、保存のときに <see cref="VehicleManagementAction.VehicleSaved"/> で送ります。
/// モデルの編集中の車両が変わったら（写真を選んだときなど）、iOS 版と同じく下書きをそれに置き換えます。
/// </remarks>
public sealed partial class WindowsVehicleEditorView : UserControl
{
    /// <summary>操作の送り先です。</summary>
    private readonly Action<VehicleManagementAction> _send;

    /// <summary>モデルの編集中の車両です。</summary>
    private readonly VehicleProfile _vehicle;

    /// <summary>画面の一時的な状態：入力中の下書きです。</summary>
    private VehicleProfile _draft;

    /// <summary>保存のボタンです。</summary>
    private readonly Button _saveButton;

    /// <summary>写真の上の名前の文字です。</summary>
    private readonly TextBlock _photoTitle;

    /// <summary>燃料の選択です（動力の種類で選択肢が変わる）。</summary>
    private readonly ComboBox _energyPicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    /// <summary>描いている最中かどうかです（描画中のイベントを無視するため）。</summary>
    private bool _isRendering;

    /// <summary>編集中の車両と操作の送り先を受け取って編集画面を作ります。</summary>
    /// <param name="vehicle">編集中の車両。</param>
    /// <param name="send">車両管理の操作の送り先。</param>
    public WindowsVehicleEditorView(VehicleProfile vehicle, Action<VehicleManagementAction> send)
    {
        _vehicle = vehicle;
        _send = send;
        _draft = vehicle with
        {
            EnergySources = VehicleEnergySourcePolicy.SourcesForEditing(vehicle.EnergySources, vehicle.Powertrain),
        };
        _saveButton = new Button { Style = DesignTokens.Style("AccentButtonStyle"), MinHeight = 44 };
        var saveContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        saveContent.Children.Add(new FontIcon { Glyph = FluentSymbol.Accept, FontSize = 14 });
        var saveTexts = new StackPanel();
        saveTexts.Children.Add(Typography.Strong(LocalizedText.Get("garage.save"), "BodyStrongTextBlockStyle"));
        saveTexts.Children.Add(Typography.Text(LocalizedText.Get("garage.editor.save_hint"), "CaptionTextBlockStyle"));
        saveContent.Children.Add(saveTexts);
        _saveButton.Content = saveContent;
        _saveButton.Click += (_, _) => _send(new VehicleManagementAction.VehicleSaved(_draft));
        _photoTitle = Typography.Text(string.Empty, "SubtitleTextBlockStyle", new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)));

        var page = new StackPanel { Spacing = 24, Padding = new Thickness(30), MaxWidth = 1150 };
        page.Children.Add(Header());
        page.Children.Add(ResponsiveEditor());
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        UpdateDerived();
    }

    /// <summary>この画面が編集している車両です。</summary>
    public VehicleProfile Vehicle => _vehicle;

    /// <summary>見出しの行を作ります。</summary>
    /// <remarks>責務: 戻る（取り消し）ボタン、題、保存のボタンを並べます。</remarks>
    /// <returns>行。</returns>
    private Grid Header()
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = new Button { Content = new FontIcon { Glyph = FluentSymbol.Back, FontSize = 14 }, Width = 42, Height = 42, CornerRadius = new CornerRadius(21) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back, LocalizedText.Get("garage.cancel"));
        ToolTipService.SetToolTip(back, LocalizedText.Get("garage.cancel"));
        back.Click += (_, _) => _send(new VehicleManagementAction.EditCancelled());
        grid.Children.Add(back);
        var titles = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Typography.Text(LocalizedText.Get("garage.editor.studio"), "EyebrowTextStyle"));
        titles.Children.Add(Typography.Text(LocalizedText.Get("garage.editor.title"), "PageTitleTextStyle"));
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);
        Grid.SetColumn(_saveButton, 2);
        grid.Children.Add(_saveButton);
        return grid;
    }

    /// <summary>写真と入力の部分を、幅に合わせて横並びか縦積みにします。</summary>
    /// <remarks>責務: 写真の列（固定幅）と入力のカードの列を、狭いときは縦に積みます。</remarks>
    /// <returns>表示の要素。</returns>
    private Grid ResponsiveEditor()
    {
        var grid = new Grid { ColumnSpacing = 22, RowSpacing = 20 };
        var photo = PhotoPanel();
        var panels = EditorPanels();
        grid.Children.Add(photo);
        grid.Children.Add(panels);
        grid.SizeChanged += (_, args) =>
        {
            var wide = args.NewSize.Width >= 760;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            if (wide)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(panels, 1);
                Grid.SetRow(panels, 0);
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(panels, 0);
                Grid.SetRow(panels, 1);
            }
        };
        return grid;
    }

    /// <summary>写真の列を作ります。</summary>
    /// <remarks>責務: 車両の写真（なければグラデーションと車の記号）と名前・識別子、写真を選ぶボタンを作ります。</remarks>
    /// <returns>表示の要素。</returns>
    private StackPanel PhotoPanel()
    {
        var frame = new Grid { Height = 360, CornerRadius = new CornerRadius(20) };
        if (_draft.PhotoData is { } data)
        {
            frame.Children.Add(BytesImage.Create(data, 800));
        }
        else
        {
            var gradient = new LinearGradientBrush { StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(1, 1) };
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(235, 75, 73, 201), Offset = 0 });
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(158, 10, 99, 214), Offset = 0.5 });
            gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(235, 0, 0, 0), Offset = 1 });
            frame.Background = gradient;
            frame.Children.Add(new FontIcon { Glyph = FluentSymbol.Car, FontSize = 64, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) });
        }

        var shade = new LinearGradientBrush { StartPoint = new global::Windows.Foundation.Point(0, 0.5), EndPoint = new global::Windows.Foundation.Point(0, 1) };
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(209, 0, 0, 0), Offset = 1 });
        frame.Children.Add(new Border { Background = shade });
        var caption = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(18) };
        caption.Children.Add(_photoTitle);
        caption.Children.Add(Typography.Text(_draft.DisplayIdentifier, "MonospaceCaptionTextStyle", new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)), wraps: false));
        frame.Children.Add(caption);
        var choose = new Button { Content = new GlyphLabel(FluentSymbol.Camera, LocalizedText.Get("garage.photo.choose")), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40 };
        choose.Click += async (_, _) =>
        {
            if (await ImageFilePicker.PickAsync(XamlRoot) is { } url)
            {
                _send(new VehicleManagementAction.PhotoSelected(url));
            }
        };
        var stack = new StackPanel { Spacing = 15 };
        stack.Children.Add(frame);
        stack.Children.Add(choose);
        return stack;
    }

    /// <summary>入力のカードの列を作ります。</summary>
    /// <remarks>責務: 識別、動力と燃料、その他（年式・タンク容量・メモ）のカードを作ります。</remarks>
    /// <returns>表示の要素。</returns>
    private StackPanel EditorPanels()
    {
        var stack = new StackPanel { Spacing = 16 };
        var identity = new AdaptiveGrid { MinItemWidth = 210, Spacing = 13 };
        identity.Children.Add(TextInput("garage.field.name", _draft.Name, value => _draft = _draft with { Name = value }));
        identity.Children.Add(TextInput("garage.field.manufacturer", _draft.Manufacturer, value => _draft = _draft with { Manufacturer = value }));
        identity.Children.Add(TextInput("garage.field.engine_model", _draft.EngineModel, value => _draft = _draft with { EngineModel = value }));
        var identifier = new TextBox
        {
            Header = LocalizedText.Get(_draft.Vin.Length == 0 ? "garage.field.obd_identifier" : "garage.field.vin"),
            Text = _draft.DisplayIdentifier,
            IsReadOnly = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        };
        identity.Children.Add(identifier);
        stack.Children.Add(EditorCard("garage.section.identity", FluentSymbol.Person, identity));

        var energy = new AdaptiveGrid { MinItemWidth = 210, Spacing = 13 };
        var powertrain = new ComboBox { Header = LocalizedText.Get("garage.field.powertrain"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var kind in Enum.GetValues<VehiclePowertrainKind>())
        {
            powertrain.Items.Add(new ComboBoxItem { Content = LocalizedText.Get("garage.powertrain." + kind.ToRawValue()), Tag = kind });
        }

        powertrain.SelectedIndex = Array.IndexOf(Enum.GetValues<VehiclePowertrainKind>(), _draft.Powertrain);
        powertrain.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && powertrain.SelectedItem is ComboBoxItem { Tag: VehiclePowertrainKind kind })
            {
                _draft = _draft with
                {
                    Powertrain = kind,
                    EnergySources = VehicleEnergySourcePolicy.NormalizedSources(_draft.EnergySources, kind),
                };
                UpdateEnergyPicker();
            }
        };
        energy.Children.Add(powertrain);
        _energyPicker.Header = LocalizedText.Get("garage.field.energy");
        _energyPicker.SelectionChanged += (_, _) =>
        {
            if (!_isRendering && _energyPicker.SelectedItem is ComboBoxItem { Tag: VehicleEnergySource source })
            {
                _draft = _draft with { EnergySources = [source] };
            }
        };
        energy.Children.Add(_energyPicker);
        UpdateEnergyPicker();
        stack.Children.Add(EditorCard("garage.section.energy", FluentSymbol.Lightning, energy));

        var other = new StackPanel { Spacing = 13 };
        var numbers = new AdaptiveGrid { MinItemWidth = 210, Spacing = 13 };
        numbers.Children.Add(NumberInput("garage.field.model_year", _draft.ModelYear, value => _draft = _draft with { ModelYear = value is { } year ? (int)year : null }));
        numbers.Children.Add(NumberInput("garage.field.tank", _draft.TankCapacityLiters, value => _draft = _draft with { TankCapacityLiters = value }));
        other.Children.Add(numbers);
        var note = TextInput("garage.field.note", _draft.Note, value => _draft = _draft with { Note = value });
        note.AcceptsReturn = true;
        note.TextWrapping = TextWrapping.Wrap;
        note.MinHeight = 90;
        other.Children.Add(note);
        stack.Children.Add(EditorCard("garage.section.other", FluentSymbol.Gauge, other));
        return stack;
    }

    /// <summary>燃料の選択肢を、今の動力の種類に合わせて作り直します。</summary>
    /// <remarks>責務: 動力の種類で選べる燃料だけを選択肢にし、今の燃料を選んだ状態にします。</remarks>
    private void UpdateEnergyPicker()
    {
        _isRendering = true;
        var sources = VehicleEnergySourcePolicy.AvailableSources(_draft.Powertrain);
        _energyPicker.Items.Clear();
        foreach (var source in sources)
        {
            _energyPicker.Items.Add(new ComboBoxItem { Content = LocalizedText.Get("garage.energy." + source.ToRawValue()), Tag = source });
        }

        var selected = _draft.EnergySources.FirstOrDefault();
        var index = sources.ToList().IndexOf(selected);
        _energyPicker.SelectedIndex = _draft.EnergySources.Count > 0 && index >= 0 ? index : (sources.Count > 0 ? 0 : -1);
        _isRendering = false;
    }

    /// <summary>入力から決まる表示（保存できるか、写真の上の名前）を更新します。</summary>
    /// <remarks>責務: 名前が空なら保存できないようにし、写真の上の名前を下書きに合わせます。</remarks>
    private void UpdateDerived()
    {
        var hasName = _draft.Name.Trim().Length > 0;
        _saveButton.IsEnabled = hasName;
        _photoTitle.Text = _draft.Name.Length == 0 ? LocalizedText.Get("garage.editor.title") : _draft.Name;
    }

    /// <summary>入力のカードを作ります。</summary>
    /// <remarks>責務: 記号付きの題の下に入力を置いたカードを作ります。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="glyph">記号。</param>
    /// <param name="content">中身。</param>
    /// <returns>カード。</returns>
    private static Border EditorCard(string titleKey, string glyph, UIElement content)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new GlyphLabel(glyph, LocalizedText.Get(titleKey), "BodyStrongTextBlockStyle"));
        stack.Children.Add(content);
        return SurfaceCard.Create(stack, padding: 17);
    }

    /// <summary>文字の入力欄を作ります。</summary>
    /// <remarks>責務: 見出し付きの入力欄を作り、変更のたびに下書きを更新します。</remarks>
    /// <param name="titleKey">見出しのキー。</param>
    /// <param name="value">初期値。</param>
    /// <param name="changed">下書きを更新する処理。</param>
    /// <returns>入力欄。</returns>
    private TextBox TextInput(string titleKey, string value, Action<string> changed)
    {
        var box = new TextBox { Header = LocalizedText.Get(titleKey), PlaceholderText = LocalizedText.Get(titleKey), Text = value };
        box.TextChanged += (_, _) =>
        {
            changed(box.Text);
            UpdateDerived();
        };
        return box;
    }

    /// <summary>数の入力欄を作ります。</summary>
    /// <remarks>責務: 見出し付きの数の入力欄を作り、数として読めた値（空なら null）で下書きを更新します。</remarks>
    /// <param name="titleKey">見出しのキー。</param>
    /// <param name="value">初期値。</param>
    /// <param name="changed">下書きを更新する処理。</param>
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
}
