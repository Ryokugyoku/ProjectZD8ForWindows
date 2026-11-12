using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;

namespace ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Views;

/// <summary>
/// ガレージ画面です。登録車両の一覧、接続時の車両の確認、車両の編集、PID の収集設定を扱います（iOS 版の <c>MacOSGarageView</c>）。
/// </summary>
public sealed partial class WindowsGarageView : UserControl
{
    /// <summary>車両管理のモデルです。</summary>
    private readonly VehicleManagementModel _model;

    /// <summary>PID の収集設定のダイアログです。</summary>
    private readonly WindowsVehiclePIDSettingsDialog _pidSettings;

    /// <summary>車両の削除の確認です。</summary>
    private readonly StateBoundDialog _deletionDialog = new();

    /// <summary>ページの置き場です。</summary>
    private readonly Border _host = new();

    /// <summary>画面の一時的な状態：削除を確かめている車両です。</summary>
    private VehicleProfile? _deletionCandidate;

    /// <summary>表示中の編集画面です（同じ車両の間は作り直さない）。</summary>
    private WindowsVehicleEditorView? _editor;

    /// <summary>操作中の作り直しを待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>最後に描いた一覧の鍵です（PID の設定など、一覧に関係のない変化で作り直さないため）。</summary>
    private string? _catalogKey;

    /// <summary>表示中の車両の確認です。</summary>
    private VehicleIdentificationSnapshot? _shownSnapshot;

    /// <summary>車両管理のモデルを受け取ってガレージ画面を作ります。</summary>
    /// <param name="model">車両管理のモデル。</param>
    public WindowsGarageView(VehicleManagementModel model)
    {
        _model = model;
        _pidSettings = new WindowsVehiclePIDSettingsDialog(model.Send);
        Content = _host;
        _gate = new RenderGate(this);
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            Render();
            _model.Send(new VehicleManagementAction.RefreshRequested());
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を描き直します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>画面を描きます。</summary>
    /// <remarks>責務: 確認中なら車両の確認、登録・編集中なら編集画面、それ以外は一覧を出し、ダイアログを状態に合わせます。</remarks>
    private void Render()
    {
        var state = _model.State;
        if (state.Phase == VehicleManagementStatePhase.ConfirmingIdentification && state.PendingIdentification is { } snapshot)
        {
            _editor = null;
            _catalogKey = null;
            if (!ReferenceEquals(_shownSnapshot, snapshot) && !_gate.TryDefer(Render))
            {
                _shownSnapshot = snapshot;
                _host.Child = IdentificationConfirmation(snapshot);
            }
        }
        else if (state.EditingVehicle is { } vehicle
            && state.Phase is VehicleManagementStatePhase.Registering or VehicleManagementStatePhase.Editing)
        {
            _shownSnapshot = null;
            _catalogKey = null;
            if (_editor is null || _editor.Vehicle.Id != vehicle.Id)
            {
                _editor = new WindowsVehicleEditorView(vehicle, _model.Send);
                _host.Child = _editor;
            }
            else if (!_editor.Vehicle.Equals(vehicle))
            {
                // 写真を選んだときなど。入力中の内容を残したまま、変わった所（写真）だけを取り込む。
                _editor.ApplyExternalChange(vehicle);
            }
        }
        else
        {
            _editor = null;
            _shownSnapshot = null;
            var key = CatalogKey(state);
            if (key != _catalogKey && !_gate.TryDefer(Render))
            {
                _catalogKey = key;
                _host.Child = Catalog(state);
            }
        }

        _pidSettings.Render(state, XamlRoot);
        _deletionDialog.Sync(_deletionCandidate is not null, _deletionCandidate?.Id, XamlRoot, () => new ContentDialog
        {
            Title = LocalizedText.Get("garage.delete.title"),
            Content = Typography.Text(LocalizedText.Get("garage.delete.message"), "BodyTextBlockStyle"),
            PrimaryButtonText = LocalizedText.Get("garage.delete.action"),
            PrimaryButtonStyle = DesignTokens.Style("DangerButtonStyle"),
            CloseButtonText = LocalizedText.Get("garage.cancel"),
            DefaultButton = ContentDialogButton.Close,
        }, result =>
        {
            var candidate = _deletionCandidate;
            _deletionCandidate = null;
            if (result == ContentDialogResult.Primary && candidate is not null)
            {
                UiLog.VehicleDeleteConfirmed(UiLog.Logger);
                _model.Send(new VehicleManagementAction.VehicleDeleted(candidate.Id));
            }
        });
    }

    /// <summary>一覧を作り直すかを見分ける鍵を作ります。</summary>
    /// <param name="state">車両管理の状態。</param>
    /// <returns>鍵。</returns>
    private static string CatalogKey(VehicleManagementState state) => string.Join(
        "|",
        new object?[]
        {
            state.Phase,
            state.FailureKey,
            state.IdentificationFailureStage,
            state.ConnectionVehicle is null,
        }
        .Concat(state.Vehicles.Select(vehicle =>
        {
            var activity = state.ActivityByVehicleID.TryGetValue(vehicle.Id, out var value) ? value : new VehicleActivitySummary();
            state.SpecialPIDModelCodeByVehicleID.TryGetValue(vehicle.Id, out var modelCode);
            return (object)string.Join(
                ":",
                vehicle.Id,
                vehicle.Name,
                vehicle.Manufacturer,
                vehicle.DisplayIdentifier,
                vehicle.PhotoData?.Length,
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(vehicle.PhotoData ?? []),
                activity.SessionCount,
                activity.TotalRecordedDuration,
                activity.IsConnected,
                activity.LatestOdometerKilometers,
                activity.OdometerModelCode,
                activity.LastLoggedAt,
                modelCode);
        })));

    /// <summary>車両の一覧を作ります。</summary>
    /// <remarks>責務: 見出し、状態の帯、台数などの要約、車両のカード（なければ空の表示）を並べます。</remarks>
    /// <param name="state">車両管理の状態。</param>
    /// <returns>表示の要素。</returns>
    private PageScaffold Catalog(VehicleManagementState state)
    {
        var scaffold = new PageScaffold();
        var page = scaffold.Body;
        var refresh = new Button { Content = new GlyphLabel(FluentSymbol.Refresh, LocalizedText.Get("garage.refresh")) };
        refresh.Click += (_, _) => _model.Send(new VehicleManagementAction.RefreshRequested());
        refresh.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.F5 });
        page.Children.Add(new ScreenHeader(
            FluentSymbol.Car,
            LocalizedText.Get("garage.eyebrow"),
            LocalizedText.Get("garage.title"),
            LocalizedText.Get("garage.subtitle"),
            refresh));
        if (StatusBanner(state) is { } banner)
        {
            page.Children.Add(banner);
        }

        var summary = AdaptiveGrid.Tiles();
        summary.Children.Add(SummaryTile(NumberText.Integer(state.Vehicles.Count), "garage.summary.vehicles", FluentSymbol.Car));
        summary.Children.Add(SummaryTile(NumberText.Integer(state.ActivityByVehicleID.Values.Sum(activity => activity.SessionCount)), "garage.summary.sessions", FluentSymbol.Pulse));
        summary.Children.Add(SummaryTile(NumberText.Integer(state.ActivityByVehicleID.Values.Count(activity => activity.IsConnected)), "garage.summary.connected", FluentSymbol.Signal));
        page.Children.Add(summary);

        if (state.Vehicles.Count == 0 && state.Phase != VehicleManagementStatePhase.Loading)
        {
            page.Children.Add(new ContentUnavailablePanel(FluentSymbol.Car, LocalizedText.Get("garage.empty.title"), LocalizedText.Get("garage.empty.description")));
        }
        else
        {
            var grid = AdaptiveGrid.Cards();
            grid.SetMaxColumns(1, 2, 3, 4);
            foreach (var vehicle in state.Vehicles)
            {
                grid.Children.Add(VehicleCard(state, vehicle));
            }

            page.Children.Add(grid);
        }

        return scaffold;
    }

    /// <summary>状態の帯を作ります。</summary>
    /// <remarks>責務: 読み込み中・車両の識別中・失敗（失敗した段階の診断コード付き）・接続の受け渡しを出します。</remarks>
    /// <param name="state">車両管理の状態。</param>
    /// <returns>帯。出さないなら null。</returns>
    private static UIElement? StatusBanner(VehicleManagementState state)
    {
        if (state.Phase == VehicleManagementStatePhase.Loading)
        {
            return new GlyphLabel(FluentSymbol.Download, LocalizedText.Get("garage.status.loading"), "SecondaryBodyTextStyle");
        }

        if (state.Phase == VehicleManagementStatePhase.Identifying)
        {
            var bar = new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Title = LocalizedText.Get("garage.connection.in_progress"),
                Message = LocalizedText.Get("garage.identification.loading_hint"),
                Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, Space.S) },
            };
            return bar;
        }

        if (state.FailureKey is { } failureKey)
        {
            var message = LocalizedText.Get(failureKey);
            if (state.IdentificationFailureStage is { } stage)
            {
                message += $"\n{LocalizedText.Get("garage.identification.failure_stage")} {stage.DiagnosticCode()}";
            }

            return ErrorReport.Bar(InfoBarSeverity.Warning, message);
        }

        if (state.ConnectionVehicle is not null && state.Phase == VehicleManagementStatePhase.ReadyToConnect)
        {
            return new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = LocalizedText.Get("garage.connection.handoff") };
        }

        return null;
    }

    /// <summary>要約のタイルを作ります。</summary>
    /// <remarks>責務: 記号・数・説明を並べたタイルを作ります。</remarks>
    /// <param name="value">数。</param>
    /// <param name="key">説明のキー。</param>
    /// <param name="glyph">記号。</param>
    /// <returns>タイル。</returns>
    private static Border SummaryTile(string value, string key, string glyph)
    {
        var grid = new Grid { ColumnSpacing = Space.M };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(IconTile.Create(glyph, Tone.Accent, Metrics.IconTileMedium));
        var metric = new MetricBlock(value, LocalizedText.Get(key), valueStyleKey: TextStyles.MetricLarge);
        Grid.SetColumn(metric, 1);
        grid.Children.Add(metric);
        return SurfaceCard.Create(grid, padding: Space.L);
    }

    /// <summary>車両のカードを作ります。</summary>
    /// <remarks>
    /// 責務: 写真、接続中・車種専用の印、セッション数・記録時間、走行距離、最後の記録、名前と識別子、操作のボタンを並べます。
    /// 「接続中」と走行距離は、記録したセッションの集計が裏付けるときだけ出します。
    /// </remarks>
    /// <param name="state">車両管理の状態。</param>
    /// <param name="vehicle">車両。</param>
    /// <returns>カード。</returns>
    private Border VehicleCard(VehicleManagementState state, VehicleProfile vehicle)
    {
        var activity = state.ActivityByVehicleID.TryGetValue(vehicle.Id, out var value) ? value : new VehicleActivitySummary();
        var stack = new StackPanel { Spacing = Space.M };
        var photo = new Grid { CornerRadius = new CornerRadius(Radius.L), Background = DesignTokens.Brush("RevTorqueQuietFillBrush") };
        ResponsiveLayout.Bind(photo, widthClass => photo.Height = widthClass == WidthClass.Compact ? 140 : 176);
        if (vehicle.PhotoData is { } data)
        {
            photo.Children.Add(BytesImage.Create(data, 600));
        }
        else
        {
            photo.Background = DesignTokens.Brush("RevTorqueHeroBrush");
            photo.Children.Add(new FontIcon { Glyph = FluentSymbol.Car, FontSize = Metrics.IconTileLarge, Foreground = DesignTokens.Foreground(Tone.Accent) });
        }

        if (activity.IsConnected)
        {
            var connected = StatusPill.Create(FluentSymbol.Signal, LocalizedText.Get("garage.connection.active"), Tone.Success);
            connected.Margin = new Thickness(Space.M);
            connected.VerticalAlignment = VerticalAlignment.Top;
            connected.HorizontalAlignment = HorizontalAlignment.Left;
            photo.Children.Add(connected);
        }

        if (state.SpecialPIDModelCodeByVehicleID.TryGetValue(vehicle.Id, out var modelCode))
        {
            photo.Children.Add(new VehicleModelBadge(modelCode) { Margin = new Thickness(Space.M), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
        }

        stack.Children.Add(photo);
        var activityRow = new Grid { ColumnSpacing = Space.S };
        activityRow.ColumnDefinitions.Add(new ColumnDefinition());
        activityRow.ColumnDefinitions.Add(new ColumnDefinition());
        activityRow.Children.Add(new MetricBlock(NumberText.Integer(activity.SessionCount), LocalizedText.Get("garage.activity.sessions"), valueStyleKey: "BodyStrongTextBlockStyle"));
        var duration = new MetricBlock(DurationText.HoursAndMinutes(activity.TotalRecordedDuration), LocalizedText.Get("garage.activity.duration"), valueStyleKey: "BodyStrongTextBlockStyle");
        Grid.SetColumn(duration, 1);
        activityRow.Children.Add(duration);
        stack.Children.Add(activityRow);
        if (activity.LatestOdometerKilometers is { } odometer)
        {
            var row = KeyValueRow(FluentSymbol.Gauge, "garage.activity.odometer", NumberText.Decimal(odometer, 1) + " km");
            if (activity.OdometerModelCode is { } odometerModel)
            {
                var badge = new VehicleModelBadge(odometerModel, 26);
                Grid.SetColumn(badge, 3);
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(badge);
            }

            stack.Children.Add(row);
        }

        stack.Children.Add(KeyValueRow(
            FluentSymbol.Calendar,
            "garage.activity.last_log",
            activity.LastLoggedAt is { } last ? DateText.DateAndMinute(last) : LocalizedText.Get("garage.activity.no_logs")));
        var names = new StackPanel { Spacing = Space.XS };
        names.Children.Add(Typography.Text(vehicle.Name.Length == 0 ? vehicle.DisplayIdentifier : vehicle.Name, "SubtitleTextBlockStyle"));
        names.Children.Add(Typography.Text(vehicle.Manufacturer.Length == 0 ? LocalizedText.Get("garage.unknown_manufacturer") : vehicle.Manufacturer, "SecondaryBodyTextStyle"));
        names.Children.Add(Typography.Text(vehicle.DisplayIdentifier, "MonospaceCaptionTextStyle"));
        stack.Children.Add(names);
        // 幅の狭いカードで（スペイン語など）3 つ目のボタンが切れないよう、収まらなければ次の行へ送る。
        var actions = new FlowPanel();
        var edit = new Button { Content = new GlyphLabel(FluentSymbol.Edit, LocalizedText.Get("garage.edit")), Style = DesignTokens.Style("AccentButtonStyle") };
        edit.Click += (_, _) => _model.Send(new VehicleManagementAction.EditRequested(vehicle.Id));
        actions.Children.Add(edit);
        var pid = new Button { Content = new GlyphLabel(FluentSymbol.Pulse, LocalizedText.Get("garage.pid_settings.open")) };
        pid.Click += (_, _) => _model.Send(new VehicleManagementAction.PidSettingsRequested(vehicle.Id));
        actions.Children.Add(pid);
        var delete = new Button { Content = new FontIcon { Glyph = FluentSymbol.Delete, FontSize = 14, Foreground = DesignTokens.Foreground(Tone.Danger) } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(delete, LocalizedText.Get("garage.delete.action"));
        ToolTipService.SetToolTip(delete, LocalizedText.Get("garage.delete.action"));
        delete.Click += (_, _) =>
        {
            _deletionCandidate = vehicle;
            Render();
        };
        actions.Children.Add(delete);
        stack.Children.Add(actions);
        var card = SurfaceCard.Create(stack, padding: Space.L);
        card.ContextFlyout = new ContextMenu()
            .Item(FluentSymbol.Edit, LocalizedText.Get("garage.edit"), () => _model.Send(new VehicleManagementAction.EditRequested(vehicle.Id)))
            .Item(FluentSymbol.Pulse, LocalizedText.Get("garage.pid_settings.open"), () => _model.Send(new VehicleManagementAction.PidSettingsRequested(vehicle.Id)))
            .Separator()
            .Item(FluentSymbol.Delete, LocalizedText.Get("garage.delete.action"), () =>
            {
                _deletionCandidate = vehicle;
                Render();
            }, Tone.Danger)
            .Build();
        return card;
    }

    /// <summary>記号・項目名・値の行を作ります。</summary>
    /// <remarks>責務: 項目名を左、値を右に寄せた行を作ります。</remarks>
    /// <param name="glyph">記号。</param>
    /// <param name="key">項目名のキー。</param>
    /// <param name="value">値。</param>
    /// <returns>行。</returns>
    private static Grid KeyValueRow(string glyph, string key, string value)
    {
        var row = new Grid { ColumnSpacing = Space.S };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, Foreground = DesignTokens.Foreground(Tone.Accent) });
        var label = Typography.Text(LocalizedText.Get(key), "SecondaryCaptionTextStyle");
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        var text = Typography.Strong(value, "CaptionTextBlockStyle");
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        return row;
    }

    /// <summary>接続時の車両の確認を作ります。</summary>
    /// <remarks>責務: 読み取った VIN（または OBD の識別子）と識別の項目を出し、登録へ進むか取り消すボタンを作ります。</remarks>
    /// <param name="snapshot">車両の識別の結果。</param>
    /// <returns>表示の要素。</returns>
    private PageScaffold IdentificationConfirmation(VehicleIdentificationSnapshot snapshot)
    {
        var scaffold = new PageScaffold(Metrics.ReadingMaxWidth + Space.XXXL * 4);
        var page = scaffold.Body;
        // ほかの画面と同じ見出し（記号のタイル・Eyebrow・題・副題）。
        page.Children.Add(new ScreenHeader(
            FluentSymbol.Car,
            LocalizedText.Get("garage.identification.eyebrow"),
            LocalizedText.Get("garage.identification.title"),
            LocalizedText.Get("garage.identification.description")));
        var vin = Typography.Text(snapshot.Vin ?? snapshot.ObdIdentifier ?? "—", "SubtitleTextBlockStyle");
        vin.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        vin.IsTextSelectionEnabled = true;
        page.Children.Add(vin);
        var fields = new Grid { ColumnSpacing = Space.XXL, RowSpacing = Space.M };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var index = 0; index < snapshot.Fields.Count; index++)
        {
            var field = snapshot.Fields[index];
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Typography.Text(field.Label, "SecondaryBodyTextStyle");
            var value = Typography.Text(field.Value, "BodyTextBlockStyle");
            value.IsTextSelectionEnabled = true;
            var source = Typography.Text(field.Source, "SecondaryCaptionTextStyle");
            Grid.SetRow(label, index);
            Grid.SetRow(value, index);
            Grid.SetColumn(value, 1);
            Grid.SetRow(source, index);
            Grid.SetColumn(source, 2);
            fields.Children.Add(label);
            fields.Children.Add(value);
            fields.Children.Add(source);
        }

        page.Children.Add(SurfaceCard.Create(fields));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.M };
        var cancel = new Button { Content = LocalizedText.Get("garage.cancel"), MinHeight = Metrics.ControlHeight };
        cancel.Click += (_, _) => _model.Send(new VehicleManagementAction.RegistrationCancelled());
        actions.Children.Add(cancel);
        var confirm = new Button { Content = LocalizedText.Get("garage.identification.confirm"), Style = DesignTokens.Style("AccentButtonStyle"), MinHeight = Metrics.ControlHeight };
        confirm.Click += (_, _) => _model.Send(new VehicleManagementAction.IdentificationConfirmed());
        actions.Children.Add(confirm);
        page.Children.Add(actions);
        return scaffold;
    }
}
