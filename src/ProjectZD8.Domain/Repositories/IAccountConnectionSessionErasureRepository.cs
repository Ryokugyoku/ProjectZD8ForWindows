namespace ProjectZD8.Domain.Repositories;

/// <summary>アカウント削除時に接続履歴とRawログをまとめて消去します。</summary>
public interface IAccountConnectionSessionErasureRepository
{
    /// <summary>指定アカウントの全接続セッションを削除します。</summary>
    /// <remarks>責務: 1件のアカウント識別子に属する接続履歴と子Rawログを削除済み状態へします。</remarks>
    /// <param name="accountIdentifier">削除対象のAppleアカウント識別子。</param>
    /// <exception cref="Exception">永続化済み運転データを完全に削除できない場合の保存先エラー。</exception>
    void DeleteSessions(string accountIdentifier);
}