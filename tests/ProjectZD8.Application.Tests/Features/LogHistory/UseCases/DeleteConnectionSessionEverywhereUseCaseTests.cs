using ProjectZD8.Application.LogHistory.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LogHistory.UseCases;

/// <summary>全端末セッション削除の副作用順序と状態制約を検証します。</summary>
public sealed class DeleteConnectionSessionEverywhereUseCaseTests
{
    /// <summary>CloudKit削除が成功した後に現在端末を物理削除します。</summary>
    /// <remarks>責務: 1件の終了済みセッション削除が再アップロードを防ぐ順序で実行されることを確認します。</remarks>
    [Fact]
    public async Task ExecuteDeletesCloudBeforeLocalRepository()
    {
        var recorder = new SessionDeletionRecorder();
        var local = new SessionErasureRepositorySpy(recorder);
        var cloud = new SessionTransferRepositorySpy(recorder);
        var useCase = new DeleteConnectionSessionEverywhereUseCase(local, cloud);
        var session = new ConnectionSession("account") with
        {
            EndedAt = DateTimeOffset.UtcNow,
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };

        await useCase.Execute(session);

        Assert.Equal(["cloud", "local"], recorder.Events);
    }

    /// <summary>取得中セッションはCloudKitにもローカルにも削除要求を出しません。</summary>
    /// <remarks>責務: 未終了セッションに対する全端末削除拒否を確認します。</remarks>
    [Fact]
    public async Task ExecuteRejectsActiveSession()
    {
        var recorder = new SessionDeletionRecorder();
        var useCase = new DeleteConnectionSessionEverywhereUseCase(
            new SessionErasureRepositorySpy(recorder),
            new SessionTransferRepositorySpy(recorder));

        var error = await Assert.ThrowsAsync<ConnectionSessionRepositoryException>(
            () => useCase.Execute(new ConnectionSession("account")));

        Assert.Equal(ConnectionSessionRepositoryError.InvalidState, error.Reason);
        Assert.Empty(recorder.Events);
    }
}

/// <summary>セッション削除副作用を発生順に保持します。</summary>
file sealed class SessionDeletionRecorder
{
    /// <summary>発生順の副作用名です。</summary>
    public List<string> Events { get; } = [];
}

/// <summary>ローカルセッション物理削除を記録します。</summary>
file sealed class SessionErasureRepositorySpy : IConnectionSessionErasureRepository
{
    /// <summary>削除順序の共有記録先です。</summary>
    private readonly SessionDeletionRecorder recorder;

    /// <summary>共有記録先を固定して生成します。</summary>
    /// <remarks>責務: ローカル削除Spyを1件の順序記録先へ結び付けます。</remarks>
    /// <param name="recorder">削除副作用を保持する共有記録先。</param>
    public SessionErasureRepositorySpy(SessionDeletionRecorder recorder)
    {
        this.recorder = recorder;
    }

    /// <summary>ローカル物理削除を共有履歴へ記録します。</summary>
    /// <remarks>責務: 1件のローカル削除要求を順序検証可能なイベントへ変換します。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    public void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
    {
        recorder.Events.Add("local");
    }
}

/// <summary>CloudKitセッション操作のうち単体削除だけを記録します。</summary>
file sealed class SessionTransferRepositorySpy : IConnectionSessionTransferRepository
{
    /// <summary>削除順序の共有記録先です。</summary>
    private readonly SessionDeletionRecorder recorder;

    /// <summary>共有記録先を固定して生成します。</summary>
    /// <remarks>責務: CloudKit削除Spyを1件の順序記録先へ結び付けます。</remarks>
    /// <param name="recorder">削除副作用を保持する共有記録先。</param>
    public SessionTransferRepositorySpy(SessionDeletionRecorder recorder)
    {
        this.recorder = recorder;
    }

    /// <summary>このテストでは送信を使用しません。</summary>
    /// <remarks>責務: テスト対象外の送信要求へ固定Digestを返します。</remarks>
    /// <param name="package">使用しない転送Payload。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>固定Digest。</returns>
    public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) =>
        Task.FromResult("digest");

    /// <summary>このテストでは転送を返しません。</summary>
    /// <remarks>責務: テスト対象外の転送取得へ空配列を返します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空配列。</returns>
    public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>([]);

    /// <summary>このテストでは受領証を公開しません。</summary>
    /// <remarks>責務: テスト対象外の受領証公開を副作用なしで満たします。</remarks>
    /// <param name="receipt">使用しない受領証。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
        Task.CompletedTask;

    /// <summary>このテストでは受領証を返しません。</summary>
    /// <remarks>責務: テスト対象外の受領証取得へ空配列を返します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空配列。</returns>
    public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
        string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([]);

    /// <summary>このテストでは削除マーカーを返しません。</summary>
    /// <remarks>責務: テスト対象外の削除マーカー取得へ空集合を返します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空集合。</returns>
    public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
        Task.FromResult<IReadOnlySet<ConnectionSessionID>>(new HashSet<ConnectionSessionID>());

    /// <summary>CloudKitセッション削除を共有履歴へ記録します。</summary>
    /// <remarks>責務: 1件のCloudKit削除要求を順序検証可能なイベントへ変換します。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
    {
        recorder.Events.Add("cloud");
        return Task.CompletedTask;
    }

    /// <summary>このテストではアカウント全削除を使用しません。</summary>
    /// <remarks>責務: テスト対象外のアカウント全削除を副作用なしで満たします。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task DeleteAll(string accountIdentifier) => Task.CompletedTask;
}
