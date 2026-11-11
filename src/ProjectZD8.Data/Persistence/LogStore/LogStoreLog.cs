using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>
/// 生ログストア（<c>&lt;データ&gt;\logs\&lt;セッション&gt;\</c>）が書く診断ログです（分類 <c>store.log</c>、ID は 7000 番台）。
/// </summary>
/// <remarks>
/// <para>1 件ごとの追記（<c>Append</c>）のホットパスには書きません。開き直し・末尾の切り詰め・書込みの失敗・セグメントの確定だけです。</para>
/// <para><c>session</c> はセッションのフォルダー名（接続セッション ID。個人を特定しない）です。パスは書きません。</para>
/// </remarks>
internal static partial class LogStoreLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.StoreLog);

    /// <summary>セッションのフォルダー（または世代フォルダー）からセッション名を取ります。</summary>
    /// <param name="sessionDirectory">セッションのディレクトリー。</param>
    /// <returns>フォルダー名。</returns>
    public static string SessionName(string sessionDirectory) =>
        Path.GetFileName(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>世代フォルダーから、親のセッション名を取ります。</summary>
    /// <param name="generationDirectory">世代のディレクトリー。</param>
    /// <returns>セッション名。</returns>
    public static string SessionNameOfGeneration(string generationDirectory) =>
        SessionName(Path.GetDirectoryName(generationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty);

    /// <summary>索引ファイルから開き直したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="generation">世代番号。</param>
    [LoggerMessage(
        EventId = 7001,
        EventName = "store.log.open.indexed",
        Level = LogLevel.Debug,
        Message = "生ログを索引ファイルから開きました（{Session}、世代 {Generation}）")]
    public static partial void OpenedWithIndex(ILogger logger, string session, int generation);

    /// <summary>全走査で開き直したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="generation">世代番号。</param>
    /// <param name="segments">セグメントの数。</param>
    [LoggerMessage(
        EventId = 7002,
        EventName = "store.log.open.scanned",
        Level = LogLevel.Debug,
        Message = "生ログを全走査で開きました（{Session}、世代 {Generation}、セグメント {Segments} 個）")]
    public static partial void OpenedByScan(ILogger logger, string session, int generation, int segments);

    /// <summary>書きかけの末尾を切り詰めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="segment">セグメント番号。</param>
    /// <param name="fromBytes">切る前の長さ。</param>
    /// <param name="toBytes">切った後の長さ。</param>
    [LoggerMessage(
        EventId = 7003,
        EventName = "store.log.tail.truncated",
        Level = LogLevel.Warning,
        Message = "書きかけの末尾を切り詰めました（{Session}、セグメント {Segment}、{FromBytes} → {ToBytes} バイト）")]
    public static partial void TailTruncated(ILogger logger, string session, int segment, long fromBytes, long toBytes);

    /// <summary>読めない圧縮済みセグメントを壊れたものとして扱ったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="segment">セグメント番号。</param>
    [LoggerMessage(
        EventId = 7004,
        EventName = "store.log.segment.unreadable",
        Level = LogLevel.Warning,
        Message = "圧縮済みセグメントを読めないため壊れたものとして扱います（{Session}、セグメント {Segment}）")]
    public static partial void SegmentUnreadable(ILogger logger, Exception exception, string session, int segment);

    /// <summary>壊れたセグメントより後ろのセグメントを消したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="count">消したセグメントの数。</param>
    [LoggerMessage(
        EventId = 7005,
        EventName = "store.log.segments.discarded",
        Level = LogLevel.Warning,
        Message = "壊れたセグメントより後ろのセグメントを消しました（{Session}、{Count} 個）")]
    public static partial void SegmentsDiscarded(ILogger logger, string session, int count);

    /// <summary>索引ファイルが合わないため捨てたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="reason">理由（固定の短い名前）。</param>
    [LoggerMessage(
        EventId = 7006,
        EventName = "store.log.index.discarded",
        Level = LogLevel.Debug,
        Message = "索引ファイルを使わず捨てました（{Session}、{Reason}）")]
    public static partial void IndexDiscarded(ILogger logger, string session, string reason);

    /// <summary>索引ファイルを読めずに捨てたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    /// <param name="session">セッション名。</param>
    [LoggerMessage(
        EventId = 7007,
        EventName = "store.log.index.unreadable",
        Level = LogLevel.Warning,
        Message = "索引ファイルを読めないため捨てて全走査します（{Session}）")]
    public static partial void IndexUnreadable(ILogger logger, Exception exception, string session);

    /// <summary>背景の書込みが失敗したことを書きます（そのログの最初の失敗だけ）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="stage">失敗した段階（<c>write</c>・<c>fsync</c>・<c>close</c>）。</param>
    [LoggerMessage(
        EventId = 7010,
        EventName = "store.log.write.failed",
        Level = LogLevel.Error,
        Message = "生ログの書込みに失敗しました（{Session}、{Stage}）")]
    public static partial void WriteFailed(ILogger logger, Exception exception, string session, string stage);

    /// <summary>セグメントを確定（次のセグメントへ切り替え）したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="segment">確定したセグメント番号。</param>
    [LoggerMessage(
        EventId = 7011,
        EventName = "store.log.segment.sealed",
        Level = LogLevel.Debug,
        Message = "セグメントを確定しました（{Session}、セグメント {Segment}）")]
    public static partial void SegmentSealed(ILogger logger, string session, int segment);

    /// <summary>圧縮に失敗したことを書きます（非圧縮のまま残るため回復済み）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="session">セッション名。</param>
    /// <param name="segment">セグメント番号。</param>
    [LoggerMessage(
        EventId = 7012,
        EventName = "store.log.compress.failed",
        Level = LogLevel.Warning,
        Message = "セグメントの圧縮に失敗しました。非圧縮のまま残します（{Session}、セグメント {Segment}）")]
    public static partial void CompressionFailed(ILogger logger, Exception exception, string session, int segment);

    /// <summary>取得証拠の状態を作れなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="session">セッション名。</param>
    [LoggerMessage(
        EventId = 7013,
        EventName = "store.log.evidence.failed",
        Level = LogLevel.Error,
        Message = "取得証拠の状態を生ログから復元できませんでした（{Session}）")]
    public static partial void EvidenceRebuildFailed(ILogger logger, Exception exception, string session);
}
