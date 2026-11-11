using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
/// 要素はコンストラクターで 1 回だけ組み、モデルの知らせは <see cref="UiUpdateThrottle"/>（100ms 間隔・1 フレームに 1 回）で束ねて、
/// 値の文字だけを差し替えます。PID の組や分類が変わったときだけ <see cref="KeyedChildren{TKey, TItem, TElement}"/> で差分を直します
/// （分類のボタンは作り直さないので、取得中に連打してもクリックを取りこぼさない）。
/// 説明のフライアウトを開いている間などは、タイルの出し入れだけを待たせます（<see cref="RenderGate"/>）。
/// </remarks>
public sealed partial class WindowsLiveTelemetryView : UserControl
{
    /// <summary>主の 2 つの PID（回転数・車速）です。</summary>
    private static readonly OBDPIDRequest[] PrimaryRequests = [new(0x01, 0x0C), new(0x01, 0x0D)];

    /// <summary>人が読む数値の更新の最短間隔です。</summary>
    private static readonly TimeSpan ValueInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>ライブ取得のモデルです。</summary>
    private readonly LiveTelemetryModel _model;

    /// <summary>更新を束ねる仕組みです。</summary>
    private readonly UiUpdateThrottle _throttle;

    /// <summary>タイルの出し入れを操作中は待たせる仕組みです。</summary>
    private readonly RenderGate _gate;

    /// <summary>画面の一時的な状態：選んでいる分類です。</summary>
    private OBDPIDCategory _selectedCategory = OBDPIDCategory.Engine;

    /// <summary>分類のボタンを押した直後で、待たせずにタイルを入れ替えるなら true です。</summary>
    private bool _forceStructure;

    /// <summary>最後に整えた PID の組です（変わったときだけ入れ替える）。</summary>
    private (OBDPIDRequest Request, bool IsVehicleSpecific)[] _structure = [];

    /// <summary>最後に整えた分類です。</summary>
    private OBDPIDCategory? _structureCategory;

    /// <summary>最後に反映した取得の段階と失敗の鍵です。</summary>
    private (LiveTelemetryStatePhase Phase, string? FailureKey)? _shownStatus;

    /// <summary>見出しの応答数です。</summary>
    private readonly MetricBlock _headerCount;

    /// <summary>見出しの右の束（応答数・車種のバッジ）です。</summary>
    private readonly StackPanel _headerSummary = new() { Orientation = Orientation.Horizontal, Spacing = Space.M };

    /// <summary>車種のバッジの置き場です。</summary>
    private readonly ContentControl _headerBadge = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>取得の状態の帯です。</summary>
    private readonly InfoBar _banner = new() { IsClosable = false, IsOpen = false };

    /// <summary>帯の中の進み具合です。</summary>
    private readonly ProgressBar _bannerProgress = new() { Margin = new Thickness(0, 0, 0, Space.S) };

    /// <summary>失敗の帯の再試行のボタンです。</summary>
    private readonly Button _retryButton = new() { Style = DesignTokens.Style("AccentButtonStyle") };

    /// <summary>待機中（読み取りなし）の表示です。</summary>
    private readonly Border _waiting;

    /// <summary>計器帯の主の値のタイルです。</summary>
    private readonly AdaptiveGrid _heroGrid = new() { MinItemWidth = 280 };

    private readonly KeyedChildren<OBDPIDRequest, OBDPIDSample, SampleTile> _heroTiles;

    /// <summary>計器帯の右の副の欄（応答数・状態。Expanded 以上）です。</summary>
    private readonly StackPanel _bandSide = new() { Spacing = Space.M, VerticalAlignment = VerticalAlignment.Center, MinWidth = 168 };

    /// <summary>副の欄の応答数です。</summary>
    private readonly MetricBlock _bandCount;

    /// <summary>副の欄の状態の印です。</summary>
    private readonly Border _bandStatus = StatusPill.Create(null, string.Empty, Tone.Neutral);

    /// <summary>計器帯です。</summary>
    private readonly Grid _band = new() { ColumnSpacing = Space.L };

    /// <summary>車種専用の値の区画です。</summary>
    private readonly Border _vehicleSection;

    private readonly AdaptiveGrid _vehicleGrid = TileGrid();

    private readonly KeyedChildren<OBDPIDRequest, OBDPIDSample, SampleTile> _vehicleTiles;

    /// <summary>車種専用の区画の車種のバッジの置き場です。</summary>
    private readonly ContentControl _vehicleBadge = new() { VerticalAlignment = VerticalAlignment.Center };

    /// <summary>分類の区画です。</summary>
    private readonly StackPanel _categorySection = new() { Spacing = Space.M };

    /// <summary>分類のボタンの並びです。</summary>
    private readonly ChipFlowPanel _chips = new();

    /// <summary>分類のボタンの横スクロールです（折り返す幅クラスでは無効）。</summary>
    private readonly ScrollViewer _chipScroller;

    private readonly KeyedChildren<OBDPIDCategory, OBDPIDCategory, CategoryChip> _chipItems;

    private readonly AdaptiveGrid _categoryGrid = TileGrid();

    private readonly KeyedChildren<OBDPIDRequest, OBDPIDSample, SampleTile> _categoryTiles;

    /// <summary>ライブ取得のモデルを受け取って画面を作ります。</summary>
    /// <param name="model">ライブ取得のモデル。</param>
    public WindowsLiveTelemetryView(LiveTelemetryModel model)
    {
        _model = model;
        InitializeComponent();
        _gate = new RenderGate(this);

        _heroGrid.SetMaxColumns(1, 2, 2, 2);
        _heroTiles = new(_heroGrid, sample => sample.Request, sample => new SampleTile(sample, hero: true, OnHeroGlyph(sample)), (tile, sample) => tile.Bind(sample));
        _vehicleTiles = new(_vehicleGrid, sample => sample.Request, sample => new SampleTile(sample, hero: false, null), (tile, sample) => tile.Bind(sample));
        _categoryTiles = new(_categoryGrid, sample => sample.Request, sample => new SampleTile(sample, hero: false, null), (tile, sample) => tile.Bind(sample));
        _chipItems = new(_chips, category => category, category => NewChip(category), (chip, _) => chip.SetChecked(_selectedCategory));

        _headerCount = new MetricBlock(string.Empty, LocalizedText.Get("telemetry.status.responding_count"), HorizontalAlignment.Right);
        _headerSummary.Children.Add(_headerCount);
        _headerSummary.Children.Add(_headerBadge);

        _bandCount = new MetricBlock(string.Empty, LocalizedText.Get("telemetry.status.responding_count"), valueStyleKey: TextStyles.MetricLarge);
        _bandSide.Children.Add(_bandCount);
        _bandSide.Children.Add(_bandStatus);
        _band.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _band.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _band.Children.Add(_heroGrid);
        Grid.SetColumn(_bandSide, 1);
        _band.Children.Add(_bandSide);

        _banner.Content = null;
        _retryButton.Content = new GlyphLabel(FluentSymbol.Refresh, LocalizedText.Get("telemetry.retry"));
        AutomationProperties.SetAutomationId(_retryButton, "windows-telemetry-retry");
        _retryButton.Click += (_, _) => _model.Send(new LiveTelemetryAction.RetryRequested());

        _waiting = SurfaceCard.Create(new ContentUnavailablePanel(
            FluentSymbol.Plug,
            LocalizedText.Get("telemetry.status.waiting_for_connection")));

        var vehicleHead = new Grid();
        vehicleHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        vehicleHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        vehicleHead.Children.Add(new GlyphLabel(FluentSymbol.Car, LocalizedText.Get("telemetry.section.brz"), TextStyles.SectionTitle));
        Grid.SetColumn(_vehicleBadge, 1);
        vehicleHead.Children.Add(_vehicleBadge);
        var vehicleStack = new StackPanel { Spacing = Space.M };
        vehicleStack.Children.Add(vehicleHead);
        vehicleStack.Children.Add(_vehicleGrid);
        _vehicleSection = SurfaceCard.Create(vehicleStack, Tone.Accent, Space.XL);

        _chipScroller = new ScrollViewer
        {
            Content = _chips,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        _categorySection.Children.Add(new GlyphLabel(FluentSymbol.Filter, LocalizedText.Get("telemetry.section.all"), TextStyles.SectionTitle));
        _categorySection.Children.Add(_chipScroller);
        _categorySection.Children.Add(_categoryGrid);

        var page = new PageScaffold();
        page.Body.Children.Add(new ScreenHeader(
            FluentSymbol.Gauge,
            LocalizedText.Get("telemetry.eyebrow"),
            LocalizedText.Get("telemetry.title"),
            LocalizedText.Get("telemetry.subtitle"),
            _headerSummary));
        page.Body.Children.Add(_banner);
        page.Body.Children.Add(_band);
        page.Body.Children.Add(_vehicleSection);
        page.Body.Children.Add(_categorySection);
        page.Body.Children.Add(_waiting);
        Content = page;

        _throttle = new UiUpdateThrottle(this, Update, ValueInterval);
        ResponsiveLayout.Bind(this, ApplyLayout);
        Loaded += (_, _) =>
        {
            _model.PropertyChanged += OnModelChanged;
            _throttle.FlushNow();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
        Update();
    }

    /// <summary>モデルが変わったときの処理です。</summary>
    /// <remarks>責務: 次のフレームでの更新を頼みます（何度来ても 1 回にまとまる）。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">変更の内容。</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => _throttle.Request();

    /// <summary>
    /// 幅クラスに合わせて配置を切り替えます。
    /// </summary>
    /// <remarks>責務: 副の欄・見出しの応答数の出し分けと、分類のボタンの折り返しを切り替えます（木は作り直さない）。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    private void ApplyLayout(WidthClass widthClass)
    {
        var wide = widthClass >= WidthClass.Expanded;
        Retained.SetVisible(_bandSide, wide);
        Retained.SetVisible(_headerCount, !wide);
        _band.ColumnSpacing = wide ? Space.XXL : 0;
        _chips.Wraps = wide;
        _chipScroller.HorizontalScrollMode = wide ? ScrollMode.Disabled : ScrollMode.Enabled;
        _chipScroller.HorizontalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    /// <summary>
    /// 画面を今の状態に合わせます。
    /// </summary>
    /// <remarks>責務: 状態の帯と値の文字を差し替え、PID の組や分類が変わったときだけタイルを差分で入れ替えます。</remarks>
    private void Update()
    {
        var state = _model.State;
        UpdateStatus(state);
        var count = NumberText.Integer(state.SupportedPIDCount);
        _headerCount.SetValue(count);
        _bandCount.SetValue(count);

        var hasSamples = state.Samples.Count > 0;
        Retained.SetVisible(_waiting, !hasSamples && state.Phase == LiveTelemetryStatePhase.Idle);
        Retained.SetVisible(_band, hasSamples);
        Retained.SetVisible(_categorySection, hasSamples);
        if (!hasSamples)
        {
            Retained.SetVisible(_vehicleSection, false);
            SyncStructure(state);
            return;
        }

        SyncStructure(state);
        UpdateValues(state);
    }

    /// <summary>取得の状態の帯と印を差し替えます。</summary>
    /// <remarks>責務: 段階か失敗の鍵が変わったときだけ、帯の重さ・文言・再試行のボタンを直します。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    private void UpdateStatus(LiveTelemetryState state)
    {
        var status = (state.Phase, state.FailureKey);
        if (_shownStatus == status)
        {
            return;
        }

        _shownStatus = status;
        var (key, severity, showsProgress) = state.Phase switch
        {
            LiveTelemetryStatePhase.Reading => ("telemetry.status.reading", InfoBarSeverity.Informational, true),
            LiveTelemetryStatePhase.Stopping => ("telemetry.status.stopping", InfoBarSeverity.Warning, true),
            LiveTelemetryStatePhase.Loaded => ("telemetry.status.polling", InfoBarSeverity.Success, false),
            LiveTelemetryStatePhase.Failed => (state.FailureKey ?? "telemetry.error.read_failed", InfoBarSeverity.Warning, false),
            _ => (null, InfoBarSeverity.Informational, false),
        };
        var (pillGlyph, pillTone) = state.Phase switch
        {
            LiveTelemetryStatePhase.Reading => (null, Tone.Accent),
            LiveTelemetryStatePhase.Stopping => (null, Tone.Caution),
            LiveTelemetryStatePhase.Loaded => (FluentSymbol.Completed, Tone.Success),
            LiveTelemetryStatePhase.Failed => (FluentSymbol.Warning, Tone.Caution),
            _ => ((string?)null, Tone.Neutral),
        };
        StatusPill.Set(_bandStatus, pillGlyph, LocalizedText.Get(key ?? "telemetry.status.waiting_for_connection"), pillTone);

        if (key is null)
        {
            _banner.IsOpen = false;
            _banner.Content = null;
            _bannerProgress.IsIndeterminate = false;
            return;
        }

        _banner.Severity = severity;
        _banner.Message = LocalizedText.Get(key);
        _banner.Content = null;
        _banner.ActionButton = null;
        _bannerProgress.IsIndeterminate = showsProgress;
        if (showsProgress)
        {
            _banner.Content = _bannerProgress;
        }
        else if (state.Phase == LiveTelemetryStatePhase.Failed)
        {
            // 失敗のときだけ、報告用の番号と再試行の操作を付ける。
            ErrorReport.AttachTo(_banner);
            _banner.ActionButton = _retryButton;
        }

        _banner.IsOpen = true;
    }

    /// <summary>
    /// PID の組と分類が変わったときだけ、タイルと分類のボタンを差分で入れ替えます。
    /// </summary>
    /// <remarks>
    /// 責務: 主・車種専用・選んだ分類のタイルと、分類のボタンを整えます。説明のフライアウトを開いている間などは、
    /// 出し入れだけを待たせて（値は更新する）、閉じたあとにもう一度試します。分類のボタンを押した直後は待たせません。
    /// </remarks>
    /// <param name="state">ライブ取得の状態。</param>
    private void SyncStructure(LiveTelemetryState state)
    {
        var categories = AvailableCategories(state);
        if (!categories.Contains(_selectedCategory) && categories.Count > 0)
        {
            _selectedCategory = categories[0];
        }

        var structure = new (OBDPIDRequest, bool)[state.Samples.Count];
        for (var index = 0; index < structure.Length; index++)
        {
            var sample = state.Samples[index];
            structure[index] = (sample.Request, !string.IsNullOrEmpty(sample.VehicleModelCode));
        }

        if (_structureCategory == _selectedCategory && structure.AsSpan().SequenceEqual(_structure))
        {
            return;
        }

        if (!_forceStructure && _gate.TryDefer(_throttle.Request))
        {
            return;
        }

        _structure = structure;
        _structureCategory = _selectedCategory;

        var vehicleSpecific = state.Samples.Where(sample => !string.IsNullOrEmpty(sample.VehicleModelCode)).ToList();
        var modelCode = vehicleSpecific.FirstOrDefault()?.VehicleModelCode;
        SetBadge(_headerBadge, modelCode);
        SetBadge(_vehicleBadge, modelCode);
        Retained.SetVisible(_vehicleSection, vehicleSpecific.Count > 0);
        _vehicleTiles.Sync(vehicleSpecific);
        _heroTiles.Sync(PrimarySamples(state));
        _chipItems.Sync(categories);
        _categoryTiles.Sync(SamplesIn(state, _selectedCategory));
    }

    /// <summary>値の文字だけを書き換えます。</summary>
    /// <remarks>責務: 作りを変えずに、タイルと分類のボタンの値を最新の読み取り結果にします。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    private void UpdateValues(LiveTelemetryState state)
    {
        var representative = new OBDPIDSample?[5];
        var exact = new bool[5];
        foreach (var sample in state.Samples)
        {
            if (_heroTiles.TryGet(sample.Request, out var hero))
            {
                hero.SetValue(sample);
            }

            if (_vehicleTiles.TryGet(sample.Request, out var vehicle))
            {
                vehicle.SetValue(sample);
            }

            if (_categoryTiles.TryGet(sample.Request, out var tile))
            {
                tile.SetValue(sample);
            }

            // 分類のボタンの代表値：代表 PID があればそれ、なければ最初の結果。
            var category = OBDPIDCategoryExtensions.Category(sample.Request);
            var slot = (int)category;
            if (!exact[slot] && (representative[slot] is null || sample.Request == category.RepresentativeRequest()))
            {
                representative[slot] = sample;
                exact[slot] = sample.Request == category.RepresentativeRequest();
            }
        }

        foreach (var category in OBDPIDCategoryRawValue.AllCases)
        {
            if (_chipItems.TryGet(category, out var chip) && representative[(int)category] is { } sample)
            {
                chip.SetValue($"{NumberText.Decimal(sample.Value, 2)} {sample.Unit}");
            }
        }
    }

    /// <summary>分類のボタンを作ります。</summary>
    /// <remarks>責務: 押すと分類を切り替えるボタンを作ります。押した時点で待たせずにタイルを入れ替えます。</remarks>
    /// <param name="category">分類。</param>
    /// <returns>ボタン。</returns>
    private CategoryChip NewChip(OBDPIDCategory category)
    {
        var chip = new CategoryChip(category, CategoryGlyph(category), LocalizedText.Get(category.NameKey()));
        chip.Click += (_, _) =>
        {
            _selectedCategory = category;
            _forceStructure = true;
            try
            {
                _throttle.FlushNow();
            }
            finally
            {
                _forceStructure = false;
            }

            foreach (var other in _chips.Children.OfType<CategoryChip>())
            {
                other.SetChecked(_selectedCategory);
            }
        };
        return chip;
    }

    /// <summary>車種のバッジを出し入れします。</summary>
    /// <remarks>責務: 型式が変わったときだけバッジを作り直し、なければ消します。</remarks>
    /// <param name="host">置き場。</param>
    /// <param name="modelCode">型式。なければ null。</param>
    private static void SetBadge(ContentControl host, string? modelCode)
    {
        if (host.Tag as string == modelCode)
        {
            return;
        }

        host.Tag = modelCode;
        host.Content = modelCode is null ? null : new VehicleModelBadge(modelCode);
    }

    /// <summary>主の値（回転数・車速）の読み取り結果を返します。</summary>
    /// <remarks>責務: 回転数・車速を優先し、足りなければ先頭から補って最大 2 件にします。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>読み取り結果。</returns>
    private static List<OBDPIDSample> PrimarySamples(LiveTelemetryState state)
    {
        var preferred = PrimaryRequests
            .Select(request => state.Samples.FirstOrDefault(sample => sample.Request == request))
            .OfType<OBDPIDSample>()
            .ToList();
        return preferred.Concat(state.Samples.Where(sample => !preferred.Contains(sample))).Take(2).ToList();
    }

    /// <summary>主の値の記号を返します。</summary>
    /// <remarks>責務: 回転数は計器、車速は車、それ以外は脈拍の記号にします。</remarks>
    /// <param name="sample">読み取り結果。</param>
    /// <returns>記号の文字。</returns>
    private static string OnHeroGlyph(OBDPIDSample sample) =>
        sample.Request == PrimaryRequests[0] ? FluentSymbol.Gauge : sample.Request == PrimaryRequests[1] ? FluentSymbol.Car : FluentSymbol.Pulse;

    /// <summary>値の格子を作ります。</summary>
    /// <remarks>責務: Compact 2 列〜Wide 6 列の値のタイルの格子を作ります。</remarks>
    /// <returns>格子。</returns>
    private static AdaptiveGrid TileGrid()
    {
        var grid = new AdaptiveGrid { MinItemWidth = 200 };
        grid.SetMaxColumns(2, 3, 4, 6);
        return grid;
    }

    /// <summary>値がある分類を返します。</summary>
    /// <remarks>責務: 分類の宣言順のうち、読み取り結果がある分類を選びます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <returns>分類。</returns>
    private static List<OBDPIDCategory> AvailableCategories(LiveTelemetryState state)
    {
        var present = new bool[5];
        foreach (var sample in state.Samples)
        {
            present[(int)OBDPIDCategoryExtensions.Category(sample.Request)] = true;
        }

        return OBDPIDCategoryRawValue.AllCases.Where(category => present[(int)category]).ToList();
    }

    /// <summary>分類の読み取り結果を返します。</summary>
    /// <remarks>責務: PID の分類が一致する読み取り結果を選びます。</remarks>
    /// <param name="state">ライブ取得の状態。</param>
    /// <param name="category">分類。</param>
    /// <returns>読み取り結果。</returns>
    private static List<OBDPIDSample> SamplesIn(LiveTelemetryState state, OBDPIDCategory category) =>
        state.Samples.Where(sample => OBDPIDCategoryExtensions.Category(sample.Request) == category).ToList();

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

/// <summary>分類の切り替えボタンです（記号・名前・代表値）。一度作ったら値の文字だけ差し替えます。</summary>
internal sealed class CategoryChip : ToggleButton
{
    /// <summary>代表値の文字です。</summary>
    private readonly TextBlock _value = Typography.Text(string.Empty, TextStyles.Monospace, wraps: false);

    /// <summary>ボタンを作ります。</summary>
    /// <param name="category">分類。</param>
    /// <param name="glyph">記号。</param>
    /// <param name="name">分類の名前。</param>
    public CategoryChip(OBDPIDCategory category, string glyph, string name)
    {
        Category = category;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Space.S + 1 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        content.Children.Add(Typography.Strong(name, TextStyles.Caption));
        content.Children.Add(_value);
        Content = content;
        MinHeight = Metrics.ControlHeight + Space.XS;
        CornerRadius = new CornerRadius(Radius.Pill);
        Padding = new Thickness(Space.M + 2, Space.XS + 2, Space.M + 2, Space.XS + 2);
        AutomationProperties.SetName(this, name);
    }

    /// <summary>分類です。</summary>
    public OBDPIDCategory Category { get; }

    /// <summary>代表値の文字を差し替えます。</summary>
    /// <remarks>責務: 違うときだけ設定します。</remarks>
    /// <param name="text">値と単位。</param>
    public void SetValue(string text) => Retained.SetText(_value, text);

    /// <summary>選んでいるかどうかを合わせます。</summary>
    /// <remarks>責務: 選んだ分類と同じときだけ押された状態にします（押し直して外れた状態も戻す）。</remarks>
    /// <param name="selected">選んでいる分類。</param>
    public void SetChecked(OBDPIDCategory selected)
    {
        var isChecked = selected == Category;
        if (IsChecked != isChecked)
        {
            IsChecked = isChecked;
        }
    }
}

/// <summary>1 つの PID の値のタイルです。名前・値・単位・説明のボタンを持ち、値の文字だけを差し替えます。</summary>
internal sealed class SampleTile : UserControl
{
    /// <summary>値の小数の桁です。</summary>
    private readonly int _decimals;

    /// <summary>名前の文字です。</summary>
    private readonly TextBlock _name;

    /// <summary>値の文字です。</summary>
    private readonly TextBlock _value;

    /// <summary>単位の文字です。</summary>
    private readonly TextBlock _unit;

    /// <summary>説明のフライアウトです（開くときに中身を作る）。</summary>
    private readonly Flyout _help = new();

    /// <summary>説明に使う読み取り結果です。</summary>
    private OBDPIDSample _sample;

    /// <summary>タイルを作ります。</summary>
    /// <param name="sample">読み取り結果。</param>
    /// <param name="hero">計器帯の大きなタイルなら true。</param>
    /// <param name="heroGlyph">大きなタイルの記号。</param>
    public SampleTile(OBDPIDSample sample, bool hero, string? heroGlyph)
    {
        _sample = sample;
        _decimals = hero ? 1 : 2;
        _name = Typography.Text(string.Empty, TextStyles.SecondaryCaption);
        _name.MaxLines = 2;
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _value = Typography.Metric(string.Empty, hero ? TextStyles.MetricHero : TextStyles.MetricValue);
        _unit = Typography.Text(string.Empty, TextStyles.Monospace, DesignTokens.Foreground(Tone.Accent), wraps: false);
        _unit.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.Margin = new Thickness(0, 0, 0, hero ? Space.S : Space.XS);

        var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = hero ? Space.S : Space.XS + 2 };
        valueRow.Children.Add(_value);
        valueRow.Children.Add(_unit);

        var help = HelpButton();
        UIElement content;
        if (hero)
        {
            var grid = new Grid { ColumnSpacing = Space.L };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(IconTile.Create(heroGlyph ?? FluentSymbol.Pulse, Tone.Accent, Metrics.IconTileLarge));
            var texts = new StackPanel { Spacing = Space.XS, VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(_name);
            texts.Children.Add(valueRow);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            Grid.SetColumn(help, 2);
            grid.Children.Add(help);
            content = grid;
        }
        else
        {
            var stack = new StackPanel { Spacing = Space.S, MinHeight = 64 };
            var head = new Grid { ColumnSpacing = Space.XS };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(_name);
            Grid.SetColumn(help, 1);
            head.Children.Add(help);
            stack.Children.Add(head);
            stack.Children.Add(valueRow);
            content = stack;
        }

        Content = SurfaceCard.Create(content, hero ? Tone.Accent : null, hero ? Space.XL : Space.L, tinted: hero);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Bind(sample);
    }

    /// <summary>読み取り結果に合わせて名前と単位を直します。</summary>
    /// <remarks>責務: 名前・単位・説明の元を最新にします（値の文字は <see cref="SetValue"/>）。</remarks>
    /// <param name="sample">読み取り結果。</param>
    public void Bind(OBDPIDSample sample)
    {
        _sample = sample;
        Retained.SetText(_name, LocalizedText.Get(sample.NameKey));
        Retained.SetText(_unit, sample.Unit);
        SetValue(sample);
    }

    /// <summary>値の文字を差し替えます。</summary>
    /// <remarks>責務: 違うときだけ設定します。</remarks>
    /// <param name="sample">読み取り結果。</param>
    public void SetValue(OBDPIDSample sample) => Retained.SetText(_value, NumberText.Decimal(sample.Value, _decimals));

    /// <summary>PID の説明のボタンを作ります。</summary>
    /// <remarks>責務: 押すと PID の概要・高いとき・低いとき・相関の説明を出すボタンを作ります（中身は開くときに作る）。</remarks>
    /// <returns>ボタン。</returns>
    private Button HelpButton()
    {
        _help.Opening += (_, _) => _help.Content = HelpBody(_sample);
        var button = new Button
        {
            Content = new FontIcon { Glyph = FluentSymbol.Info, FontSize = 14 },
            Style = DesignTokens.Style("SubtleButtonStyle"),
            Padding = new Thickness(Space.S - 2),
            VerticalAlignment = VerticalAlignment.Top,
            Flyout = _help,
        };
        AutomationProperties.SetName(button, LocalizedText.Get("telemetry.help.open"));
        ToolTipService.SetToolTip(button, LocalizedText.Get("telemetry.help.open"));
        return button;
    }

    /// <summary>説明の中身を作ります。</summary>
    /// <remarks>責務: PID の名前と、概要・高いとき・低いとき・相関の説明を並べます。</remarks>
    /// <param name="sample">読み取り結果。</param>
    /// <returns>中身。</returns>
    private static StackPanel HelpBody(OBDPIDSample sample)
    {
        var body = new StackPanel { Spacing = Space.M, MaxWidth = 360 };
        body.Children.Add(Typography.Text(LocalizedText.Get(sample.NameKey), TextStyles.BodyStrong));
        foreach (var (title, value) in new[]
        {
            ("telemetry.help.summary", sample.SummaryKey),
            ("telemetry.help.high", sample.HighValueKey),
            ("telemetry.help.low", sample.LowValueKey),
            ("telemetry.help.correlation", sample.CorrelationKey),
        })
        {
            var row = new StackPanel { Spacing = 3 };
            row.Children.Add(Typography.Strong(LocalizedText.Get(title), TextStyles.SecondaryCaption));
            row.Children.Add(Typography.Text(LocalizedText.Get(value), TextStyles.Body));
            body.Children.Add(row);
        }

        return body;
    }
}
