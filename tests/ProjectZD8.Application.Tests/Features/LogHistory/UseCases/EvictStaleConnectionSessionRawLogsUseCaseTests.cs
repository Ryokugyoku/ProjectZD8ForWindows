using ProjectZD8.Application.LogHistory.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LogHistory.UseCases;

/// <summary>期限切れRawキャッシュのアカウント単位退避を検証します。</summary>
public sealed class EvictStaleConnectionSessionRawLogsUseCaseTests
{
    /// <summary>3日以上未閲覧のCloudKit保存済みRawだけを端末から除去します。</summary>
    /// <remarks>責務: 保持期限とCloudKit保護条件を満たすセッションだけがRepositoryへ渡ることを確認します。</remarks>
    [Fact]
    public void EvictsOnlyExpiredCloudProtectedRaw()
    {
        var expired = MakeSession(ConnectionSessionID.New(), 100, ConnectionSessionCloudSyncState.Uploaded);
        var recent = MakeSession(ConnectionSessionID.New(), 300_000, ConnectionSessionCloudSyncState.Uploaded);
        var unsafeSession = MakeSession(ConnectionSessionID.New(), 100, ConnectionSessionCloudSyncState.Failed);
        var repository = new EvictionRepository([expired, recent, unsafeSession]);
        var useCase = new EvictStaleConnectionSessionRawLogsUseCase(
            sessionRepository: repository,
            rawLogRepository: repository,
            now: () => DateTimeOffset.UnixEpoch.AddSeconds(300_000));

        useCase.Execute("account");

        Assert.Equal([expired.Id], repository.RemovedSessionIDs);
    }

    /// <summary>指定条件の終了済みRaw保有セッションを生成します。</summary>
    /// <remarks>責務: 退避ユースケーステストへ保持期限とCloud状態だけが異なるセッションを供給します。</remarks>
    /// <param name="id">セッションの安定識別子。</param>
    /// <param name="lastAccessedAt">最終閲覧日時のUNIX秒。</param>
    /// <param name="cloudState">CloudKit転送状態。</param>
    /// <returns>Rawを現在端末に保持する終了済みセッション。</returns>
    private static ConnectionSession MakeSession(
        ConnectionSessionID id,
        double lastAccessedAt,
        ConnectionSessionCloudSyncState cloudState) =>
        new ConnectionSession("account", id: id, startedAt: DateTimeOffset.UnixEpoch.AddSeconds(50)) with
        {
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(60),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 2,
                localState: ConnectionSessionLocalRawState.Available,
                cloudState: cloudState,
                manifestDigest: cloudState == ConnectionSessionCloudSyncState.Uploaded ? "manifest" : null,
                macImportReceipt: null,
                lastAccessedAt: DateTimeOffset.UnixEpoch.AddSeconds(lastAccessedAt)),
        };
}

/// <summary>退避対象セッションとRaw除去要求をメモリ上で記録します。</summary>
file sealed class EvictionRepository : IConnectionSessionRepository, IConnectionSessionRawLogRepository
{
    /// <summary>一覧取得で返すセッションです。</summary>
    private readonly IReadOnlyList<ConnectionSession> storedSessions;

    /// <summary>固定セッション一覧を保持して生成します。</summary>
    /// <remarks>責務: 退避テスト用のローカル保存状態を固定します。</remarks>
    /// <param name="sessions">一覧取得で返すセッション。</param>
    public EvictionRepository(IReadOnlyList<ConnectionSession> sessions)
    {
        storedSessions = sessions;
    }

    /// <summary>除去要求を受けたセッションIDです。</summary>
    public List<ConnectionSessionID> RemovedSessionIDs { get; } = [];

    /// <summary>このテストでは保存要求を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のセッション保存要求を満たします。</remarks>
    /// <param name="session">使用しないセッション。</param>
    public void Save(ConnectionSession session)
    {
    }

    /// <summary>固定セッション一覧を返します。</summary>
    /// <remarks>責務: 退避ユースケースへ注入済みセッションを供給します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>初期化時に保持したセッション一覧。</returns>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) => storedSessions;

    /// <summary>このテストではRaw追記を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のRaw追記要求を満たします。</remarks>
    /// <param name="observation">使用しないRaw応答。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID)
    {
    }

    /// <summary>このテストでは空のRawログを返します。</summary>
    /// <remarks>責務: テスト対象外のRaw照会要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <returns>空配列。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) => [];

    /// <summary>このテストでは空の車両別Rawログを返します。</summary>
    /// <remarks>責務: テスト対象外の車両別Raw照会要求を満たします。</remarks>
    /// <param name="vehicleID">使用しない車両ID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空配列。</returns>
    public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier) => [];

    /// <summary>このテストではCloudKit保存状態更新を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のCloudKit保存済み更新要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="manifestDigest">使用しないManifest。</param>
    public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest)
    {
    }

    /// <summary>このテストではCloudKit失敗更新を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のCloudKit失敗更新要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void MarkCloudUploadFailed(ConnectionSessionID sessionID)
    {
    }

    /// <summary>このテストではMac受領証更新を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のMac受領証更新要求を満たします。</remarks>
    /// <param name="receipt">使用しないMac受領証。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID)
    {
    }

    /// <summary>このテストでは転送取込を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外の転送取込要求を満たします。</remarks>
    /// <param name="transfer">使用しない転送Payload。</param>
    public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer)
    {
    }

    /// <summary>Raw除去要求を検査可能な履歴へ追加します。</summary>
    /// <remarks>責務: 1件の期限切れRaw除去要求をセッションIDとして記録します。</remarks>
    /// <param name="sessionID">除去対象のセッションID。</param>
    public void RemoveLocalEntries(ConnectionSessionID sessionID)
    {
        RemovedSessionIDs.Add(sessionID);
    }
}
