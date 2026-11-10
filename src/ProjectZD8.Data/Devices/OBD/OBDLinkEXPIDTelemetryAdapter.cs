using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>OBDLink EXまたはMX+でPID定義DB由来のService 01要求を読み取ります。</summary>
/// <remarks>
/// Swift の <c>actor</c> に対応します。UI スレッドから1件ずつ呼ぶ前提で、接続資源をフィールドに保持します。
/// </remarks>
public sealed class OBDLinkEXPIDTelemetryAdapter : IOBDPIDTelemetryPort
{
    /// <summary>ELM初期化で順に送るコマンドです。</summary>
    private static readonly ELM327Command[] InitializationCommands =
    [
        ELM327Command.Reset, ELM327Command.EchoOff, ELM327Command.LinefeedsOff,
        ELM327Command.SpacesOn, ELM327Command.HeadersOff, ELM327Command.AutomaticProtocol,
    ];

    /// <summary>設定応答として受理しない既知の拒否文字列です。</summary>
    private static readonly string[] RejectedSettingResponses =
        ["?", "ERROR", "UNABLE", "OUT OF MEMORY", "NO DATA", "STOPPED", "BUFFER FULL"];

    /// <summary>ELM/STNバイトストリームTransportを生成するFactoryです。</summary>
    private readonly Func<OBDConnectionEndpoint, IOBDCommandTransport> makeTransport;

    /// <summary>現在開いているELM/STNバイトストリームTransportです。</summary>
    private IOBDCommandTransport? activeTransport;

    /// <summary>現在の単一inflightコマンドチャネルです。</summary>
    private SerializedELMCommandChannel? activeChannel;

    /// <summary>現在の接続資源が属するOBD終端です。</summary>
    private OBDConnectionEndpoint? activeEndpoint;

    /// <summary>現在ELMへ設定している11bit送信ヘッダーです。</summary>
    private ushort? activeTransmitHeader;

    /// <summary>Transport Factoryを固定して生成します。</summary>
    /// <remarks>責務: 主要PID読取を物理Transport生成境界へ結び付けます。</remarks>
    /// <param name="makeTransport">接続終端に対応するTransport生成処理。</param>
    public OBDLinkEXPIDTelemetryAdapter(Func<OBDConnectionEndpoint, IOBDCommandTransport> makeTransport)
    {
        this.makeTransport = makeTransport;
    }

    /// <summary>指定PIDを順番に送信し、要求単位の直接観測結果を返します。</summary>
    /// <remarks>責務: Service 01要求群を正応答、期限切れ、取消、通信失敗、未分類応答へ変換します。</remarks>
    /// <param name="requests">固定許可リストと照合するService/PID要求。</param>
    /// <param name="endpoint">ELM/STNバイトストリーム終端。</param>
    /// <returns>実際に送信を開始した要求ごとの入力順観測。</returns>
    /// <exception cref="Exception">要求送信前の終端、Service、接続、初期化、またはヘッダー設定失敗。</exception>
    public async Task<IReadOnlyList<OBDPIDRequestTransportObservation>> ReadObservations(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        if (!endpoint.Transport.SupportsELMByteStream())
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable);
        }

        var commands = CurrentDataCommands(requests);
        if (commands.Count == 0)
        {
            return [];
        }

        var channel = await Channel(endpoint);
        await SetTransmitHeader(0x7DF, channel);
        var observations = new List<OBDPIDRequestTransportObservation>();
        foreach (var command in commands)
        {
            try
            {
                var response = await channel.Execute(command);
                try
                {
                    var payload = new ELM327PIDResponseParser().Parse(response, command.Request);
                    observations.Add(new(command.Request, new OBDPIDRequestTransportOutcome.Responded(payload)));
                }
                catch (OBDPIDTelemetryException error) when (IsUnclassified(error))
                {
                    observations.Add(new(command.Request, new OBDPIDRequestTransportOutcome.UnclassifiedResponse()));
                }
            }
            catch (VehicleIdentificationException error) when (error.Reason is VehicleIdentificationError.ResponseTimedOut)
            {
                observations.Add(new(command.Request, new OBDPIDRequestTransportOutcome.TimedOut()));
                await CloseActiveSession();
                break;
            }
            catch (VehicleIdentificationException error) when (error.Reason is VehicleIdentificationError.ConnectionFailed)
            {
                observations.Add(new(command.Request, new OBDPIDRequestTransportOutcome.TransportFailure()));
                await CloseActiveSession();
                break;
            }
            catch (OperationCanceledException)
            {
                observations.Add(new(command.Request, new OBDPIDRequestTransportOutcome.Cancelled()));
                await CloseActiveSession();
                break;
            }
            catch
            {
                await CloseActiveSession();
                throw;
            }
        }

        return observations;
    }

    /// <summary>指定PIDを同じEXシリアル接続で順番に読み取ります。</summary>
    /// <remarks>責務: PID定義DB由来のService 01要求群を1回のEX接続で応答済みバイト辞書へ変換します。</remarks>
    /// <param name="requests">固定許可リストと照合するService/PID要求。</param>
    /// <param name="endpoint">EXが公開するUSBシリアル終端。</param>
    /// <returns>各要求に対応する未加工データバイト。</returns>
    /// <exception cref="Exception">非シリアル終端、Service非対応、接続、または初期化に失敗した場合のエラー。</exception>
    public async Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        if (!endpoint.Transport.SupportsELMByteStream())
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable);
        }

        var commands = CurrentDataCommands(requests);
        if (commands.Count == 0)
        {
            return new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        }

        try
        {
            var channel = await Channel(endpoint);
            await SetTransmitHeader(0x7DF, channel);
            return await Read(commands, channel);
        }
        catch (Exception error)
        {
            await CloseActiveSession();
            throw MapSessionError(error);
        }
    }

    /// <summary>型式確認済み定義を指定ECUへ1件ずつ送信します。</summary>
    /// <remarks>責務: 車種専用PID定義群をヘッダー切替済みの応答辞書へ変換します。</remarks>
    /// <param name="definitions">11bitヘッダーと型式を持つ専用PID定義。</param>
    /// <param name="endpoint">EXが公開するUSBシリアル終端。</param>
    /// <returns>正応答を確認できたService/PIDごとのデータバイト。</returns>
    /// <exception cref="Exception">定義、ヘッダー、接続、または応答処理に失敗した場合のエラー。</exception>
    public async Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> ReadVehicleSpecific(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint)
    {
        if (!endpoint.Transport.SupportsELMByteStream())
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable);
        }

        var prepared = new List<(ushort Header, ELM327VehicleSpecificPIDCommand Command)>();
        foreach (var definition in definitions)
        {
            var command = ELM327VehicleSpecificPIDCommand.Create(definition);
            if (definition.Header is not { } header || command is null)
            {
                throw new OBDPIDTelemetryException(OBDPIDTelemetryError.UnsupportedPID);
            }

            prepared.Add((header, command));
        }

        if (prepared.Count == 0)
        {
            return new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        }

        try
        {
            var channel = await Channel(endpoint);
            var result = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
            foreach (var (header, command) in prepared)
            {
                await SetTransmitHeader(header, channel);
                var response = await channel.Execute(command);
                try
                {
                    result[command.Request] = new ELM327PIDResponseParser().Parse(response, command.Request);
                }
                catch (OBDPIDTelemetryException)
                {
                    // Swift の try? と同じく、正応答でない専用PIDは結果へ含めません。
                }
            }

            return result;
        }
        catch (Exception error)
        {
            await CloseActiveSession();
            throw MapSessionError(error);
        }
    }

    /// <summary>現在の継続取得セッションを終了します。</summary>
    /// <remarks>責務: 保持中のシリアルTransportを1回だけ閉じて接続状態を消去します。</remarks>
    /// <returns>終了処理の完了。</returns>
    public Task EndSession() => CloseActiveSession();

    /// <summary>Service/PID要求を固定許可リスト内のコマンドへ変換します。</summary>
    /// <remarks>責務: 1件でも Service 01 以外が含まれる要求群を送信前に拒否します。</remarks>
    /// <param name="requests">変換する要求。</param>
    /// <returns>入力順の読取コマンド。</returns>
    /// <exception cref="OBDPIDTelemetryException">Service 01 以外を含む場合は <c>UnsupportedPID</c>。</exception>
    private static List<ELM327CurrentDataCommand> CurrentDataCommands(IReadOnlyList<OBDPIDRequest> requests) =>
        requests
            .Select(request => ELM327CurrentDataCommand.Create(request)
                ?? throw new OBDPIDTelemetryException(OBDPIDTelemetryError.UnsupportedPID))
            .ToList();

    /// <summary>解析失敗が未分類応答として扱う種類かを返します。</summary>
    /// <remarks>責務: 拒否応答と形式不正だけを未分類応答へ分類します。</remarks>
    /// <param name="error">応答解析の失敗。</param>
    /// <returns>未分類応答として扱う場合は <c>true</c>。</returns>
    private static bool IsUnclassified(OBDPIDTelemetryException error) =>
        error.Reason is OBDPIDTelemetryError.CommandRejected or OBDPIDTelemetryError.MalformedResponse;

    /// <summary>接続中の失敗をTelemetryエラーへ変換します。</summary>
    /// <remarks>責務: 期限切れを車両無応答へ、接続失敗を接続喪失へ変換し、それ以外はそのまま返します。</remarks>
    /// <param name="error">接続中に発生した失敗。</param>
    /// <returns>呼び出し元へ送出する例外。</returns>
    private static Exception MapSessionError(Exception error) => error switch
    {
        VehicleIdentificationException { Reason: VehicleIdentificationError.ResponseTimedOut } =>
            new OBDPIDTelemetryException(OBDPIDTelemetryError.NoVehicleResponse),
        VehicleIdentificationException { Reason: VehicleIdentificationError.ConnectionFailed } =>
            new OBDPIDTelemetryException(OBDPIDTelemetryError.ConnectionLost),
        _ => error,
    };

    /// <summary>必要な場合だけELM送信ヘッダーを切り替えます。</summary>
    /// <remarks>責務: 現在値と異なる1件の11bit CAN IDをELMへ設定します。</remarks>
    /// <param name="header">設定する11bit CAN ID。</param>
    /// <param name="channel">初期化済みELMチャネル。</param>
    /// <returns>設定の完了。</returns>
    /// <exception cref="OBDPIDTelemetryException">ヘッダー範囲不正またはELM拒否時のエラー。</exception>
    private async Task SetTransmitHeader(ushort header, SerializedELMCommandChannel channel)
    {
        if (activeTransmitHeader == header)
        {
            return;
        }

        var command = ELM327TransmitHeaderCommand.Create(header)
            ?? throw new OBDPIDTelemetryException(OBDPIDTelemetryError.UnsupportedPID);
        var response = await channel.Execute(command);
        if (!IsAccepted(response))
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.CommandRejected);
        }

        activeTransmitHeader = header;
    }

    /// <summary>ELM設定応答が既知の拒否文字列を含まないか判定します。</summary>
    /// <remarks>責務: 1件のELM設定応答を受理可否へ変換します。</remarks>
    /// <param name="response">プロンプトを除いたELM設定応答。</param>
    /// <returns>空でなく既知の拒否文字列を含まない場合は <c>true</c>。</returns>
    private static bool IsAccepted(string response)
    {
        var normalized = ELM327ResponseText.TrimWhitespaceAndNewlines(response).ToUpperInvariant();
        return normalized.Length > 0
            && !RejectedSettingResponses.Any(value => normalized.Contains(value, StringComparison.Ordinal));
    }

    /// <summary>指定終端の再利用可能なコマンドチャネルを返します。</summary>
    /// <remarks>責務: 1件のOBD終端を初期化済みの継続シリアルセッションへ変換します。</remarks>
    /// <param name="endpoint">PID取得に使用するシリアル終端。</param>
    /// <returns>同一終端で再利用する単一inflightコマンドチャネル。</returns>
    /// <exception cref="Exception">Transport生成、接続、またはELM初期化に失敗した場合のエラー。</exception>
    private async Task<SerializedELMCommandChannel> Channel(OBDConnectionEndpoint endpoint)
    {
        if (activeEndpoint == endpoint && activeChannel is not null)
        {
            return activeChannel;
        }

        await CloseActiveSession();
        var transport = makeTransport(endpoint);
        await transport.Open();
        var channel = new SerializedELMCommandChannel(transport);
        try
        {
            foreach (var command in InitializationCommands)
            {
                var response = await channel.Execute(command);
                if (!IsAccepted(response))
                {
                    throw new OBDPIDTelemetryException(OBDPIDTelemetryError.CommandRejected);
                }
            }
        }
        catch
        {
            await transport.Close();
            throw;
        }

        activeTransport = transport;
        activeChannel = channel;
        activeEndpoint = endpoint;
        return channel;
    }

    /// <summary>開かれたチャネルでPIDを読み取ります。</summary>
    /// <remarks>責務: 1件の初期化済みELMチャネルへPID要求を順番に適用します。</remarks>
    /// <param name="commands">実行する固定許可済みPIDコマンド。</param>
    /// <param name="channel">単一inflightを保つELMチャネル。</param>
    /// <returns>Service/PIDごとの未加工データバイト。</returns>
    /// <exception cref="Exception">初期化、読取、または応答解析に失敗した場合のエラー。</exception>
    private static async Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<ELM327CurrentDataCommand> commands,
        SerializedELMCommandChannel channel)
    {
        var result = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        foreach (var command in commands)
        {
            var response = await channel.Execute(command);
            try
            {
                result[command.Request] = new ELM327PIDResponseParser().Parse(response, command.Request);
            }
            catch (OBDPIDTelemetryException error) when (IsUnclassified(error))
            {
                // 拒否応答と形式不正は結果へ含めず次の要求へ進みます。
            }
        }

        return result;
    }

    /// <summary>保持中のTransportを閉じて再利用状態を解除します。</summary>
    /// <remarks>責務: 現在のPID接続資源を閉じた空状態へ遷移させます。</remarks>
    /// <returns>終了処理の完了。</returns>
    private async Task CloseActiveSession()
    {
        if (activeTransport is not null)
        {
            await activeTransport.Close();
        }

        activeTransport = null;
        activeChannel = null;
        activeEndpoint = null;
        activeTransmitHeader = null;
    }
}
