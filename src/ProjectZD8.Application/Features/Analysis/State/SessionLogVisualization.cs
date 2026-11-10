using System.Globalization;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Analysis.State;

/// <summary>PID時系列の1系列を用途別表示と折れ線グラフへ渡す状態です。</summary>
public sealed record SessionPIDSeries
{
    /// <summary>折れ線上の1観測点です。</summary>
    /// <param name="Id">Rawログ順序に由来する安定識別子です。</param>
    /// <param name="ObservedAt">応答を観測した日時です。</param>
    /// <param name="Value">数式変換後の値です。</param>
    public sealed record Point(long Id, DateTimeOffset ObservedAt, double Value);

    /// <summary>ServiceとPIDからなる系列識別子です。</summary>
    public OBDPIDRequest Id { get; }
    /// <summary>PID定義が提供する表示名キーです。</summary>
    public string? NameKey { get; }
    /// <summary>数値の単位です。</summary>
    public string Unit { get; }
    /// <summary>車種専用PIDの場合に表示する型式です。</summary>
    public string? VehicleModelCode { get; }
    /// <summary>元タイムラインに存在するこのPIDの数値化済み総件数です。</summary>
    public int TotalPointCount { get; }
    /// <summary>全数値化済み観測の最小値です。</summary>
    public double MinimumValue { get; }
    /// <summary>全数値化済み観測の算術平均です。</summary>
    public double AverageValue { get; }
    /// <summary>全数値化済み観測の最大値です。</summary>
    public double MaximumValue { get; }
    /// <summary>セッション内で最後に観測した数値です。</summary>
    public double LatestValue { get; }
    /// <summary>PIDの意味とグラフから観察できる内容を説明するローカライズキーです。</summary>
    public string InterpretationKey { get; }
    /// <summary>セッション内の観測順に並ぶ表示用代表点です。</summary>
    public IReadOnlyList<Point> Points { get; }

    /// <summary>1件のPID系列を生成します。</summary>
    /// <remarks>責務: 1件のService/PIDの集計値と代表点を不変な表示状態へ固定します（Swift 版のメンバーワイズ初期化子に相当）。</remarks>
    /// <param name="id">ServiceとPIDからなる系列識別子。</param>
    /// <param name="nameKey">PID定義が提供する表示名キー。</param>
    /// <param name="unit">数値の単位。</param>
    /// <param name="vehicleModelCode">車種専用PIDの場合に表示する型式。</param>
    /// <param name="totalPointCount">元タイムラインに存在するこのPIDの数値化済み総件数。</param>
    /// <param name="minimumValue">全数値化済み観測の最小値。</param>
    /// <param name="averageValue">全数値化済み観測の算術平均。</param>
    /// <param name="maximumValue">全数値化済み観測の最大値。</param>
    /// <param name="latestValue">セッション内で最後に観測した数値。</param>
    /// <param name="interpretationKey">PIDの意味とグラフから観察できる内容を説明するローカライズキー。</param>
    /// <param name="points">セッション内の観測順に並ぶ表示用代表点。</param>
    public SessionPIDSeries(
        OBDPIDRequest id,
        string? nameKey,
        string unit,
        string? vehicleModelCode,
        int totalPointCount,
        double minimumValue,
        double averageValue,
        double maximumValue,
        double latestValue,
        string interpretationKey,
        IReadOnlyList<Point> points)
    {
        Id = id;
        NameKey = nameKey;
        Unit = unit;
        VehicleModelCode = vehicleModelCode;
        TotalPointCount = totalPointCount;
        MinimumValue = minimumValue;
        AverageValue = averageValue;
        MaximumValue = maximumValue;
        LatestValue = latestValue;
        InterpretationKey = interpretationKey;
        Points = points.ToArray();
    }

    /// <summary>代表点を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく代表点を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(SessionPIDSeries? other) =>
        other is not null
        && Id == other.Id
        && string.Equals(NameKey, other.NameKey, StringComparison.Ordinal)
        && string.Equals(Unit, other.Unit, StringComparison.Ordinal)
        && string.Equals(VehicleModelCode, other.VehicleModelCode, StringComparison.Ordinal)
        && TotalPointCount == other.TotalPointCount
        && MinimumValue == other.MinimumValue
        && AverageValue == other.AverageValue
        && MaximumValue == other.MaximumValue
        && LatestValue == other.LatestValue
        && string.Equals(InterpretationKey, other.InterpretationKey, StringComparison.Ordinal)
        && Points.SequenceEqual(other.Points);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 代表点を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(NameKey);
        hash.Add(Unit);
        hash.Add(VehicleModelCode);
        hash.Add(TotalPointCount);
        hash.Add(MinimumValue);
        hash.Add(AverageValue);
        hash.Add(MaximumValue);
        hash.Add(LatestValue);
        hash.Add(InterpretationKey);
        foreach (var point in Points)
        {
            hash.Add(point);
        }
        return hash.ToHashCode();
    }
}

/// <summary>観測値が属した数値帯と推定滞在時間です。</summary>
public sealed record SessionDurationBand
{
    /// <summary>数値帯の下限を安定識別子として使用します。</summary>
    public double Id => LowerBound;
    /// <summary>数値帯に含める下限値です。</summary>
    public double LowerBound { get; }
    /// <summary>数値帯から除外する上限値です。</summary>
    public double UpperBound { get; }
    /// <summary>長い欠測を除いた観測間隔の合計秒数です。</summary>
    public double Duration { get; }
    /// <summary>同じPIDで集計できた全時間に占める割合です。</summary>
    public double Proportion { get; }

    /// <summary>1件の数値帯を生成します。</summary>
    /// <remarks>責務: 1件の数値帯の範囲と滞在時間を不変な表示状態へ固定します（Swift 版のメンバーワイズ初期化子に相当）。</remarks>
    /// <param name="lowerBound">数値帯に含める下限値。</param>
    /// <param name="upperBound">数値帯から除外する上限値。</param>
    /// <param name="duration">長い欠測を除いた観測間隔の合計秒数。</param>
    /// <param name="proportion">同じPIDで集計できた全時間に占める割合。</param>
    public SessionDurationBand(double lowerBound, double upperBound, double duration, double proportion)
    {
        LowerBound = lowerBound;
        UpperBound = upperBound;
        Duration = duration;
        Proportion = proportion;
    }
}

/// <summary>1セッションの走行傾向を表す集計状態です。</summary>
public sealed record SessionPerformanceSummary
{
    /// <summary>データがない初期状態です。</summary>
    public static SessionPerformanceSummary Empty { get; } = new(
        speedBands: Array.Empty<SessionDurationBand>(),
        rpmBands: Array.Empty<SessionDurationBand>(),
        estimatedMovingDuration: null,
        estimatedDistanceKilometers: null);

    /// <summary>車速を10 km/h単位へ集計した上位帯です。</summary>
    public IReadOnlyList<SessionDurationBand> SpeedBands { get; }
    /// <summary>回転数を500 rpm単位へ集計した上位帯です。</summary>
    public IReadOnlyList<SessionDurationBand> RpmBands { get; }
    /// <summary>車速が1 km/h以上だった信頼できる観測間隔の合計です。</summary>
    public double? EstimatedMovingDuration { get; }
    /// <summary>車速観測を時間積分した参考走行距離です。</summary>
    public double? EstimatedDistanceKilometers { get; }

    /// <summary>走行サマリーを生成します。</summary>
    /// <remarks>責務: 速度帯、回転域、走行時間、参考距離を不変な表示状態へ固定します（Swift 版のメンバーワイズ初期化子に相当）。</remarks>
    /// <param name="speedBands">車速を10 km/h単位へ集計した上位帯。</param>
    /// <param name="rpmBands">回転数を500 rpm単位へ集計した上位帯。</param>
    /// <param name="estimatedMovingDuration">車速が1 km/h以上だった信頼できる観測間隔の合計。</param>
    /// <param name="estimatedDistanceKilometers">車速観測を時間積分した参考走行距離。</param>
    public SessionPerformanceSummary(
        IReadOnlyList<SessionDurationBand> speedBands,
        IReadOnlyList<SessionDurationBand> rpmBands,
        double? estimatedMovingDuration,
        double? estimatedDistanceKilometers)
    {
        SpeedBands = speedBands.ToArray();
        RpmBands = rpmBands.ToArray();
        EstimatedMovingDuration = estimatedMovingDuration;
        EstimatedDistanceKilometers = estimatedDistanceKilometers;
    }

    /// <summary>数値帯を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく数値帯を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(SessionPerformanceSummary? other) =>
        other is not null
        && SpeedBands.SequenceEqual(other.SpeedBands)
        && RpmBands.SequenceEqual(other.RpmBands)
        && EstimatedMovingDuration == other.EstimatedMovingDuration
        && EstimatedDistanceKilometers == other.EstimatedDistanceKilometers;

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 数値帯を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var band in SpeedBands)
        {
            hash.Add(band);
        }
        foreach (var band in RpmBands)
        {
            hash.Add(band);
        }
        hash.Add(EstimatedMovingDuration);
        hash.Add(EstimatedDistanceKilometers);
        return hash.ToHashCode();
    }
}

/// <summary>部品・系統の表示分類です。</summary>
/// <remarks>Swift の <c>SessionComponentInsight.Component</c> に対応します。C# では同名のプロパティ <see cref="SessionComponentInsight.Component"/> と入れ子にできないため外へ出しています。</remarks>
public enum SessionComponentInsightComponent
{
    /// <summary>ECU電源電圧から観察する充電・電源系です。</summary>
    Electrical,
    /// <summary>冷却水温または油温から観察する冷却・潤滑系です。</summary>
    Thermal,
    /// <summary>燃料補正または吸入空気量から観察する燃料・吸気系です。</summary>
    FuelAndIntake,
    /// <summary>触媒またはDPF温度から観察する排気後処理系です。</summary>
    Exhaust,
    /// <summary>故障コード消去後またはMIL点灯中の累積距離から観察する診断履歴です。</summary>
    Diagnostics,
}

/// <summary><see cref="SessionComponentInsightComponent"/> の raw value を扱います。</summary>
public static class SessionComponentInsightComponentRawValue
{
    /// <summary>部品・系統分類を raw value の文字列へ変換します。</summary>
    /// <remarks>責務: 1件の部品・系統分類を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する分類。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this SessionComponentInsightComponent value) => value switch
    {
        SessionComponentInsightComponent.Electrical => "electrical",
        SessionComponentInsightComponent.Thermal => "thermal",
        SessionComponentInsightComponent.FuelAndIntake => "fuelAndIntake",
        SessionComponentInsightComponent.Exhaust => "exhaust",
        SessionComponentInsightComponent.Diagnostics => "diagnostics",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value の文字列から部品・系統分類を復元します。</summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して部品・系統分類へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた分類。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out SessionComponentInsightComponent value)
    {
        switch (rawValue)
        {
            case "electrical": value = SessionComponentInsightComponent.Electrical; return true;
            case "thermal": value = SessionComponentInsightComponent.Thermal; return true;
            case "fuelAndIntake": value = SessionComponentInsightComponent.FuelAndIntake; return true;
            case "exhaust": value = SessionComponentInsightComponent.Exhaust; return true;
            case "diagnostics": value = SessionComponentInsightComponent.Diagnostics; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>車両部品・系統に結び付く1件の観測要約です。</summary>
public sealed record SessionComponentInsight
{
    /// <summary>対象PIDを安定識別子として使用します。</summary>
    public OBDPIDRequest Id => Series.Id;
    /// <summary>観察対象の部品・系統です。</summary>
    public SessionComponentInsightComponent Component { get; }
    /// <summary>集計値と説明を提供するPID系列です。</summary>
    public SessionPIDSeries Series { get; }

    /// <summary>1件の観察候補を生成します。</summary>
    /// <remarks>責務: 1件の部品・系統分類と根拠PID系列を不変な表示状態へ固定します（Swift 版のメンバーワイズ初期化子に相当）。</remarks>
    /// <param name="component">観察対象の部品・系統。</param>
    /// <param name="series">集計値と説明を提供するPID系列。</param>
    public SessionComponentInsight(SessionComponentInsightComponent component, SessionPIDSeries series)
    {
        Component = component;
        Series = series;
    }
}

/// <summary>2件のPIDを近接時刻で対応付けた散布図状態です。</summary>
public sealed record SessionPIDRelationship
{
    /// <summary>散布図上の1対応点です。</summary>
    /// <param name="Id">対応元系列のRawログ順序に由来する安定識別子です。</param>
    /// <param name="X">横軸PIDの値です。</param>
    /// <param name="Y">縦軸PIDの値です。</param>
    public sealed record Point(long Id, double X, double Y);

    /// <summary>PID組を表す安定識別子です。</summary>
    public string Id { get; }
    /// <summary>横軸へ使用するPID系列です。</summary>
    public SessionPIDSeries XSeries { get; }
    /// <summary>縦軸へ使用するPID系列です。</summary>
    public SessionPIDSeries YSeries { get; }
    /// <summary>近接時刻で対応付けられた散布点です。</summary>
    public IReadOnlyList<Point> Points { get; }

    /// <summary>1件の散布図状態を生成します。</summary>
    /// <remarks>責務: 1件のPID組と対応点を不変な表示状態へ固定します（Swift 版のメンバーワイズ初期化子に相当）。</remarks>
    /// <param name="id">PID組を表す安定識別子。</param>
    /// <param name="xSeries">横軸へ使用するPID系列。</param>
    /// <param name="ySeries">縦軸へ使用するPID系列。</param>
    /// <param name="points">近接時刻で対応付けられた散布点。</param>
    public SessionPIDRelationship(string id, SessionPIDSeries xSeries, SessionPIDSeries ySeries, IReadOnlyList<Point> points)
    {
        Id = id;
        XSeries = xSeries;
        YSeries = ySeries;
        Points = points.ToArray();
    }

    /// <summary>散布点を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく散布点を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(SessionPIDRelationship? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && XSeries == other.XSeries
        && YSeries == other.YSeries
        && Points.SequenceEqual(other.Points);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 散布点を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(XSeries);
        hash.Add(YSeries);
        foreach (var point in Points)
        {
            hash.Add(point);
        }
        return hash.ToHashCode();
    }
}

/// <summary>バックグラウンド集計から一括反映するグラフ表示状態です。</summary>
public sealed record SessionLogVisualizationSnapshot
{
    /// <summary>Service/PID単位の折れ線系列です。</summary>
    public IReadOnlyList<SessionPIDSeries> Series { get; }
    /// <summary>速度帯と回転域の推定滞在時間をまとめた走行サマリーです。</summary>
    public SessionPerformanceSummary PerformanceSummary { get; }
    /// <summary>取得できたPIDを部品・系統へ結び付けた観察候補です。</summary>
    public IReadOnlyList<SessionComponentInsight> ComponentInsights { get; }
    /// <summary>近接時刻で対応付けた代表PID組です。</summary>
    public IReadOnlyList<SessionPIDRelationship> Relationships { get; }

    /// <summary>グラフ表示状態を生成します。</summary>
    /// <remarks>責務: 各表示で共有するグラフ表示状態を不変値へ固定します（Swift 版のメンバーワイズ初期化子に相当）。</remarks>
    /// <param name="series">Service/PID単位の折れ線系列。</param>
    /// <param name="performanceSummary">速度帯と回転域の推定滞在時間をまとめた走行サマリー。</param>
    /// <param name="componentInsights">取得できたPIDを部品・系統へ結び付けた観察候補。</param>
    /// <param name="relationships">近接時刻で対応付けた代表PID組。</param>
    public SessionLogVisualizationSnapshot(
        IReadOnlyList<SessionPIDSeries> series,
        SessionPerformanceSummary performanceSummary,
        IReadOnlyList<SessionComponentInsight> componentInsights,
        IReadOnlyList<SessionPIDRelationship> relationships)
    {
        Series = series.ToArray();
        PerformanceSummary = performanceSummary;
        ComponentInsights = componentInsights.ToArray();
        Relationships = relationships.ToArray();
    }

    /// <summary>各一覧を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく各一覧を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(SessionLogVisualizationSnapshot? other) =>
        other is not null
        && Series.SequenceEqual(other.Series)
        && PerformanceSummary == other.PerformanceSummary
        && ComponentInsights.SequenceEqual(other.ComponentInsights)
        && Relationships.SequenceEqual(other.Relationships);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 各一覧の件数と走行サマリーからハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Series.Count, PerformanceSummary, ComponentInsights.Count, Relationships.Count);
}

/// <summary>数値化済みPID時系列を用途別系列と代表的な相関候補へ変換します。</summary>
public static class SessionLogVisualizationBuilder
{
    /// <summary>1系列のグラフへ渡す最大代表点数です。</summary>
    private const int MaximumChartPointCount = 600;

    /// <summary>散布図で比較する標準PIDの組です。</summary>
    private static readonly (OBDPIDRequest X, OBDPIDRequest Y)[] RelationshipPairs =
    [
        (new(0x01, 0x0C), new(0x01, 0x0D)),
        (new(0x01, 0x0C), new(0x01, 0x04)),
        (new(0x01, 0x0C), new(0x01, 0x10)),
        (new(0x01, 0x11), new(0x01, 0x04)),
        (new(0x01, 0x06), new(0x01, 0x07)),
        (new(0x01, 0x0D), new(0x21, 0x17)),
    ];

    /// <summary>解析済みタイムラインから全グラフ表示状態を一度だけ構築します。</summary>
    /// <remarks>責務: 1件の解析済みタイムラインを用途別、折れ線、散布図の表示スナップショットへ変換します。</remarks>
    /// <param name="timeline">Raw順序と数値化結果を保持する解析済みサンプル。</param>
    /// <returns>各表示で共有する不変なグラフ表示状態。</returns>
    public static SessionLogVisualizationSnapshot Build(IReadOnlyList<SessionLogAnalysisState.TimelineSample> timeline)
    {
        var series = Series(timeline);
        return new SessionLogVisualizationSnapshot(
            series: series,
            performanceSummary: PerformanceSummary(timeline),
            componentInsights: ComponentInsights(series),
            relationships: Relationships(series));
    }

    /// <summary>数値化済みサンプルをService/PID単位の折れ線系列へまとめます。</summary>
    /// <remarks>責務: 1件の解析済みタイムラインを数値化可能なPID系列の一覧へ変換します。</remarks>
    /// <param name="timeline">Raw順序と数値化結果を保持する解析済みサンプル。</param>
    /// <returns>Service/PID順で安定整列した数値系列。</returns>
    public static IReadOnlyList<SessionPIDSeries> Series(IReadOnlyList<SessionLogAnalysisState.TimelineSample> timeline)
    {
        var decoded = new List<(OBDPIDRequest Request, SessionLogAnalysisState.TimelineSample Sample, double Value)>();
        foreach (var sample in timeline)
        {
            if (sample.Value is not { } value)
            {
                continue;
            }
            decoded.Add((new OBDPIDRequest(sample.Service, sample.Pid), sample, value));
        }

        return decoded
            .GroupBy(item => item.Request)
            .Select(group =>
            {
                var samples = group.ToList();
                var first = samples[0].Sample;
                var minimum = samples[0].Value;
                var maximum = samples[0].Value;
                var sum = 0.0;
                foreach (var item in samples)
                {
                    sum += item.Value;
                    if (item.Value < minimum)
                    {
                        minimum = item.Value;
                    }
                    if (item.Value > maximum)
                    {
                        maximum = item.Value;
                    }
                }
                return new SessionPIDSeries(
                    id: group.Key,
                    nameKey: first.NameKey,
                    unit: first.Unit ?? "",
                    vehicleModelCode: first.VehicleModelCode,
                    totalPointCount: samples.Count,
                    minimumValue: minimum,
                    averageValue: sum / samples.Count,
                    maximumValue: maximum,
                    latestValue: samples[^1].Value,
                    interpretationKey: InterpretationKey(group.Key),
                    points: SampledPoints(samples
                        .Select(item => new SessionPIDSeries.Point(item.Sample.Sequence, item.Sample.ObservedAt, item.Value))
                        .ToList()));
            })
            .OrderBy(item => item.Id.Service)
            .ThenBy(item => item.Id.Pid)
            .ToList();
    }

    /// <summary>PIDの意味と折れ線から観察できる内容を説明キーへ変換します。</summary>
    /// <remarks>責務: 1件の標準PID識別子を推測を含まない観察ガイドへ分類します。</remarks>
    /// <param name="request">説明するService/PID識別子。</param>
    /// <returns>PID固有または用途分類の説明ローカライズキー。</returns>
    private static string InterpretationKey(OBDPIDRequest request)
    {
        if (request == new OBDPIDRequest(0x21, 0x02)) { return "analysis.trend.guide.zd8_distance"; }
        if (request == new OBDPIDRequest(0x21, 0x17)) { return "analysis.trend.guide.zd8_atf"; }
        if (request.Service != 0x01) { return "analysis.trend.guide.generic"; }
        return request.Pid switch
        {
            0x0C => "analysis.trend.guide.rpm",
            0x0D => "analysis.trend.guide.speed",
            0x04 => "analysis.trend.guide.load",
            0x05 => "analysis.trend.guide.coolant",
            >= 0x06 and <= 0x09 => "analysis.trend.guide.fuel_trim",
            0x10 => "analysis.trend.guide.maf",
            0x11 or 0x45 or (>= 0x47 and <= 0x4C) or 0x5A or 0x8D => "analysis.trend.guide.throttle",
            0x1F => "analysis.trend.guide.runtime",
            0x21 => "analysis.trend.guide.mil_distance",
            0x31 => "analysis.trend.guide.codes_cleared_distance",
            0xA6 => "analysis.trend.guide.odometer",
            (>= 0x3C and <= 0x3F) or 0x7C => "analysis.trend.guide.exhaust_temperature",
            0x42 => "analysis.trend.guide.voltage",
            0x5C => "analysis.trend.guide.oil_temperature",
            _ => "analysis.trend.guide.generic",
        };
    }

    /// <summary>速度帯、回転域、車速積算距離をタイムラインから集計します。</summary>
    /// <remarks>責務: 1件の解析済みタイムラインを走行サマリー表示状態へ変換します。</remarks>
    /// <param name="timeline">数値化結果を含む全観測。</param>
    /// <returns>長い欠測区間を除外した時間帯ランキングと参考距離。</returns>
    private static SessionPerformanceSummary PerformanceSummary(IReadOnlyList<SessionLogAnalysisState.TimelineSample> timeline)
    {
        var speed = DecodedPoints(new OBDPIDRequest(0x01, 0x0D), timeline);
        var rpm = DecodedPoints(new OBDPIDRequest(0x01, 0x0C), timeline);
        return new SessionPerformanceSummary(
            speedBands: DurationBands(speed, width: 10),
            rpmBands: DurationBands(rpm, width: 500),
            estimatedMovingDuration: EstimatedMovingDuration(speed),
            estimatedDistanceKilometers: EstimatedDistance(speed));
    }

    /// <summary>指定PIDの数値化済み観測を時刻順で返します。</summary>
    /// <remarks>責務: 全タイムラインから1件のPIDに属する有限数値観測だけを抽出します。</remarks>
    /// <param name="request">抽出するService/PID識別子。</param>
    /// <param name="timeline">抽出元の全観測。</param>
    /// <returns>観測時刻で昇順にした有限数値点。</returns>
    private static List<SessionPIDSeries.Point> DecodedPoints(
        OBDPIDRequest request,
        IReadOnlyList<SessionLogAnalysisState.TimelineSample> timeline)
    {
        var points = new List<SessionPIDSeries.Point>();
        foreach (var sample in timeline)
        {
            if (sample.Service != request.Service || sample.Pid != request.Pid
                || sample.Value is not { } value || !double.IsFinite(value))
            {
                continue;
            }
            points.Add(new SessionPIDSeries.Point(sample.Sequence, sample.ObservedAt, value));
        }
        return points.OrderBy(point => point.ObservedAt).ToList();
    }

    /// <summary>一定幅の数値帯ごとに信頼できる観測間隔を合計します。</summary>
    /// <remarks>責務: 1件の時系列を長い欠測を含めない推定滞在時間ランキングへ変換します。</remarks>
    /// <param name="points">時刻順の数値観測。</param>
    /// <param name="width">1区間の数値幅。</param>
    /// <returns>滞在時間の長い順で最大5件の数値帯。</returns>
    private static IReadOnlyList<SessionDurationBand> DurationBands(List<SessionPIDSeries.Point> points, double width)
    {
        var intervals = ReliableIntervals(points);
        var durations = new Dictionary<double, double>();
        foreach (var (point, duration) in intervals)
        {
            var lowerBound = Math.Floor(Math.Max(point.Value, 0) / width) * width;
            durations[lowerBound] = durations.GetValueOrDefault(lowerBound) + duration;
        }
        var total = 0.0;
        foreach (var duration in durations.Values)
        {
            total += duration;
        }
        if (!(total > 0))
        {
            return Array.Empty<SessionDurationBand>();
        }
        var bands = new List<SessionDurationBand>(durations.Count);
        foreach (var (lowerBound, duration) in durations)
        {
            bands.Add(new SessionDurationBand(
                lowerBound: lowerBound,
                upperBound: lowerBound + width,
                duration: duration,
                proportion: duration / total));
        }
        bands.Sort((lhs, rhs) =>
            lhs.Duration == rhs.Duration
                ? lhs.LowerBound.CompareTo(rhs.LowerBound)
                : rhs.Duration.CompareTo(lhs.Duration));
        return bands.Take(5).ToList();
    }

    /// <summary>代表的な観測周期から外れる長い空白を除いた隣接間隔を返します。</summary>
    /// <remarks>責務: 1件の時系列を継続観測とみなせる点と秒数の組へ変換します。</remarks>
    /// <param name="points">時刻順の数値観測。</param>
    /// <returns>中央観測周期の3倍以下かつ10秒以下の正の隣接間隔。</returns>
    private static List<(SessionPIDSeries.Point Point, double Gap)> ReliableIntervals(List<SessionPIDSeries.Point> points)
    {
        var result = new List<(SessionPIDSeries.Point Point, double Gap)>();
        if (points.Count < 2)
        {
            return result;
        }
        var gaps = new List<double>();
        for (var index = 0; index + 1 < points.Count; index++)
        {
            var gap = TimeIntervalSince(points[index + 1].ObservedAt, points[index].ObservedAt);
            if (gap > 0)
            {
                gaps.Add(gap);
            }
        }
        if (gaps.Count == 0)
        {
            return result;
        }
        gaps.Sort();
        var median = gaps[gaps.Count / 2];
        var maximumGap = Math.Min(Math.Max(median * 3, 1), 10);
        for (var index = 0; index + 1 < points.Count; index++)
        {
            var current = points[index];
            var gap = TimeIntervalSince(points[index + 1].ObservedAt, current.ObservedAt);
            if (gap > 0 && gap <= maximumGap)
            {
                result.Add((current, gap));
            }
        }
        return result;
    }

    /// <summary>車速と信頼できる観測間隔から走行距離を台形積分します。</summary>
    /// <remarks>責務: 1件の車速時系列を欠測区間を含まない参考走行距離へ変換します。</remarks>
    /// <param name="points">時刻順の車速観測。</param>
    /// <returns>積分できた場合のキロメートル値。</returns>
    private static double? EstimatedDistance(List<SessionPIDSeries.Point> points)
    {
        var intervals = ReliableIntervals(points);
        if (intervals.Count == 0)
        {
            return null;
        }
        var indexed = points.ToDictionary(point => point.Id);
        var ordered = new Dictionary<long, SessionPIDSeries.Point>();
        for (var offset = 0; offset < points.Count; offset++)
        {
            if (offset + 1 >= points.Count)
            {
                continue;
            }
            ordered[points[offset].Id] = points[offset + 1];
        }
        var kilometers = 0.0;
        foreach (var (point, gap) in intervals)
        {
            if (!indexed.ContainsKey(point.Id) || !ordered.TryGetValue(point.Id, out var next))
            {
                continue;
            }
            kilometers = kilometers + Math.Max(point.Value + next.Value, 0) / 2 * gap / 3_600;
        }
        return kilometers;
    }

    /// <summary>走行状態だった信頼できる車速観測間隔を合計します。</summary>
    /// <remarks>責務: 1件の車速時系列を1 km/h以上だった参考走行時間へ変換します。</remarks>
    /// <param name="points">時刻順の車速観測。</param>
    /// <returns>集計できた場合の推定走行秒数。</returns>
    private static double? EstimatedMovingDuration(List<SessionPIDSeries.Point> points)
    {
        var intervals = ReliableIntervals(points);
        if (intervals.Count == 0)
        {
            return null;
        }
        var result = 0.0;
        foreach (var (point, gap) in intervals)
        {
            result = result + (point.Value >= 1 ? gap : 0);
        }
        return result;
    }

    /// <summary>取得済み系列から部品・系統単位で説明できるPIDを抽出します。</summary>
    /// <remarks>責務: 1件のPID系列一覧を根拠PID付きの部品・系統観察候補へ変換します。</remarks>
    /// <param name="series">数値化済みPID系列。</param>
    /// <returns>電源、熱、吸排気の順に安定整列した観察候補。</returns>
    private static IReadOnlyList<SessionComponentInsight> ComponentInsights(IReadOnlyList<SessionPIDSeries> series)
    {
        var insights = new List<SessionComponentInsight>();
        foreach (var item in series)
        {
            SessionComponentInsightComponent? component = (item.Id.Service, item.Id.Pid) switch
            {
                (0x01, 0x42) => SessionComponentInsightComponent.Electrical,
                (0x01, 0x05) or (0x01, 0x5C) or (0x21, 0x17) => SessionComponentInsightComponent.Thermal,
                (0x01, >= 0x06 and <= 0x09) or (0x01, 0x10) => SessionComponentInsightComponent.FuelAndIntake,
                (0x01, >= 0x3C and <= 0x3F) or (0x01, 0x7C) => SessionComponentInsightComponent.Exhaust,
                (0x01, 0x21) or (0x01, 0x31) or (0x21, 0x02) => SessionComponentInsightComponent.Diagnostics,
                _ => null,
            };
            if (component is { } value)
            {
                insights.Add(new SessionComponentInsight(value, item));
            }
        }
        return insights
            .OrderBy(insight => insight.Component.ToRawValue(), StringComparer.Ordinal)
            .ThenBy(insight => insight.Series.Id.Pid)
            .ToList();
    }

    /// <summary>先頭と末尾を含む等間隔の代表点へ表示用系列を縮約します。</summary>
    /// <remarks>責務: 1件の数値時系列をRaw件数を変えずグラフ描画上限内の代表点へ変換します。</remarks>
    /// <param name="points">観測順に並ぶ全数値点。</param>
    /// <returns>上限以下の場合は元配列、それ以外は先頭と末尾を含む代表点。</returns>
    private static IReadOnlyList<SessionPIDSeries.Point> SampledPoints(List<SessionPIDSeries.Point> points)
    {
        if (points.Count <= MaximumChartPointCount)
        {
            return points;
        }
        var stride = (double)(points.Count - 1) / (MaximumChartPointCount - 1);
        var result = new List<SessionPIDSeries.Point>(MaximumChartPointCount);
        for (var index = 0; index < MaximumChartPointCount; index++)
        {
            result.Add(points[(int)Math.Round(index * stride, MidpointRounding.AwayFromZero)]);
        }
        return result;
    }

    /// <summary>代表的なPID組を近接観測時刻で対応付けます。</summary>
    /// <remarks>責務: 1件のPID系列一覧を3秒以内の近接点を持つ代表散布図の一覧へ変換します。</remarks>
    /// <param name="series">対応付ける数値化済みPID系列。</param>
    /// <returns>2点以上を生成できた標準PID組の散布図状態。</returns>
    public static IReadOnlyList<SessionPIDRelationship> Relationships(IReadOnlyList<SessionPIDSeries> series)
    {
        var indexed = series.ToDictionary(item => item.Id);
        var relationships = new List<SessionPIDRelationship>();
        foreach (var (xID, yID) in RelationshipPairs)
        {
            if (!indexed.TryGetValue(xID, out var xSeries) || !indexed.TryGetValue(yID, out var ySeries))
            {
                continue;
            }
            var points = NearestPoints(xSeries, ySeries);
            if (points.Count < 2)
            {
                continue;
            }
            relationships.Add(new SessionPIDRelationship(
                id: string.Create(CultureInfo.InvariantCulture, $"{xID.Service}-{xID.Pid}-{yID.Service}-{yID.Pid}"),
                xSeries: xSeries,
                ySeries: ySeries,
                points: points));
        }
        return relationships;
    }

    /// <summary>横軸の各点へ3秒以内で最も近い縦軸点を対応付けます。</summary>
    /// <remarks>責務: 2件の時系列を順序を保った近接時刻の散布点へ変換します。</remarks>
    /// <param name="xSeries">横軸へ使用する時系列。</param>
    /// <param name="ySeries">縦軸へ使用する時系列。</param>
    /// <returns>観測時刻差が3秒以内の近接点。</returns>
    private static List<SessionPIDRelationship.Point> NearestPoints(SessionPIDSeries xSeries, SessionPIDSeries ySeries)
    {
        var yIndex = 0;
        var result = new List<SessionPIDRelationship.Point>();
        foreach (var xPoint in xSeries.Points)
        {
            while (yIndex + 1 < ySeries.Points.Count
                   && Math.Abs(TimeIntervalSince(ySeries.Points[yIndex + 1].ObservedAt, xPoint.ObservedAt))
                   <= Math.Abs(TimeIntervalSince(ySeries.Points[yIndex].ObservedAt, xPoint.ObservedAt)))
            {
                yIndex += 1;
            }
            var yPoint = ySeries.Points[yIndex];
            if (!(Math.Abs(TimeIntervalSince(yPoint.ObservedAt, xPoint.ObservedAt)) <= 3))
            {
                continue;
            }
            result.Add(new SessionPIDRelationship.Point(xPoint.Id, xPoint.Value, yPoint.Value));
        }
        return result;
    }

    /// <summary>2件の日時の差を秒で返します。</summary>
    /// <remarks>責務: Swift の <c>Date.timeIntervalSince(_:)</c> と同じ向きの秒差を計算します。</remarks>
    /// <param name="date">基準からの差を求める日時。</param>
    /// <param name="reference">基準日時。</param>
    /// <returns><paramref name="date"/> から <paramref name="reference"/> を引いた秒数。</returns>
    private static double TimeIntervalSince(DateTimeOffset date, DateTimeOffset reference) =>
        (date - reference).TotalSeconds;
}
