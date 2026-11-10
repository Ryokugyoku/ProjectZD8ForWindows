using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.LogHistory.UseCases;

/// <summary>3日以上閲覧されていないCloudKit保存済みRawログを端末から退避します。</summary>
public sealed class EvictStaleConnectionSessionRawLogsUseCase
{
    /// <summary>退避可否を判定する保持方針です。</summary>
    private readonly ConnectionSessionRawCacheRetentionPolicy policy;
    /// <summary>セッション一覧の取得先です。</summary>
    private readonly IConnectionSessionRepository sessionRepository;
    /// <summary>Rawログのローカル保存先です。</summary>
    private readonly IConnectionSessionRawLogRepository rawLogRepository;
    /// <summary>判定基準日時を生成するクロックです。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>保持方針、保存先、クロックを固定して生成します。</summary>
    /// <remarks>責務: Rawキャッシュ保持判断をローカルRaw除去操作へ結び付けます。</remarks>
    /// <param name="sessionRepository">アカウント単位のセッション取得先。</param>
    /// <param name="rawLogRepository">Rawログのローカル保存先。</param>
    /// <param name="policy">CloudKit保護と最終閲覧日時を評価する保持方針。<see langword="null"/> なら既定の方針です。</param>
    /// <param name="now">判定基準日時を生成するクロック。<see langword="null"/> なら現在時刻です。</param>
    public EvictStaleConnectionSessionRawLogsUseCase(
        IConnectionSessionRepository sessionRepository,
        IConnectionSessionRawLogRepository rawLogRepository,
        ConnectionSessionRawCacheRetentionPolicy? policy = null,
        Func<DateTimeOffset>? now = null)
    {
        this.policy = policy ?? new ConnectionSessionRawCacheRetentionPolicy();
        this.sessionRepository = sessionRepository;
        this.rawLogRepository = rawLogRepository;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>指定アカウントの期限切れRawキャッシュを退避します。</summary>
    /// <remarks>責務: 1件のアカウントスコープを保持期限切れRawログのローカル除去結果へ変換します。</remarks>
    /// <param name="accountIdentifier">退避対象を所有するAppleアカウント識別子。</param>
    /// <exception cref="Exception">履歴取得またはローカルRaw除去に失敗した場合のエラー。</exception>
    public void Execute(string accountIdentifier)
    {
        var referenceDate = now();
        foreach (var session in sessionRepository.Sessions(accountIdentifier))
        {
            if (policy.ShouldEvict(session, referenceDate))
            {
                rawLogRepository.RemoveLocalEntries(session.Id);
            }
        }
    }
}
