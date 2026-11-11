using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Devices;

/// <summary>
/// 機器の探索と到来の監視の診断ログ（<c>obd.discovery</c>）のうち、探索の実装（Data・Data.Windows）が書くものです。
/// </summary>
/// <remarks>
/// <para>
/// テスターの手元でアダプターが一覧に出ないとき、OS から何件見えて、どれをなぜ外したかが分かるようにします。
/// 候補ごとの判断は Debug、手順ごとの例外（想定内）は Debug、探索の一方が丸ごと失敗したときは Warning です。
/// </para>
/// <para>
/// ID は 3011〜3099（3001〜3010 は Application の探索のユースケースが使う）。
/// Bluetooth のアドレスは <see cref="Redact.BluetoothAddress(string?)"/>、シリアル番号は <see cref="Redact.SerialNumber"/> を通してから渡します。
/// </para>
/// </remarks>
[DiagnosticCategory(DiagnosticCategories.ObdDiscovery)]
internal static partial class OBDDiscoveryLog
{
    /// <summary><c>obd.discovery</c> のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.ObdDiscovery);

    /// <summary>機器を候補にしたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">種類（<c>usb</c> / <c>rfcomm</c> / <c>ble</c>）。</param>
    /// <param name="name">機器の名前。</param>
    /// <param name="device">機器の識別子（伏せたもの）。</param>
    /// <param name="reason">候補にした理由。</param>
    [LoggerMessage(
        EventId = 3011,
        EventName = "obd.discovery.candidate.accepted",
        Level = LogLevel.Debug,
        Message = "{Transport} の {Name}（{Device}）を候補にしました（{Reason}）")]
    public static partial void CandidateAccepted(ILogger logger, string transport, string name, string device, string reason);

    /// <summary>機器を候補から外したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">種類（<c>usb</c> / <c>rfcomm</c> / <c>ble</c>）。</param>
    /// <param name="name">機器の名前。</param>
    /// <param name="device">機器の識別子（伏せたもの）。</param>
    /// <param name="reason">外した理由。</param>
    [LoggerMessage(
        EventId = 3012,
        EventName = "obd.discovery.candidate.rejected",
        Level = LogLevel.Debug,
        Message = "{Transport} の {Name}（{Device}）を外しました（{Reason}）")]
    public static partial void CandidateRejected(ILogger logger, string transport, string name, string device, string reason);

    /// <summary>探索の一方（USB・Classic・BLE など）が失敗し、残りの結果だけを返すことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の例外。</param>
    /// <param name="source">失敗した探索（<c>system</c> / <c>ble</c> / <c>live</c> など）。</param>
    /// <param name="mode">探索の種類（<c>Usb</c> / <c>Bluetooth</c>）。</param>
    [LoggerMessage(
        EventId = 3013,
        EventName = "obd.discovery.source.failed",
        Level = LogLevel.Warning,
        Message = "{Source} の探索（{Mode}）が失敗したため、残りの結果だけを返します")]
    public static partial void SourceFailed(ILogger logger, Exception exception, string source, string mode);

    /// <summary>探索の 1 手順で例外が起き、飛ばして続けたことを書きます（想定内）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="step">手順（<c>usb-interfaces</c> / <c>usb-ports-class</c> / <c>usb-parent</c> / <c>rfcomm-device</c> など）。</param>
    [LoggerMessage(
        EventId = 3014,
        EventName = "obd.discovery.step.failed",
        Level = LogLevel.Debug,
        Message = "探索の {Step} で例外が起きたため、飛ばして続けます")]
    public static partial void StepFailed(ILogger logger, Exception exception, string step);

    /// <summary>OS から見えた機器の件数を書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">種類（<c>usb</c> / <c>rfcomm</c> / <c>ble</c>）。</param>
    /// <param name="seen">OS から見えた件数。</param>
    /// <param name="accepted">候補にした件数。</param>
    /// <param name="elapsedMs">所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3015,
        EventName = "obd.discovery.source.completed",
        Level = LogLevel.Debug,
        Message = "{Transport} の探索：見えた {Seen} 件、候補 {Accepted} 件（{ElapsedMs} ms）")]
    public static partial void SourceCompleted(ILogger logger, string transport, int seen, int accepted, long elapsedMs);

    /// <summary>Bluetooth を使える状態だったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="requiresLowEnergy">BLE の中央機能を確かめたか。</param>
    [LoggerMessage(
        EventId = 3021,
        EventName = "obd.discovery.bluetooth.available",
        Level = LogLevel.Debug,
        Message = "Bluetooth を使えます（BLE {RequiresLowEnergy}）")]
    public static partial void BluetoothAvailable(ILogger logger, bool requiresLowEnergy);

    /// <summary>Bluetooth を使えない状態だったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="requiresLowEnergy">BLE の中央機能を確かめたか。</param>
    /// <param name="reason">使えない理由（<c>BluetoothPoweredOff</c> など）。</param>
    /// <param name="detail">OS の状態の短い説明。</param>
    [LoggerMessage(
        EventId = 3022,
        EventName = "obd.discovery.bluetooth.unavailable",
        Level = LogLevel.Information,
        Message = "Bluetooth を使えません（{Reason}、{Detail}、BLE {RequiresLowEnergy}）")]
    public static partial void BluetoothUnavailable(ILogger logger, bool requiresLowEnergy, string reason, string detail);

    /// <summary>BLE のスキャンが OS のエラーで止まったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="error">OS の Bluetooth のエラー。</param>
    [LoggerMessage(
        EventId = 3023,
        EventName = "obd.discovery.ble.scan.stopped",
        Level = LogLevel.Warning,
        Message = "BLE のスキャンが {Error} で止まりました")]
    public static partial void BluetoothLEScanStopped(ILogger logger, string error);

    /// <summary>既定のアダプターの到来の監視を始めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">種類。</param>
    /// <param name="device">機器の識別子（伏せたもの）。</param>
    [LoggerMessage(
        EventId = 3031,
        EventName = "obd.discovery.arrival.started",
        Level = LogLevel.Information,
        Message = "既定のアダプター（{Transport}、{Device}）の到来の監視を始めます")]
    public static partial void ArrivalStarted(ILogger logger, string transport, string device);

    /// <summary>到来の監視を止めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(
        EventId = 3032,
        EventName = "obd.discovery.arrival.stopped",
        Level = LogLevel.Debug,
        Message = "到来の監視を止めました")]
    public static partial void ArrivalStopped(ILogger logger);

    /// <summary>既定のアダプターの到来を検出したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">種類。</param>
    /// <param name="device">機器の識別子（伏せたもの）。</param>
    [LoggerMessage(
        EventId = 3033,
        EventName = "obd.discovery.arrival.detected",
        Level = LogLevel.Information,
        Message = "既定のアダプター（{Transport}、{Device}）がつながりました")]
    public static partial void ArrivalDetected(ILogger logger, string transport, string device);

    /// <summary>到来の監視を始められず、監視なしにしたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="transport">種類。</param>
    [LoggerMessage(
        EventId = 3034,
        EventName = "obd.discovery.arrival.start-failed",
        Level = LogLevel.Warning,
        Message = "到来の監視（{Transport}）を始められないため、監視なしにします")]
    public static partial void ArrivalStartFailed(ILogger logger, Exception exception, string transport);

    /// <summary>既定のアダプターが監視できない種類のため、監視しないことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="transport">種類。</param>
    [LoggerMessage(
        EventId = 3035,
        EventName = "obd.discovery.arrival.not-monitorable",
        Level = LogLevel.Debug,
        Message = "既定のアダプター（{Transport}）は到来を監視できる種類ではありません")]
    public static partial void ArrivalNotMonitorable(ILogger logger, string transport);

    /// <summary>監視を止めるときに例外が起きたことを書きます（想定内）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="watcher">監視の種類（<c>serial</c> / <c>ble</c>）。</param>
    [LoggerMessage(
        EventId = 3036,
        EventName = "obd.discovery.arrival.stop-failed",
        Level = LogLevel.Debug,
        Message = "{Watcher} の監視を止めるときに例外が起きました（止まっているものとして扱います）")]
    public static partial void ArrivalStopFailed(ILogger logger, Exception exception, string watcher);
}
