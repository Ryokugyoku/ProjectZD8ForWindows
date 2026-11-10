using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>選択済みELM/STN互換バイトストリームへ型付きコマンドだけを送り車両識別子を取得します。</summary>
public sealed class SerialELMVehicleIdentificationAdapter : IVehicleIdentificationPort
{
    /// <summary>初期化設定として順に送るコマンドです。</summary>
    private static readonly ELM327Command[] ConfigurationCommands =
    [
        ELM327Command.EchoOff, ELM327Command.LinefeedsOff, ELM327Command.SpacesOn,
        ELM327Command.HeadersOff, ELM327Command.AutomaticProtocol,
    ];

    /// <summary>成功として扱わない既知の拒否文字列です。</summary>
    private static readonly string[] Rejected = ["?", "NO DATA", "UNABLE TO CONNECT", "BUS ERROR", "CAN ERROR", "STOPPED"];

    /// <summary>ELM/STNバイトストリームTransportを生成するFactoryです。</summary>
    private readonly Func<OBDConnectionEndpoint, IOBDCommandTransport> makeTransport;

    /// <summary>観測日時を提供する注入済みクロックです。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>Transport Factoryとクロックを注入して生成します。</summary>
    /// <remarks>責務: ELM/STN車両識別処理を物理Transport生成と時刻取得境界へ固定します。</remarks>
    /// <param name="makeTransport">接続終端に対応するTransport生成処理。</param>
    /// <param name="now">観測完了日時の供給元。<c>null</c> の場合は現在時刻。</param>
    public SerialELMVehicleIdentificationAdapter(
        Func<OBDConnectionEndpoint, IOBDCommandTransport> makeTransport,
        Func<DateTimeOffset>? now = null)
    {
        this.makeTransport = makeTransport;
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>選択済みELM/STNアダプターを初期化してService 09 PID 02の車両識別子を取得します。</summary>
    /// <remarks>責務: 1件の選択済みELM/STN接続から型付き読取専用識別観測を生成します。</remarks>
    /// <param name="endpoint">選択済みアダプターが公開する連続バイトストリーム終端。</param>
    /// <returns>VIN、アダプター、プロトコル、加工前応答を含む観測。</returns>
    /// <exception cref="VehicleIdentificationException">非シリアル終端、接続、コマンド、応答解析に失敗した場合の識別エラー。</exception>
    public async Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint)
    {
        if (!endpoint.Transport.SupportsELMByteStream())
        {
            throw StageFailed(VehicleIdentificationErrorStage.EndpointValidation, VehicleIdentificationErrorCause.TransportUnsupported);
        }

        IOBDCommandTransport transport;
        try
        {
            transport = makeTransport(endpoint);
        }
        catch (Exception error)
        {
            throw Staged(error, VehicleIdentificationErrorStage.TransportCreation);
        }

        try
        {
            try
            {
                await transport.Open();
            }
            catch (Exception error)
            {
                throw Staged(error, VehicleIdentificationErrorStage.TransportOpen);
            }

            var snapshot = await Identify(new SerializedELMCommandChannel(transport), endpoint);
            await transport.Close();
            return snapshot;
        }
        catch
        {
            await transport.Close();
            throw;
        }
    }

    /// <summary>開かれた直列チャネルで初期化とVIN要求を実行します。</summary>
    /// <remarks>責務: 1件の開かれたシリアルチャネルへ承認済み識別コマンド列を適用します。</remarks>
    /// <param name="channel">単一inflightを保証するELMコマンドチャネル。</param>
    /// <param name="endpoint">表示用アダプター情報を持つ接続終端。</param>
    /// <returns>VINと全原文応答を保持する観測。</returns>
    /// <exception cref="VehicleIdentificationException">いずれかのコマンドまたはVIN解析に失敗した場合のエラー。</exception>
    private async Task<VehicleIdentificationSnapshot> Identify(SerializedELMCommandChannel channel, OBDConnectionEndpoint endpoint)
    {
        var raw = new List<VehicleIdentificationSnapshot.RawResponse>();
        var resetResponse = await Execute(ELM327Command.Reset, VehicleIdentificationErrorStage.AdapterReset, channel);
        raw.Add(new(ELM327Command.Reset.RequestID, resetResponse));
        foreach (var command in ConfigurationCommands)
        {
            var response = await Execute(command, VehicleIdentificationErrorStage.AdapterConfiguration, channel);
            raw.Add(new(command.RequestID, response));
        }

        var adapterResponse = await Execute(ELM327Command.AdapterIdentity, VehicleIdentificationErrorStage.AdapterIdentity, channel);
        raw.Add(new(ELM327Command.AdapterIdentity.RequestID, adapterResponse));

        var vinResponse = await Execute(
            ELM327Command.VehicleIdentificationNumber,
            VehicleIdentificationErrorStage.VehicleIdentificationRequest,
            channel);
        raw.Add(new(ELM327Command.VehicleIdentificationNumber.RequestID, vinResponse));
        ELM327VehicleIdentifierParser.Result identifier;
        try
        {
            identifier = new ELM327VehicleIdentifierParser().Parse(vinResponse);
        }
        catch (Exception error)
        {
            throw Staged(error, VehicleIdentificationErrorStage.VehicleIdentificationParsing);
        }

        var protocolResponse = await Execute(
            ELM327Command.ProtocolDescription,
            VehicleIdentificationErrorStage.ProtocolDescription,
            channel);
        raw.Add(new(ELM327Command.ProtocolDescription.RequestID, protocolResponse));

        return new VehicleIdentificationSnapshot(
            vin: identifier.Vin,
            fields:
            [
                new("adapter", "OBD Adapter", Cleaned(adapterResponse), endpoint.DisplayName),
                new("obdProtocol", "OBD Protocol", Cleaned(protocolResponse), endpoint.DisplayName),
            ],
            observedAt: now(),
            obdIdentifier: identifier.ObdIdentifier,
            rawResponses: raw);
    }

    /// <summary>1件の型付きコマンドを実行して失敗へ識別段階を付与します。</summary>
    /// <remarks>責務: 単一コマンドの送受信と既知拒否応答の検証を指定段階へ結び付けます。</remarks>
    /// <param name="command">実行する許可済みコマンド。</param>
    /// <param name="stage">コマンド失敗時に保持する識別段階。</param>
    /// <param name="channel">単一inflightを保証するELMコマンドチャネル。</param>
    /// <returns>検証済みの加工前応答。</returns>
    /// <exception cref="VehicleIdentificationException">段階と原因を保持した <c>StageFailed</c>。</exception>
    private static async Task<string> Execute(
        ELM327Command command,
        VehicleIdentificationErrorStage stage,
        SerializedELMCommandChannel channel)
    {
        try
        {
            var response = await channel.Execute(command);
            Validate(response);
            return response;
        }
        catch (Exception error)
        {
            throw Staged(error, stage);
        }
    }

    /// <summary>ELM/STNの明示的な失敗応答を成功として扱わないよう検証します。</summary>
    /// <remarks>責務: 1件のコマンド応答を既知の拒否語と空応答に対して検証します。</remarks>
    /// <param name="response">プロンプトを除いた加工前応答。</param>
    /// <exception cref="VehicleIdentificationException">空、疑問符、NO DATA、BUS/CAN ERRORの場合は <c>CommandRejected</c>。</exception>
    private static void Validate(string response)
    {
        var normalized = Cleaned(response).ToUpperInvariant();
        if (normalized.Length == 0 || Rejected.Any(value => normalized.Contains(value, StringComparison.Ordinal)))
        {
            throw new VehicleIdentificationException(new VehicleIdentificationError.CommandRejected());
        }
    }

    /// <summary>下位境界の失敗を識別段階と単一原因を持つエラーへ変換します。</summary>
    /// <remarks>責務: 1件の下位エラーを指定された車両識別段階へ関連付けます。</remarks>
    /// <param name="error">Transport、チャネル、検証、解析のいずれかが返したエラー。</param>
    /// <param name="stage">失敗時に実行していた単一段階。</param>
    /// <returns>段階と表示可能な原因を保持する型付き識別エラー。</returns>
    private static VehicleIdentificationException Staged(Exception error, VehicleIdentificationErrorStage stage)
    {
        if (error is not VehicleIdentificationException identificationError)
        {
            return StageFailed(stage, VehicleIdentificationErrorCause.Unavailable);
        }

        return identificationError.Reason switch
        {
            VehicleIdentificationError.StageFailed existing => StageFailed(existing.Stage, existing.Cause),
            VehicleIdentificationError.Unavailable
                or VehicleIdentificationError.PidCatalogUnavailable
                or VehicleIdentificationError.VinUnavailable => StageFailed(stage, VehicleIdentificationErrorCause.Unavailable),
            VehicleIdentificationError.TransportUnsupported => StageFailed(stage, VehicleIdentificationErrorCause.TransportUnsupported),
            VehicleIdentificationError.ConnectionFailed => StageFailed(stage, VehicleIdentificationErrorCause.ConnectionFailed),
            VehicleIdentificationError.ResponseTimedOut => StageFailed(stage, VehicleIdentificationErrorCause.ResponseTimedOut),
            VehicleIdentificationError.CommandRejected => StageFailed(stage, VehicleIdentificationErrorCause.CommandRejected),
            VehicleIdentificationError.MalformedResponse => StageFailed(stage, VehicleIdentificationErrorCause.MalformedResponse),
            _ => StageFailed(stage, VehicleIdentificationErrorCause.Unavailable),
        };
    }

    /// <summary>段階と原因から識別エラーを生成します。</summary>
    /// <remarks>責務: <c>StageFailed</c> の例外を1か所で組み立てます。</remarks>
    /// <param name="stage">失敗した段階。</param>
    /// <param name="cause">失敗の原因。</param>
    /// <returns>送出できる識別エラー。</returns>
    private static VehicleIdentificationException StageFailed(
        VehicleIdentificationErrorStage stage,
        VehicleIdentificationErrorCause cause) =>
        new(new VehicleIdentificationError.StageFailed(stage, cause));

    /// <summary>表示用応答から制御用空白だけを整理します。</summary>
    /// <remarks>責務: 1件のアダプター応答を単一行の表示値へ縮約します。</remarks>
    /// <param name="response">加工前応答。</param>
    /// <returns>空白区切りの表示用文字列。</returns>
    private static string Cleaned(string response) =>
        string.Join(' ', ELM327ResponseText.WhitespaceSeparatedTokens(response));
}
