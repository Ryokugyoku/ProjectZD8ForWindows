using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Policies;

/// <summary>CloudKit保存済みRawログを端末へ保持する期間を判定します。</summary>
public sealed class ConnectionSessionRawCacheRetentionPolicy
{
    /// <summary>Rawログを最後の閲覧から保持する秒数です。</summary>
    /// <remarks>Swift の <c>TimeInterval</c>（秒の <c>Double</c>）をそのまま秒数の <see cref="double"/> で保持します。</remarks>
    public double RetentionInterval { get; }

    /// <summary>標準の3日保持期間を固定して生成します。</summary>
    /// <remarks>責務: Rawキャッシュ保持期間を3日間の既定値へ固定します。</remarks>
    /// <param name="retentionInterval">最終閲覧から保持する秒数。</param>
    public ConnectionSessionRawCacheRetentionPolicy(double retentionInterval = 3 * 24 * 60 * 60)
    {
        RetentionInterval = retentionInterval;
    }

    /// <summary>指定日時時点で端末Rawキャッシュを退避できるかを返します。</summary>
    /// <remarks>責務: 1件の終了済みセッションをCloudKit保護状態と最終閲覧経過時間で退避判定します。</remarks>
    /// <param name="session">退避候補の接続セッション。</param>
    /// <param name="now">判定基準日時。</param>
    /// <returns>ローカルRawだけを削除してよい場合は <see langword="true"/>。</returns>
    public bool ShouldEvict(ConnectionSession session, DateTimeOffset now)
    {
        if (session.EndedAt is null
            || session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Available
            || session.RawLogSummary.CloudState != ConnectionSessionCloudSyncState.Uploaded
            || string.IsNullOrEmpty(session.RawLogSummary.ManifestDigest))
        {
            return false;
        }
        var lastReference = session.RawLogSummary.LastAccessedAt ?? session.EndedAt ?? session.StartedAt;
        return (now - lastReference).TotalSeconds >= RetentionInterval;
    }
}
