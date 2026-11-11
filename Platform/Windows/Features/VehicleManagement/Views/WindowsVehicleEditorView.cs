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
using ProjectZD8ForWindows.Platform.Windows.Logging;

namespace ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Views;

/// <summary>
/// 車両の登録・編集の画面です（iOS 版の <c>MacOSVehicleEditorView</c>）。
/// </summary>
/// <remarks>
/// 入力中の値は画面の一時的な下書きとして持ち、保存のときに <see cref="VehicleManagementAction.VehicleSaved"/> で送ります。
/// モデルの編集中の車両が変わったら（写真を選んだときなど）、変わった写真だけを下書きへ取り込み、入力中の名前などは残します
/// （<see cref="ApplyExternalChange"/>。以前は下書きごと置き換えていたので、写真を選ぶと入力が消えていた）。
/// 保存は Ctrl+S でもできます。
/// </remarks>
public sealed partial class WindowsVehicleEditorView : UserControl
{
    /// <summary>操作の送り先です。</summary>
    private readonly Action<VehicleManagementAction> _send;

    /// <summary>モデルの編集中の車両です。</summary>
    private VehicleProfile _vehicle;

    /// <summary>写真の枠です。</summary>
    private readonly Grid _photoFrame = new() { CornerRadius = new CornerRadius(Radius.L), Background = DesignTokens.Brush("RevTorqueQuietFillBrush") };

    /// <summary>画面の一時的な状態：入力中の下書きです。</summary>
    private VehicleProfile _draft;

    /// <summary>保存のボタンです。</summary>
    private readonly Button _saveButton;

    /// <summary>保存のボタンの補足です（狭いときは省く）。</summary>
    private readonly TextBlock _saveHint;

    /// <summary>写真の下の名前の文字です。</summary>
    private readonly TextBlock _photoTitle;

    /// <summary>写真の下の識別子の文字です。</summary>
    private readonly TextBlock _photoIdentifier;

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
        _saveButton = new Button { Style = DesignTokens.Style("AccentButtonStyle"), MinHeight = Metrics.PrimaryControlHeight };
        var saveContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M };
        saveContent.Children.Add(new FontIcon { Glyph = FluentSymbol.Accept, FontSize = 14 });
        var saveTexts = new StackPanel();
        saveTexts.Children.Add(Typography.Strong(LocalizedText.Get("garage.save"), TextStyles.BodyStrong));
        _saveHint = Typography.Text(LocalizedText.Get("garage.editor.save_hint"), TextStyles.Caption);
        saveTexts.Children.Add(_saveHint);
        saveContent.Children.Add(saveTexts);
        _saveButton.Content = saveContent;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_saveButton, LocalizedText.Get("garage.save"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_saveButton, "windows-vehicle-editor-save");
        _saveButton.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = global::Windows.System.VirtualKey.S,
            Modifiers = global::Windows.System.VirtualKeyModifiers.Control,
        });
        _saveButton.Click += (_, _) =>
        {
            UiLog.VehicleSaveRequested(UiLog.Logger);
            _send(new VehicleManagementAction.VehicleSaved(_draft));
        };
        _photoTitle = Typography.Text(string.Empty, TextStyles.CardTitle, wraps: false);
        _photoTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _photoIdentifier = Typography.Text(_draft.DisplayIdentifier, TextStyles.Monospace, wraps: false);
        _photoIdentifier.TextTrimming = TextTrimming.CharacterEllipsis;

        var scaffold = new PageScaffold(1100);
        scaffold.Body.Children.Add(Header());
        scaffold.Body.Children.Add(ResponsiveEditor());
        Content = scaffold;
        ResponsiveLayout.Bind(this, widthClass =>
        {
            var compact = widthClass == WidthClass.Compact;
            Retained.SetVisible(_saveHint, !compact);
            _photoFrame.Height = compact ? 200 : 260;
        });
        UpdateDerived();
    }

    /// <summary>この画面が編集している車両です。</summary>
    public VehicleProfile Vehicle => _vehicle;

    /// <summary>見出しの行を作ります。</summary>
    /// <remarks>責務: 戻る（取り消し）ボタン、題、保存のボタンを並べます（保存は右端、Ctrl+S でも押せる）。</remarks>
    /// <returns>行。</returns>
    private Grid Header()
    {
        var grid = new Grid { ColumnSpacing = Space.M + Space.XS };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = new Button { Content = new FontIcon { Glyph = FluentSymbol.Back, FontSize = 14 }, Width = Metrics.IconTileMedium, Height = Metrics.IconTileMedium, CornerRadius = new CornerRadius(Radius.Pill) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(back, LocalizedText.Get("garage.cancel"));
        ToolTipService.SetToolTip(back, LocalizedText.Get("garage.cancel"));
        back.Click += (_, _) => _send(new VehicleManagementAction.EditCancelled());
        grid.Children.Add(back);
        var titles = new StackPanel { Spacing = Space.XXS, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Typography.Text(LocalizedText.Get("garage.editor.studio"), TextStyles.Eyebrow));
        titles.Children.Add(Typography.Text(LocalizedText.Get("garage.editor.title"), TextStyles.PageTitle));
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);
        Grid.SetColumn(_saveButton, 2);
        grid.Children.Add(_saveButton);
        return grid;
    }

    /// <summary>写真と入力の部分を、幅クラスに合わせて左右か縦積みにします。</summary>
    /// <remarks>責務: 副＝写真（320）を左、主＝入力のカードを右に置き、Medium 以下は写真を上に積みます。</remarks>
    /// <returns>表示の要素。</returns>
    private AdaptiveSplit ResponsiveEditor() => new()
    {
        Primary = EditorPanels(),
        Secondary = PhotoPanel(),
        SecondaryFirst = true,
        SecondaryWidth = 320,
        CollapseBelow = WidthClass.Expanded,
        CollapseMode = SplitCollapseMode.Stack,
    };

    /// <summary>写真の列を作ります。</summary>
    /// <remarks>責務: 車両の写真（なければ背景と車の記号）と名前・識別子、写真を選ぶボタンを作ります。</remarks>
    /// <returns>表示の要素。</returns>
    private StackPanel PhotoPanel()
    {
        RenderPhotoFrame();
        var choose = new Button { Content = new GlyphLabel(FluentSymbol.Camera, LocalizedText.Get("garage.photo.choose")), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = Metrics.ControlHeight };
        choose.Click += async (_, _) =>
        {
            if (await ImageFilePicker.PickAsync(XamlRoot) is { } url)
            {
                _send(new VehicleManagementAction.PhotoSelected(url));
            }
        };
        var caption = new StackPanel { Spacing = Space.XXS };
        caption.Children.Add(_photoTitle);
        caption.Children.Add(_photoIdentifier);
        var stack = new StackPanel { Spacing = Space.M };
        stack.Children.Add(_photoFrame);
        stack.Children.Add(caption);
        stack.Children.Add(choose);
        return stack;
    }

    /// <summary>
    /// モデル側で車両が変わったとき（写真を選んだときなど）に、入力中の内容を残したまま取り込みます。
    /// </summary>
    /// <remarks>責務: 写真だけを下書きへ写して描き直します。名前などの入力中の欄は作り直しません。</remarks>
    /// <param name="vehicle">モデル側の最新の車両。</param>
    public void ApplyExternalChange(VehicleProfile vehicle)
    {
        _vehicle = vehicle;
        _draft = _draft with { PhotoData = vehicle.PhotoData };
        RenderPhotoFrame();
    }

    /// <summary>写真の枠を描きます。</summary>
    /// <remarks>責務: 下書きの写真（なければ背景と車の記号）を枠に入れます。名前・識別子は枠の下の文字（作り直さない）。</remarks>
    private void RenderPhotoFrame()
    {
        var frame = _photoFrame;
        frame.Children.Clear();
        if (_draft.PhotoData is { } data)
        {
            frame.Background = DesignTokens.Brush("RevTorqueQuietFillBrush");
            frame.Children.Add(BytesImage.Create(data, 800));
        }
        else
        {
            frame.Background = DesignTokens.Brush("RevTorqueHeroBrush");
            frame.Children.Add(new FontIcon { Glyph = FluentSymbol.Car, FontSize = Metrics.IconTileHero, Foreground = DesignTokens.Foreground(Tone.Accent) });
        }
    }

    /// <summary>入力のカードの列を作ります。</summary>
    /// <remarks>責務: 識別、動力と燃料、その他（年式・タンク容量・メモ）のカードを作ります。</remarks>
    /// <returns>表示の要素。</returns>
    private StackPanel EditorPanels()
    {
        var stack = new StackPanel { Spacing = Space.L };
        var identity = new AdaptiveGrid { MinItemWidth = 240, Spacing = Space.M };
        identity.Children.Add(TextInput("garage.field.name", _draft.Name, value => _draft = _draft with { Name = value }));
        identity.Children.Add(TextInput("garage.field.manufacturer", _draft.Manufacturer, value => _draft = _draft with { Manufacturer = value }));
        identity.Children.Add(TextInput("garage.field.engine_model", _draft.EngineModel, value => _draft = _draft with { EngineModel = value }));
        var identifier = new TextBox
        {
            Header = LocalizedText.Get(_draft.Vin.Length == 0 ? "garage.field.obd_identifier" : "garage.field.vin"),
            Text = _draft.DisplayIdentifier,
            IsReadOnly = true,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
        };
        identity.Children.Add(identifier);
        stack.Children.Add(EditorCard("garage.section.identity", FluentSymbol.Person, identity));

        var energy = new AdaptiveGrid { MinItemWidth = 240, Spacing = Space.M };
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

        var other = new StackPanel { Spacing = Space.M };
        var numbers = new AdaptiveGrid { MinItemWidth = 240, Spacing = Space.M };
        numbers.Children.Add(NumberInput("garage.field.model_year", _draft.ModelYear, value => _draft = _draft with { ModelYear = value is { } year ? (int)year : null }));
        numbers.Children.Add(NumberInput("garage.field.tank", _draft.TankCapacityLiters, value => _draft = _draft with { TankCapacityLiters = value }));
        other.Children.Add(numbers);
        var note = TextInput("garage.field.note", _draft.Note, value => _draft = _draft with { Note = value });
        note.AcceptsReturn = true;
        note.TextWrapping = TextWrapping.Wrap;
        note.MinHeight = Metrics.RowMinHeight * 1.5;
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
        var stack = new StackPanel { Spacing = Space.M };
        stack.Children.Add(new GlyphLabel(glyph, LocalizedText.Get(titleKey), TextStyles.BodyStrong));
        stack.Children.Add(content);
        return SurfaceCard.Create(stack, padding: Space.L);
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
