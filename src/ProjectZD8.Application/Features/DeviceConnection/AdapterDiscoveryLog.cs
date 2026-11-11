using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Application.DeviceConnection;

/// <summary>
/// 機器の探索の診断ログ（<c>obd.discovery</c>）のうち、探索のユースケースが書く開始と結果です。
/// </summary>
/// <remarks>
/// 候補ごとの判断と OS の手順は探索の実装（Data・Data.Windows）が 3011〜 で書きます。ここは 3001〜3010。
/// </remarks>
[DiagnosticCategory(DiagnosticCategories.ObdDiscovery)]
internal static partial class AdapterDiscoveryLog
{
    /// <summary><c>obd.discovery</c> のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.ObdDiscovery);

    /// <summary>探索を始めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="mode">探索の種類（<c>Usb</c> / <c>Bluetooth</c>）。</param>
    [LoggerMessage(
        EventId = 3001,
        EventName = "obd.discovery.started",
        Level = LogLevel.Information,
        Message = "{Mode} の機器を探します")]
    public static partial void Started(ILogger logger, string mode);

    /// <summary>探索の結果を種類ごとの件数で書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="mode">探索の種類。</param>
    /// <param name="count">候補の件数。</param>
    /// <param name="usb">USB シリアルの件数。</param>
    /// <param name="rfcomm">Bluetooth Classic の件数。</param>
    /// <param name="ble">Bluetooth Low Energy の件数。</param>
    /// <param name="demo">デモの件数。</param>
    /// <param name="elapsedMs">所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3002,
        EventName = "obd.discovery.completed",
        Level = LogLevel.Information,
        Message = "{Mode} の探索が済みました（候補 {Count} 件：USB {Usb}、RFCOMM {Rfcomm}、BLE {Ble}、デモ {Demo}、{ElapsedMs} ms）")]
    public static partial void Completed(ILogger logger, string mode, int count, int usb, int rfcomm, int ble, int demo, long elapsedMs);

    /// <summary>Bluetooth を使えないなどの理由で探索できなかったことを書きます（画面には理由が出る）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">探索のエラー。</param>
    /// <param name="mode">探索の種類。</param>
    /// <param name="reason">理由（<c>BluetoothPoweredOff</c> など）。</param>
    /// <param name="elapsedMs">所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3003,
        EventName = "obd.discovery.unavailable",
        Level = LogLevel.Warning,
        Message = "{Mode} の探索ができません（{Reason}、{ElapsedMs} ms）")]
    public static partial void Unavailable(ILogger logger, Exception exception, string mode, string reason, long elapsedMs);

    /// <summary>探索が想定外の例外で失敗したことを書きます（画面にはエラーが出る）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="mode">探索の種類。</param>
    /// <param name="elapsedMs">所要時間（ミリ秒）。</param>
    [LoggerMessage(
        EventId = 3004,
        EventName = "obd.discovery.failed",
        Level = LogLevel.Error,
        Message = "{Mode} の探索が失敗しました（{ElapsedMs} ms）")]
    public static partial void Failed(ILogger logger, Exception exception, string mode, long elapsedMs);

    /// <summary>探索が取り消されたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="mode">探索の種類。</param>
    [LoggerMessage(
        EventId = 3005,
        EventName = "obd.discovery.cancelled",
        Level = LogLevel.Debug,
        Message = "{Mode} の探索が取り消されました")]
    public static partial void Cancelled(ILogger logger, string mode);

    /// <summary>新しい探索に置き換えられたため、結果を捨てたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="mode">探索の種類。</param>
    [LoggerMessage(
        EventId = 3006,
        EventName = "obd.discovery.result-discarded",
        Level = LogLevel.Debug,
        Message = "{Mode} の探索の結果を、新しい探索に置き換えられたため捨てました")]
    public static partial void ResultDiscarded(ILogger logger, string mode);
}
