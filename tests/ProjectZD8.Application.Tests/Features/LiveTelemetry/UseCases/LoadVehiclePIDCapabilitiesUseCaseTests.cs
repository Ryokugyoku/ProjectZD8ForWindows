using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LiveTelemetry.UseCases;

/// <summary>車両別対応PIDの再利用とService 01範囲探索を検証します。</summary>
public sealed class LoadVehiclePIDCapabilitiesUseCaseTests
{
    /// <summary>テスト用車両IDです。</summary>
    private static VehicleID VehicleID => new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    /// <summary>テスト用OBD終端です。</summary>
    private static OBDConnectionEndpoint Endpoint => new(OBDConnectionEndpointTransport.Serial, "/dev/test", "Test");

    /// <summary>00の継続ビットから20範囲へ進み、範囲照会PID自体を収集対象から除外します。</summary>
    /// <remarks>責務: 0x20刻みの対応ビットマップ連鎖を全件有効の車両別設定へ変換することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestDiscoversChainedRangesAndPersistsAllEnabled()
    {
        var repository = new CapabilityRepositoryFake();
        var telemetry = new CapabilityTelemetryFake(new Dictionary<byte, byte[]>
        {
            [0x00] = [0x80, 0x00, 0x00, 0x01],
            [0x20] = [0x80, 0x00, 0x00, 0x00],
        });
        var useCase = new LoadVehiclePIDCapabilitiesUseCase(
            repository,
            telemetry,
            now: () => DateTimeOffset.FromUnixTimeSeconds(123));

        var capabilities = await useCase.Execute(vehicleID: VehicleID, endpoint: Endpoint);

        Assert.Equal(new byte[] { 0x00, 0x20 }, telemetry.QueriedPIDs);
        Assert.Equal(new byte[] { 0x01, 0x21 }, capabilities.Select(capability => capability.Id.Request.Pid));
        Assert.True(capabilities.All(capability => capability.IsCollectionEnabled));
        Assert.Equal(
            new[] { DateTimeOffset.FromUnixTimeSeconds(123), DateTimeOffset.FromUnixTimeSeconds(123) },
            capabilities.Select(capability => capability.DiscoveredAt));
        Assert.Equal(capabilities, repository.Capabilities(VehicleID));
    }

    /// <summary>保存済み対応PIDが1件でもあれば車両通信を行いません。</summary>
    /// <remarks>責務: 既存の車両別収集選択を再探索せずそのまま返すことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestReusesStoredCapabilitiesWithoutPolling()
    {
        var stored = new VehiclePIDCapability(
            VehicleID,
            new OBDPIDRequest(0x01, 0x0C),
            isCollectionEnabled: false,
            discoveredAt: DateTimeOffset.MinValue);
        var repository = new CapabilityRepositoryFake([stored]);
        var telemetry = new CapabilityTelemetryFake(new Dictionary<byte, byte[]>());
        var useCase = new LoadVehiclePIDCapabilitiesUseCase(repository, telemetry);

        var result = await useCase.Execute(vehicleID: VehicleID, endpoint: Endpoint);

        Assert.Equal([stored], result);
        Assert.Empty(telemetry.QueriedPIDs);
    }

    /// <summary>ZD8型式では対応一覧にない専用PIDを個別送信して応答済みだけ追加します。</summary>
    /// <remarks>責務: 車種専用PID探索が型式一致、正応答、非破壊登録を満たすことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TestProbesAndRegistersRespondingZD8PID()
    {
        var stored = new VehiclePIDCapability(
            VehicleID,
            new OBDPIDRequest(0x01, 0x0C),
            isCollectionEnabled: true,
            discoveredAt: DateTimeOffset.MinValue);
        var repository = new CapabilityRepositoryFake([stored]);
        var telemetry = new VehicleSpecificTelemetryFake();
        var useCase = new LoadVehiclePIDCapabilitiesUseCase(
            repository,
            telemetry,
            definitionRepository: new DefinitionRepositoryFake(ZD8OBDPIDSeedForTests.Definitions),
            now: () => DateTimeOffset.FromUnixTimeSeconds(456));

        var result = await useCase.Execute(
            vehicleID: VehicleID,
            vehicleModelCode: "ZD8",
            endpoint: Endpoint);

        Assert.Equal(
            new[] { new OBDPIDRequest(0x01, 0x0C), new OBDPIDRequest(0x21, 0x02) },
            result.Select(capability => capability.Id.Request));
        Assert.Equal(new byte[] { 0x02, 0x17 }, telemetry.RequestedPIDs);
    }
}

/// <summary>Data層の <c>ZD8OBDPIDSeed</c> と同じ専用PID定義をテスト用に提供します。</summary>
/// <remarks>Application テストは Data 層を参照できないため、走行距離とAT油温の定義を写しています。</remarks>
file static class ZD8OBDPIDSeedForTests
{
    /// <summary>重複なく識別できる走行距離とAT油温の定義です。</summary>
    public static readonly IReadOnlyList<OBDPIDDefinition> Definitions =
    [
        new OBDPIDDefinition(
            service: 0x21,
            pid: 0x02,
            header: 0x7E0,
            vehicleModelCode: ZD8VehicleModelPolicy.ModelCode,
            nameKey: "obd.pid.zd8.21.02.name",
            requiredByteCount: 4,
            formula: "(A * 16777216 + B * 65536 + C * 256 + D) / 10",
            unit: "km",
            minimumValue: 0,
            maximumValue: 429_496_729.5,
            sourceURI: "projectzd8://requirements/zd8-pids/2026-07-23",
            revision: 1,
            summaryKey: "obd.pid.zd8.21.02.summary",
            highValueKey: "obd.pid.help.unconfirmed.high",
            lowValueKey: "obd.pid.help.unconfirmed.low",
            correlationKey: "obd.pid.zd8.21.02.correlation"),
        new OBDPIDDefinition(
            service: 0x21,
            pid: 0x17,
            header: 0x7E1,
            vehicleModelCode: ZD8VehicleModelPolicy.ModelCode,
            nameKey: "obd.pid.zd8.21.17.name",
            requiredByteCount: 1,
            formula: "A - 58",
            unit: "°C",
            minimumValue: -58,
            maximumValue: 197,
            sourceURI: "projectzd8://requirements/zd8-pids/2026-07-23",
            revision: 1,
            summaryKey: "obd.pid.zd8.21.17.summary",
            highValueKey: "obd.pid.zd8.21.17.high",
            lowValueKey: "obd.pid.zd8.21.17.low",
            correlationKey: "obd.pid.zd8.21.17.correlation"),
    ];
}

/// <summary>専用PID定義を固定一覧で返すテストカタログです。</summary>
file sealed class DefinitionRepositoryFake : IOBDPIDDefinitionRepository
{
    /// <summary>返すPID定義です。</summary>
    private readonly IReadOnlyList<OBDPIDDefinition> definitionsValue;

    /// <summary>固定定義を保持します。</summary>
    /// <remarks>責務: 専用PID探索テスト用カタログを初期化します。</remarks>
    /// <param name="definitions">読込時に返す定義。</param>
    public DefinitionRepositoryFake(IReadOnlyList<OBDPIDDefinition> definitions)
    {
        definitionsValue = definitions;
    }

    /// <summary>固定定義一覧を返します。</summary>
    /// <remarks>責務: テスト用PIDカタログをそのまま返します。</remarks>
    /// <returns>初期化時の定義。</returns>
    public IReadOnlyList<OBDPIDDefinition> Definitions() => definitionsValue;

    /// <summary>テスト対象外の書込を変更なしで完了します。</summary>
    /// <remarks>責務: 専用PID探索テストで不要な保存操作を受理します。</remarks>
    /// <param name="definition">使用しない定義。</param>
    public void Upsert(OBDPIDDefinition definition)
    {
    }

    /// <summary>固定一覧からService/PID一致定義を返します。</summary>
    /// <remarks>責務: 1件のService/PIDをテスト用定義検索へ変換します。</remarks>
    /// <param name="service">OBD Service番号。</param>
    /// <param name="pid">Service内PID番号。</param>
    /// <returns>一致する定義。</returns>
    public OBDPIDDefinition? Definition(byte service, byte pid) =>
        definitionsValue.FirstOrDefault(definition => definition.Service == service && definition.Pid == pid);
}

/// <summary>専用PIDの一部だけへ正応答を返すテスト通信境界です。</summary>
file sealed class VehicleSpecificTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>専用探索で要求されたPIDです。</summary>
    public IReadOnlyList<byte> RequestedPIDs { get; private set; } = [];

    /// <summary>標準探索を行わないテストでは空応答を返します。</summary>
    /// <remarks>責務: テスト対象外の標準PID要求を空応答へ変換します。</remarks>
    /// <param name="requests">使用しない標準PID要求。</param>
    /// <param name="endpoint">使用しないOBD終端。</param>
    /// <returns>空の応答辞書。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) =>
        Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>());

    /// <summary>走行距離だけへ応答してAT油温を非対応として扱います。</summary>
    /// <remarks>責務: 専用PID定義群を一部応答の探索結果へ変換します。</remarks>
    /// <param name="definitions">探索する専用PID定義。</param>
    /// <param name="endpoint">使用しないOBD終端。</param>
    /// <returns>Service 21 PID 02だけの応答。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> ReadVehicleSpecific(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint)
    {
        RequestedPIDs = definitions.Select(definition => definition.Pid).ToList();
        return Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>
            {
                [new OBDPIDRequest(0x21, 0x02)] = new byte[] { 0, 0, 0, 10 },
            });
    }
}

/// <summary>対応PID設定をメモリ保持するテスト境界です。</summary>
file sealed class CapabilityRepositoryFake : IVehiclePIDCapabilityRepository
{
    /// <summary>現在の固定設定です。</summary>
    private List<VehiclePIDCapability> stored;

    /// <summary>初期設定を保持します。</summary>
    /// <remarks>責務: テスト用の車両別PID保存状態を初期化します。</remarks>
    /// <param name="stored">初期設定。</param>
    public CapabilityRepositoryFake(IReadOnlyList<VehiclePIDCapability>? stored = null)
    {
        this.stored = stored?.ToList() ?? [];
    }

    /// <summary>現在設定を返します。</summary>
    /// <remarks>責務: テスト用設定一覧を返します。</remarks>
    /// <param name="vehicleID">使用しない車両ID。</param>
    /// <returns>現在設定。</returns>
    public IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID vehicleID) => stored.ToList();

    /// <summary>初回設定を保持します。</summary>
    /// <remarks>責務: 探索結果をテスト用状態へ保存します。</remarks>
    /// <param name="capabilities">保存する設定。</param>
    /// <param name="vehicleID">使用しない車両ID。</param>
    public void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID)
    {
        stored = capabilities.ToList();
    }

    /// <summary>新たに応答した設定を既存テスト状態へ追加します。</summary>
    /// <remarks>責務: テスト用の追加探索結果を重複なく保持します。</remarks>
    /// <param name="capabilities">追加する対応PID。</param>
    /// <param name="vehicleID">使用しない車両ID。</param>
    public void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID)
    {
        foreach (var capability in capabilities)
        {
            if (!stored.Any(existing => existing.Id == capability.Id))
            {
                stored.Add(capability);
            }
        }
    }

    /// <summary>テスト対象外の選択更新を受け付けます。</summary>
    /// <remarks>責務: テスト対象外の収集選択変更を変更なしで完了します。</remarks>
    /// <param name="isEnabled">使用しない選択状態。</param>
    /// <param name="request">使用しないPID。</param>
    /// <param name="vehicleID">使用しない車両ID。</param>
    public void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID vehicleID)
    {
    }
}

/// <summary>対応ビットマップをPID範囲別に返すテスト境界です。</summary>
file sealed class CapabilityTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>範囲先頭PID別の固定応答です。</summary>
    private readonly IReadOnlyDictionary<byte, byte[]> responses;

    /// <summary>照会された範囲先頭PIDです。</summary>
    private readonly List<byte> queriedPIDs = [];

    /// <summary>固定応答を保持します。</summary>
    /// <remarks>責務: 範囲探索テスト用応答を初期化します。</remarks>
    /// <param name="responses">範囲先頭PID別の4バイト応答。</param>
    public CapabilityTelemetryFake(IReadOnlyDictionary<byte, byte[]> responses)
    {
        this.responses = responses;
    }

    /// <summary>照会された範囲先頭PIDです。</summary>
    public IReadOnlyList<byte> QueriedPIDs => queriedPIDs.ToList();

    /// <summary>単一範囲照会へ固定ビットマップを返します。</summary>
    /// <remarks>責務: 受信した範囲先頭PIDを記録して対応する固定応答を返します。</remarks>
    /// <param name="requests">単一の対応範囲照会。</param>
    /// <param name="endpoint">使用しないOBD終端。</param>
    /// <returns>照会PIDへ対応する固定応答。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        var result = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        if (requests.Count != 0 && responses.TryGetValue(requests[0].Pid, out var bytes))
        {
            queriedPIDs.Add(requests[0].Pid);
            result[requests[0]] = bytes;
        }

        return Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(result);
    }
}
