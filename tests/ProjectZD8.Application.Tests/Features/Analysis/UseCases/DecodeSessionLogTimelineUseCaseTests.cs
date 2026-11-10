using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.Analysis.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Analysis.UseCases;

/// <summary>保存済みPIDログの時系列変換を検証します。</summary>
public sealed class DecodeSessionLogTimelineUseCaseTests
{
    /// <summary>定義済みPIDは数式と単位を適用し、未定義PIDはRawとして保持します。</summary>
    /// <remarks>責務: セッション内の全PIDが時系列順と変換来歴を保って表示値になることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task ExecuteDecodesKnownPIDAndRetainsUnknownPIDAsRaw()
    {
        var sessionID = ConnectionSessionID.New();
        var entries = new[]
        {
            new ConnectionSessionRawLogEntry(sequence: 1, observedAt: DateTimeOffset.FromUnixTimeSeconds(20), batchElapsedNanoseconds: 2, service: 0x01, pid: 0x0C, payload: new byte[] { 0x1A, 0xF8 }),
            new ConnectionSessionRawLogEntry(sequence: 0, observedAt: DateTimeOffset.FromUnixTimeSeconds(10), batchElapsedNanoseconds: 1, service: 0x01, pid: 0xFF, payload: new byte[] { 0x12 }),
        };
        var definition = new OBDPIDDefinition(service: 0x01, pid: 0x0C, nameKey: "obd.pid.01.0C.name", requiredByteCount: 2, formula: "(A * 256 + B) / 4", unit: "rpm", minimumValue: 0, maximumValue: 16_383.75, sourceURI: "https://example.invalid", revision: 1);
        var useCase = new DecodeSessionLogTimelineUseCase(rawLogRepository: new RawLogRepositoryFake(entries), definitionRepository: new DefinitionRepositoryFake(new[] { definition }), batchSize: 1);
        long? preparedCount = null;
        var batches = new List<IReadOnlyList<SessionLogAnalysisState.TimelineSample>>();

        await useCase.Execute(
            sessionID,
            prepared: count => preparedCount = count,
            batchDecoded: batches.Add);
        var result = batches.SelectMany(batch => batch).ToList();

        Assert.Equal(2L, preparedCount);
        Assert.Equal(new[] { 1, 1 }, batches.Select(batch => batch.Count));
        Assert.Equal(new long[] { 0, 1 }, result.Select(sample => sample.Sequence));
        Assert.Equal(SessionLogAnalysisState.TimelineSampleDecodingFailure.MissingDefinition, result[0].DecodingFailure);
        Assert.Equal(new byte[] { 0x12 }, result[0].Payload);
        Assert.Equal(1_726, result[1].Value);
        Assert.Equal("rpm", result[1].Unit);
    }

    /// <summary>グラフ表示はRaw時系列を保持したまま系列ごとの代表点上限へ縮約します。</summary>
    /// <remarks>責務: 大量の解析済みPIDを走行集計と上限内の折れ線および散布図へ変換できることを確認します。</remarks>
    [Fact]
    public void VisualizationBuildDownsamplesChartsWithoutMutatingTimeline()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_000);
        var rpm = new List<SessionLogAnalysisState.TimelineSample>(1_000);
        var speed = new List<SessionLogAnalysisState.TimelineSample>(1_000);
        for (var index = 0; index < 1_000; index++)
        {
            rpm.Add(new SessionLogAnalysisState.TimelineSample(
                sequence: index * 2,
                observedAt: start.AddSeconds(index * 0.1),
                service: 0x01,
                pid: 0x0C,
                nameKey: "obd.pid.01.0C.name",
                value: index,
                unit: "rpm",
                payload: new byte[] { 0x00, 0x00 },
                decodingFailure: null));
            speed.Add(new SessionLogAnalysisState.TimelineSample(
                sequence: index * 2 + 1,
                observedAt: start.AddSeconds(index * 0.1 + 0.02),
                service: 0x01,
                pid: 0x0D,
                nameKey: "obd.pid.01.0D.name",
                value: index % 120,
                unit: "km/h",
                payload: new byte[] { 0x00 },
                decodingFailure: null));
        }
        var timeline = rpm.Concat(speed).OrderBy(sample => sample.Sequence).ToList();

        var snapshot = SessionLogVisualizationBuilder.Build(timeline);
        var totalPointCounts = snapshot.Series.Select(series => series.TotalPointCount);

        Assert.Equal(2_000, timeline.Count);
        Assert.Equal(new[] { 1_000, 1_000 }, totalPointCounts);
        Assert.True(snapshot.Series.All(series => series.Points.Count <= 600));
        Assert.Equal(600, snapshot.Relationships.FirstOrDefault()?.Points.Count);
        Assert.NotEmpty(snapshot.PerformanceSummary.SpeedBands);
        Assert.NotEmpty(snapshot.PerformanceSummary.RpmBands);
        Assert.NotNull(snapshot.PerformanceSummary.EstimatedDistanceKilometers);
    }

    /// <summary>速度と回転数の観測間隔は指定幅の滞在時間ランキングへ変換されます。</summary>
    /// <remarks>責務: 10 km/h速度帯と500 rpm回転域を長い欠測を除外して時間順に集計することを確認します。</remarks>
    [Fact]
    public void VisualizationBuildRanksObservedSpeedAndRPMDuration()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_000);
        var timeline = new[]
        {
            Sample(sequence: 0, date: start, pid: 0x0D, value: 23, unit: "km/h"),
            Sample(sequence: 1, date: start.AddSeconds(1), pid: 0x0D, value: 27, unit: "km/h"),
            Sample(sequence: 2, date: start.AddSeconds(2), pid: 0x0D, value: 41, unit: "km/h"),
            Sample(sequence: 3, date: start.AddSeconds(20), pid: 0x0D, value: 90, unit: "km/h"),
            Sample(sequence: 4, date: start, pid: 0x0C, value: 1_200, unit: "rpm"),
            Sample(sequence: 5, date: start.AddSeconds(1), pid: 0x0C, value: 1_450, unit: "rpm"),
            Sample(sequence: 6, date: start.AddSeconds(2), pid: 0x0C, value: 2_100, unit: "rpm"),
        };

        var summary = SessionLogVisualizationBuilder.Build(timeline).PerformanceSummary;

        var speedBand = Assert.IsType<SessionDurationBand>(summary.SpeedBands.FirstOrDefault());
        Assert.Equal(20, speedBand.LowerBound);
        Assert.Equal(2, speedBand.Duration, 0.001);
        Assert.Equal(2, summary.EstimatedMovingDuration);
        var rpmBand = Assert.IsType<SessionDurationBand>(summary.RpmBands.FirstOrDefault());
        Assert.Equal(1_000, rpmBand.LowerBound);
        Assert.Equal(2, rpmBand.Duration, 0.001);
    }

    /// <summary>故障コード消去後の走行距離は診断履歴として最新値を保持します。</summary>
    /// <remarks>責務: Service 01 PID 31をセッション距離と混同しない診断系観察候補へ変換することを確認します。</remarks>
    [Fact]
    public void VisualizationBuildExposesDistanceSinceCodesClearedAsDiagnosticHistory()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(2_000);
        var timeline = new[]
        {
            Sample(sequence: 0, date: start, pid: 0x31, value: 120, unit: "km"),
            Sample(sequence: 1, date: start.AddSeconds(1), pid: 0x31, value: 121, unit: "km"),
        };

        var insight = Assert.IsType<SessionComponentInsight>(SessionLogVisualizationBuilder.Build(timeline).ComponentInsights.FirstOrDefault());

        Assert.Equal(SessionComponentInsightComponent.Diagnostics, insight.Component);
        Assert.Equal(new OBDPIDRequest(0x01, 0x31), insight.Series.Id);
        Assert.Equal(121, insight.Series.LatestValue);
        Assert.Equal("analysis.trend.guide.codes_cleared_distance", insight.Series.InterpretationKey);
    }

    /// <summary>ZD8のAT油温を車速との散布図および温度系観察へ追加します。</summary>
    /// <remarks>責務: 専用PIDの型式表示情報を維持したまま車速とAT油温を相関表示へ変換することを確認します。</remarks>
    [Fact]
    public void VisualizationBuildAddsZD8ATFRelationshipAndBadgeScope()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(3_000);
        var timeline = new[]
        {
            Sample(sequence: 0, date: start, pid: 0x0D, value: 40, unit: "km/h"),
            Sample(sequence: 1, date: start.AddSeconds(0.1), service: 0x21, pid: 0x17, value: 72, unit: "°C", vehicleModelCode: "ZD8"),
            Sample(sequence: 2, date: start.AddSeconds(1), pid: 0x0D, value: 60, unit: "km/h"),
            Sample(sequence: 3, date: start.AddSeconds(1.1), service: 0x21, pid: 0x17, value: 78, unit: "°C", vehicleModelCode: "ZD8"),
        };

        var snapshot = SessionLogVisualizationBuilder.Build(timeline);
        var relationship = Assert.IsType<SessionPIDRelationship>(snapshot.Relationships.FirstOrDefault(item => item.YSeries.Id == new OBDPIDRequest(0x21, 0x17)));
        var thermal = Assert.IsType<SessionComponentInsight>(snapshot.ComponentInsights.FirstOrDefault(item => item.Series.Id == new OBDPIDRequest(0x21, 0x17)));

        Assert.Equal(2, relationship.Points.Count);
        Assert.Equal("ZD8", relationship.YSeries.VehicleModelCode);
        Assert.Equal("analysis.trend.guide.zd8_atf", relationship.YSeries.InterpretationKey);
        Assert.Equal(SessionComponentInsightComponent.Thermal, thermal.Component);
        Assert.Equal("ZD8", thermal.Series.VehicleModelCode);
    }

    /// <summary>テスト用の数値化済み観測を生成します。</summary>
    /// <remarks>責務: 1件の時刻、PID、値を走行集計テスト用サンプルへ変換します。</remarks>
    /// <param name="sequence">Rawログ順序。</param>
    /// <param name="date">観測時刻。</param>
    /// <param name="pid">Service内PID番号。</param>
    /// <param name="value">数式変換後の値。</param>
    /// <param name="unit">定義済み単位。</param>
    /// <param name="service">OBD Service番号。</param>
    /// <param name="vehicleModelCode">車種専用PIDの場合の適用型式。</param>
    /// <returns>数値化済みタイムラインサンプル。</returns>
    private static SessionLogAnalysisState.TimelineSample Sample(long sequence, DateTimeOffset date, byte pid, double value, string unit, byte service = 0x01, string? vehicleModelCode = null) =>
        new(
            sequence: sequence,
            observedAt: date,
            service: service,
            pid: pid,
            nameKey: null,
            value: value,
            unit: unit,
            vehicleModelCode: vehicleModelCode,
            payload: Array.Empty<byte>(),
            decodingFailure: null);

    /// <summary>テスト用の固定Rawログを返すリポジトリです。</summary>
    /// <remarks>件数とページ読取はインターフェイスの既定実装（全件読取からの互換動作）を使います。</remarks>
    private sealed class RawLogRepositoryFake : IConnectionSessionRawLogRepository
    {
        /// <summary>テストで返すRawログです。</summary>
        private readonly IReadOnlyList<ConnectionSessionRawLogEntry> _entries;

        /// <summary>固定Rawログを保持して生成します。</summary>
        /// <remarks>責務: 任意のセッション読取要求へ返すRawログを固定します。</remarks>
        /// <param name="entries">テストで返すRawログ。</param>
        public RawLogRepositoryFake(IReadOnlyList<ConnectionSessionRawLogEntry> entries) => _entries = entries;

        /// <summary>このテストでは使用しない追記要求を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要なRaw追記を副作用なく受理します。</remarks>
        /// <param name="observation">使用しない応答。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID) { }

        /// <summary>固定Rawログを返します。</summary>
        /// <remarks>責務: 任意のセッション読取要求へ固定Rawログを返します。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <returns>固定Rawログ。</returns>
        public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) => _entries;

        /// <summary>このテストでは使用しない車両別Rawログを返します。</summary>
        /// <remarks>責務: 解析読取テストで不要な車両別照会へ空配列を返します。</remarks>
        /// <param name="vehicleID">使用しない車両ID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空配列。</returns>
        public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier) => [];

        /// <summary>このテストでは使用しないCloudKit保存結果を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要なCloudKit保存結果を副作用なく受理します。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="manifestDigest">使用しないDigest。</param>
        public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest) { }

        /// <summary>このテストでは使用しないCloudKit失敗を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要なCloudKit失敗を副作用なく受理します。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void MarkCloudUploadFailed(ConnectionSessionID sessionID) { }

        /// <summary>このテストでは使用しないMac取込結果を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要なMac取込結果を副作用なく受理します。</remarks>
        /// <param name="receipt">使用しない受領証。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID) { }

        /// <summary>このテストでは使用しない転送取込を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要な転送取込を副作用なく受理します。</remarks>
        /// <param name="transfer">使用しない転送。</param>
        public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) { }

        /// <summary>このテストでは使用しないローカル除去を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要なローカル除去を副作用なく受理します。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void RemoveLocalEntries(ConnectionSessionID sessionID) { }
    }

    /// <summary>テスト用の固定PID定義を返すリポジトリです。</summary>
    private sealed class DefinitionRepositoryFake : IOBDPIDDefinitionRepository
    {
        /// <summary>テストで返すPID定義です。</summary>
        private readonly IReadOnlyList<OBDPIDDefinition> _storedDefinitions;

        /// <summary>固定PID定義を保持して生成します。</summary>
        /// <remarks>責務: 定義一覧要求へ返すPID定義を固定します。</remarks>
        /// <param name="storedDefinitions">テストで返すPID定義。</param>
        public DefinitionRepositoryFake(IReadOnlyList<OBDPIDDefinition> storedDefinitions) => _storedDefinitions = storedDefinitions;

        /// <summary>固定PID定義を返します。</summary>
        /// <remarks>責務: 定義一覧要求へ固定PID定義を返します。</remarks>
        /// <returns>固定PID定義。</returns>
        public IReadOnlyList<OBDPIDDefinition> Definitions() => _storedDefinitions;

        /// <summary>このテストでは使用しない保存要求を受理します。</summary>
        /// <remarks>責務: 解析読取テストで不要な定義保存を副作用なく受理します。</remarks>
        /// <param name="definition">使用しない定義。</param>
        public void Upsert(OBDPIDDefinition definition) { }

        /// <summary>対応する固定定義を返します。</summary>
        /// <remarks>責務: Service/PID検索を対応する固定定義へ変換します。</remarks>
        /// <param name="service">OBD Service番号。</param>
        /// <param name="pid">Service内PID番号。</param>
        /// <returns>一致する固定定義。なければ <see langword="null"/>。</returns>
        public OBDPIDDefinition? Definition(byte service, byte pid) =>
            _storedDefinitions.FirstOrDefault(definition => definition.Service == service && definition.Pid == pid);
    }
}
