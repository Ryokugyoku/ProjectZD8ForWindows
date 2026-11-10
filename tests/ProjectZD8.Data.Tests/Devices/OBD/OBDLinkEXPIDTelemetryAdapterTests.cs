using System.Text;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>OBDLink EX／MX+主要PID読取の許可コマンド列と応答保持を検証します。</summary>
public sealed class OBDLinkEXPIDTelemetryAdapterTests
{
    /// <summary>テスト用EXシリアル終端です。</summary>
    private static OBDConnectionEndpoint Endpoint =>
        new(OBDConnectionEndpointTransport.Serial, "COM9", "OBDLink EX");

    /// <summary>拒否文字列と正応答を要求単位で区別します。</summary>
    /// <remarks>責務: NO DATAを非対応と推測せず未分類応答として保持し、後続正応答を失わないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ReadObservationsKeepsRejectedResponseUnclassifiedAndContinues()
    {
        var transport = new PIDTransportFake(
            "ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>",
            "OK\r>", "NO DATA\r>", "41 0C 1F 40\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);
        OBDPIDRequest[] requests = [new(0x01, 0x05), new(0x01, 0x0C)];

        var observations = await adapter.ReadObservations(requests, Endpoint);

        Assert.Equal(
            [
                new OBDPIDRequestTransportObservation(requests[0], new OBDPIDRequestTransportOutcome.UnclassifiedResponse()),
                new OBDPIDRequestTransportObservation(requests[1], new OBDPIDRequestTransportOutcome.Responded([0x1F, 0x40])),
            ],
            observations);
    }

    /// <summary>送信後の期限切れを該当要求だけへ対応付けます。</summary>
    /// <remarks>責務: 期限切れ要求をtyped結果として残し、未送信の後続要求を観測済みにしないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ReadObservationsStopsAfterTypedTimeout()
    {
        var transport = new PIDTransportFake("ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);
        OBDPIDRequest[] requests = [new(0x01, 0x05), new(0x01, 0x0C)];

        var observations = await adapter.ReadObservations(requests, Endpoint);

        Assert.Equal(
            [new OBDPIDRequestTransportObservation(requests[0], new OBDPIDRequestTransportOutcome.TimedOut())],
            observations);
        Assert.True(transport.DidClose);
    }

    /// <summary>空のPID要求では物理接続を開きません。</summary>
    /// <remarks>責務: 空バッチをBluetoothまたはシリアル接続なしの空応答へ変換することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task EmptyBatchDoesNotOpenTransport()
    {
        var transport = new PIDTransportFake();
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);

        var values = await adapter.Read([], Endpoint);

        Assert.Empty(values);
        Assert.False(transport.DidOpen);
    }

    /// <summary>2種の検証済みPIDを1回の接続で読み取ります。</summary>
    /// <remarks>責務: 初期化後に0105と010Cだけを送り、応答バイトを要求別に保持することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ReadsAllowlistedMajorPIDsAndClosesTransport()
    {
        var transport = new PIDTransportFake(
            "ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>",
            "OK\r>", "SEARCHING...\r41 05 85\r>", "41 0C 00 00\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);
        OBDPIDRequest[] requests = [new(0x01, 0x05), new(0x01, 0x0C)];

        var values = await adapter.Read(requests, Endpoint);
        await adapter.EndSession();

        Assert.Equal([0x85], values[requests[0]]);
        Assert.Equal([0x00, 0x00], values[requests[1]]);
        Assert.Equal(
            ["ATZ\r", "ATE0\r", "ATL0\r", "ATS1\r", "ATH0\r", "ATSP0\r", "ATSH7DF\r", "0105\r", "010C\r"],
            transport.Commands);
        Assert.True(transport.DidClose);
    }

    /// <summary>同じ終端への続けての読取では接続と初期化を再利用します。</summary>
    /// <remarks>責務: 継続取得で ATZ と ATSH を繰り返さず PID 要求だけを送ることを確認します（iOS の継続セッションと同じ）。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task SecondReadReusesInitializedSession()
    {
        var transport = new PIDTransportFake(
            "ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>",
            "OK\r>", "41 0D 10\r>", "41 0D 11\r>");
        var openCount = 0;
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ =>
        {
            openCount += 1;
            return transport;
        });
        var request = new OBDPIDRequest(0x01, 0x0D);

        var first = await adapter.Read([request], Endpoint);
        var second = await adapter.Read([request], Endpoint);

        Assert.Equal([0x10], first[request]);
        Assert.Equal([0x11], second[request]);
        Assert.Equal(1, openCount);
        Assert.Equal(["ATZ\r", "ATE0\r", "ATL0\r", "ATS1\r", "ATH0\r", "ATSP0\r", "ATSH7DF\r", "010D\r", "010D\r"], transport.Commands);
    }

    /// <summary>MX+のBluetooth Classic終端からも許可済み主要PIDを読み取ります。</summary>
    /// <remarks>責務: Bluetooth Classic終端がELM/STNバイトストリームとして主要PID取得へ進むことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ReadsAllowlistedPIDThroughBluetoothClassicByteStream()
    {
        var transport = new PIDTransportFake(
            "ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "41 0C 1F 40\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);
        var request = new OBDPIDRequest(0x01, 0x0C);
        var endpoint = new OBDConnectionEndpoint(OBDConnectionEndpointTransport.BluetoothClassic, "00-04-3E-12-34-56", "OBDLink MX+ 48318");

        var values = await adapter.Read([request], endpoint);
        await adapter.EndSession();

        Assert.Equal([0x1F, 0x40], values[request]);
        Assert.True(transport.DidOpen);
    }

    /// <summary>既知UARTを提供するBLE終端から許可済み主要PIDを読み取ります。</summary>
    /// <remarks>責務: BLE UART終端が主要PID取得の共通ELM/STNチャネルへ進むことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ReadsAllowlistedPIDThroughBluetoothLowEnergyByteStream()
    {
        var transport = new PIDTransportFake(
            "ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "41 0C 1F 40\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);
        var request = new OBDPIDRequest(0x01, 0x0C);
        var endpoint = new OBDConnectionEndpoint(OBDConnectionEndpointTransport.BluetoothLowEnergy, "AA-BB-CC-DD-EE-FF", "OBDLink BLE candidate");

        var values = await adapter.Read([request], endpoint);
        await adapter.EndSession();

        Assert.Equal([0x1F, 0x40], values[request]);
        Assert.True(transport.DidOpen);
    }

    /// <summary>ZD8専用PIDを定義済み物理ヘッダーへ送信します。</summary>
    /// <remarks>責務: 車種専用定義が7E0と7E1へ個別送信され、正応答だけを保持することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ReadsZD8PIDsWithDefinitionHeaders()
    {
        var transport = new PIDTransportFake(
            "ELM327 v1.4b\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>",
            "OK\r>", "61 02 00 01 E2 40\r>", "OK\r>", "61 17 76\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);

        var values = await adapter.ReadVehicleSpecific(ZD8OBDPIDSeedForTests.Definitions, Endpoint);
        await adapter.EndSession();

        Assert.Equal([0x00, 0x01, 0xE2, 0x40], values[new OBDPIDRequest(0x21, 0x02)]);
        Assert.Equal([0x76], values[new OBDPIDRequest(0x21, 0x17)]);
        Assert.Contains("ATSH7E0\r", transport.Commands);
        Assert.Contains("2102\r", transport.Commands);
        Assert.Contains("ATSH7E1\r", transport.Commands);
        Assert.Contains("2117\r", transport.Commands);
    }

    /// <summary>ELM初期化拒否時はPID要求へ進まずTransportを閉じます。</summary>
    /// <remarks>責務: 1件の拒否された初期化応答を接続資源解放済みのコマンド拒否へ変換することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task RejectedInitializationClosesTransportBeforePIDRequest()
    {
        var transport = new PIDTransportFake("ELM327 v1.4b\r>", "?\r>");
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);

        var error = await Assert.ThrowsAsync<OBDPIDTelemetryException>(() => adapter.Read([new(0x01, 0x0C)], Endpoint));

        Assert.Equal(OBDPIDTelemetryError.CommandRejected, error.Reason);
        Assert.Equal(["ATZ\r", "ATE0\r"], transport.Commands);
        Assert.True(transport.DidClose);
    }

    /// <summary>Service 01以外をTransport生成前に拒否します。</summary>
    /// <remarks>責務: 現在値取得以外のService/PIDが物理送信されないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task RejectsUnsupportedServiceBeforeOpeningTransport()
    {
        var transport = new PIDTransportFake();
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);

        var error = await Assert.ThrowsAsync<OBDPIDTelemetryException>(() => adapter.Read([new(0x02, 0x0C)], Endpoint));

        Assert.Equal(OBDPIDTelemetryError.UnsupportedPID, error.Reason);
        Assert.False(transport.DidOpen);
    }

    /// <summary>シリアル応答期限切れを車両無応答として返し、Transportを閉じます。</summary>
    /// <remarks>責務: 期限切れを自動切断可能なTelemetryエラーへ変換することを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TimeoutMapsToNoVehicleResponseAndClosesTransport()
    {
        var transport = new PIDTransportFake();
        var adapter = new OBDLinkEXPIDTelemetryAdapter(_ => transport);

        var error = await Assert.ThrowsAsync<OBDPIDTelemetryException>(() => adapter.Read([new(0x01, 0x0C)], Endpoint));

        Assert.Equal(OBDPIDTelemetryError.NoVehicleResponse, error.Reason);
        Assert.True(transport.DidClose);
    }
}

/// <summary>PID読取テストへ固定応答列を返すTransportです。</summary>
file sealed class PIDTransportFake : IOBDCommandTransport
{
    /// <summary>未使用の固定応答列です。</summary>
    private readonly Queue<string> responses;

    /// <summary>固定応答列を保持します。</summary>
    /// <remarks>責務: read順に返す決定的な応答列を初期化します。</remarks>
    /// <param name="responses">各readで先頭から返す応答。</param>
    public PIDTransportFake(params string[] responses)
    {
        this.responses = new Queue<string>(responses);
    }

    /// <summary>送信されたコマンド履歴です。</summary>
    public List<string> Commands { get; } = [];

    /// <summary>openが実行されたかどうかです。</summary>
    public bool DidOpen { get; private set; }

    /// <summary>closeが実行されたかどうかです。</summary>
    public bool DidClose { get; private set; }

    /// <summary>open呼出しを記録します。</summary>
    /// <remarks>責務: Transportのopen事実だけを観測可能にします。</remarks>
    /// <returns>完了済みのタスク。</returns>
    public Task Open()
    {
        DidOpen = true;
        return Task.CompletedTask;
    }

    /// <summary>送信コマンドを記録します。</summary>
    /// <remarks>責務: 1件のASCII送信データをコマンド履歴へ追加します。</remarks>
    /// <param name="data">送信するコマンドデータ。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task Write(byte[] data)
    {
        Commands.Add(Encoding.UTF8.GetString(data));
        return Task.CompletedTask;
    }

    /// <summary>次の固定応答を返します。</summary>
    /// <remarks>責務: 未使用応答列の先頭1件をプロンプト終端データとして返します。</remarks>
    /// <returns>次の固定応答データ。</returns>
    public Task<byte[]> ReadUntilPrompt() =>
        responses.Count == 0
            ? Task.FromException<byte[]>(new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut()))
            : Task.FromResult(Encoding.UTF8.GetBytes(responses.Dequeue()));

    /// <summary>close呼出しを記録します。</summary>
    /// <remarks>責務: Transportのclose事実だけを観測可能にします。</remarks>
    /// <returns>完了済みのタスク。</returns>
    public Task Close()
    {
        DidClose = true;
        return Task.CompletedTask;
    }
}

/// <summary>Data層の <c>ZD8OBDPIDSeed</c> をテスト用に写した定義です（永続化担当のフォルダーにあるため）。</summary>
file static class ZD8OBDPIDSeedForTests
{
    /// <summary>iOS 版と同じ出典URIです。</summary>
    private const string SourceURI = "projectzd8://requirements/zd8-pids/2026-07-23";

    /// <summary>走行距離とAT油温の定義です。</summary>
    public static readonly IReadOnlyList<OBDPIDDefinition> Definitions =
    [
        new(0x21, 0x02, "obd.pid.zd8.21.02.name", 4, "(A * 16777216 + B * 65536 + C * 256 + D) / 10", "km", 0, 429_496_729.5,
            SourceURI, 1, header: 0x7E0, vehicleModelCode: ZD8VehicleModelPolicy.ModelCode),
        new(0x21, 0x17, "obd.pid.zd8.21.17.name", 1, "A - 58", "°C", -58, 197,
            SourceURI, 1, header: 0x7E1, vehicleModelCode: ZD8VehicleModelPolicy.ModelCode),
    ];
}
