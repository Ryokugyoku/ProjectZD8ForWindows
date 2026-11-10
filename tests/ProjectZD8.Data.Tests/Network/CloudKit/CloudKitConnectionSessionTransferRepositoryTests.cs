using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Network.CloudKit;

/// <summary>
/// CloudKit の接続セッション転送リポジトリの、レコード名・フィールド・検証・削除を、メモリ上の CloudKit で確かめます。
/// </summary>
public sealed class CloudKitConnectionSessionTransferRepositoryTests
{
    /// <summary>テストのアカウントです。</summary>
    private const string Account = "001234.abcdef";

    /// <summary>テストのセッション ID です。</summary>
    private static readonly ConnectionSessionID SessionID = new(Guid.Parse("9F0E6B3A-1C2D-4E5F-8A9B-0C1D2E3F4A5B"));

    /// <summary>
    /// 生ログを送ると iOS と同じ名前とフィールドのレコードになり、読み戻すと要約値が一致することを確かめます。
    /// </summary>
    /// <remarks>責務: payloadAsset の形式バージョン 2 の往復を確かめます。</remarks>
    [Fact]
    public async Task UploadThenDownloadTransferRoundTrips()
    {
        var database = new InMemoryCloudKitDatabase();
        var repository = new CloudKitConnectionSessionTransferRepository(database);
        var package = new ConnectionSessionTransferPackage(
            SessionID,
            Account,
            [new ConnectionSessionRawLogEntry(1, DateTimeOffset.FromUnixTimeMilliseconds(1000), 5, 1, 0x0C, [0x1A, 0xF8])]);
        var progress = new List<double>();

        var digest = await repository.Upload(package, Account, progress.Add);
        var downloaded = await repository.DownloadTransfer(SessionID, Account);

        var fingerprint = CloudKitAccountFingerprint.Of(Account);
        var record = database.Records[$"raw-session-{fingerprint}-9f0e6b3a-1c2d-4e5f-8a9b-0c1d2e3f4a5b"];
        Assert.Equal("ConnectionSessionRawLog", record.RecordType);
        Assert.Equal(fingerprint, record.GetString("accountFingerprint"));
        Assert.Equal("9f0e6b3a-1c2d-4e5f-8a9b-0c1d2e3f4a5b", record.GetString("sessionID"));
        Assert.Equal(2, record.GetInt64("payloadFormatVersion"));
        Assert.Equal(digest, record.GetString("manifestDigest"));
        Assert.Equal(64, digest.Length);
        Assert.Equal(package, downloaded.Package);
        Assert.Equal(digest, downloaded.ManifestDigest);
        Assert.Equal(1, progress[^1]);
        Assert.Equal(new Dictionary<ConnectionSessionID, string> { [SessionID] = digest }, await repository.DownloadTransferManifests(Account));
    }

    /// <summary>
    /// 資産が書き換えられて要約値と合わないときは、整合性のエラーになることを確かめます。
    /// </summary>
    /// <remarks>責務: SHA-256 の検証を確かめます。</remarks>
    [Fact]
    public async Task TamperedAssetIsIntegrityConflict()
    {
        var database = new InMemoryCloudKitDatabase();
        var repository = new CloudKitConnectionSessionTransferRepository(database);
        await repository.Upload(new ConnectionSessionTransferPackage(SessionID, Account, []), Account);
        var key = database.Assets.Keys.Single();
        database.Assets[key] = [.. database.Assets[key], (byte)' '];

        var error = await Assert.ThrowsAsync<ConnectionSessionRepositoryException>(() => repository.DownloadTransfers(Account));

        Assert.Equal(ConnectionSessionRepositoryError.IntegrityConflict, error.Reason);
    }

    /// <summary>
    /// セッション概要を保存して読み戻せること、終わっていないセッションは保存できないことを確かめます。
    /// </summary>
    /// <remarks>責務: ConnectionSessionMetadata の往復と事前の検証を確かめます。</remarks>
    [Fact]
    public async Task PublishMetadataRoundTripsAndValidatesState()
    {
        var database = new InMemoryCloudKitDatabase();
        var repository = new CloudKitConnectionSessionTransferRepository(database);
        var open = new ConnectionSession(Account, SessionID, DateTimeOffset.FromUnixTimeSeconds(100));
        var ended = open with { EndedAt = DateTimeOffset.FromUnixTimeSeconds(200), EndReason = ConnectionSessionEndReason.UserDisconnected };

        var invalid = await Assert.ThrowsAsync<ConnectionSessionRepositoryException>(
            () => repository.PublishMetadata(new ConnectionSessionCloudMetadata(open, "d"), Account));
        await repository.PublishMetadata(new ConnectionSessionCloudMetadata(ended, "d"), Account);
        var metadata = await repository.DownloadMetadata(Account);

        Assert.Equal(ConnectionSessionRepositoryError.InvalidState, invalid.Reason);
        Assert.Equal(new ConnectionSessionCloudMetadata(ended, "d"), Assert.Single(metadata));
        var record = database.Records[CloudKitConnectionSessionTransferRepository.MetadataRecordName(SessionID, Account)];
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), record.GetTimestamp("endedAt"));
        Assert.StartsWith("session-metadata-", record.RecordName);
    }

    /// <summary>
    /// 受領証が、端末の識別子の SHA-256 を含む名前で保存され、新しい順に返ることを確かめます。
    /// </summary>
    /// <remarks>責務: ConnectionSessionMacReceipt の往復と並び順を確かめます。</remarks>
    [Fact]
    public async Task MacReceiptsRoundTripNewestFirst()
    {
        var database = new InMemoryCloudKitDatabase();
        var repository = new CloudKitConnectionSessionTransferRepository(database);
        var older = new ConnectionSessionMacImportReceipt("device-a", "PC A", DateTimeOffset.FromUnixTimeSeconds(10), "d1");
        var newer = new ConnectionSessionMacImportReceipt("device-b", "PC B", DateTimeOffset.FromUnixTimeSeconds(20), "d1");

        await repository.PublishMacReceipt(older, SessionID, Account);
        await repository.PublishMacReceipt(newer, SessionID, Account);
        var receipts = await repository.DownloadMacReceipts(Account);

        Assert.Equal([newer, older], receipts.Select(pair => pair.Receipt));
        Assert.Contains(
            $"mac-receipt-{CloudKitAccountFingerprint.Of(Account)}-9f0e6b3a-1c2d-4e5f-8a9b-0c1d2e3f4a5b-{CloudKitAccountFingerprint.Of("device-a")}",
            database.Records.Keys);
    }

    /// <summary>
    /// セッションの削除が、削除の印を残し、生ログ・概要・受領証を消すことを確かめます。
    /// </summary>
    /// <remarks>責務: ConnectionSessionDeletion と関連レコードの削除を確かめます。</remarks>
    [Fact]
    public async Task DeleteSessionLeavesMarkerAndRemovesRecords()
    {
        var database = new InMemoryCloudKitDatabase();
        var repository = new CloudKitConnectionSessionTransferRepository(database);
        await repository.Upload(new ConnectionSessionTransferPackage(SessionID, Account, []), Account);
        await repository.PublishMacReceipt(new ConnectionSessionMacImportReceipt("d", "PC", DateTimeOffset.UnixEpoch, "x"), SessionID, Account);

        await repository.DeleteSession(SessionID, Account);

        Assert.Equal([SessionID], await repository.DeletedSessionIDs(Account));
        Assert.Equal([CloudKitConnectionSessionTransferRepository.DeletionRecordName(SessionID, Account)], database.Records.Keys);

        await repository.DeleteAll(Account);
        Assert.Empty(database.Records);
    }

    /// <summary>
    /// ほかのアカウントのレコードは読まないことを確かめます。
    /// </summary>
    /// <remarks>責務: accountFingerprint での絞り込みを確かめます。</remarks>
    [Fact]
    public async Task OtherAccountsAreNotVisible()
    {
        var database = new InMemoryCloudKitDatabase();
        var repository = new CloudKitConnectionSessionTransferRepository(database);
        await repository.Upload(new ConnectionSessionTransferPackage(SessionID, "other", []), "other");

        Assert.Empty(await repository.DownloadTransfers(Account));
    }

    /// <summary>
    /// ローカルモードでは取得が空・削除は何もせず、保存は Unavailable、サインインしていなければすべて Unavailable になることを確かめます。
    /// </summary>
    /// <remarks>責務: iOS の「利用できない」経路にあたる振る舞いを確かめます。</remarks>
    [Fact]
    public async Task UnavailablePathsBehaveLikeLocalOnly()
    {
        var database = new InMemoryCloudKitDatabase { Availability = CloudKitAvailability.NotConfigured };
        var repository = new CloudKitConnectionSessionTransferRepository(database);

        Assert.Empty(await repository.DownloadMetadata(Account));
        Assert.Empty(await repository.DownloadTransfers(Account));
        Assert.Empty(await repository.DeletedSessionIDs(Account));
        await repository.DeleteAll(Account);
        var upload = await Assert.ThrowsAsync<ConnectionSessionRepositoryException>(
            () => repository.Upload(new ConnectionSessionTransferPackage(SessionID, Account, []), Account));
        Assert.Equal(ConnectionSessionRepositoryError.Unavailable, upload.Reason);

        database.Availability = CloudKitAvailability.SignedOut;
        var download = await Assert.ThrowsAsync<ConnectionSessionRepositoryException>(() => repository.DownloadMetadata(Account));
        Assert.Equal(ConnectionSessionRepositoryError.Unavailable, download.Reason);
    }
}
