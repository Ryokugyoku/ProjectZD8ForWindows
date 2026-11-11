using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics.Tests.Support;

/// <summary>計装の書き方（<c>[LoggerMessage]</c>）と同じ形で書いた、テスト用のログです。</summary>
internal static partial class SampleLog
{
    /// <summary>バッチを保存したことを書きます（Debug）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="count">件数。</param>
    /// <param name="elapsedMs">所要時間。</param>
    [LoggerMessage(EventId = 5001, EventName = "acq.batch.saved", Level = LogLevel.Debug, Message = "バッチを保存しました（{Count} 件、{ElapsedMs} ms）")]
    public static partial void BatchSaved(ILogger logger, int count, double elapsedMs);

    /// <summary>ELM327 へ送ったことを書きます（Trace）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="command">コマンド。</param>
    /// <param name="length">長さ。</param>
    [LoggerMessage(EventId = 4001, EventName = "obd.wire.sent", Level = LogLevel.Trace, Message = "送信 {Command}（{Length} バイト）")]
    public static partial void WireSent(ILogger logger, string command, int length);

    /// <summary>接続に失敗したことを書きます（Error）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">例外。</param>
    /// <param name="transport">通信の種類。</param>
    [LoggerMessage(EventId = 3001, EventName = "obd.connect.failed", Level = LogLevel.Error, Message = "接続に失敗しました（{Transport}）")]
    public static partial void ConnectFailed(ILogger logger, Exception exception, string transport);
}

/// <summary><see cref="DiagnosticCategoryAttribute"/> を付けた型です。</summary>
[DiagnosticCategory("obd.connect")]
internal sealed class CategorizedType;

/// <summary>分類を付けていない型です。</summary>
internal sealed class UncategorizedType;
