using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.LogHistory.UseCases;

/// <summary>1件の接続セッションをCloudKitと現在端末から削除します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class DeleteConnectionSessionEverywhereUseCase
{
    /// <summary>現在端末のセッション物理削除境界です。</summary>
    private readonly IConnectionSessionErasureRepository localRepository;
    /// <summary>全端末へ削除を伝播するCloudKit境界です。</summary>
    private readonly IConnectionSessionTransferRepository transferRepository;

    /// <summary>ローカル保存先と端末間転送先を固定して生成します。</summary>
    /// <remarks>責務: 1件の全端末削除処理をローカル保存先とCloudKit削除境界へ結び付けます。</remarks>
    /// <param name="localRepository">現在端末のセッション物理削除境界。</param>
    /// <param name="transferRepository">削除マーカーと転送データを管理するCloudKit境界。</param>
    public DeleteConnectionSessionEverywhereUseCase(
        IConnectionSessionErasureRepository localRepository,
        IConnectionSessionTransferRepository transferRepository)
    {
        this.localRepository = localRepository;
        this.transferRepository = transferRepository;
    }

    /// <summary>削除マーカーを公開してCloud上のPayloadを除去した後、現在端末から物理削除します。</summary>
    /// <remarks>責務: 1件の終了済みセッションを全端末削除対象として確定し現在端末から物理削除します。</remarks>
    /// <param name="session">削除する終了済み接続セッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">取得中セッション、CloudKit削除、またはローカル物理削除に失敗した場合のエラー。</exception>
    public async Task Execute(ConnectionSession session)
    {
        if (session.EndedAt is null)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        await transferRepository.DeleteSession(session.Id, session.AccountIdentifier);
        localRepository.DeleteSession(session.Id, session.AccountIdentifier);
    }
}
