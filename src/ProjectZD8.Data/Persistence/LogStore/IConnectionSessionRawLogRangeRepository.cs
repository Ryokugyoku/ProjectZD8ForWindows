using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>生ログをsequence範囲と観測日時の範囲で読む能力です。</summary>
/// <remarks>
/// iOS版にはない、Windows版の追加の読取口です（Domainの <c>IConnectionSessionRawLogRepository</c> はページ読みだけを持ちます）。
/// 解析の画面やツールが長いセッションの一部だけを読むときに使います。
/// </remarks>
public interface IConnectionSessionRawLogRangeRepository
{
    /// <summary>sequence範囲の生ログを返します。</summary>
    /// <remarks>責務: 範囲内の生ログだけをsequence昇順で最大件数まで返します。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <param name="fromInclusive">範囲の開始（含む）。</param>
    /// <param name="toExclusive">範囲の終了（含まない）。</param>
    /// <param name="limit">最大件数。</param>
    /// <returns>sequence昇順の生ログ。</returns>
    IReadOnlyList<ConnectionSessionRawLogEntry> EntriesInSequenceRange(
        ConnectionSessionID sessionID,
        long fromInclusive,
        long toExclusive,
        int limit = int.MaxValue);

    /// <summary>観測日時の範囲の生ログを返します。</summary>
    /// <remarks>責務: 観測日時が範囲内（両端を含む）の生ログをsequence昇順で返します。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <param name="from">範囲の開始（含む）。</param>
    /// <param name="to">範囲の終了（含む）。</param>
    /// <returns>sequence昇順の生ログ。</returns>
    IReadOnlyList<ConnectionSessionRawLogEntry> EntriesObserved(ConnectionSessionID sessionID, DateTimeOffset from, DateTimeOffset to);
}
