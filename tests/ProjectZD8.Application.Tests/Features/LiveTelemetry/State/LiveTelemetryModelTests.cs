using System.Collections.Concurrent;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.LiveTelemetry.Actions;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Application.LiveTelemetry.UseCases;
using ProjectZD8.Application.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LiveTelemetry.State;

/// <summary>LiveTelemetryのPID定義DB失敗表示を検証します。</summary>
/// <remarks>
/// Swift のテストは <c>@MainActor</c> で動くため、各テストは単一スレッドの同期コンテキスト上で実行し、
/// モデルの非同期処理がテスト本体と同じスレッドへ戻るようにしています。
/// </remarks>
public sealed class LiveTelemetryModelTests
{
    /// <summary>テスト用OBD接続終端です。</summary>
    private static OBDConnectionEndpoint Endpoint =>
        new(OBDConnectionEndpointTransport.Serial, "/dev/cu.test", "Test adapter");

    /// <summary>テスト用のアプリ内車両IDです。</summary>
    private static VehicleID VehicleID => new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    /// <summary>初回探索後もPID取得を繰り返します。</summary>
    /// <remarks>責務: 接続開始が単発読取ではなく継続更新へ遷移することを確認します。</remarks>
    [Fact]
    public void TestStartContinuesPollingAfterInitialDiscovery() => MainThread.Run(async () =>
    {
        var telemetry = new CountingPIDTelemetryFake();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(new FixedPIDDefinitionRepository(), telemetry));

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => telemetry.ReadCount >= 2);

        Assert.Equal(LiveTelemetryStatePhase.Loaded, model.State.Phase);
        Assert.True(telemetry.ReadCount >= 2);
        model.Send(new LiveTelemetryAction.StopRequested());
        Assert.Equal(LiveTelemetryStatePhase.Stopping, model.State.Phase);
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Idle);
        Assert.Equal(LiveTelemetryStatePhase.Idle, model.State.Phase);
    });

    /// <summary>PID定義DB失敗を通信失敗とは異なる表示キーへ反映します。</summary>
    /// <remarks>責務: カタログ利用不能エラーがスリープ抑止解除済みの専用失敗表示になることを確認します。</remarks>
    [Fact]
    public void TestReadMapsDefinitionCatalogFailureToDedicatedMessage() => MainThread.Run(async () =>
    {
        var systemSleepInhibitor = new VehicleConnectionSystemSleepInhibitorSpy();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(
                new FailingPIDDefinitionRepository(),
                new UnavailableOBDPIDTelemetryFake()),
            systemSleepInhibitor: systemSleepInhibitor);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Failed);

        Assert.Equal(LiveTelemetryStatePhase.Failed, model.State.Phase);
        Assert.Equal("telemetry.error.pid_catalog_unavailable", model.State.FailureKey);
        Assert.Equal(new[] { true, false }, systemSleepInhibitor.ConnectionStates);
    });

    /// <summary>切断要求が通信セッション終了後に待機状態へ戻ることを検証します。</summary>
    /// <remarks>責務: 手動切断が通信資源解放後にスリープ抑止を解除することを確認します。</remarks>
    [Fact]
    public void TestStopWaitsForSessionEndBeforeBecomingIdle() => MainThread.Run(async () =>
    {
        var telemetry = new EndTrackingPIDTelemetryFake();
        var systemSleepInhibitor = new VehicleConnectionSystemSleepInhibitorSpy();
        var endReasons = new ConcurrentQueue<ConnectionSessionEndReason>();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(new FixedPIDDefinitionRepository(), telemetry),
            sessionDidEnd: endReasons.Enqueue,
            systemSleepInhibitor: systemSleepInhibitor);
        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        Assert.Equal(new[] { true }, systemSleepInhibitor.ConnectionStates);
        await AsyncWait.UntilAsync(() => model.State.Phase != LiveTelemetryStatePhase.Reading);

        model.Send(new LiveTelemetryAction.StopRequested());

        Assert.Equal(LiveTelemetryStatePhase.Stopping, model.State.Phase);
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Idle);
        Assert.Equal(LiveTelemetryStatePhase.Idle, model.State.Phase);
        Assert.True(telemetry.EndCount >= 1);
        Assert.Equal(new[] { ConnectionSessionEndReason.UserDisconnected }, endReasons);
        Assert.Equal(new[] { true, false }, systemSleepInhibitor.ConnectionStates);
    });

    /// <summary>継続取得中に全PID応答が消えた場合の自動切断を検証します。</summary>
    /// <remarks>責務: ECU無応答をスリープ抑止解除済みの未接続状態へ変換することを確認します。</remarks>
    [Fact]
    public void TestNoVehicleResponseEndsSessionAndBecomesDisconnected() => MainThread.Run(async () =>
    {
        var telemetry = new InitialResponseThenEmptyPIDTelemetryFake();
        var systemSleepInhibitor = new VehicleConnectionSystemSleepInhibitorSpy();
        var endReasons = new ConcurrentQueue<ConnectionSessionEndReason>();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(new FixedPIDDefinitionRepository(), telemetry),
            sessionDidEnd: endReasons.Enqueue,
            systemSleepInhibitor: systemSleepInhibitor);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => model.State.FailureKey is not null, timeoutMilliseconds: 3000);

        Assert.Equal(LiveTelemetryStatePhase.Idle, model.State.Phase);
        Assert.Equal("telemetry.disconnected.no_response", model.State.FailureKey);
        Assert.True(telemetry.EndCount >= 1);
        Assert.Equal(new[] { ConnectionSessionEndReason.VehicleNoResponse }, endReasons);
        Assert.Equal(new[] { true, false }, systemSleepInhibitor.ConnectionStates);
    });

    /// <summary>A6観測を累積走行距離通知としてLogging境界へ渡します。</summary>
    /// <remarks>責務: 数値化済みService 01 PID A6が走行距離コールバックへ通知されることを確認します。</remarks>
    [Fact]
    public void TestOdometerSampleNotifiesLoggingBoundary() => MainThread.Run(async () =>
    {
        var observations = new ConcurrentQueue<ConnectionSessionDistanceObservation>();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(
                new FixedOdometerDefinitionRepository(),
                new FixedPayloadTelemetryFake([0x00, 0x01, 0xE2, 0x40])),
            distanceDidChange: observations.Enqueue);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => !observations.IsEmpty);

        Assert.Equal(
            new ConnectionSessionDistanceObservation(ConnectionSessionDistanceSource.Odometer, 12_345.6),
            observations.FirstOrDefault());
        model.Send(new LiveTelemetryAction.StopRequested());
    });

    /// <summary>A6非対応時はPID 31観測を接続履歴の代替累積距離として渡します。</summary>
    /// <remarks>責務: 数値化済みService 01 PID 31が取得元付き走行距離コールバックへ通知されることを確認します。</remarks>
    [Fact]
    public void TestDistanceSinceCodesClearedNotifiesFallbackBoundary() => MainThread.Run(async () =>
    {
        var observations = new ConcurrentQueue<ConnectionSessionDistanceObservation>();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(
                new FixedDistanceSinceCodesClearedDefinitionRepository(),
                new FixedPayloadTelemetryFake([0x04, 0xD2])),
            distanceDidChange: observations.Enqueue);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, null));
        await AsyncWait.UntilAsync(() => !observations.IsEmpty);

        Assert.Equal(
            new ConnectionSessionDistanceObservation(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, 1_234),
            observations.FirstOrDefault());
        model.Send(new LiveTelemetryAction.StopRequested());
    });

    /// <summary>ZD8型式を指定すると専用値を含む標準ポーリングを使用します。</summary>
    /// <remarks>責務: 走行距離とAT油温をリアルタイム表示へ残し専用走行距離をLoggingへ通知することを確認します。</remarks>
    [Fact]
    public void TestZD8ExtendedPIDsUseStandardPollingAndNotifyVehicleSpecificOdometer() => MainThread.Run(async () =>
    {
        var telemetry = new ZD8ExtendedPIDTelemetryFake();
        var observations = new ConcurrentQueue<ConnectionSessionDistanceObservation>();
        var model = new LiveTelemetryModel(
            readMajorPIDs: new ReadMajorOBDPIDsUseCase(new ZD8ExtendedPIDDefinitionRepository(), telemetry),
            distanceDidChange: observations.Enqueue);

        model.Send(new LiveTelemetryAction.StartRequested(Endpoint, VehicleID, ZD8VehicleModelPolicy.ModelCode));
        await AsyncWait.UntilAsync(() => model.State.Phase == LiveTelemetryStatePhase.Loaded && !observations.IsEmpty);

        Assert.Equal(ZD8VehicleModelPolicy.ModelCode, model.State.VehicleModelCode);
        Assert.Equal(
            ZD8ExtendedPIDDefinitionRepository.Requests.ToHashSet(),
            model.State.Samples.Select(sample => sample.Request).ToHashSet());
        Assert.Equal(
            new ConnectionSessionDistanceObservation(ConnectionSessionDistanceSource.VehicleSpecificOdometer, 12_345.6, "ZD8"),
            observations.FirstOrDefault());
        model.Send(new LiveTelemetryAction.StopRequested());
    });
}

/// <summary>Swift の <c>@MainActor</c> の代わりに、テスト本体を単一スレッドの同期コンテキストで実行します。</summary>
file sealed class MainThread : SynchronizationContext
{
    /// <summary>このスレッドで実行を待つ継続です。</summary>
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];

    /// <summary>非同期のテスト本体を完了まで実行します。</summary>
    /// <remarks>責務: すべての継続を呼出元スレッドで順に処理し、テスト本体の例外をそのまま伝えます。</remarks>
    /// <param name="body">実行するテスト本体。</param>
    public static void Run(Func<Task> body)
    {
        var previous = Current;
        var context = new MainThread();
        SetSynchronizationContext(context);
        try
        {
            var task = body();
            task.ContinueWith(_ => context.queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in context.queue.GetConsumingEnumerable())
            {
                callback(state);
            }

            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    /// <summary>継続をこのスレッドの待ち行列へ積みます。</summary>
    /// <remarks>責務: テスト終了後に届いた継続はスレッドプールで実行し、取りこぼしや例外を起こさないようにします。</remarks>
    /// <param name="d">実行する処理。</param>
    /// <param name="state">処理へ渡す値。</param>
    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    /// <summary>同期送信は使わないため受け付けません。</summary>
    /// <remarks>責務: 単一スレッドでのデッドロックを避けるため同期送信を拒否します。</remarks>
    /// <param name="d">実行しない処理。</param>
    /// <param name="state">使わない値。</param>
    /// <exception cref="NotSupportedException">常に送出します。</exception>
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
}

/// <summary>読取結果の辞書を作る補助です。</summary>
file static class Responses
{
    /// <summary>全要求へ同じpayloadを割り当てた応答を返します。</summary>
    /// <remarks>責務: テスト用の固定応答辞書を完了済みタスクとして返します。</remarks>
    /// <param name="requests">応答する要求。</param>
    /// <param name="payload">割り当てるpayload。</param>
    /// <returns>要求ごとの固定応答。</returns>
    public static Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Same(
        IReadOnlyList<OBDPIDRequest> requests,
        byte[] payload) =>
        From(requests.ToDictionary(request => request, _ => (IReadOnlyList<byte>)payload));

    /// <summary>辞書を完了済みタスクとして返します。</summary>
    /// <remarks>責務: テスト用の応答辞書をポートの戻り値の型へ合わせます。</remarks>
    /// <param name="responses">返す応答辞書。</param>
    /// <returns>完了済みの応答。</returns>
    public static Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> From(
        Dictionary<OBDPIDRequest, IReadOnlyList<byte>> responses) =>
        Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(responses);
}

/// <summary>車両接続中のシステムスリープ抑止通知を記録します。</summary>
file sealed class VehicleConnectionSystemSleepInhibitorSpy : IVehicleConnectionSystemSleepInhibiting
{
    /// <summary>通知された車両接続状態です。</summary>
    private readonly ConcurrentQueue<bool> connectionStates = new();

    /// <summary>通知された車両接続状態です。</summary>
    public IReadOnlyList<bool> ConnectionStates => connectionStates.ToList();

    /// <summary>通知された車両接続状態を記録します。</summary>
    /// <remarks>責務: 1件の車両接続状態を検証用履歴へ追加します。</remarks>
    /// <param name="isActive">通知された車両接続状態。</param>
    public void SetVehicleConnectionActive(bool isActive) => connectionStates.Enqueue(isActive);
}

/// <summary>定義一覧だけを返すテスト用PID Repositoryの共通部分です。</summary>
file abstract class FixedDefinitionsRepository : IOBDPIDDefinitionRepository
{
    /// <summary>返すPID定義一覧を返します。</summary>
    /// <remarks>責務: テストごとの固定PID定義一覧を返します。</remarks>
    /// <returns>固定PID定義一覧。</returns>
    public abstract IReadOnlyList<OBDPIDDefinition> Definitions();

    /// <summary>テスト対象外の保存要求を受け付けます。</summary>
    /// <remarks>責務: 未使用のPID定義保存を変更なしで完了します。</remarks>
    /// <param name="definition">テストでは使用しない定義。</param>
    public void Upsert(OBDPIDDefinition definition)
    {
    }

    /// <summary>テスト対象外の単一定義読込へ未登録を返します。</summary>
    /// <remarks>責務: 未使用の単一PID照会を未登録結果へ変換します。</remarks>
    /// <param name="service">テストでは使用しないService番号。</param>
    /// <param name="pid">テストでは使用しないPID番号。</param>
    /// <returns>常に <c>null</c>。</returns>
    public OBDPIDDefinition? Definition(byte service, byte pid) => null;
}

/// <summary>回転数と車速定義を返すテストRepositoryです。</summary>
file sealed class EngineSpeedAndVehicleSpeedDefinitionRepository : FixedDefinitionsRepository
{
    /// <summary>回転数と車速の定義を返します。</summary>
    /// <remarks>責務: 標準ポーリングで数値化する2件の固定定義を返します。</remarks>
    /// <returns>Service 01 PID 0Cと0Dの定義。</returns>
    public override IReadOnlyList<OBDPIDDefinition> Definitions() =>
    [
        new OBDPIDDefinition(0x01, 0x0C, "test.engine_speed", 2, "(A * 256 + B) / 4", "rpm", 0, null, "test://engine-speed", 1),
        new OBDPIDDefinition(0x01, 0x0D, "test.vehicle_speed", 1, "A", "km/h", 0, null, "test://vehicle-speed", 1),
    ];
}

/// <summary>標準2件とZD8専用2件を返すテストRepositoryです。</summary>
file sealed class ZD8ExtendedPIDDefinitionRepository : FixedDefinitionsRepository
{
    /// <summary>リアルタイム表示で期待する全要求です。</summary>
    public static readonly IReadOnlyList<OBDPIDRequest> Requests =
    [
        new(0x01, 0x0C),
        new(0x01, 0x0D),
        new(0x21, 0x02),
        new(0x21, 0x17),
    ];

    /// <summary>標準2件とZD8専用2件の定義を返します。</summary>
    /// <remarks>
    /// 責務: リアルタイム取得へ標準PIDおよび確定済みZD8専用PIDを供給します。
    /// ZD8専用定義はData層の <c>ZD8OBDPIDSeed</c> を写したものです（Application テストは Data 層を参照できないため）。
    /// </remarks>
    /// <returns>回転数、車速、走行距離、AT油温の定義。</returns>
    public override IReadOnlyList<OBDPIDDefinition> Definitions() =>
    [
        .. new EngineSpeedAndVehicleSpeedDefinitionRepository().Definitions(),
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

/// <summary>標準PIDとZD8専用PIDへ固定応答を返す通信境界です。</summary>
file sealed class ZD8ExtendedPIDTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>標準要求へ回転数と車速の固定Payloadを返します。</summary>
    /// <remarks>責務: 受け取った標準要求を数式評価可能な固定応答へ変換します。</remarks>
    /// <param name="requests">標準PID要求。</param>
    /// <param name="endpoint">テストでは使用しない終端。</param>
    /// <returns>要求に存在する回転数と車速のPayload。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        var responses = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        foreach (var request in requests)
        {
            if (request == new OBDPIDRequest(0x01, 0x0C))
            {
                responses[request] = new byte[] { 0x1F, 0x40 };
            }

            if (request == new OBDPIDRequest(0x01, 0x0D))
            {
                responses[request] = new byte[] { 60 };
            }
        }

        return Responses.From(responses);
    }

    /// <summary>ZD8専用要求へ走行距離とAT油温の固定Payloadを返します。</summary>
    /// <remarks>責務: ヘッダー付き専用定義を数式評価可能な固定応答へ変換します。</remarks>
    /// <param name="definitions">ZD8専用PID定義。</param>
    /// <param name="endpoint">テストでは使用しない終端。</param>
    /// <returns>走行距離とAT油温のPayload。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> ReadVehicleSpecific(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint)
    {
        var responses = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        foreach (var definition in definitions)
        {
            var request = new OBDPIDRequest(definition.Service, definition.Pid);
            if (request.Pid == 0x02)
            {
                responses[request] = new byte[] { 0x00, 0x01, 0xE2, 0x40 };
            }

            if (request.Pid == 0x17)
            {
                responses[request] = new byte[] { 130 };
            }
        }

        return Responses.From(responses);
    }

    /// <summary>テスト通信セッションを終了します。</summary>
    /// <remarks>責務: 終了要求を副作用なしで受理します。</remarks>
    /// <returns>完了済みタスク。</returns>
    public Task EndSession() => Task.CompletedTask;
}

/// <summary>A6定義だけを返すテスト用PID Repositoryです。</summary>
file sealed class FixedOdometerDefinitionRepository : FixedDefinitionsRepository
{
    /// <summary>累積走行距離定義を1件返します。</summary>
    /// <remarks>責務: Service 01 PID A6の固定定義一覧を返します。</remarks>
    /// <returns>4バイトの累積走行距離定義1件。</returns>
    public override IReadOnlyList<OBDPIDDefinition> Definitions() =>
    [
        new OBDPIDDefinition(
            0x01, 0xA6, "obd.pid.odometer", 4, "(A * 16777216 + B * 65536 + C * 256 + D) / 10", "km", 0, null, "test://odometer", 1),
    ];
}

/// <summary>PID 31定義だけを返すテスト用PID Repositoryです。</summary>
file sealed class FixedDistanceSinceCodesClearedDefinitionRepository : FixedDefinitionsRepository
{
    /// <summary>コード消去後走行距離定義を1件返します。</summary>
    /// <remarks>責務: Service 01 PID 31の固定定義一覧を返します。</remarks>
    /// <returns>2バイトのコード消去後走行距離定義1件。</returns>
    public override IReadOnlyList<OBDPIDDefinition> Definitions() =>
    [
        new OBDPIDDefinition(
            0x01, 0x31, "obd.pid.distance_since_codes_cleared", 2, "A * 256 + B", "km", 0, 65_535,
            "test://distance-since-codes-cleared", 1),
    ];
}

/// <summary>回転数定義を返すテスト用PID Repositoryです。</summary>
file sealed class FixedPIDDefinitionRepository : FixedDefinitionsRepository
{
    /// <summary>回転数定義を1件返します。</summary>
    /// <remarks>責務: 継続取得に使用する固定PID定義一覧を返します。</remarks>
    /// <returns>Service 01 PID 0Cの定義1件。</returns>
    public override IReadOnlyList<OBDPIDDefinition> Definitions() =>
    [
        new OBDPIDDefinition(0x01, 0x0C, "test.engine_speed", 2, "(A * 256 + B) / 4", "rpm", 0, null, "test://engine-speed", 1),
    ];
}

/// <summary>全要求へ固定バイトを返すテスト用PID境界です。</summary>
/// <remarks>Swift の <c>FixedOdometerTelemetryFake</c> と <c>FixedDistanceSinceCodesClearedTelemetryFake</c> を1つにまとめています。</remarks>
file sealed class FixedPayloadTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>全要求へ返すpayloadです。</summary>
    private readonly byte[] payload;

    /// <summary>返すpayloadを固定します。</summary>
    /// <remarks>責務: 固定応答を提供するテスト境界を構築します。</remarks>
    /// <param name="payload">全要求へ返すpayload。</param>
    public FixedPayloadTelemetryFake(byte[] payload)
    {
        this.payload = payload;
    }

    /// <summary>要求PIDへ固定バイトを返します。</summary>
    /// <remarks>責務: 1回の読取要求を固定応答へ変換します。</remarks>
    /// <param name="requests">応答するPID要求。</param>
    /// <param name="endpoint">テストでは使用しない接続終端。</param>
    /// <returns>各要求へ固定バイトを割り当てた辞書。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) => Responses.Same(requests, payload);
}

/// <summary>読取回数を記録して固定回転数バイトを返します。</summary>
file sealed class CountingPIDTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>受け取った読取要求の回数です。</summary>
    private int readCount;

    /// <summary>受け取った読取要求の回数です。</summary>
    public int ReadCount => Volatile.Read(ref readCount);

    /// <summary>読取回数を増やして要求PIDへ固定バイトを返します。</summary>
    /// <remarks>責務: 1回のPID読取要求を計数済み固定応答へ変換します。</remarks>
    /// <param name="requests">応答するPID要求。</param>
    /// <param name="endpoint">テストでは使用しない接続終端。</param>
    /// <returns>各要求に対する800 rpm相当バイト。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        Interlocked.Increment(ref readCount);
        return Responses.Same(requests, [0x0C, 0x80]);
    }
}

/// <summary>終了通知回数を記録する固定応答PID境界です。</summary>
file sealed class EndTrackingPIDTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>受け取った終了通知の回数です。</summary>
    private int endCount;

    /// <summary>受け取った終了通知の回数です。</summary>
    public int EndCount => Volatile.Read(ref endCount);

    /// <summary>要求PIDへ固定回転数バイトを返します。</summary>
    /// <remarks>責務: 1回のPID読取要求を固定応答へ変換します。</remarks>
    /// <param name="requests">応答するPID要求。</param>
    /// <param name="endpoint">テストでは使用しない接続終端。</param>
    /// <returns>各要求に対する800 rpm相当バイト。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) => Responses.Same(requests, [0x0C, 0x80]);

    /// <summary>終了通知回数を増やします。</summary>
    /// <remarks>責務: 1件の通信セッション終了通知を記録します。</remarks>
    /// <returns>完了済みタスク。</returns>
    public Task EndSession()
    {
        Interlocked.Increment(ref endCount);
        return Task.CompletedTask;
    }
}

/// <summary>初回だけ応答し、その後はECU無応答を再現するPID境界です。</summary>
file sealed class InitialResponseThenEmptyPIDTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>受け取った読取回数です。</summary>
    private int readCount;

    /// <summary>受け取った終了通知の回数です。</summary>
    private int endCount;

    /// <summary>受け取った終了通知の回数です。</summary>
    public int EndCount => Volatile.Read(ref endCount);

    /// <summary>初回は固定応答、その後は空応答を返します。</summary>
    /// <remarks>責務: 継続取得中にECU応答が消失する境界を再現します。</remarks>
    /// <param name="requests">初回だけ応答するPID要求。</param>
    /// <param name="endpoint">テストでは使用しない接続終端。</param>
    /// <returns>初回は固定応答、2回目以降は空辞書。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        if (Interlocked.Increment(ref readCount) != 1)
        {
            return Responses.From([]);
        }

        return Responses.Same(requests, [0x0C, 0x80]);
    }

    /// <summary>終了通知回数を増やします。</summary>
    /// <remarks>責務: 自動切断による通信セッション終了通知を記録します。</remarks>
    /// <returns>完了済みタスク。</returns>
    public Task EndSession()
    {
        Interlocked.Increment(ref endCount);
        return Task.CompletedTask;
    }
}

/// <summary>実PID読取手段がないプラットフォームの境界を再現します。</summary>
/// <remarks>Data層の <c>UnavailableOBDPIDTelemetryAdapter</c> と同じ振る舞いです（Application テストは Data 層を参照できないため）。</remarks>
file sealed class UnavailableOBDPIDTelemetryFake : IOBDPIDTelemetryPort
{
    /// <summary>全要求を利用不能として終了します。</summary>
    /// <remarks>責務: 実車PID読取未提供状態を仮成功に変換せず返します。</remarks>
    /// <param name="requests">実行しないPID要求。</param>
    /// <param name="endpoint">使用しない接続終端。</param>
    /// <returns>この実装は常に失敗します。</returns>
    /// <exception cref="OBDPIDTelemetryException">常に <see cref="OBDPIDTelemetryError.Unavailable"/>。</exception>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) =>
        Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable));
}

/// <summary>PID定義一覧の読込に必ず失敗します。</summary>
file sealed class FailingPIDDefinitionRepository : FixedDefinitionsRepository
{
    /// <summary>PID定義一覧の読込失敗を送出します。</summary>
    /// <remarks>責務: LiveTelemetryへRepository読込失敗を通知します。</remarks>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="InvalidOperationException">常にテスト用読込エラー。</exception>
    public override IReadOnlyList<OBDPIDDefinition> Definitions() =>
        throw new InvalidOperationException("ReadError");
}
