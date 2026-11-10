using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Domain.Repositories;

/// <summary>接続セッションをアカウント単位で永続化し取得します。</summary>
public interface IConnectionSessionRepository
{
    /// <summary>セッションの現在内容を新規保存または更新します。</summary>
    /// <remarks>責務: 1件の接続セッションを安定ID単位で永続化します。</remarks>
    /// <param name="session">保存するセッションの現在内容。</param>
    /// <exception cref="Exception">永続化を完了できない場合の保存先エラー。</exception>
    void Save(ConnectionSession session);

    /// <summary>指定アカウントに属するセッションを新しい順で取得します。</summary>
    /// <remarks>責務: 1件のアカウント識別子に属する接続履歴を開始日時の降順で返します。</remarks>
    /// <param name="accountIdentifier">取得対象のAppleアカウント識別子。</param>
    /// <returns>開始日時が新しい順の接続セッション一覧。</returns>
    /// <exception cref="Exception">永続化済み履歴を取得できない場合の保存先エラー。</exception>
    IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier);
}