using System.Text;
using Microsoft.Extensions.Logging;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>OBD の接続・送受信・探索の診断ログ（<c>obd.connect</c> / <c>obd.wire</c> / <c>obd.discovery</c>）を検証します。</summary>
/// <remarks>
/// 実機のアダプターがまだ検証できていないため、接続や取得の失敗の段階がログだけで分かること、
/// VIN と Bluetooth のアドレスが伏せられていることを、偽の Transport で確かめます。
/// </remarks>
[Collection(OBDDiagnosticLogCollection.Name)]
public sealed class OBDDiagnosticLogTests
{
    /// <summary>テストの VIN です。</summary>
    private const string Vin = "1D4GP00R55B123456";

    /// <summary>識別のコマンドに対する成功の応答です（ATZ、設定 5 件、ATI、0902、ATDP）。</summary>
    private static string[] IdentificationResponses =>
    [
        "ELM327 v1.5\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>",
        "OBDLink MX+ r5.6.2\r>",
        "014\r0: 49 02 01 31 44 34\r1: 47 50 30 30 52 35 35\r2: 42 31 32 33 34 35 36\r>",
        "AUTO, ISO 15765-4 (CAN 11/500)\r>",
    ];

    /// <summary>USB シリアルの終端です。</summary>
    private static OBDConnectionEndpoint SerialEndpoint => new(OBDConnectionEndpointTransport.Serial, "COM7", "OBDLink EX");

    /// <summary>BLE の終端です（アドレスは伏せる対象）。</summary>
    private static OBDConnectionEndpoint BluetoothLowEnergyEndpoint =>
        new(OBDConnectionEndpointTransport.BluetoothLowEnergy, "AA:BB:CC:DD:EE:FF", "OBDLink CX");

    /// <summary>識別の成功で、各段階・送受信・識別・切断が順に出ることを確かめます。</summary>
    /// <remarks>責務: <c>obd.connect.started</c> から <c>obd.connect.succeeded</c> までの <c>evt</c> と主な値を確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task IdentificationSuccessWritesStagesWireAndIdentified()
    {
        using var capture = new DiagnosticLogCapture();
        var transport = new ScriptedTransport(IdentificationResponses);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport);

        _ = await adapter.IdentifyVehicle(SerialEndpoint);

        var started = Assert.Single(capture.Named("obd.connect.started"));
        Assert.Equal("serial", started.Prop("Transport"));
        Assert.Equal("identify", started.Prop("Purpose"));
        Assert.Equal("COM7", started.Prop("Device"));
        Assert.Equal(LogLevel.Information, started.Level);
        Assert.Equal(DiagnosticCategories.ObdConnect, started.Category);

        var stages = capture.Named("obd.connect.stage.succeeded").Select(stage => stage.Prop("Stage")?.ToString() ?? string.Empty).ToArray();
        Assert.Equal<string>(
            ["transport-create", "open", "reset", "configure", "configure", "configure", "configure", "configure", "adapter-identity", "vin-request", "protocol"],
            stages);

        var wire = capture.Named("obd.wire.exchanged");
        Assert.Equal(9, wire.Count);
        Assert.All(wire, exchange => Assert.Equal(LogLevel.Trace, exchange.Level));
        Assert.All(wire, exchange => Assert.Equal(DiagnosticCategories.ObdWire, exchange.Category));
        Assert.Equal("ATZ", wire[0].Prop("Command"));
        Assert.Equal("ELM327 v1.5\\r>", wire[0].Prop("Response"));

        var identified = Assert.Single(capture.Named("obd.connect.identified"));
        Assert.Equal("OBDLink MX+ r5.6.2", identified.Prop("Adapter"));
        Assert.Equal("AUTO, ISO 15765-4 (CAN 11/500)", identified.Prop("Protocol"));
        Assert.Equal("…3456", identified.Prop("Vin"));

        var disconnected = Assert.Single(capture.Named("obd.connect.disconnected"));
        Assert.Equal("identify-completed", disconnected.Prop("Reason"));
        Assert.Single(capture.Named("obd.connect.succeeded"));
        Assert.Empty(capture.Named("obd.connect.failed"));
    }

    /// <summary>VIN を返す要求（0902）の応答と VIN の全桁が、どのイベントにも入らないことを確かめます。</summary>
    /// <remarks>責務: <c>obd.wire</c> の 0902 の応答が伏せられ、識別の行には末尾 4 桁だけが入ることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task VinResponseIsRedacted()
    {
        using var capture = new DiagnosticLogCapture();
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => new ScriptedTransport(IdentificationResponses));

        _ = await adapter.IdentifyVehicle(SerialEndpoint);

        var vinExchange = Assert.Single(capture.Named("obd.wire.exchanged"), exchange => (string?)exchange.Prop("Command") == "0902");
        var response = Assert.IsType<string>(vinExchange.Prop("Response"));
        Assert.DoesNotContain("49 02", response, StringComparison.Ordinal);
        Assert.Contains("伏せました", response, StringComparison.Ordinal);
        var text = capture.AllText();
        Assert.DoesNotContain(Vin, text, StringComparison.Ordinal);
        Assert.DoesNotContain("31 44 34", text, StringComparison.Ordinal);
    }

    /// <summary>BLE の終端のアドレスが末尾 2 バイトだけになることを確かめます。</summary>
    /// <remarks>責務: 接続の開始の行の <c>Device</c> が伏せられ、アドレスの全体がどこにも出ないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task BluetoothAddressIsRedacted()
    {
        using var capture = new DiagnosticLogCapture();
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => new ScriptedTransport(IdentificationResponses));

        _ = await adapter.IdentifyVehicle(BluetoothLowEnergyEndpoint);

        var started = Assert.Single(capture.Named("obd.connect.started"));
        Assert.Equal("ble", started.Prop("Transport"));
        Assert.Equal("…:EE:FF", started.Prop("Device"));
        Assert.DoesNotContain("AA:BB:CC", capture.AllText(), StringComparison.Ordinal);
    }

    /// <summary>VIN の要求が拒否されたとき、段階・Transport・原因・例外つきの Error が 1 行出ることを確かめます。</summary>
    /// <remarks>責務: <c>obd.connect.failed</c> の <c>Stage</c> が <c>vin-request</c> で、拒否された応答も伏せられることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task RejectedVinRequestWritesStagedError()
    {
        using var capture = new DiagnosticLogCapture();
        var responses = IdentificationResponses;
        responses[7] = "NO DATA\r>";
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => new ScriptedTransport(responses));

        await Assert.ThrowsAsync<VehicleIdentificationException>(() => adapter.IdentifyVehicle(SerialEndpoint));

        var failed = Assert.Single(capture.Named("obd.connect.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.NotNull(failed.Exception);
        Assert.Equal("serial", failed.Prop("Transport"));
        Assert.Equal("vin-request", failed.Prop("Stage"));
        Assert.Equal("0902", failed.Prop("Command"));
        Assert.Equal("CommandRejected", failed.Prop("Cause"));
        Assert.DoesNotContain("NO DATA", (string?)failed.Prop("Response") ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("identify-failed", Assert.Single(capture.Named("obd.connect.disconnected")).Prop("Reason"));
        Assert.Empty(capture.Named("obd.connect.succeeded"));
    }

    /// <summary>ATZ の応答が期限切れになったとき、<c>reset</c> の段階の失敗と送受信の期限切れが出ることを確かめます。</summary>
    /// <remarks>責務: タイムアウトが <c>obd.wire.failed</c>（TimedOut=true）と <c>obd.connect.failed</c>（Stage=reset）になることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ResetTimeoutWritesWireTimeoutAndResetStageError()
    {
        using var capture = new DiagnosticLogCapture();
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => new ScriptedTransport([]));

        await Assert.ThrowsAsync<VehicleIdentificationException>(() => adapter.IdentifyVehicle(SerialEndpoint));

        var wireFailed = Assert.Single(capture.Named("obd.wire.failed"));
        Assert.Equal("ATZ", wireFailed.Prop("Command"));
        Assert.Equal(true, wireFailed.Prop("TimedOut"));
        var failed = Assert.Single(capture.Named("obd.connect.failed"));
        Assert.Equal("reset", failed.Prop("Stage"));
        Assert.Equal("ResponseTimedOut", failed.Prop("Cause"));
    }

    /// <summary>Transport を開けないとき、<c>open</c> の段階の失敗として出ることを確かめます。</summary>
    /// <remarks>責務: 開く段階の失敗が送受信より前に段階つきで残ることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task OpenFailureWritesOpenStageError()
    {
        using var capture = new DiagnosticLogCapture();
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => new ScriptedTransport([], failsOpen: true));

        await Assert.ThrowsAsync<VehicleIdentificationException>(() => adapter.IdentifyVehicle(SerialEndpoint));

        var failed = Assert.Single(capture.Named("obd.connect.failed"));
        Assert.Equal("open", failed.Prop("Stage"));
        Assert.Equal("ConnectionFailed", failed.Prop("Cause"));
        Assert.Empty(capture.Named("obd.wire.exchanged"));
    }

    /// <summary>取得中の期限切れで接続を閉じ、次の要求で開き直したことが Warning で出ることを確かめます。</summary>
    /// <remarks>責務: <c>obd.connect.request.failed</c>、理由つきの <c>obd.connect.disconnected</c>、<c>obd.connect.reopened</c> を確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryTimeoutClosesAndReopenIsWarned()
    {
        using var capture = new DiagnosticLogCapture();
        var transports = new Queue<ScriptedTransport>(
        [
            new ScriptedTransport(["ELM327 v1.5\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", ScriptedTransport.Timeout]),
            new ScriptedTransport(["ELM327 v1.5\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "41 0C 1F 40\r>"]),
        ]);
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transports.Dequeue());
        var request = new OBDPIDRequest(0x01, 0x0C);

        var first = await adapter.ReadObservations([request], SerialEndpoint);
        var second = await adapter.ReadObservations([request], SerialEndpoint);

        Assert.IsType<OBDPIDRequestTransportOutcome.TimedOut>(first[0].Outcome);
        Assert.IsType<OBDPIDRequestTransportOutcome.Responded>(second[0].Outcome);
        var requestFailed = Assert.Single(capture.Named("obd.connect.request.failed"));
        Assert.Equal(LogLevel.Warning, requestFailed.Level);
        Assert.Equal("010C", requestFailed.Prop("Command"));
        Assert.Equal("response-timeout", Assert.Single(capture.Named("obd.connect.disconnected")).Prop("Reason"));
        var reopened = Assert.Single(capture.Named("obd.connect.reopened"));
        Assert.Equal(LogLevel.Warning, reopened.Level);
        Assert.Equal("response-timeout", reopened.Prop("PreviousReason"));
        Assert.Equal(2, capture.Named("obd.connect.succeeded").Count);
        Assert.Contains(capture.Named("obd.wire.exchanged"), exchange => (string?)exchange.Prop("Command") == "ATSH7DF");
    }

    /// <summary>初期化の AT コマンドが拒否されたとき、どのコマンドで失敗したかが出ることを確かめます。</summary>
    /// <remarks>責務: 取得の接続の <c>configure</c> の段階の失敗に、コマンドと拒否された応答が入ることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TelemetryInitializationRejectionNamesCommand()
    {
        using var capture = new DiagnosticLogCapture();
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => new ScriptedTransport(["ELM327 v1.5\r>", "OK\r>", "?\r>"]));

        await Assert.ThrowsAnyAsync<Exception>(() => adapter.Read([new OBDPIDRequest(0x01, 0x0C)], SerialEndpoint));

        var failed = Assert.Single(capture.Named("obd.connect.failed"));
        Assert.Equal("configure", failed.Prop("Stage"));
        Assert.Equal("ATL0", failed.Prop("Command"));
        Assert.Equal("?\\r", failed.Prop("Response"));
        Assert.Equal("initialize-failed", Assert.Single(capture.Named("obd.connect.disconnected")).Prop("Reason"));
    }

    /// <summary>取得を終えたとき、理由つきの切断が出ることを確かめます。</summary>
    /// <remarks>責務: <c>EndSession</c> が <c>session-ended</c> の切断を書くことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task EndSessionWritesSessionEndedDisconnect()
    {
        using var capture = new DiagnosticLogCapture();
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => new ScriptedTransport(
            ["ELM327 v1.5\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "41 0C 1F 40\r>"]));

        _ = await adapter.Read([new OBDPIDRequest(0x01, 0x0C)], SerialEndpoint);
        await adapter.EndSession();

        Assert.Equal("session-ended", Assert.Single(capture.Named("obd.connect.disconnected")).Prop("Reason"));
    }

    /// <summary>無効なレベルでは送受信のイベントを作らないことを確かめます。</summary>
    /// <remarks>責務: Trace を受け取らない出力先へ <c>obd.wire.exchanged</c> を渡さないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task DisabledWireLevelEmitsNothing()
    {
        var sink = new LevelFilteredSink(LogLevel.Information);
        using (DiagnosticLog.Install(sink))
        {
            var adapter = new SerialELMVehicleIdentificationAdapter(_ => new ScriptedTransport(IdentificationResponses));
            _ = await adapter.IdentifyVehicle(SerialEndpoint);
        }

        Assert.DoesNotContain(sink.Events, diagnosticEvent => diagnosticEvent.Category == DiagnosticCategories.ObdWire);
        Assert.DoesNotContain(sink.Events, diagnosticEvent => diagnosticEvent.Level < LogLevel.Information);
        Assert.Contains(sink.Events, diagnosticEvent => diagnosticEvent.EventName == "obd.connect.identified");
    }

    /// <summary>Bluetooth の探索の一方が失敗したとき、例外つきの Warning が出ることを確かめます。</summary>
    /// <remarks>責務: 握りつぶしていた探索の失敗が <c>obd.discovery.source.failed</c> に残ることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task CombinedDiscoveryLogsFailedSource()
    {
        using var capture = new DiagnosticLogCapture();
        var discovery = new CombinedAdapterDiscovery(
            new FailingDiscovery(new InvalidOperationException("system down")),
            new FixedDiscovery([]));

        _ = await discovery.DiscoverAdapters(AdapterTransportMode.Bluetooth);

        var failed = Assert.Single(capture.Named("obd.discovery.source.failed"));
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal("system", failed.Prop("Source"));
        Assert.Equal(nameof(InvalidOperationException), failed.Exception?.Type.Split('.').Last());
    }

    /// <summary>台本どおりに応答する偽の Transport です。</summary>
    private sealed class ScriptedTransport : IOBDCommandTransport
    {
        /// <summary>この応答の代わりに期限切れを返す印です。</summary>
        public const string Timeout = "<timeout>";

        /// <summary>順番に返す応答です。</summary>
        private readonly Queue<string> responses;

        /// <summary>開くときに失敗するか。</summary>
        private readonly bool failsOpen;

        /// <summary>台本を保持して作ります。</summary>
        /// <param name="responses">読取ごとに返す応答（<see cref="Timeout"/> は期限切れ）。</param>
        /// <param name="failsOpen">開くときに失敗するか。</param>
        public ScriptedTransport(IEnumerable<string> responses, bool failsOpen = false)
        {
            this.responses = new Queue<string>(responses);
            this.failsOpen = failsOpen;
        }

        /// <inheritdoc />
        public Task Open() => failsOpen
            ? Task.FromException(new VehicleIdentificationException(new VehicleIdentificationError.ConnectionFailed()))
            : Task.CompletedTask;

        /// <inheritdoc />
        public Task Write(byte[] data) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<byte[]> ReadUntilPrompt()
        {
            if (responses.Count == 0 || responses.Peek() == Timeout)
            {
                if (responses.Count > 0)
                {
                    responses.Dequeue();
                }

                return Task.FromException<byte[]>(new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut()));
            }

            return Task.FromResult(Encoding.UTF8.GetBytes(responses.Dequeue()));
        }

        /// <inheritdoc />
        public Task Close() => Task.CompletedTask;
    }

    /// <summary>指定のレベル以上だけを受け取る出力先です。</summary>
    /// <param name="minimum">受け取る最低のレベル。</param>
    private sealed class LevelFilteredSink(LogLevel minimum) : IDiagnosticSink
    {
        /// <summary>受け取ったイベントです。</summary>
        public List<DiagnosticEvent> Events { get; } = [];

        /// <inheritdoc />
        public bool IsEnabled(string category, LogLevel level) => level >= minimum;

        /// <inheritdoc />
        public void Emit(DiagnosticEvent diagnosticEvent)
        {
            lock (Events)
            {
                Events.Add(diagnosticEvent);
            }
        }

        /// <inheritdoc />
        public bool Flush(TimeSpan timeout) => true;

        /// <inheritdoc />
        public void WriteCrash(DiagnosticEvent crash) => Emit(crash);
    }

    /// <summary>必ず失敗する探索です。</summary>
    /// <param name="error">投げる例外。</param>
    private sealed class FailingDiscovery(Exception error) : IAdapterDiscoveryPort
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode) =>
            Task.FromException<IReadOnlyList<DiscoveredAdapter>>(error);
    }

    /// <summary>決まった候補を返す探索です。</summary>
    /// <param name="adapters">返す候補。</param>
    private sealed class FixedDiscovery(IReadOnlyList<DiscoveredAdapter> adapters) : IAdapterDiscoveryPort
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode) => Task.FromResult(adapters);
    }
}
