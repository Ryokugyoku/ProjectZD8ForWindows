using System.Collections.Generic;
using System.Threading.Tasks;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】クラウドを使わないローカルモード用の走行ログ転送先です。
/// </summary>
/// <remarks>
/// クラウドにあるものは何もない（空）として扱い、アップロードは「利用できない」で失敗させます。
/// 保存できていないのに「アップロード済み」と見せないためです。
/// iOS 版の <c>CloudKitConnectionSessionTransferRepository</c> の代わり。ネットワーク担当の CloudKit 実装ができたら差し替えます。
/// </remarks>
public sealed class InterimLocalOnlyConnectionSessionTransferRepository : IConnectionSessionTransferRepository
{
    /// <summary>アップロードできないことを返します。</summary>
    /// <remarks>責務: ローカルモードでは送信先がないため、利用不能のエラーで失敗します。</remarks>
    /// <param name="package">転送パッケージ。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>常に失敗するタスク。</returns>
    public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) =>
        Task.FromException<string>(new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.Unavailable));

    /// <summary>クラウドの転送として空を返します。</summary>
    /// <remarks>責務: ローカルモードではクラウドに何もないことを表します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>空の一覧。</returns>
    public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>([]);

    /// <summary>Mac 受領証を送りません。</summary>
    /// <remarks>責務: ローカルモードでは何もせずに完了します。</remarks>
    /// <param name="receipt">受領証。</param>
    /// <param name="sessionID">対象セッション。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
        Task.CompletedTask;

    /// <summary>Mac 受領証として空を返します。</summary>
    /// <remarks>責務: ローカルモードでは受領証がないことを表します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>空の一覧。</returns>
    public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
        string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([]);

    /// <summary>クラウドで削除されたセッションとして空を返します。</summary>
    /// <remarks>責務: ローカルモードでは削除記録がないことを表します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>空の集合。</returns>
    public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
        Task.FromResult<IReadOnlySet<ConnectionSessionID>>(new HashSet<ConnectionSessionID>());

    /// <summary>クラウド側の削除を行いません（端末側の削除は別に行われます）。</summary>
    /// <remarks>責務: ローカルモードでは何もせずに完了します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) => Task.CompletedTask;

    /// <summary>クラウド側の全削除を行いません。</summary>
    /// <remarks>責務: ローカルモードでは何もせずに完了します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task DeleteAll(string accountIdentifier) => Task.CompletedTask;
}
