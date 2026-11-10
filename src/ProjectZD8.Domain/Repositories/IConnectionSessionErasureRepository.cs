using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>接続セッション本体と子Rawログをセッション単位で物理削除します。</summary>
public interface IConnectionSessionErasureRepository
{
    /// <summary>指定アカウントが所有する1件の接続セッションを削除します。</summary>
    /// <remarks>責務: 1件のセッションIDに属する履歴本体と子Rawログを原子的に物理削除します。</remarks>
    /// <param name="sessionID">削除対象の接続セッションID。</param>
    /// <param name="accountIdentifier">削除対象を所有するAppleアカウント識別子。</param>
    /// <exception cref="Exception">所有関係不一致、セッション不在、または永続化失敗。</exception>
    void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier);
}