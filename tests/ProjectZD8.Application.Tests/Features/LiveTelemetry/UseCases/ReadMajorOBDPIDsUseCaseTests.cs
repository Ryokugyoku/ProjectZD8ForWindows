using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LiveTelemetry.UseCases;

/// <summary>検証済み主要PIDの一括読取と数値化を検証します。</summary>
public sealed class ReadMajorOBDPIDsUseCaseTests
{
    /// <summary>テスト用OBD接続終端です。</summary>
    private static OBDConnectionEndpoint Endpoint =>
        new(OBDConnectionEndpointTransport.Serial, "/dev/cu.test", "OBDLink EX");

    /// <summary>実車観測バイトを冷却水温と回転数へ変換します。</summary>
    /// <remarks>責務: 2件の検証済み定義が1回の読取応答から名称と単位付き数値になることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestExecuteDecodesObservedCoolantAndEngineSpeedBytes()
    {
        var telemetry = new MajorPIDTelemetryFake(new Dictionary<OBDPIDRequest, byte[]>
        {
            [new OBDPIDRequest(0x01, 0x05)] = [0x85],
            [new OBDPIDRequest(0x01, 0x0C)] = [0x00, 0x00],
        });
        var useCase = new ReadMajorOBDPIDsUseCase(
            new MajorPIDDefinitionRepositoryFake(StandardOBDPIDSeedForTests.Definitions),
            telemetry,
            now: () => DateTimeOffset.FromUnixTimeSeconds(100));

        var samples = await useCase.Execute(Endpoint);
        var receivedRequests = telemetry.ReceivedRequests;

        Assert.Equal(new double[] { 93, 0 }, samples.Select(sample => sample.Value));
        Assert.Equal(new[] { "°C", "rpm" }, samples.Select(sample => sample.Unit));
        Assert.Equal(
            new[] { DateTimeOffset.FromUnixTimeSeconds(100), DateTimeOffset.FromUnixTimeSeconds(100) },
            samples.Select(sample => sample.ObservedAt));
        Assert.Equal(
            StandardOBDPIDSeedForTests.Definitions
                .Where(definition => definition.IsDecodable)
                .Select(definition => new OBDPIDRequest(definition.Service, definition.Pid)),
            receivedRequests);
    }

    /// <summary>応答しないPIDを対応済み一覧へ含めません。</summary>
    /// <remarks>責務: 定義済みPIDの一部が返らない場合に応答済み観測だけを返すことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestExecuteKeepsOnlyRespondingPIDs()
    {
        var telemetry = new MajorPIDTelemetryFake(new Dictionary<OBDPIDRequest, byte[]>
        {
            [new OBDPIDRequest(0x01, 0x05)] = [0x85],
        });
        var useCase = new ReadMajorOBDPIDsUseCase(
            new MajorPIDDefinitionRepositoryFake(StandardOBDPIDSeedForTests.Definitions),
            telemetry);

        var samples = await useCase.Execute(Endpoint);

        Assert.Equal(new[] { new OBDPIDRequest(0x01, 0x05) }, samples.Select(sample => sample.Request));
        Assert.Equal(new double[] { 93 }, samples.Select(sample => sample.Value));
    }

    /// <summary>Repositoryが返した数式と単位を固定seedより優先します。</summary>
    /// <remarks>責務: PID要求と数値化が実行時に取得したRepository定義だけを使用することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestExecuteUsesDefinitionsLoadedFromRepository()
    {
        var databaseDefinition = new OBDPIDDefinition(
            service: 0x01,
            pid: 0x05,
            nameKey: "database.name",
            requiredByteCount: 1,
            formula: "A + 10",
            unit: "db-unit",
            minimumValue: null,
            maximumValue: null,
            sourceURI: "test://database",
            revision: 2);
        var telemetry = new MajorPIDTelemetryFake(new Dictionary<OBDPIDRequest, byte[]>
        {
            [new OBDPIDRequest(0x01, 0x05)] = [5],
        });
        var useCase = new ReadMajorOBDPIDsUseCase(
            new MajorPIDDefinitionRepositoryFake([databaseDefinition]),
            telemetry);

        var samples = await useCase.Execute(Endpoint);
        var receivedRequests = telemetry.ReceivedRequests;

        Assert.Equal(new[] { "database.name" }, samples.Select(sample => sample.NameKey));
        Assert.Equal(new double[] { 15 }, samples.Select(sample => sample.Value));
        Assert.Equal(new[] { "db-unit" }, samples.Select(sample => sample.Unit));
        Assert.Equal(new[] { new OBDPIDRequest(0x01, 0x05) }, receivedRequests);
    }

    /// <summary>数値化前の応答バイトとバッチ経過時間をRawログ境界へ渡します。</summary>
    /// <remarks>責務: デコーダー入力が変換される前に安定順序とミリ秒精度相当の単調時間で通知されることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestExecuteRecordsRawResponsesBeforeDecoding()
    {
        var telemetry = new MajorPIDTelemetryFake(new Dictionary<OBDPIDRequest, byte[]>
        {
            [new OBDPIDRequest(0x01, 0x0C)] = [0x1A, 0xF8],
            [new OBDPIDRequest(0x01, 0x05)] = [0x85],
        });
        var recorder = new RawObservationRecorder();
        var clock = new MonotonicClockFake([1_000_000, 7_500_000]);
        var useCase = new ReadMajorOBDPIDsUseCase(
            new MajorPIDDefinitionRepositoryFake(StandardOBDPIDSeedForTests.Definitions),
            telemetry,
            now: () => DateTimeOffset.FromUnixTimeSeconds(100),
            monotonicNanoseconds: clock.Next,
            rawResponseDidReceive: observation =>
            {
                recorder.Record(observation);
                return Task.CompletedTask;
            });

        _ = await useCase.Execute(Endpoint);
        var observations = recorder.Observations;

        Assert.Equal(
            new[] { new OBDPIDRequest(0x01, 0x05), new OBDPIDRequest(0x01, 0x0C) },
            observations.Select(observation => observation.Request));
        Assert.Equal(
            new[] { new byte[] { 0x85 }, new byte[] { 0x1A, 0xF8 } },
            observations.Select(observation => observation.Payload.ToArray()));
        Assert.Equal(new ulong[] { 6_500_000, 6_500_000 }, observations.Select(observation => observation.BatchElapsedNanoseconds));
        Assert.Equal(
            new[] { DateTimeOffset.FromUnixTimeSeconds(100), DateTimeOffset.FromUnixTimeSeconds(100) },
            observations.Select(observation => observation.ObservedAt));
    }

    /// <summary>PID定義DBが空の場合は実車要求を開始しません。</summary>
    /// <remarks>責務: 空のRepository一覧を固定seedへ戻さず専用エラーとして拒否することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestExecuteRejectsEmptyDefinitionCatalogBeforeTelemetryRead()
    {
        var telemetry = new MajorPIDTelemetryFake(new Dictionary<OBDPIDRequest, byte[]>());
        var useCase = new ReadMajorOBDPIDsUseCase(
            new MajorPIDDefinitionRepositoryFake([]),
            telemetry);

        try
        {
            _ = await useCase.Execute(Endpoint);
            Assert.Fail("空のPID定義一覧は成功してはいけません");
        }
        catch (Exception error) when (error is not Xunit.Sdk.FailException)
        {
            Assert.Equal(OBDPIDTelemetryError.DefinitionCatalogUnavailable, (error as OBDPIDTelemetryException)?.Reason);
            Assert.Empty(telemetry.ReceivedRequests);
        }
    }

    /// <summary>PID定義DBの読込失敗を通信失敗と区別します。</summary>
    /// <remarks>責務: Repositoryエラーを専用カタログ利用不能エラーへ変換することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestExecuteMapsRepositoryFailureBeforeTelemetryRead()
    {
        var telemetry = new MajorPIDTelemetryFake(new Dictionary<OBDPIDRequest, byte[]>());
        var useCase = new ReadMajorOBDPIDsUseCase(
            new MajorPIDDefinitionRepositoryFake(new RepositoryReadError()),
            telemetry);

        try
        {
            _ = await useCase.Execute(Endpoint);
            Assert.Fail("PID定義読込失敗は成功してはいけません");
        }
        catch (Exception error) when (error is not Xunit.Sdk.FailException)
        {
            Assert.Equal(OBDPIDTelemetryError.DefinitionCatalogUnavailable, (error as OBDPIDTelemetryException)?.Reason);
            Assert.Empty(telemetry.ReceivedRequests);
        }
    }
}

/// <summary>Data層の <c>StandardOBDPIDSeed</c> の一部をテスト用に写した定義です。</summary>
/// <remarks>
/// Application テストは Data 層を参照できないため、検証で使う冷却水温・回転数を含む数値化可能定義と、
/// 数式を持たないmetadata専用定義（数値化対象外）だけを、seed と同じ Service/PID 順で並べています。
/// </remarks>
file static class StandardOBDPIDSeedForTests
{
    /// <summary>seed と同じ出典URIです。</summary>
    private const string SourceURI = "https://www.csselectronics.com/pages/obd2-pid-table-on-board-diagnostics-j1979";

    /// <summary>テストで使うService 01の定義一覧です。</summary>
    public static readonly IReadOnlyList<OBDPIDDefinition> Definitions =
    [
        new OBDPIDDefinition(0x01, 0x00, "obd.pid.01.00.name", null, null, "", null, null, SourceURI, 1),
        new OBDPIDDefinition(0x01, 0x04, "obd.pid.01.04.name", 1, "A * 100 / 255", "%", 0, 100, SourceURI, 1),
        new OBDPIDDefinition(0x01, 0x05, "obd.pid.01.05.name", 1, "A - 40", "°C", -40, 215, SourceURI, 1),
        new OBDPIDDefinition(0x01, 0x0C, "obd.pid.01.0C.name", 2, "(A * 256 + B) / 4", "rpm", 0, 16_383.75, SourceURI, 1),
        new OBDPIDDefinition(0x01, 0x0D, "obd.pid.01.0D.name", 1, "A", "km/h", 0, 255, SourceURI, 1),
        new OBDPIDDefinition(0x01, 0x20, "obd.pid.01.20.name", null, null, "", null, null, SourceURI, 1),
    ];
}

/// <summary>テスト用のPID定義一覧または読込失敗を返します。</summary>
file sealed class MajorPIDDefinitionRepositoryFake : IOBDPIDDefinitionRepository
{
    /// <summary>返却するPID定義一覧です。</summary>
    private readonly IReadOnlyList<OBDPIDDefinition> storedDefinitions;

    /// <summary>一覧読込時に送出するエラーです。</summary>
    private readonly Exception? storedError;

    /// <summary>固定PID定義一覧を保持します。</summary>
    /// <remarks>責務: テストで返すPID定義一覧を初期化します。</remarks>
    /// <param name="definitions">一覧読込時に返す定義。</param>
    public MajorPIDDefinitionRepositoryFake(IReadOnlyList<OBDPIDDefinition> definitions)
    {
        storedDefinitions = definitions;
        storedError = null;
    }

    /// <summary>固定読込エラーを保持します。</summary>
    /// <remarks>責務: テストで送出するRepositoryエラーを初期化します。</remarks>
    /// <param name="error">一覧読込時に送出するエラー。</param>
    public MajorPIDDefinitionRepositoryFake(Exception error)
    {
        storedDefinitions = [];
        storedError = error;
    }

    /// <summary>固定PID定義一覧を返すか固定エラーを送出します。</summary>
    /// <remarks>責務: 注入済みのRepository読込結果を再現します。</remarks>
    /// <returns>初期化時に保持したPID定義一覧。</returns>
    /// <exception cref="Exception">初期化時に保持した読込エラー。</exception>
    public IReadOnlyList<OBDPIDDefinition> Definitions()
    {
        if (storedError is not null)
        {
            throw storedError;
        }

        return storedDefinitions;
    }

    /// <summary>このテストでは使用しないPID定義保存です。</summary>
    /// <remarks>責務: テスト対象外の保存要求を変更なしで受け付けます。</remarks>
    /// <param name="definition">使用しないPID定義。</param>
    public void Upsert(OBDPIDDefinition definition)
    {
    }

    /// <summary>このテストでは使用しない単一PID定義読込です。</summary>
    /// <remarks>責務: テスト対象外の単一読込へ未登録を返します。</remarks>
    /// <param name="service">使用しないOBD Service番号。</param>
    /// <param name="pid">使用しないService内PID番号。</param>
    /// <returns>常に <c>null</c>。</returns>
    public OBDPIDDefinition? Definition(byte service, byte pid) => null;
}

/// <summary>PID定義Repositoryの読込失敗を再現します。</summary>
file sealed class RepositoryReadError : Exception;

/// <summary>Raw応答通知を並行安全に保持します。</summary>
file sealed class RawObservationRecorder
{
    /// <summary>記録を直列化するロックです。</summary>
    private readonly Lock gate = new();

    /// <summary>通知されたRaw応答順です。</summary>
    private readonly List<OBDRawResponseObservation> observations = [];

    /// <summary>通知されたRaw応答順です。</summary>
    public IReadOnlyList<OBDRawResponseObservation> Observations
    {
        get
        {
            lock (gate)
            {
                return observations.ToList();
            }
        }
    }

    /// <summary>1件のRaw応答を通知順へ追記します。</summary>
    /// <remarks>責務: 非同期Raw応答通知をテストで検査できる配列へ保存します。</remarks>
    /// <param name="observation">記録する未デコード応答。</param>
    public void Record(OBDRawResponseObservation observation)
    {
        lock (gate)
        {
            observations.Add(observation);
        }
    }
}

/// <summary>注入順に単調時間を返すテスト用クロックです。</summary>
file sealed class MonotonicClockFake
{
    /// <summary>次回返す単調時間一覧です。</summary>
    private readonly Queue<ulong> values;

    /// <summary>値取得を直列化するLockです。</summary>
    private readonly Lock gate = new();

    /// <summary>固定単調時間列を保持します。</summary>
    /// <remarks>責務: テスト対象へ返す単調時間の順序を初期化します。</remarks>
    /// <param name="values">呼出順に返すナノ秒値。</param>
    public MonotonicClockFake(IEnumerable<ulong> values)
    {
        this.values = new Queue<ulong>(values);
    }

    /// <summary>次の単調時間を返します。</summary>
    /// <remarks>責務: 注入済み時間列から1件を並行安全に取り出します。</remarks>
    /// <returns>次に予定されたナノ秒値。</returns>
    public ulong Next()
    {
        lock (gate)
        {
            return values.Dequeue();
        }
    }
}

/// <summary>主要PIDユースケースへ固定バイト辞書を返す境界です。</summary>
file sealed class MajorPIDTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>要求別に返す固定バイトです。</summary>
    private readonly IReadOnlyDictionary<OBDPIDRequest, byte[]> values;

    /// <summary>固定応答辞書を保持します。</summary>
    /// <remarks>責務: テストで返すService/PID別バイトを初期化します。</remarks>
    /// <param name="values">要求別の固定応答。</param>
    public MajorPIDTelemetryFake(IReadOnlyDictionary<OBDPIDRequest, byte[]> values)
    {
        this.values = values;
    }

    /// <summary>直近に受け取った要求順です。</summary>
    public IReadOnlyList<OBDPIDRequest> ReceivedRequests { get; private set; } = [];

    /// <summary>受信要求を記録して固定応答を返します。</summary>
    /// <remarks>責務: 1回の読取要求群を観測して注入済み応答辞書を返します。</remarks>
    /// <param name="requests">観測するService/PID要求。</param>
    /// <param name="endpoint">テストでは使用しない接続終端。</param>
    /// <returns>初期化時に保持した応答辞書。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        ReceivedRequests = requests.ToList();
        return Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<byte>)pair.Value));
    }
}
