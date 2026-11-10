using ProjectZD8.Application.LogHistory.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LogHistory.UseCases;

/// <summary>Raw非包含セッション概要とCloudKit Raw保管の端末間同期を検証します。</summary>
/// <remarks>
/// Swift 版の <c>testRoundTripBetweenIPhoneAndMacPreservesSharedArchiveMetadata</c> は実GRDB保存先（Data 層）を使うため、
/// Data 層の移植時に移します（Application のテストは Domain だけに依存するため）。
/// </remarks>
public sealed class SynchronizeConnectionSessionsUseCaseTests
{
    /// <summary>CloudKit削除マーカーを送信判定より先にローカル物理削除へ反映します。</summary>
    /// <remarks>責務: 削除済みセッションが別端末から再アップロードされない同期順序を確認します。</remarks>
    [Fact]
    public async Task DeletionMarkerRemovesLocalSessionBeforeUpload()
    {
        var session = new ConnectionSession("account") with
        {
            EndedAt = DateTimeOffset.UtcNow,
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = Summary(1, 1, ConnectionSessionLocalRawState.Available, ConnectionSessionCloudSyncState.Pending, null),
        };
        var local = new SynchronizationLocalRepository([session]);
        var cloud = new SynchronizationTransferRepository(
            transfers: [],
            receipts: [],
            deletedIDs: [session.Id]);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account");

        Assert.Equal([session.Id], local.DeletedSessionIDs);
        Assert.Empty(cloud.UploadedSessionIDs);
    }

    /// <summary>通常の履歴更新では送信待ちセッションをアップロードせず、個別要求で対象だけを送信します。</summary>
    /// <remarks>責務: 受信中心の更新と明示的な単一セッションアップロードが混在しないことを確認します。</remarks>
    [Fact]
    public async Task RefreshDoesNotUploadPendingSessionUntilExplicitRequest()
    {
        var session = new ConnectionSession("account") with
        {
            EndedAt = DateTimeOffset.UtcNow,
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = Summary(1, 1, ConnectionSessionLocalRawState.Available, ConnectionSessionCloudSyncState.Pending, null),
        };
        var local = new SynchronizationLocalRepository([session]);
        var cloud = new SynchronizationTransferRepository(transfers: [], receipts: []);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account");
        Assert.Empty(cloud.UploadedSessionIDs);

        await useCase.Upload(session.Id, "account");
        Assert.Equal([session.Id], cloud.UploadedSessionIDs);
        var first = local.Sessions("account").FirstOrDefault();
        Assert.NotNull(first);
        Assert.Equal(ConnectionSessionCloudSyncState.Uploaded, first.RawLogSummary.CloudState);
    }

    /// <summary>同じセッションの複数受領証ではCloudKitが返した最新1件だけを反映します。</summary>
    /// <remarks>責務: iPhoneが古いMac受領証で最新の取込証跡を上書きしないことを確認します。</remarks>
    [Fact]
    public async Task IPhoneAppliesOnlyNewestReceiptForEachSession()
    {
        var sessionID = ConnectionSessionID.New();
        var newest = MakeReceipt("new", 200);
        var older = MakeReceipt("old", 100);
        var session = new ConnectionSession("account", id: sessionID) with
        {
            RawLogSummary = Summary(0, 0, ConnectionSessionLocalRawState.Empty, ConnectionSessionCloudSyncState.Uploaded, "manifest"),
        };
        var local = new SynchronizationLocalRepository([session]);
        var cloud = new SynchronizationTransferRepository(
            transfers: [],
            receipts: [(sessionID, newest), (sessionID, older)]);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account");

        Assert.Single(local.MarkedReceipts);
        Assert.Equal(sessionID, local.MarkedReceipts[0].SessionID);
        Assert.Equal(newest, local.MarkedReceipts[0].Receipt);
    }

    /// <summary>iPhoneはCloudKit上の検証済みセッションをローカル履歴へ取り込みます。</summary>
    /// <remarks>責務: Macなど別端末で保存されたセッションがiPhone側へ復元されることを確認します。</remarks>
    [Fact]
    public async Task IPhoneImportsMetadataWithoutDownloadingRawPayload()
    {
        var session = MakeVehicleSession();
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(session, []), "manifest");
        var local = new SynchronizationLocalRepository([]);
        var cloud = new SynchronizationTransferRepository(
            transfers: [transfer],
            metadata: [new ConnectionSessionCloudMetadata(session, "manifest")],
            receipts: []);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account");

        Assert.Equal([session.Id], local.ImportedMetadata.Select(item => item.Session.Id));
        Assert.Empty(local.ImportedTransfers);
        Assert.Empty(local.MarkedReceipts);
        Assert.Empty(cloud.PublishedReceipts);
    }

    /// <summary>Raw応答がないMacセッションも履歴としてCloudKitへ保存しiPhoneへ取り込みます。</summary>
    /// <remarks>責務: macOSで終了したメタデータのみのセッションがiPhone履歴へ届く双方向経路を確認します。</remarks>
    [Fact]
    public async Task MacMetadataOnlySessionUploadsAndIPhoneImports()
    {
        var session = MakeVehicleSession();
        var macLocal = new SynchronizationLocalRepository([session]);
        var iPhoneLocal = new SynchronizationLocalRepository([]);
        var cloud = new SynchronizationTransferRepository(transfers: [], receipts: []);
        var macUseCase = MakeUseCase(macLocal, cloud, ConnectionSessionSyncDeviceRole.MacOS);
        var iPhoneUseCase = MakeUseCase(iPhoneLocal, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await macUseCase.Execute("account", uploadsPendingSessions: true);
        await iPhoneUseCase.Execute("account");

        Assert.Equal([session.Id], cloud.UploadedSessionIDs);
        Assert.Equal([session.Id], iPhoneLocal.ImportedMetadata.Select(item => item.Session.Id));
        Assert.Empty(iPhoneLocal.ImportedTransfers);
    }

    /// <summary>ローカルが送信済みでもCloudKitレコードが欠けていれば既存セッションを再公開します。</summary>
    /// <remarks>責務: 過去の送信済み状態だけが残ったMacセッションをCloudKitへ自己修復することを確認します。</remarks>
    [Fact]
    public async Task UploadedLocalSessionIsReuploadedWhenRemoteTransferIsMissing()
    {
        var session = new ConnectionSession("account") with
        {
            EndedAt = Unix(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = Summary(0, 0, ConnectionSessionLocalRawState.Empty, ConnectionSessionCloudSyncState.Uploaded, "missing-remote-manifest"),
        };
        var local = new SynchronizationLocalRepository([session]);
        var cloud = new SynchronizationTransferRepository(transfers: [], receipts: []);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account", uploadsPendingSessions: true);

        Assert.Equal([session.Id], cloud.UploadedSessionIDs);
        Assert.Equal([session.Id], local.ImportedMetadata.Select(item => item.Session.Id));
        var transferredSummary = local.ImportedMetadata.FirstOrDefault()?.Session.RawLogSummary;
        Assert.NotNull(transferredSummary);
        Assert.Equal(ConnectionSessionCloudSyncState.Uploaded, transferredSummary.CloudState);
        Assert.Equal("uploaded", transferredSummary.ManifestDigest);
        Assert.Null(transferredSummary.MacImportReceipt);
    }

    /// <summary>CloudKitに同じManifestが実在する送信済みセッションは再公開しません。</summary>
    /// <remarks>責務: 既存セッション救済が正常なCloudKitレコードを不要に上書きしないことを確認します。</remarks>
    [Fact]
    public async Task UploadedLocalSessionIsNotReuploadedWhenRemoteManifestMatches()
    {
        var session = new ConnectionSession("account") with
        {
            EndedAt = Unix(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = Summary(0, 0, ConnectionSessionLocalRawState.Empty, ConnectionSessionCloudSyncState.Uploaded, "remote-manifest"),
        };
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(session, []), "remote-manifest");
        var local = new SynchronizationLocalRepository([session]);
        var cloud = new SynchronizationTransferRepository(
            transfers: [transfer],
            metadata: [new ConnectionSessionCloudMetadata(session, "remote-manifest")],
            receipts: []);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account", uploadsPendingSessions: true);

        Assert.Empty(cloud.UploadedSessionIDs);
        Assert.Equal([session.Id], local.ImportedMetadata.Select(item => item.Session.Id));
        Assert.Empty(local.ImportedTransfers);
    }

    /// <summary>失敗状態のローカルセッションは同じ旧Manifestがリモートに残っていても再公開します。</summary>
    /// <remarks>責務: 保存後に内容が進んだセッションが古いCloudKit転送を置換し、自身の旧Payloadを再取込しないことを確認します。</remarks>
    [Fact]
    public async Task FailedLocalSessionReplacesStaleRemoteTransferBeforeImport()
    {
        var localSession = new ConnectionSession("account") with
        {
            EndedAt = Unix(120),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = Summary(0, 0, ConnectionSessionLocalRawState.Empty, ConnectionSessionCloudSyncState.Failed, "stale-manifest"),
        };
        var staleRemoteSession = localSession with { EndedAt = Unix(110) };
        var staleTransfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(staleRemoteSession, []),
            "stale-manifest");
        var local = new SynchronizationLocalRepository([localSession]);
        var cloud = new SynchronizationTransferRepository(
            transfers: [staleTransfer],
            metadata: [new ConnectionSessionCloudMetadata(staleRemoteSession, "stale-manifest")],
            receipts: []);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.IPhone);

        await useCase.Execute("account", uploadsPendingSessions: true);

        Assert.Equal([localSession.Id], cloud.UploadedSessionIDs);
        Assert.Single(local.ImportedMetadata);
        Assert.Equal("uploaded", local.ImportedMetadata.FirstOrDefault()?.ManifestDigest);
        Assert.Equal(localSession.EndedAt, local.ImportedMetadata.FirstOrDefault()?.Session.EndedAt);
    }

    /// <summary>Macは検証済みPayloadをローカルへ取り込んでから同じManifestの受領証を公開します。</summary>
    /// <remarks>責務: Mac同期が車両付きセッション取込、ローカル証跡、CloudKit受領証の順で完了することを確認します。</remarks>
    [Fact]
    public async Task MacImportsMetadataWithoutDownloadingRawOrPublishingReceipt()
    {
        var session = MakeVehicleSession();
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(session, []), "manifest");
        var local = new SynchronizationLocalRepository([]);
        var cloud = new SynchronizationTransferRepository(
            transfers: [transfer],
            metadata: [new ConnectionSessionCloudMetadata(session, "manifest")],
            receipts: []);
        var useCase = MakeUseCase(local, cloud, ConnectionSessionSyncDeviceRole.MacOS);

        await useCase.Execute("account");

        Assert.Equal([session.Id], local.ImportedMetadata.Select(item => item.Session.Id));
        Assert.Empty(local.ImportedTransfers);
        Assert.Empty(local.MarkedReceipts);
        Assert.Empty(cloud.PublishedReceipts);
    }

    /// <summary>テスト用保存先と端末役割で同期ユースケースを生成します。</summary>
    /// <remarks>責務: 1件のローカルテスト保存先を全ローカル境界として同期ユースケースへ結び付けます。</remarks>
    /// <typeparam name="TLocal">セッション・Raw・削除を兼ねるローカル保存先の型。</typeparam>
    /// <param name="local">セッション・Raw・削除を兼ねるローカル保存先。</param>
    /// <param name="cloud">CloudKitテスト実装。</param>
    /// <param name="role">現在端末の役割。</param>
    /// <returns>生成した同期ユースケース。</returns>
    private static SynchronizeConnectionSessionsUseCase MakeUseCase<TLocal>(
        TLocal local,
        IConnectionSessionTransferRepository cloud,
        ConnectionSessionSyncDeviceRole role)
        where TLocal : IConnectionSessionRepository, IConnectionSessionRawLogRepository, IConnectionSessionErasureRepository =>
        new(
            sessionRepository: local,
            rawLogRepository: local,
            sessionErasureRepository: local,
            transferRepository: cloud,
            role: role);

    /// <summary>車両付きの終了済みセッションを生成します。</summary>
    /// <remarks>責務: 概要同期テストへ開始100秒・終了110秒の車両付きセッションを供給します。</remarks>
    /// <returns>終了済みセッション。</returns>
    private static ConnectionSession MakeVehicleSession() =>
        new ConnectionSession(
            "account",
            startedAt: Unix(100),
            vehicle: new ConnectionSessionVehicle(new VehicleProfile("ZD8VIN", "BRZ"))) with
        {
            EndedAt = Unix(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };

    /// <summary>Rawログ概要を生成します。</summary>
    /// <remarks>責務: テスト入力の件数と保管状態を1件のRaw概要へまとめます。</remarks>
    /// <param name="recordCount">Raw件数。</param>
    /// <param name="byteCount">Rawバイト数。</param>
    /// <param name="localState">ローカル保管状態。</param>
    /// <param name="cloudState">CloudKit保管状態。</param>
    /// <param name="manifestDigest">Manifest。</param>
    /// <returns>生成したRaw概要。</returns>
    private static ConnectionSessionRawLogSummary Summary(
        long recordCount,
        long byteCount,
        ConnectionSessionLocalRawState localState,
        ConnectionSessionCloudSyncState cloudState,
        string? manifestDigest) =>
        new(recordCount, byteCount, localState, cloudState, manifestDigest, macImportReceipt: null);

    /// <summary>UNIX秒を日時へ変換します。</summary>
    /// <remarks>責務: Swift の <c>Date(timeIntervalSince1970:)</c> と同じ日時を作ります。</remarks>
    /// <param name="seconds">UNIX秒。</param>
    /// <returns>対応する日時。</returns>
    private static DateTimeOffset Unix(double seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    /// <summary>指定情報を持つMac取込受領証を生成します。</summary>
    /// <remarks>責務: iPhone受領証テストへ同一Manifestの時系列データを供給します。</remarks>
    /// <param name="deviceID">取込元Mac識別子。</param>
    /// <param name="importedAt">取込日時のUNIX秒。</param>
    /// <returns><c>manifest</c> に対応するMac受領証。</returns>
    private static ConnectionSessionMacImportReceipt MakeReceipt(string deviceID, double importedAt) =>
        new(deviceID, deviceID, Unix(importedAt), "manifest");
}

/// <summary>同期ユースケースのローカル保存操作を記録します。</summary>
file sealed class SynchronizationLocalRepository :
    IConnectionSessionRepository,
    IConnectionSessionRawLogRepository,
    IConnectionSessionErasureRepository
{
    /// <summary>一覧取得で返すセッションです。</summary>
    private readonly List<ConnectionSession> storedSessions;

    /// <summary>固定セッション一覧を保持します。</summary>
    /// <remarks>責務: 同期対象として返すローカル履歴を初期化します。</remarks>
    /// <param name="sessions">一覧取得で返すセッション。</param>
    public SynchronizationLocalRepository(IReadOnlyList<ConnectionSession> sessions)
    {
        storedSessions = [.. sessions];
    }

    /// <summary>取り込まれた検証済み転送です。</summary>
    public List<VerifiedConnectionSessionTransfer> ImportedTransfers { get; } = [];
    /// <summary>取り込まれたRaw非包含セッション概要です。</summary>
    public List<ConnectionSessionCloudMetadata> ImportedMetadata { get; } = [];
    /// <summary>反映されたセッションIDとMac受領証です。</summary>
    public List<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)> MarkedReceipts { get; } = [];
    /// <summary>物理削除された接続セッションIDです。</summary>
    public List<ConnectionSessionID> DeletedSessionIDs { get; } = [];

    /// <summary>このテストではセッション保存を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のセッション保存要求を満たします。</remarks>
    /// <param name="session">使用しないセッション。</param>
    public void Save(ConnectionSession session)
    {
    }

    /// <summary>固定セッション一覧を返します。</summary>
    /// <remarks>責務: 同期ユースケースへ注入済みローカル履歴を供給します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>初期化時に保持したセッション一覧。</returns>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) => storedSessions.ToList();

    /// <summary>このテストではRaw応答追記を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のRaw応答追記要求を満たします。</remarks>
    /// <param name="observation">使用しないRaw応答。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID)
    {
    }

    /// <summary>このテストでは空のRawログを返します。</summary>
    /// <remarks>責務: 送信対象外テストへ空のRawログを供給します。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <returns>空配列。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) => [];

    /// <summary>このテストでは空の車両別Rawログを返します。</summary>
    /// <remarks>責務: テスト対象外の車両別Rawログ照会要求を満たします。</remarks>
    /// <param name="vehicleID">使用しない登録車両ID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空配列。</returns>
    public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier) => [];

    /// <summary>CloudKit保存済みManifestをテスト用セッションへ反映します。</summary>
    /// <remarks>責務: 1件のアップロード結果を後続の概要公開で参照できる状態へ更新します。</remarks>
    /// <param name="sessionID">更新するセッションID。</param>
    /// <param name="manifestDigest">保存済みRaw PayloadのManifest。</param>
    public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest)
    {
        var index = storedSessions.FindIndex(session => session.Id == sessionID);
        if (index < 0)
        {
            return;
        }
        var stored = storedSessions[index];
        storedSessions[index] = stored with
        {
            RawLogSummary = stored.RawLogSummary with
            {
                CloudState = ConnectionSessionCloudSyncState.Uploaded,
                ManifestDigest = manifestDigest,
            },
        };
    }

    /// <summary>このテストではCloudKit失敗更新を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外の転送失敗要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void MarkCloudUploadFailed(ConnectionSessionID sessionID)
    {
    }

    /// <summary>Mac受領証と対象セッションを記録します。</summary>
    /// <remarks>責務: 同期ユースケースが反映したMac受領証を検査可能にします。</remarks>
    /// <param name="receipt">反映されたMac受領証。</param>
    /// <param name="sessionID">対象セッションID。</param>
    public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID)
    {
        MarkedReceipts.Add((sessionID, receipt));
    }

    /// <summary>検証済み転送を記録します。</summary>
    /// <remarks>責務: Mac同期がローカルへ渡した転送Payloadを検査可能にします。</remarks>
    /// <param name="transfer">取り込まれた転送Payload。</param>
    public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer)
    {
        ImportedTransfers.Add(transfer);
    }

    /// <summary>セッション概要をRaw Payloadとは別に記録します。</summary>
    /// <remarks>責務: 同期ユースケースが取り込んだ軽量概要を検査可能にします。</remarks>
    /// <param name="metadata">取り込まれたセッション概要。</param>
    public void ImportCloudMetadata(ConnectionSessionCloudMetadata metadata)
    {
        ImportedMetadata.Add(metadata);
    }

    /// <summary>このテストではローカル除去を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のローカル除去要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void RemoveLocalEntries(ConnectionSessionID sessionID)
    {
    }

    /// <summary>削除マーカーに対応するセッションIDを記録します。</summary>
    /// <remarks>責務: 1件のローカル物理削除要求を検査可能な履歴へ追加します。</remarks>
    /// <param name="sessionID">物理削除するセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    public void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
    {
        DeletedSessionIDs.Add(sessionID);
        storedSessions.RemoveAll(session =>
            session.Id == sessionID
            && string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal));
    }
}

/// <summary>同期ユースケースのCloudKit操作をメモリ上で再現します。</summary>
file sealed class SynchronizationTransferRepository : IConnectionSessionTransferRepository
{
    /// <summary>取得で返す検証済み転送です。</summary>
    private readonly List<VerifiedConnectionSessionTransfer> transfers;
    /// <summary>取得で返すRaw非包含セッション概要です。</summary>
    private readonly List<ConnectionSessionCloudMetadata> metadata;
    /// <summary>取得で返すMac受領証です。</summary>
    private List<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)> receipts;
    /// <summary>取得で返す削除済みセッションIDです。</summary>
    private readonly HashSet<ConnectionSessionID> deletedIDs;

    /// <summary>固定転送と受領証を保持します。</summary>
    /// <remarks>責務: CloudKit取得結果として返す同期データを初期化します。</remarks>
    /// <param name="transfers">取得で返す検証済み転送。</param>
    /// <param name="receipts">取得で返すMac受領証。</param>
    /// <param name="metadata">取得で返すRaw非包含セッション概要。</param>
    /// <param name="deletedIDs">取得で返す削除済みセッションID。</param>
    public SynchronizationTransferRepository(
        IReadOnlyList<VerifiedConnectionSessionTransfer> transfers,
        IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)> receipts,
        IReadOnlyList<ConnectionSessionCloudMetadata>? metadata = null,
        IReadOnlyList<ConnectionSessionID>? deletedIDs = null)
    {
        this.transfers = [.. transfers];
        this.metadata = [.. metadata ?? []];
        this.receipts = [.. receipts];
        this.deletedIDs = [.. deletedIDs ?? []];
    }

    /// <summary>公開されたMac受領証です。</summary>
    public List<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)> PublishedReceipts { get; } = [];
    /// <summary>CloudKitへ送信された接続セッションIDです。</summary>
    public List<ConnectionSessionID> UploadedSessionIDs { get; } = [];

    /// <summary>送信Payloadを後続端末が取得できる転送として記録します。</summary>
    /// <remarks>責務: 1件のCloudKit送信要求を検証済み転送と送信履歴へ変換します。</remarks>
    /// <param name="package">後続の取得へ保存する転送Payload。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>固定Digest。</returns>
    public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier)
    {
        UploadedSessionIDs.Add(package.SessionID);
        transfers.RemoveAll(transfer => transfer.Package.SessionID == package.SessionID);
        transfers.Add(new VerifiedConnectionSessionTransfer(package, "uploaded"));
        return Task.FromResult("uploaded");
    }

    /// <summary>固定の検証済み転送を返します。</summary>
    /// <remarks>責務: Mac同期へ注入済み転送Payloadを供給します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>初期化時に保持した検証済み転送。</returns>
    public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>(transfers.ToList());

    /// <summary>公開されたRaw非包含概要を後続端末が取得できる状態へ保存します。</summary>
    /// <remarks>責務: 1件の概要公開要求をセッションID単位のメモリ上Metadataレコードへ変換します。</remarks>
    /// <param name="item">保存するRaw非包含セッション概要。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task PublishMetadata(ConnectionSessionCloudMetadata item, string accountIdentifier)
    {
        metadata.RemoveAll(existing => existing.Session.Id == item.Session.Id);
        metadata.Add(item);
        return Task.CompletedTask;
    }

    /// <summary>メモリ上に保存したRaw非包含概要を返します。</summary>
    /// <remarks>責務: 概要取得要求を現在保存中のMetadata配列へ変換します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>現在保存中のRaw非包含概要。</returns>
    public Task<IReadOnlyList<ConnectionSessionCloudMetadata>> DownloadMetadata(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<ConnectionSessionCloudMetadata>>(metadata.ToList());

    /// <summary>公開されたMac受領証を記録します。</summary>
    /// <remarks>責務: Mac同期がCloudKitへ公開した受領証を検査可能にします。</remarks>
    /// <param name="receipt">公開されたMac受領証。</param>
    /// <param name="sessionID">対象セッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier)
    {
        PublishedReceipts.Add((sessionID, receipt));
        receipts.RemoveAll(existing =>
            existing.SessionID == sessionID
            && string.Equals(existing.Receipt.DeviceID, receipt.DeviceID, StringComparison.Ordinal));
        receipts.Add((sessionID, receipt));
        receipts = receipts.OrderByDescending(existing => existing.Receipt.ImportedAt).ToList();
        return Task.CompletedTask;
    }

    /// <summary>固定のMac受領証を返します。</summary>
    /// <remarks>責務: iPhone同期へ注入済み受領証順を供給します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>初期化時に保持したMac受領証。</returns>
    public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
        string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>(receipts.ToList());

    /// <summary>固定の削除済みセッションIDを返します。</summary>
    /// <remarks>責務: 同期ユースケースへ注入済み削除マーカーを供給します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>初期化時に保持した削除済みセッションID集合。</returns>
    public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
        Task.FromResult<IReadOnlySet<ConnectionSessionID>>(deletedIDs);

    /// <summary>この同期テストでは単体のCloudKit削除を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のセッション削除要求を副作用なしで満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) => Task.CompletedTask;

    /// <summary>このテストではCloudKit全削除を変更なしで受け付けます。</summary>
    /// <remarks>責務: テスト対象外のアカウント運転データ削除要求を満たします。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task DeleteAll(string accountIdentifier) => Task.CompletedTask;
}
