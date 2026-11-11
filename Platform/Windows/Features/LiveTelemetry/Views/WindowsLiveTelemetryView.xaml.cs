using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Formatting;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LiveTelemetry.Views;

/// <summary>
/// ライブログ画面です。接続中の車両から読んだ PID の値を出します（iOS 版の <c>MacOSLiveTelemetryView</c>）。
/// </summary>
/// <remarks>
/// 値はライブ取得の状態にある読み取り結果だけを出し、推測した値は出しません。
/// ポーリングのたびに全体を作り直すと説明のポップアップが閉じるため、PID の組と分類が同じ間は値の文字だけを書き換えます。
/// </remarks>
public sealed partial class WindowsLiveTelemetryView : UserControl
{
    /// <summary>主の 2 つの PID（回転数・車速）です。</summary>
    private static readonly OBDPIDRequest[] PrimaryRequests = [new(0x01, 0x0C), new(0x01, 0x0D)];

    /// <summary>ライブ取得のモデルです。</summary>
    private readonly LiveTelemetryModel _model;

    /// <summary>値の文字（セクションと PID ごと）です。</summary>
    private readonly Dictionary<(string Section, OBDPIDRequest Request), TextBlock> _valueTexts = [];

    /// <summary>分類のボタンの代表値の文字です。</summary>
    private readonly Dictionary<OBDPIDCategory, TextBlock> _categoryValueTexts = [];

    /// <summary>画面の一時的な状態：選んでいる分類です。</summary>
    private OBDPIDCategory _selectedCategory = OBDPIDCategory.Engine;

    /// <summary>今の画面の作りを表す鍵です（変わったら作り直す）。</summary>
    private string? _structureKey;

    /// <summary>応答している PID の数の文字です。</summary>
    private TextBlock? _supportedCountText;

    /// <summary>操作中の作り直しを待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>ライブ取得のモデルを受け取って画面を作ります。</summary>
    /// <param name="model">ライブ取得のモデル。</param>
    public WindowsLiveTelemetryView(LiveTelemetryModel model)
    {
        _model = model;
        InitializeComponent();
        _gate = new RenderGate(this);
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            Render();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 画面を更新します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Render();

    /// <summary>
    /// 画面を更新します。作りが同じなら値の文字だけを書き換えます。
    /// </summary>
    /// <remarks>責務: 状態の段階・PID の組・分類から作りの鍵を決め、作り直すか値だけ更新するかを選びます。</remarks>
    private void Render()
    {
        var state = _model.State;
        var categories = AvailableCategories(state);
        if (!categories.Contains(_selectedCategory) && categories.Count > 0)
        {
            _selectedCategory = categories[0];
        }

        var key = string.Join(
            ",",
            new[] { state.Phase.ToString(), state.FailureKey ?? string.Empty, _selectedCategory.ToString() }
                .Concat(state.Samples.Select(sample => $"{sample.Request.Service:X2}{sample.Request.Pid:X2}{sample.VehicleModelCode}")));
        if (key == _structureKey || _gate.TryDefer(Render))
        {
            // 操作中（分類のボタンを押している最中や、説明のフライアウトを開いている間）は作り直さず、値だけを変える。
            UpdateValues(state);
            return;
        }

        _structureKey = key;
        Rebuild(state, categories);
    }

    /// <summary>画面を作り直します。</summary>
    /// <remarks>責務: 見出し、状態の帯、主の値、車種専用の値、分類ごとの値、待機・再試行の表示を作ります。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <param name="categories">値がある分類。</param>
    private void Rebuild(LiveTelemetryState state, IReadOnlyList<OBDPIDCategory> categories)
    {
        _valueTexts.Clear();
        _categoryValueTexts.Clear();
        Page.Children.Clear();
        Page.Children.Add(Hero(state));
        if (StatusBanner(state) is { } banner)
        {
            if (state.Phase == LiveTelemetryStatePhase.Failed)
            {
                // 再試行は失敗の知らせの中に置く（Windows の InfoBar の操作ボタン）。
                var retry = new Button
                {
                    Content = new GlyphLabel(FluentSymbol.Refresh, LocalizedText.Get("telemetry.retry")),
                    Style = DesignTokens.Style("AccentButtonStyle"),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(retry, "windows-telemetry-retry");
                retry.Click += (_, _) => _model.Send(new LiveTelemetryAction.RetryRequested());
                banner.ActionButton = retry;
            }

            Page.Children.Add(banner);
        }

        if (state.Samples.Count > 0)
        {
            Page.Children.Add(PrimaryDashboard(state));
            var vehicleSpecific = VehicleSpecificSamples(state);
            if (vehicleSpecific.Count > 0)
            {
                Page.Children.Add(VehicleSpecificSection(vehicleSpecific));
            }

            Page.Children.Add(CategoryWorkspace(state, categories));
        }
        else if (state.Phase == LiveTelemetryStatePhase.Idle)
        {
            Page.Children.Add(SurfaceCard.Create(new ContentUnavailablePanel(
                FluentSymbol.Plug,
                LocalizedText.Get("telemetry.status.waiting_for_connection"))));
        }
    }

    /// <summary>値の文字だけを書き換えます。</summary>
    /// <remarks>責務: 作りを変えずに、PID の値と応答数の文字を最新の読み取り結果にします。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    private void UpdateValues(LiveTelemetryState state)
    {
        if (_supportedCountText is not null)
        {
            _supportedCountText.Text = NumberText.Integer(state.SupportedPIDCount);
        }

        foreach (var sample in state.Samples)
        {
            foreach (var section in new[] { "primary", "vehicle", "category" })
            {
                if (_valueTexts.TryGetValue((section, sample.Request), out var text))
                {
                    text.Text = NumberText.Decimal(sample.Value, section == "primary" ? 1 : 2);
                }
            }
        }

        foreach (var (category, text) in _categoryValueTexts)
        {
            if (Representative(SamplesIn(state, category), category) is { } sample)
            {
                text.Text = Formatted(sample);
            }
        }
    }

    /// <summary>見出しのカードを作ります。</summary>
    /// <remarks>責務: 題と、応答している PID の数（と車種専用のバッジ）を並べます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>カード。</returns>
    private Border Hero(LiveTelemetryState state)
    {
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var count = new MetricBlock(
            NumberText.Integer(state.SupportedPIDCount),
            LocalizedText.Get("telemetry.status.responding_count"),
            HorizontalAlignment.Right,
            valueStyleKey: "TitleTextBlockStyle");
        _supportedCountText = (TextBlock)count.Children[0];
        summary.Children.Add(count);
        if (VehicleSpecificSamples(state).FirstOrDefault()?.VehicleModelCode is { } modelCode)
        {
            summary.Children.Add(new VehicleModelBadge(modelCode));
        }

        return SurfaceCard.Create(new ScreenHeader(
            FluentSymbol.Gauge,
            LocalizedText.Get("telemetry.eyebrow"),
            LocalizedText.Get("telemetry.title"),
            LocalizedText.Get("telemetry.subtitle"),
            summary), padding: 24);
    }

    /// <summary>取得の状態の帯を作ります。</summary>
    /// <remarks>責務: 読み取り中・停止中・取得中・失敗を InfoBar で出します（待機中は出さない）。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>帯。出さないなら null。</returns>
    private static InfoBar? StatusBanner(LiveTelemetryState state)
    {
        var (key, severity, showsProgress) = state.Phase switch
        {
            LiveTelemetryStatePhase.Reading => ("telemetry.status.reading", InfoBarSeverity.Informational, true),
            LiveTelemetryStatePhase.Stopping => ("telemetry.status.stopping", InfoBarSeverity.Warning, true),
            LiveTelemetryStatePhase.Loaded => ("telemetry.status.polling", InfoBarSeverity.Success, false),
            LiveTelemetryStatePhase.Failed => (state.FailureKey ?? "telemetry.error.read_failed", InfoBarSeverity.Warning, false),
            _ => (null, InfoBarSeverity.Informational, false),
        };
        if (key is null)
        {
            return null;
        }

        var bar = new InfoBar { IsOpen = true, IsClosable = false, Severity = severity, Message = LocalizedText.Get(key) };
        if (showsProgress)
        {
            bar.Content = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 8) };
        }
        else if (state.Phase == LiveTelemetryStatePhase.Failed)
        {
            // 失敗のときだけ、報告用の番号を付ける。
            ErrorReport.AttachTo(bar);
        }

        return bar;
    }

    /// <summary>主の値（回転数・車速）の部分を作ります。</summary>
    /// <remarks>責務: 回転数・車速を優先した最大 2 件の大きな値のカードを並べます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>表示の要素。</returns>
    private StackPanel PrimaryDashboard(LiveTelemetryState state)
    {
        var preferred = PrimaryRequests
            .Select(request => state.Samples.FirstOrDefault(sample => sample.Request == request))
            .OfType<OBDPIDSample>()
            .ToList();
        var samples = preferred.Concat(state.Samples.Where(sample => !preferred.Contains(sample))).Take(2);
        var grid = new AdaptiveGrid { MinItemWidth = 300, Spacing = 16 };
        foreach (var sample in samples)
        {
            grid.Children.Add(PrimaryCard(sample));
        }

        return Section("telemetry.section.now", FluentSymbol.Gauge, grid);
    }

    /// <summary>主の値のカードを作ります。</summary>
    /// <remarks>責務: PID の名前・大きな値・単位・説明のボタンを並べます。</remarks>
    /// <param name="sample">読み取り結果。</param>
    /// <returns>カード。</returns>
    private Border PrimaryCard(OBDPIDSample sample)
    {
        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = sample.Request == PrimaryRequests[0] ? FluentSymbol.Gauge : sample.Request == PrimaryRequests[1] ? FluentSymbol.Car : FluentSymbol.Pulse;
        grid.Children.Add(IconTile.Create(glyph, Tone.Accent, 58));
        var texts = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Typography.Text(LocalizedText.Get(sample.NameKey), "SecondaryCaptionTextStyle"));
        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var value = Typography.Text(NumberText.Decimal(sample.Value, 1), "TitleLargeTextBlockStyle", wraps: false);
        _valueTexts[("primary", sample.Request)] = value;
        valueRow.Children.Add(value);
        var unit = Typography.Text(sample.Unit, "MonospaceCaptionTextStyle", DesignTokens.Foreground(Tone.Accent), wraps: false);
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 8);
        valueRow.Children.Add(unit);
        texts.Children.Add(valueRow);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var help = HelpButton(sample);
        Grid.SetColumn(help, 2);
        grid.Children.Add(help);
        return SurfaceCard.Create(grid, Tone.Accent, 20, tinted: true);
    }

    /// <summary>車種専用の値の部分を作ります。</summary>
    /// <remarks>責務: 車種専用 PID の値を、車種のバッジ付きの区画にまとめます。</remarks>
    /// <param name="samples">車種専用の読み取り結果。</param>
    /// <returns>区画。</returns>
    private Border VehicleSpecificSection(IReadOnlyList<OBDPIDSample> samples)
    {
        var grid = new AdaptiveGrid { MinItemWidth = 205, Spacing = 12 };
        foreach (var sample in samples)
        {
            grid.Children.Add(SampleCard(sample, "vehicle", Tone.Accent));
        }

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new GlyphLabel(FluentSymbol.Car, LocalizedText.Get("telemetry.section.brz"), "SectionTitleTextStyle"));
        if (samples[0].VehicleModelCode is { } modelCode)
        {
            var badge = new VehicleModelBadge(modelCode);
            Grid.SetColumn(badge, 1);
            head.Children.Add(badge);
        }

        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(head);
        stack.Children.Add(grid);
        return SurfaceCard.Create(stack, Tone.Accent, 19);
    }

    /// <summary>分類ごとの値の部分を作ります。</summary>
    /// <remarks>責務: 分類の切り替えボタン（代表値付き）と、選んだ分類の値のカードを並べます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <param name="categories">値がある分類。</param>
    /// <returns>表示の要素。</returns>
    private StackPanel CategoryWorkspace(LiveTelemetryState state, IReadOnlyList<OBDPIDCategory> categories)
    {
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var category in categories)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
            content.Children.Add(new FontIcon { Glyph = CategoryGlyph(category), FontSize = 14 });
            content.Children.Add(Typography.Strong(LocalizedText.Get(category.NameKey()), "CaptionTextBlockStyle"));
            if (Representative(SamplesIn(state, category), category) is { } sample)
            {
                var valueText = Typography.Text(Formatted(sample), "MonospaceCaptionTextStyle", wraps: false);
                _categoryValueTexts[category] = valueText;
                content.Children.Add(valueText);
            }

            var chip = new ToggleButton
            {
                Content = content,
                IsChecked = category == _selectedCategory,
                MinHeight = 40,
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(14, 6, 14, 6),
            };
            var selected = category;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, LocalizedText.Get(category.NameKey()));
            chip.Click += (_, _) =>
            {
                _selectedCategory = selected;
                _structureKey = null;
                Render();
            };
            chips.Children.Add(chip);
        }

        var grid = new AdaptiveGrid { MinItemWidth = 210, Spacing = 12 };
        foreach (var sample in SamplesIn(state, _selectedCategory))
        {
            grid.Children.Add(SampleCard(sample, "category", Tone.Accent));
        }

        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new ScrollViewer
        {
            Content = chips,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
        });
        stack.Children.Add(grid);
        return Section("telemetry.section.all", FluentSymbol.Filter, stack);
    }

    /// <summary>値のカードを作ります。</summary>
    /// <remarks>責務: PID の名前・値・単位・説明のボタンを持つ小さなカードを作ります。</remarks>
    /// <param name="sample">読み取り結果。</param>
    /// <param name="section">どの部分のカードか（値の書き換えの鍵）。</param>
    /// <param name="tone">単位の色の役割。</param>
    /// <returns>カード。</returns>
    private Border SampleCard(OBDPIDSample sample, string section, Tone tone)
    {
        var stack = new StackPanel { Spacing = 10 };
        var head = new Grid { ColumnSpacing = 4 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Typography.Text(LocalizedText.Get(sample.NameKey), "SecondaryCaptionTextStyle");
        name.MaxLines = 2;
        head.Children.Add(name);
        var help = HelpButton(sample);
        Grid.SetColumn(help, 1);
        head.Children.Add(help);
        stack.Children.Add(head);
        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var value = Typography.Text(NumberText.Decimal(sample.Value, 2), "SubtitleTextBlockStyle", wraps: false);
        _valueTexts[(section, sample.Request)] = value;
        valueRow.Children.Add(value);
        var unit = Typography.Text(sample.Unit, "MonospaceCaptionTextStyle", DesignTokens.Foreground(tone), wraps: false);
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 4);
        valueRow.Children.Add(unit);
        stack.Children.Add(valueRow);
        stack.MinHeight = 64;
        return SurfaceCard.Create(stack, padding: 16);
    }

    /// <summary>PID の説明のボタンを作ります。</summary>
    /// <remarks>責務: 押すと PID の概要・高いとき・低いとき・相関の説明を出すボタンを作ります。</remarks>
    /// <param name="sample">読み取り結果。</param>
    /// <returns>ボタン。</returns>
    private static Button HelpButton(OBDPIDSample sample)
    {
        var body = new StackPanel { Spacing = 12, MaxWidth = 360 };
        body.Children.Add(Typography.Text(LocalizedText.Get(sample.NameKey), "BodyStrongTextBlockStyle"));
        foreach (var (title, value) in new[]
        {
            ("telemetry.help.summary", sample.SummaryKey),
            ("telemetry.help.high", sample.HighValueKey),
            ("telemetry.help.low", sample.LowValueKey),
            ("telemetry.help.correlation", sample.CorrelationKey),
        })
        {
            var row = new StackPanel { Spacing = 3 };
            row.Children.Add(Typography.Strong(LocalizedText.Get(title), "SecondaryCaptionTextStyle"));
            row.Children.Add(Typography.Text(LocalizedText.Get(value), "BodyTextBlockStyle"));
            body.Children.Add(row);
        }

        var button = new Button
        {
            Content = new FontIcon { Glyph = FluentSymbol.Info, FontSize = 14 },
            Style = DesignTokens.Style("SubtleButtonStyle"),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top,
            Flyout = new Flyout { Content = body },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, LocalizedText.Get("telemetry.help.open"));
        ToolTipService.SetToolTip(button, LocalizedText.Get("telemetry.help.open"));
        return button;
    }

    /// <summary>見出し付きの区画を作ります。</summary>
    /// <remarks>責務: 記号と題の見出しの下に中身を置きます。</remarks>
    /// <param name="titleKey">題のキー。</param>
    /// <param name="glyph">記号。</param>
    /// <param name="content">中身。</param>
    /// <returns>区画。</returns>
    private static StackPanel Section(string titleKey, string glyph, UIElement content)
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new GlyphLabel(glyph, LocalizedText.Get(titleKey), "SectionTitleTextStyle"));
        stack.Children.Add(content);
        return stack;
    }

    /// <summary>車種専用の読み取り結果を返します。</summary>
    /// <remarks>責務: 車種コードがある読み取り結果だけを選びます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>車種専用の読み取り結果。</returns>
    private static List<OBDPIDSample> VehicleSpecificSamples(LiveTelemetryState state) =>
        state.Samples.Where(sample => !string.IsNullOrEmpty(sample.VehicleModelCode)).ToList();

    /// <summary>値がある分類を返します。</summary>
    /// <remarks>責務: 分類の宣言順のうち、読み取り結果がある分類を選びます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>分類。</returns>
    private static List<OBDPIDCategory> AvailableCategories(LiveTelemetryState state) =>
        OBDPIDCategoryRawValue.AllCases.Where(category => SamplesIn(state, category).Count > 0).ToList();

    /// <summary>分類の読み取り結果を返します。</summary>
    /// <remarks>責務: PID の分類が一致する読み取り結果を選びます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <param name="category">分類。</param>
    /// <returns>読み取り結果。</returns>
    private static List<OBDPIDSample> SamplesIn(LiveTelemetryState state, OBDPIDCategory category) =>
        state.Samples.Where(sample => OBDPIDCategoryExtensions.Category(sample.Request) == category).ToList();

    /// <summary>分類の代表の読み取り結果を返します。</summary>
    /// <remarks>責務: 分類の代表 PID があればそれを、なければ最初の結果を返します。</remarks>
    /// <param name="samples">分類の読み取り結果。</param>
    /// <param name="category">分類。</param>
    /// <returns>代表。なければ null。</returns>
    private static OBDPIDSample? Representative(IReadOnlyList<OBDPIDSample> samples, OBDPIDCategory category) =>
        samples.FirstOrDefault(sample => sample.Request == category.RepresentativeRequest()) ?? samples.FirstOrDefault();

    /// <summary>値と単位の文字列を返します。</summary>
    /// <remarks>責務: 小数 2 桁までの値と単位をつなぎます。</remarks>
    /// <param name="sample">読み取り結果。</param>
    /// <returns>文字列。</returns>
    private static string Formatted(OBDPIDSample sample) => $"{NumberText.Decimal(sample.Value, 2)} {sample.Unit}";

    /// <summary>分類の記号を返します。</summary>
    /// <remarks>責務: 分類を記号の文字へ変換します。</remarks>
    /// <param name="category">分類。</param>
    /// <returns>記号の文字。</returns>
    private static string CategoryGlyph(OBDPIDCategory category) => category switch
    {
        OBDPIDCategory.Engine => FluentSymbol.Gauge,
        OBDPIDCategory.Temperature => FluentSymbol.Temperature,
        OBDPIDCategory.FuelAndAir => FluentSymbol.Wind,
        OBDPIDCategory.Driving => FluentSymbol.Steering,
        _ => FluentSymbol.Diagnostics,
    };
}
