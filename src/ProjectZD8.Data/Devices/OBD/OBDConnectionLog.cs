using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>
/// OBD アダプターへの接続・初期化・切断の診断ログ（<c>obd.connect</c>）です。
/// </summary>
/// <remarks>
/// <para>
/// 実機のアダプター（USB シリアル・RFCOMM・BLE）で接続に失敗したとき、どの段階（<c>Stage</c>）で、
/// どの Transport で、どれだけ待って失敗したかがログだけで分かるようにします。
/// </para>
/// <para>
/// ID は 3501〜3699（3501〜3599 はアダプター、3601〜3699 は Data.Windows の Transport の内側）。
/// 段階の名前（<see cref="OBDConnectionStage"/>）と Transport の名前（<see cref="OBDDiagnosticText.Transport"/>）は固定の文字列です。
/// </para>
/// </remarks>
[DiagnosticCategory(DiagnosticCategories.ObdConnect)]
internal static partial class OBDConnectionLog
{
    /// <summary><c>obd.connect</c> のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.ObdConnect);

    /// <summary>接続（識別または取得のための開始）を始めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="purpose">目的（<c>identify</c> / <c>telemetry</c>）。</param>
    /// <param name="device">機器の識別子（伏せたもの）。</param>
    /// <param name="name">機器の名前。</param>
    [LoggerMessage(
        EventId = 3501,
        EventName = "obd.connect.started",
        Level = LogLevel.Information,
        Message = "{Transport} の {Name}（{Device}）へ接続します（{Purpose}）")]
    public static partial void Started(ILogger logger, string transport, string purpose, string device, string name);

    /// <summary>接続の 1 段階を始めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="stage">段階。</param>
    /// <param name="command">段階で送るコマンド（なければ空）。</param>
    [LoggerMessage(
        EventId = 3502,
        EventName = "obd.connect.stage.started",
        Level = LogLevel.Debug,
        Message = "{Stage} を始めます（{Transport}、{Command}）")]
    public static partial void StageStarted(ILogger logger, string transport, string stage, string command);

    /// <summary>接続の 1 段階が済んだことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="stage">段階。</param>
    /// <param name="command">段階で送ったコマンド（なければ空）。</param>
    /// <param name="elapsedMs">段階の所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3503,
        EventName = "obd.connect.stage.succeeded",
        Level = LogLevel.Debug,
        Message = "{Stage} が済みました（{Transport}、{Command}、{ElapsedMs} ms）")]
    public static partial void StageSucceeded(ILogger logger, string transport, string stage, string command, long elapsedMs);

    /// <summary>接続の 1 段階で失敗したことを書きます（利用者には接続の失敗として見える）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の元の例外。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="stage">失敗した段階。</param>
    /// <param name="command">段階で送ったコマンド（なければ空）。</param>
    /// <param name="cause">原因の短い名前（<c>ResponseTimedOut</c> など）。</param>
    /// <param name="response">拒否されたときの応答（切ったもの。VIN の応答は伏せる。なければ空）。</param>
    /// <param name="elapsedMs">段階の所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3504,
        EventName = "obd.connect.failed",
        Level = LogLevel.Error,
        Message = "接続に失敗しました（{Transport}、{Stage}、{Command}、{Cause}、{ElapsedMs} ms、応答 {Response}）")]
    public static partial void Failed(
        ILogger logger,
        Exception exception,
        string transport,
        string stage,
        string command,
        string cause,
        string response,
        long elapsedMs);

    /// <summary>接続（初期化まで）が済んだことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="purpose">目的（<c>identify</c> / <c>telemetry</c>）。</param>
    /// <param name="elapsedMs">開始からの所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3505,
        EventName = "obd.connect.succeeded",
        Level = LogLevel.Information,
        Message = "接続しました（{Transport}、{Purpose}、{ElapsedMs} ms）")]
    public static partial void Succeeded(ILogger logger, string transport, string purpose, long elapsedMs);

    /// <summary>アダプターと車両を識別できたことを書きます（プロトコルの決定を含む）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="adapter">ATI の応答（アダプターの種類と版）。</param>
    /// <param name="protocol">ATDP の応答（決まったプロトコル）。</param>
    /// <param name="vin">VIN の末尾 4 桁（<see cref="Redact.Vin"/>。なければ空）。</param>
    /// <param name="hasObdIdentifier">VIN でない OBD 由来の識別子があったか。</param>
    [LoggerMessage(
        EventId = 3506,
        EventName = "obd.connect.identified",
        Level = LogLevel.Information,
        Message = "識別しました（{Transport}、アダプター {Adapter}、プロトコル {Protocol}、VIN {Vin}、OBD 由来の識別子 {HasObdIdentifier}）")]
    public static partial void Identified(
        ILogger logger,
        string transport,
        string adapter,
        string protocol,
        string vin,
        bool hasObdIdentifier);

    /// <summary>接続を閉じたことを理由つきで書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="reason">理由（<see cref="OBDDisconnectReason"/>）。</param>
    [LoggerMessage(
        EventId = 3507,
        EventName = "obd.connect.disconnected",
        Level = LogLevel.Information,
        Message = "接続を閉じました（{Transport}、{Reason}）")]
    public static partial void Disconnected(ILogger logger, string transport, string reason);

    /// <summary>応答の期限切れ・通信の失敗のあとで、接続を開き直したことを書きます（自動の回復）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="previousReason">前の接続を閉じた理由。</param>
    [LoggerMessage(
        EventId = 3508,
        EventName = "obd.connect.reopened",
        Level = LogLevel.Warning,
        Message = "前の接続が {PreviousReason} で閉じたため、開き直します（{Transport}）")]
    public static partial void Reopened(ILogger logger, string transport, string previousReason);

    /// <summary>取得中の要求が期限切れ・通信の失敗で終わり、接続を閉じることを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の例外。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="command">送っていたコマンド。</param>
    /// <param name="cause">原因の短い名前。</param>
    [LoggerMessage(
        EventId = 3509,
        EventName = "obd.connect.request.failed",
        Level = LogLevel.Warning,
        Message = "要求 {Command} が {Cause} で終わったため、接続を閉じます（{Transport}）")]
    public static partial void RequestFailed(ILogger logger, Exception exception, string transport, string command, string cause);

    /// <summary>Transport の内側の 1 手順を書きます（RFCOMM の SDP 照会、BLE の GATT の探索など）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="step">手順。</param>
    /// <param name="detail">結果の短い説明。</param>
    [LoggerMessage(
        EventId = 3601,
        EventName = "obd.connect.transport.step",
        Level = LogLevel.Debug,
        Message = "{Transport} の {Step}：{Detail}")]
    public static partial void TransportStep(ILogger logger, string transport, string step, string detail);

    /// <summary>Transport の内側の手順で例外が起き、接続の失敗に変えたことを書きます（元の例外を残すため）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">元の例外。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="step">手順。</param>
    [LoggerMessage(
        EventId = 3602,
        EventName = "obd.connect.transport.failed",
        Level = LogLevel.Debug,
        Message = "{Transport} の {Step} で例外が起きました")]
    public static partial void TransportFailed(ILogger logger, Exception exception, string transport, string step);

    /// <summary>Transport が期限切れで待つのをやめたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="operation">待っていた処理（<c>open</c> / <c>write</c> / <c>read</c> / <c>sdp-query</c>）。</param>
    /// <param name="timeoutMs">期限（ミリ秒）。</param>
    /// <param name="receivedBytes">期限までに受け取っていたバイト数（読取のとき）。</param>
    [LoggerMessage(
        EventId = 3603,
        EventName = "obd.connect.transport.timeout",
        Level = LogLevel.Debug,
        Message = "{Transport} の {Operation} が {TimeoutMs} ms で期限切れになりました（受信 {ReceivedBytes} バイト）")]
    public static partial void TransportTimeout(ILogger logger, string transport, string operation, long timeoutMs, int receivedBytes);

    /// <summary>機器の側から接続が切れたことを書きます（アプリが閉じたのではない）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">Transport の種類。</param>
    /// <param name="reason">切れたと分かった理由。</param>
    [LoggerMessage(
        EventId = 3604,
        EventName = "obd.connect.transport.lost",
        Level = LogLevel.Information,
        Message = "{Transport} の接続が切れました（{Reason}）")]
    public static partial void TransportLost(ILogger logger, string transport, string reason);

    /// <summary>BLE の機器が公開している Service と Characteristic の要約を書きます（未知の UART 構成を調べるため）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="services">Service UUID と Characteristic（UUID と能力）の要約。</param>
    /// <param name="resolved">既知の UART 構成に当たったか。</param>
    [LoggerMessage(
        EventId = 3605,
        EventName = "obd.connect.ble.gatt",
        Level = LogLevel.Debug,
        Message = "GATT の構成：{Services}（既知の UART：{Resolved}）")]
    public static partial void BluetoothLEGatt(ILogger logger, string services, bool resolved);
}

/// <summary>
/// ELM327 のコマンド 1 往復の診断ログ（<c>obd.wire</c>）です。
/// </summary>
/// <remarks>
/// 実機の不具合の主な手がかりです。1 往復で 1 行（Trace）。VIN を返す要求（Mode 09 PID 02）の応答は伏せます。
/// ID は 4001〜4099。
/// </remarks>
[DiagnosticCategory(DiagnosticCategories.ObdWire)]
internal static partial class OBDWireLog
{
    /// <summary><c>obd.wire</c> のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.ObdWire);

    /// <summary>コマンド 1 往復を書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="command">送ったコマンド（<c>ATZ</c>、<c>010C</c> など）。</param>
    /// <param name="response">応答（制御文字を表記に変え、長すぎるものは切る。VIN の応答は伏せる）。</param>
    /// <param name="responseBytes">応答のバイト数（プロンプトを含む）。</param>
    /// <param name="elapsedMs">送信から応答のプロンプトまでの時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 4001,
        EventName = "obd.wire.exchanged",
        Level = LogLevel.Trace,
        Message = "{Command} → {Response}（{ResponseBytes} バイト、{ElapsedMs} ms）")]
    public static partial void Exchanged(ILogger logger, string command, string response, int responseBytes, long elapsedMs);

    /// <summary>コマンド 1 往復が失敗したことを書きます（期限切れ・切断・不正な文字列）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の例外。</param>
    /// <param name="command">送ったコマンド。</param>
    /// <param name="cause">原因の短い名前。</param>
    /// <param name="timedOut">応答の期限切れか。</param>
    /// <param name="elapsedMs">送信から失敗までの時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 4002,
        EventName = "obd.wire.failed",
        Level = LogLevel.Debug,
        Message = "{Command} が失敗しました（{Cause}、期限切れ {TimedOut}、{ElapsedMs} ms）")]
    public static partial void Failed(ILogger logger, Exception exception, string command, string cause, bool timedOut, long elapsedMs);

    /// <summary>応答境界を失ったチャネルへの送信を断ったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="command">送ろうとしたコマンド。</param>
    [LoggerMessage(
        EventId = 4003,
        EventName = "obd.wire.boundary-lost",
        Level = LogLevel.Debug,
        Message = "前の失敗で応答の境界を失ったため、{Command} を送りません")]
    public static partial void BoundaryLost(ILogger logger, string command);

    /// <summary>応答を値として読めず、結果に含めなかったことを書きます（想定内）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="command">送ったコマンド。</param>
    /// <param name="cause">読めなかった理由。</param>
    [LoggerMessage(
        EventId = 4004,
        EventName = "obd.wire.response.unparsed",
        Level = LogLevel.Debug,
        Message = "{Command} の応答を読めませんでした（{Cause}）")]
    public static partial void ResponseUnparsed(ILogger logger, string command, string cause);
}

/// <summary>接続の段階の名前です（<c>obd.connect</c> の <c>Stage</c>。変えない固定の文字列）。</summary>
internal static class OBDConnectionStage
{
    /// <summary>Transport を作る。</summary>
    public const string TransportCreation = "transport-create";

    /// <summary>物理の終端を開く。</summary>
    public const string Open = "open";

    /// <summary>ATZ でアダプターを初期状態へ戻す。</summary>
    public const string Reset = "reset";

    /// <summary>初期化の AT コマンド（ATE0・ATL0・ATS1・ATH0・ATSP0）。</summary>
    public const string Configure = "configure";

    /// <summary>ATI でアダプターを識別する。</summary>
    public const string AdapterIdentity = "adapter-identity";

    /// <summary>Mode 09 PID 02 で VIN を要求する。</summary>
    public const string VinRequest = "vin-request";

    /// <summary>VIN の応答を読む。</summary>
    public const string VinParse = "vin-parse";

    /// <summary>ATDP で決まったプロトコルを読む。</summary>
    public const string Protocol = "protocol";

    /// <summary>ATSH で送信ヘッダーを切り替える。</summary>
    public const string TransmitHeader = "transmit-header";

    /// <summary>識別の段階の名前を返します。</summary>
    /// <remarks>責務: 識別エラーの段階を診断ログの固定の段階名へ写します。</remarks>
    /// <param name="stage">識別の段階。</param>
    /// <returns>段階名。</returns>
    public static string From(VehicleIdentificationErrorStage stage) => stage switch
    {
        VehicleIdentificationErrorStage.EndpointValidation => "endpoint-validation",
        VehicleIdentificationErrorStage.TransportCreation => TransportCreation,
        VehicleIdentificationErrorStage.TransportOpen => Open,
        VehicleIdentificationErrorStage.AdapterReset => Reset,
        VehicleIdentificationErrorStage.AdapterConfiguration => Configure,
        VehicleIdentificationErrorStage.AdapterIdentity => AdapterIdentity,
        VehicleIdentificationErrorStage.VehicleIdentificationRequest => VinRequest,
        VehicleIdentificationErrorStage.VehicleIdentificationParsing => VinParse,
        VehicleIdentificationErrorStage.ProtocolDescription => Protocol,
        _ => "unknown",
    };
}

/// <summary>接続を閉じた理由です（<c>obd.connect.disconnected</c> の <c>Reason</c>。変えない固定の文字列）。</summary>
internal static class OBDDisconnectReason
{
    /// <summary>識別が済んだ。</summary>
    public const string IdentificationCompleted = "identify-completed";

    /// <summary>識別が失敗した。</summary>
    public const string IdentificationFailed = "identify-failed";

    /// <summary>初期化の AT コマンドが失敗した。</summary>
    public const string InitializationFailed = "initialize-failed";

    /// <summary>取得の終了（利用者の切断・取得の停止）。</summary>
    public const string SessionEnded = "session-ended";

    /// <summary>別の終端へ切り替える。</summary>
    public const string EndpointChanged = "endpoint-changed";

    /// <summary>応答の期限切れ。</summary>
    public const string ResponseTimeout = "response-timeout";

    /// <summary>通信の失敗（切断）。</summary>
    public const string ConnectionFailed = "connection-failed";

    /// <summary>取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>そのほかの失敗。</summary>
    public const string Error = "error";
}

/// <summary>診断ログに入れる OBD の値を、短く・伏せた文字列にします。</summary>
internal static class OBDDiagnosticText
{
    /// <summary>応答を書く最大の文字数です（これより長いものは切る）。</summary>
    public const int MaxResponseLength = 160;

    /// <summary>
    /// Transport の種類の名前です。
    /// </summary>
    /// <remarks>責務: 物理方式を診断ログの固定の短い名前へ写します。</remarks>
    /// <param name="transport">物理方式。</param>
    /// <returns><c>serial</c> / <c>rfcomm</c> / <c>ble</c>。</returns>
    public static string Transport(OBDConnectionEndpointTransport transport) => transport switch
    {
        OBDConnectionEndpointTransport.Serial => TransportSerial,
        OBDConnectionEndpointTransport.BluetoothClassic => TransportRFCOMM,
        OBDConnectionEndpointTransport.BluetoothLowEnergy => TransportBLE,
        _ => "unknown",
    };

    /// <summary>USB シリアルの名前です。</summary>
    public const string TransportSerial = "serial";

    /// <summary>Bluetooth Classic（RFCOMM）の名前です。</summary>
    public const string TransportRFCOMM = "rfcomm";

    /// <summary>Bluetooth Low Energy の名前です。</summary>
    public const string TransportBLE = "ble";

    /// <summary>
    /// 終端の識別子を、個人を特定しない形にします。
    /// </summary>
    /// <remarks>責務: COM ポート名はそのまま、Bluetooth のアドレスは末尾 2 バイトだけにします。</remarks>
    /// <param name="endpoint">終端。</param>
    /// <returns>伏せた識別子。</returns>
    public static string Device(OBDConnectionEndpoint endpoint) =>
        endpoint.Transport == OBDConnectionEndpointTransport.Serial
            ? endpoint.SystemIdentifier
            : Redact.BluetoothAddress(endpoint.SystemIdentifier);

    /// <summary>
    /// 開始からの経過ミリ秒です。
    /// </summary>
    /// <remarks>責務: <see cref="Stopwatch.GetTimestamp"/> の値から、割り当てなしで経過時間を求めます。</remarks>
    /// <param name="startTimestamp">開始の時刻印。</param>
    /// <returns>経過ミリ秒。</returns>
    public static long ElapsedMilliseconds(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    /// <summary>
    /// コマンドの送信文字列です（<c>ATZ</c>、<c>010C</c> など）。
    /// </summary>
    /// <remarks>責務: 固定コマンドは本文、それ以外は要求識別子（送信文字列と同じ）を返します。</remarks>
    /// <param name="command">コマンド。</param>
    /// <returns>送信文字列。</returns>
    public static string Command(IELM327CommandEncoding command) =>
        command is ELM327Command fixedCommand ? fixedCommand.RawValue : command.RequestID;

    /// <summary>
    /// VIN を返す要求（Mode 09 PID 02）かを返します。
    /// </summary>
    /// <remarks>責務: 応答を伏せる必要があるコマンドを見分けます。</remarks>
    /// <param name="command">コマンド。</param>
    /// <returns>VIN を返す要求なら true。</returns>
    public static bool ReturnsVin(IELM327CommandEncoding command) =>
        ReferenceEquals(command, ELM327Command.VehicleIdentificationNumber)
        || string.Equals(Command(command), ELM327Command.VehicleIdentificationNumber.RawValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 応答を、1 行に収まる短い表記にします。VIN の応答は伏せます。
    /// </summary>
    /// <remarks>責務: 復帰・改行を <c>\r</c>・<c>\n</c> の表記に、ほかの制御文字を <c>.</c> に変え、長すぎるものを切ります。</remarks>
    /// <param name="command">応答を返したコマンド。</param>
    /// <param name="response">応答（null なら空）。</param>
    /// <returns>表記。</returns>
    public static string Response(IELM327CommandEncoding command, string? response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return string.Empty;
        }

        if (ReturnsVin(command))
        {
            return $"（VIN の応答 {response.Length} 文字を伏せました）";
        }

        var length = Math.Min(response.Length, MaxResponseLength);
        var builder = new StringBuilder(length + 16);
        for (var index = 0; index < length; index++)
        {
            var character = response[index];
            switch (character)
            {
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                default:
                    builder.Append(char.IsControl(character) ? '.' : character);
                    break;
            }
        }

        if (response.Length > length)
        {
            builder.Append("…(+").Append(response.Length - length).Append(')');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 例外の原因の短い名前です。
    /// </summary>
    /// <remarks>責務: 型付きの識別エラー・取得エラーは理由の名前、それ以外は例外の型名を返します。</remarks>
    /// <param name="error">例外。</param>
    /// <returns>原因の名前。</returns>
    public static string Cause(Exception error) => error switch
    {
        VehicleIdentificationException { Reason: VehicleIdentificationError.StageFailed staged } =>
            $"{staged.Stage}/{staged.Cause}",
        VehicleIdentificationException identification => identification.Reason.GetType().Name,
        OBDPIDTelemetryException telemetry => telemetry.Reason.ToString(),
        _ => error.GetType().Name,
    };

    /// <summary>
    /// 応答の期限切れによる失敗かを返します。
    /// </summary>
    /// <remarks>責務: 識別エラーの期限切れ（段階つきを含む）を見分けます。</remarks>
    /// <param name="error">例外。</param>
    /// <returns>期限切れなら true。</returns>
    public static bool IsTimeout(Exception error) => error switch
    {
        VehicleIdentificationException { Reason: VehicleIdentificationError.ResponseTimedOut } => true,
        VehicleIdentificationException { Reason: VehicleIdentificationError.StageFailed { Cause: VehicleIdentificationErrorCause.ResponseTimedOut } } => true,
        TimeoutException => true,
        _ => false,
    };
}
