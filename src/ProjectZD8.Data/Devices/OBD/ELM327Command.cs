using System.Globalization;
using System.Text;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>ELM/STNへ送信できる読取専用コマンドの符号化契約です。</summary>
public interface IELM327CommandEncoding
{
    /// <summary>未加工観測で使用する安定要求識別子です。</summary>
    string RequestID { get; }

    /// <summary>復帰文字を付けた送信用ASCIIデータです。</summary>
    byte[] EncodedData { get; }
}

/// <summary>車両識別フローで許可する読取専用ELM/STNコマンドです。</summary>
/// <remarks>
/// Swift では <c>String</c> の raw value を持つ <c>enum</c> ですが、C# の列挙はインターフェースを実装できないため、
/// 固定インスタンスだけを持つ封印クラスにしています。<see cref="RequestID"/> は Swift のケース名（<c>String(describing:)</c>）と同じです。
/// </remarks>
public sealed class ELM327Command : IELM327CommandEncoding
{
    /// <summary>アダプターを既知の初期状態へ戻します。</summary>
    public static readonly ELM327Command Reset = new("reset", "ATZ");

    /// <summary>コマンドエコーを無効化します。</summary>
    public static readonly ELM327Command EchoOff = new("echoOff", "ATE0");

    /// <summary>応答の改行追加を無効化します。</summary>
    public static readonly ELM327Command LinefeedsOff = new("linefeedsOff", "ATL0");

    /// <summary>応答バイト間の空白を有効化します。</summary>
    public static readonly ELM327Command SpacesOn = new("spacesOn", "ATS1");

    /// <summary>応答ヘッダー表示を無効化します。</summary>
    public static readonly ELM327Command HeadersOff = new("headersOff", "ATH0");

    /// <summary>法定OBDプロトコルの自動選択を有効化します。</summary>
    public static readonly ELM327Command AutomaticProtocol = new("automaticProtocol", "ATSP0");

    /// <summary>アダプター識別文字列を要求します。</summary>
    public static readonly ELM327Command AdapterIdentity = new("adapterIdentity", "ATI");

    /// <summary>現在選択されたOBDプロトコル記述を要求します。</summary>
    public static readonly ELM327Command ProtocolDescription = new("protocolDescription", "ATDP");

    /// <summary>OBD Service 09 PID 02のVINを要求します。</summary>
    public static readonly ELM327Command VehicleIdentificationNumber = new("vehicleIdentificationNumber", "0902");

    /// <summary>宣言順の全コマンドです。</summary>
    public static IReadOnlyList<ELM327Command> AllCases { get; } =
    [
        Reset, EchoOff, LinefeedsOff, SpacesOn, HeadersOff, AutomaticProtocol,
        AdapterIdentity, ProtocolDescription, VehicleIdentificationNumber,
    ];

    /// <summary>復帰文字を除いたASCIIコマンド本文です。</summary>
    public string RawValue { get; }

    /// <summary>未加工観測で使用する安定要求識別子です。</summary>
    public string RequestID { get; }

    /// <summary>復帰文字を付けた送信用ASCIIデータです。</summary>
    public byte[] EncodedData => Encoding.UTF8.GetBytes(RawValue + "\r");

    /// <summary>ケース名とコマンド本文を固定して生成します。</summary>
    /// <remarks>責務: 1件の許可済みコマンドを識別子と送信本文の組へ固定します。</remarks>
    /// <param name="requestID">Swift のケース名と同じ安定要求識別子。</param>
    /// <param name="rawValue">復帰文字を除いたコマンド本文。</param>
    private ELM327Command(string requestID, string rawValue)
    {
        RequestID = requestID;
        RawValue = rawValue;
    }

    /// <summary>ケース名を返します。</summary>
    /// <remarks>責務: デバッグ表示を Swift の <c>String(describing:)</c> と揃えます。</remarks>
    /// <returns>安定要求識別子。</returns>
    public override string ToString() => RequestID;
}

/// <summary>PID定義DBで許可されたService 01読取コマンドです。</summary>
public sealed record ELM327CurrentDataCommand : IELM327CommandEncoding
{
    /// <summary>Service/PIDの複合識別子です。</summary>
    public OBDPIDRequest Request { get; }

    /// <summary>検証済みService/PIDを保持します。</summary>
    /// <remarks>責務: 生成済み要求を不変値へ固定します。</remarks>
    /// <param name="request">Service 01 の要求。</param>
    private ELM327CurrentDataCommand(OBDPIDRequest request)
    {
        Request = request;
    }

    /// <summary>指定Service/PIDが許可済みコマンドに対応する場合だけ生成します。</summary>
    /// <remarks>責務: 1件のService/PIDを固定許可リスト内の読取コマンドへ変換します。</remarks>
    /// <param name="request">変換するService/PID。</param>
    /// <returns>Service 01 の場合はコマンド、それ以外は <c>null</c>。</returns>
    public static ELM327CurrentDataCommand? Create(OBDPIDRequest request) =>
        request.Service == 0x01 ? new ELM327CurrentDataCommand(request) : null;

    /// <summary>未加工観測で使用する安定要求識別子です。</summary>
    public string RequestID => string.Create(CultureInfo.InvariantCulture, $"{Request.Service:X2}{Request.Pid:X2}");

    /// <summary>復帰文字を付けた送信用ASCIIデータです。</summary>
    public byte[] EncodedData => Encoding.UTF8.GetBytes(RequestID + "\r");
}

/// <summary>定義済みの車種専用PIDへ送る読取り専用ELMコマンドです。</summary>
public sealed record ELM327VehicleSpecificPIDCommand : IELM327CommandEncoding
{
    /// <summary>Service/PIDの複合識別子です。</summary>
    public OBDPIDRequest Request { get; }

    /// <summary>検証済みService/PIDを保持します。</summary>
    /// <remarks>責務: 生成済み要求を不変値へ固定します。</remarks>
    /// <param name="request">車種専用の要求。</param>
    private ELM327VehicleSpecificPIDCommand(OBDPIDRequest request)
    {
        Request = request;
    }

    /// <summary>定義済みの車種専用Service/PIDを固定します。</summary>
    /// <remarks>責務: 1件の専用PID定義を読取りコマンドへ変換します。</remarks>
    /// <param name="definition">型式と11bitヘッダーを持つ専用PID定義。</param>
    /// <returns>車種専用かつ Service 21 の場合はコマンド、それ以外は <c>null</c>。</returns>
    public static ELM327VehicleSpecificPIDCommand? Create(OBDPIDDefinition definition) =>
        definition.IsVehicleSpecific && definition.Service == 0x21
            ? new ELM327VehicleSpecificPIDCommand(new OBDPIDRequest(definition.Service, definition.Pid))
            : null;

    /// <summary>未加工観測で使用する安定要求識別子です。</summary>
    public string RequestID => string.Create(CultureInfo.InvariantCulture, $"{Request.Service:X2}{Request.Pid:X2}");

    /// <summary>復帰文字を付けた送信用ASCIIデータです。</summary>
    public byte[] EncodedData => Encoding.UTF8.GetBytes(RequestID + "\r");
}

/// <summary>ELMの11bit送信ヘッダーを検証済みCAN IDへ切り替えます。</summary>
public sealed record ELM327TransmitHeaderCommand : IELM327CommandEncoding
{
    /// <summary>設定する11bit CAN IDです。</summary>
    public ushort Header { get; }

    /// <summary>検証済みヘッダーを保持します。</summary>
    /// <remarks>責務: 生成済みヘッダーを不変値へ固定します。</remarks>
    /// <param name="header">11bit範囲のCAN ID。</param>
    private ELM327TransmitHeaderCommand(ushort header)
    {
        Header = header;
    }

    /// <summary>11bit範囲の送信ヘッダーだけを受理します。</summary>
    /// <remarks>責務: 1件の11bit CAN IDをELM送信ヘッダー設定へ変換します。</remarks>
    /// <param name="header">設定するCAN ID。</param>
    /// <returns>0x7FF 以下の場合はコマンド、それ以外は <c>null</c>。</returns>
    public static ELM327TransmitHeaderCommand? Create(ushort header) =>
        header <= 0x7FF ? new ELM327TransmitHeaderCommand(header) : null;

    /// <summary>未加工観測で使用する安定要求識別子です。</summary>
    public string RequestID => string.Create(CultureInfo.InvariantCulture, $"ATSH{Header:X3}");

    /// <summary>復帰文字を付けた送信用ASCIIデータです。</summary>
    public byte[] EncodedData => Encoding.UTF8.GetBytes(RequestID + "\r");
}
