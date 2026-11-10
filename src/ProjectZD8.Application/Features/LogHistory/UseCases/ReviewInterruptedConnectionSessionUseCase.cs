using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LogHistory.UseCases;

/// <summary>ユーザーが意図した停止を元の終了理由を保ったまま正式データとして保存します。</summary>
public sealed class ReviewInterruptedConnectionSessionUseCase
{
    /// <summary>確認結果を保存する接続セッション境界です。</summary>
    private readonly IConnectionSessionRepository repository;

    /// <summary>接続セッション保存先を固定して生成します。</summary>
    /// <remarks>責務: 1件の接続セッション保存先を中断確認ワークフローへ結び付けます。</remarks>
    /// <param name="repository">確認結果を保存する接続セッション境界。</param>
    public ReviewInterruptedConnectionSessionUseCase(IConnectionSessionRepository repository)
    {
        this.repository = repository;
    }

    /// <summary>応答停止または接続喪失をユーザー操作による停止として確認します。</summary>
    /// <remarks>責務: 1件の確認可能な終了済みセッションへユーザー操作停止の確認結果を保存します。</remarks>
    /// <param name="session">確認対象の接続セッション。</param>
    /// <returns>確認結果を反映した接続セッション。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">対象が確認不能な状態、または永続化に失敗した場合のエラー。</exception>
    public ConnectionSession Execute(ConnectionSession session)
    {
        if (session.EndedAt is null || !session.NeedsStopReview)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        var reviewed = session with { StopReviewDecision = ConnectionSessionStopReviewDecision.UserInitiated };
        repository.Save(reviewed);
        return reviewed;
    }
}
