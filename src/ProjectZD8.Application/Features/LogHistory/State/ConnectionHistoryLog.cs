using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Application.LogHistory.State;

/// <summary>
/// 接続履歴モデルと同期ユースケースが書く診断ログです（分類 <c>ui</c>、ID は 2521 番台）。
/// </summary>
/// <remarks>
/// 失敗の原因（型・メッセージ・スタック）が画面用のエラー状態に変わって消えないよう、エラー状態へ変える前に書きます。
/// 車両の名前・整備記録の本文・VIN は書きません。<c>operation</c> は固定の短い名前です。
/// </remarks>
internal static partial class ConnectionHistoryLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.UI);

    /// <summary>利用者に見える失敗を書きます。</summary>
    /// <param name="exception">原因の例外。</param>
    /// <param name="operation">失敗した操作の名前。</param>
    public static void Failed(Exception exception, string operation) => Failed(Logger, exception, operation);

    /// <summary>自動で回復または無視した失敗を書きます。</summary>
    /// <param name="exception">原因の例外。</param>
    /// <param name="operation">失敗した操作の名前。</param>
    public static void Degraded(Exception exception, string operation) => Degraded(Logger, exception, operation);

    /// <summary>想定内の中断（キャンセルなど）を書きます。</summary>
    /// <param name="operation">中断された操作の名前。</param>
    public static void Canceled(string operation) => Canceled(Logger, operation);

    /// <summary>利用者に見える失敗です。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">原因の例外。</param>
    /// <param name="operation">失敗した操作の名前。</param>
    [LoggerMessage(
        EventId = 2521,
        EventName = "ui.history.operation.failed",
        Level = LogLevel.Error,
        Message = "操作に失敗しました: {Operation}")]
    private static partial void Failed(ILogger logger, Exception exception, string operation);

    /// <summary>回復または無視した失敗です。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">原因の例外。</param>
    /// <param name="operation">失敗した操作の名前。</param>
    [LoggerMessage(
        EventId = 2522,
        EventName = "ui.history.operation.degraded",
        Level = LogLevel.Warning,
        Message = "操作の一部が失敗しました（続行します）: {Operation}")]
    private static partial void Degraded(ILogger logger, Exception exception, string operation);

    /// <summary>想定内の中断です。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="operation">中断された操作の名前。</param>
    [LoggerMessage(
        EventId = 2523,
        EventName = "ui.history.operation.canceled",
        Level = LogLevel.Debug,
        Message = "操作が中断されました: {Operation}")]
    private static partial void Canceled(ILogger logger, string operation);
}
