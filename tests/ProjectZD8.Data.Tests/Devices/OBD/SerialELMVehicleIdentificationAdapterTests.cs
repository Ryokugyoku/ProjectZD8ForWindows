using System.Text;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>選択済みELM/STN互換Adapterのコマンド順序、観測保持、失敗終端を検証します。</summary>
public sealed class SerialELMVehicleIdentificationAdapterTests
{
    /// <summary>テスト用EXシリアル終端です。</summary>
    private static OBDConnectionEndpoint Endpoint =>
        new(OBDConnectionEndpointTransport.Serial, "COM9", "OBDLink EX");

    /// <summary>全許可コマンドへ対応する成功応答です。</summary>
    private static string[] SuccessfulResponses =>
    [
        "OBDLink EX 5.9.1\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>", "OK\r>",
        "ELM327 v1.4b\r>",
        "014\r0: 49 02 01 31 44 34\r1: 47 50 30 30 52 35 35\r2: 42 31 32 33 34 35 36\r>",
        "AUTO, ISO 15765-4 (CAN 11/500)\r>",
    ];

    /// <summary>完全な識別フローがVINと全原文を返します。</summary>
    /// <remarks>責務: 標準初期化、VIN、protocol要求が順序通り実行されTransportが閉じることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task IdentifyExecutesAllowlistedSequenceAndPreservesRawResponses()
    {
        var transport = new OBDCommandTransportFake(SuccessfulResponses);
        var observedAt = DateTimeOffset.FromUnixTimeSeconds(100);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport, () => observedAt);

        var snapshot = await adapter.IdentifyVehicle(Endpoint);

        Assert.Equal("1D4GP00R55B123456", snapshot.Vin);
        Assert.Null(snapshot.ObdIdentifier);
        Assert.Equal(9, snapshot.RawResponses.Count);
        Assert.Equal(
            ["reset", "echoOff", "linefeedsOff", "spacesOn", "headersOff", "automaticProtocol", "adapterIdentity", "vehicleIdentificationNumber", "protocolDescription"],
            snapshot.RawResponses.Select(response => response.RequestID));
        Assert.Equal(["ATZ\r", "ATE0\r", "ATL0\r", "ATS1\r", "ATH0\r", "ATSP0\r", "ATI\r", "0902\r", "ATDP\r"], transport.Commands);
        Assert.Equal("ELM327 v1.4b", snapshot.Fields.Single(field => field.Id == "adapter").Value);
        Assert.Equal("AUTO, ISO 15765-4 (CAN 11/500)", snapshot.Fields.Single(field => field.Id == "obdProtocol").Value);
        Assert.Equal(observedAt, snapshot.ObservedAt);
        Assert.True(transport.DidClose);
    }

    /// <summary>空白埋めの実車識別子をVINと混同しません。</summary>
    /// <remarks>責務: Service 09の空白埋め値が非VINのOBD由来識別子として観測に残ることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task IdentifyPreservesSpacePaddedIdentifierSeparatelyFromVIN()
    {
        var responses = SuccessfulResponses;
        responses[7] = "014\r0: 49 02 01 20 20 20\r1: 20 20 20 20 5A 44 38\r2: 31 32 33 34 35 36 37\r>";
        var transport = new OBDCommandTransportFake(responses);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport);

        var snapshot = await adapter.IdentifyVehicle(Endpoint);

        Assert.Null(snapshot.Vin);
        Assert.Equal("ZD81234567", snapshot.ObdIdentifier);
    }

    /// <summary>既知UARTを提供するBLE終端でも同じ読取専用識別シーケンスを実行します。</summary>
    /// <remarks>責務: BLE UART終端がPeripheral固有Transport経由でVIN要求へ進むことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task IdentifiesThroughBluetoothLowEnergyByteStream()
    {
        var transport = new OBDCommandTransportFake(SuccessfulResponses);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport);
        var endpoint = new OBDConnectionEndpoint(OBDConnectionEndpointTransport.BluetoothLowEnergy, "AA-BB-CC-DD-EE-FF", "OBDLink BLE candidate");

        var snapshot = await adapter.IdentifyVehicle(endpoint);

        Assert.Equal("1D4GP00R55B123456", snapshot.Vin);
        Assert.Contains("0902\r", transport.Commands);
    }

    /// <summary>NO DATA応答をVIN未取得成功へ変換しません。</summary>
    /// <remarks>責務: 車両未応答時に型付き失敗を返してTransportを閉じることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task NoDataFailsAndClosesTransport()
    {
        var responses = SuccessfulResponses;
        responses[7] = "NO DATA\r>";
        var transport = new OBDCommandTransportFake(responses);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport);

        var error = await Assert.ThrowsAsync<VehicleIdentificationException>(() => adapter.IdentifyVehicle(Endpoint));

        Assert.Equal(
            new VehicleIdentificationError.StageFailed(
                VehicleIdentificationErrorStage.VehicleIdentificationRequest,
                VehicleIdentificationErrorCause.CommandRejected),
            error.Reason);
        Assert.True(transport.DidClose);
    }

    /// <summary>製品固有コマンドなしで選択済みシリアル機器へ車両識別を要求します。</summary>
    /// <remarks>責務: <c>ATI</c> の製品名に依存せず標準 <c>0902</c> 要求へ進むことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task SelectedSerialAdapterDoesNotRequireOBDLinkProductName()
    {
        var responses = SuccessfulResponses;
        responses[6] = "ELM327 compatible\r>";
        var transport = new OBDCommandTransportFake(responses);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport);

        _ = await adapter.IdentifyVehicle(Endpoint);

        Assert.Contains("0902\r", transport.Commands);
        Assert.DoesNotContain("STDIX\r", transport.Commands);
    }

    /// <summary>MX+のBluetooth Classic終端でも同じ読取専用識別シーケンスを実行します。</summary>
    /// <remarks>責務: Bluetooth ClassicバイトストリームがBLEとして拒否されずVIN要求へ進むことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task IdentifiesThroughBluetoothClassicByteStream()
    {
        var transport = new OBDCommandTransportFake(SuccessfulResponses);
        var adapter = new SerialELMVehicleIdentificationAdapter(_ => transport);
        var endpoint = new OBDConnectionEndpoint(OBDConnectionEndpointTransport.BluetoothClassic, "00-04-3E-12-34-56", "OBDLink MX+ 48318");

        var snapshot = await adapter.IdentifyVehicle(endpoint);

        Assert.Equal("1D4GP00R55B123456", snapshot.Vin);
        Assert.Contains("0902\r", transport.Commands);
    }

    /// <summary>Transport を作れない場合は生成段階の失敗として返します。</summary>
    /// <remarks>責務: Factory の失敗が <c>TransportCreation</c> 段階へ結び付くことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task TransportCreationFailureIsStaged()
    {
        var adapter = new SerialELMVehicleIdentificationAdapter(
            _ => throw new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported()));

        var error = await Assert.ThrowsAsync<VehicleIdentificationException>(() => adapter.IdentifyVehicle(Endpoint));

        Assert.Equal(
            new VehicleIdentificationError.StageFailed(
                VehicleIdentificationErrorStage.TransportCreation,
                VehicleIdentificationErrorCause.TransportUnsupported),
            error.Reason);
    }
}

/// <summary>EX識別テストへ決定的なコマンド応答列を返します。</summary>
file sealed class OBDCommandTransportFake : IOBDCommandTransport
{
    /// <summary>順番に返す加工前応答です。</summary>
    private readonly Queue<string> responses;

    /// <summary>応答列を保持して生成します。</summary>
    /// <remarks>責務: 1件のテストシナリオに必要な応答順序を固定します。</remarks>
    /// <param name="responses">readごとに先頭から返す応答。</param>
    public OBDCommandTransportFake(IEnumerable<string> responses)
    {
        this.responses = new Queue<string>(responses);
    }

    /// <summary>書き込まれたASCIIコマンドです。</summary>
    public List<string> Commands { get; } = [];

    /// <summary>openが呼ばれたかどうかです。</summary>
    public bool DidOpen { get; private set; }

    /// <summary>closeが呼ばれたかどうかです。</summary>
    public bool DidClose { get; private set; }

    /// <summary>Fake終端をopen済みにします。</summary>
    /// <remarks>責務: open呼出しを観測可能な真偽値へ記録します。</remarks>
    /// <returns>完了済みのタスク。</returns>
    public Task Open()
    {
        DidOpen = true;
        return Task.CompletedTask;
    }

    /// <summary>送信コマンドを記録します。</summary>
    /// <remarks>責務: 1件の書込みバイトをASCIIコマンド履歴へ追加します。</remarks>
    /// <param name="data">送信されたASCIIデータ。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task Write(byte[] data)
    {
        Commands.Add(Encoding.UTF8.GetString(data));
        return Task.CompletedTask;
    }

    /// <summary>次の固定応答を返します。</summary>
    /// <remarks>責務: 現在シナリオの未使用応答を先頭から1件返します。</remarks>
    /// <returns>UTF-8応答データ。</returns>
    public Task<byte[]> ReadUntilPrompt() =>
        responses.Count == 0
            ? Task.FromException<byte[]>(new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut()))
            : Task.FromResult(Encoding.UTF8.GetBytes(responses.Dequeue()));

    /// <summary>Fake終端をclose済みにします。</summary>
    /// <remarks>責務: close呼出しを観測可能な真偽値へ記録します。</remarks>
    /// <returns>完了済みのタスク。</returns>
    public Task Close()
    {
        DidClose = true;
        return Task.CompletedTask;
    }
}
