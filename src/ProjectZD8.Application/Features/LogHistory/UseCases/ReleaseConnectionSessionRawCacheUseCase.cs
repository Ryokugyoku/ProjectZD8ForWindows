using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.LogHistory.UseCases;

/// <summary>現在端末内のセッションRawログだけを除去します。</summary>
public sealed class ReleaseConnectionSessionRawCacheUseCase
{
    /// <summary>ローカル除去前の安全判断方針です。</summary>
    private readonly ConnectionSessionLocalRemovalPolicy policy;
    /// <summary>Rawログのローカル保存先です。</summary>
    private readonly IConnectionSessionRawLogRepository repository;

    /// <summary>除去方針とRawログ保存先を固定して生成します。</summary>
    /// <remarks>責務: 現在端末のローカル除去判断をRawログ保存先へ結び付けます。</remarks>
    /// <param name="repository">Rawログのローカル保存先。</param>
    /// <param name="policy">CloudKit保管証跡を評価する除去方針。<see langword="null"/> なら既定の方針です。</param>
    public ReleaseConnectionSessionRawCacheUseCase(
        IConnectionSessionRawLogRepository repository,
        ConnectionSessionLocalRemovalPolicy? policy = null)
    {
        this.policy = policy ?? new ConnectionSessionLocalRemovalPolicy();
        this.repository = repository;
    }

    /// <summary>指定セッションのローカル除去前判断を返します。</summary>
    /// <remarks>責務: 1件の接続セッションを標準確認、データ消失警告、利用不能へ分類します。</remarks>
    /// <param name="session">現在端末からRawログを除去する候補セッション。</param>
    /// <returns>CloudKit保管証跡を含む現在状態に対応した除去判断。</returns>
    public ConnectionSessionLocalRemovalDecision Decision(ConnectionSession session) => policy.Decision(session);

    /// <summary>ユーザー確認済みセッションのRawログを現在端末から除去します。</summary>
    /// <remarks>責務: 1件の除去可能な終了済みセッションからローカルRawログだけを削除します。</remarks>
    /// <param name="session">ユーザーが除去を確定した接続セッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">現在状態で除去不能または保存先更新に失敗した場合のエラー。</exception>
    public void Execute(ConnectionSession session)
    {
        if (policy.Decision(session) == ConnectionSessionLocalRemovalDecision.Unavailable)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        repository.RemoveLocalEntries(session.Id);
    }
}
