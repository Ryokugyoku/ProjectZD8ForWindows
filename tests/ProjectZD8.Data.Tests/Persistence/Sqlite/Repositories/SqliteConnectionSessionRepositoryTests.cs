using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>接続セッション・生ログの保存一式を検証します。</summary>
/// <remarks>
/// iOS版 <c>GRDBConnectionSessionRepositoryTests</c> の移植です。iOS版はGRDBが日時をミリ秒で保存するため一部のテストが丸めを前提にしますが、
/// Windows版は100ナノ秒（Ticks）で保存するので、ローカル値はそのまま往復します（テストの期待値は同じです）。
/// <c>testRawTransferAcceptsDateRepresentationAfterCloudCodecRoundTrip</c> のCloudKit JSONの往復部分は Data/Mapping の担当なので、
/// ミリ秒に丸めた日時の復元だけを確かめます。
/// </remarks>
public sealed class SqliteConnectionSessionRepositoryTests
{
    /// <summary>同じDBを全リポジトリで共有できることを確かめます。</summary>
    [Fact]
    public void SharedProductDatabaseAcceptsAllRepositories()
    {
        using var fixture = new ConnectionSessionStorageFixture();

        _ = new SqliteOBDPIDDefinitionRepository(fixture.Database);
        _ = new SqliteVehiclePIDCapabilityRepository(fixture.Database);
        _ = new SqliteMaintenanceRecordRepository(fixture.Database);
        _ = new SqliteVehicleRepository(fixture.Database);
    }

    /// <summary>保存と読込が車両と終了原因を保つことを確かめます。</summary>
    [Fact]
    public void SaveAndLoadRoundTripPreservesVehicleAndEndReason()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var vehicle = new VehicleProfile("TESTVIN", "ZD8");
        var session = new ConnectionSession(
            "account",
            startedAt: TestDates.Unix(100),
            vehicle: new ConnectionSessionVehicle(vehicle),
            acquisitionDevice: new ConnectionSessionAcquisitionDevice(ConnectionSessionAcquisitionPlatform.MacOS, "MacBook Pro"))
        {
            EndedAt = TestDates.Unix(200),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            StartingOdometerKilometers = 98_765.4,
            EndingOdometerKilometers = 98_767.9,
            DistanceSourceModelCode = "ZD8",
        };

        repository.Save(session);
        var loaded = repository.Sessions("account")[0];

        Assert.Equal(session, loaded);
        Assert.Equal(ConnectionSessionStatus.Completed, loaded.Status);
        Assert.Equal(session.AcquisitionDevice, loaded.AcquisitionDevice);
        Assert.Equal("ZD8", loaded.DistanceSourceModelCode);
        Assert.Equal(2.5, loaded.RecordedDistanceKilometers!.Value, 4);
        Assert.Empty(repository.Sessions("different-account"));
    }

    /// <summary>ユーザー操作の停止確認を保つことを確かめます。</summary>
    [Fact]
    public void SaveAndLoadPreservesUserInitiatedStopReview()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100))
        {
            EndedAt = TestDates.Unix(200),
            EndReason = ConnectionSessionEndReason.ConnectionLost,
            StopReviewDecision = ConnectionSessionStopReviewDecision.UserInitiated,
        };

        fixture.Repository.Save(session);
        var loaded = fixture.Repository.Sessions("account")[0];

        Assert.Equal(ConnectionSessionEndReason.ConnectionLost, loaded.EndReason);
        Assert.Equal(ConnectionSessionStopReviewDecision.UserInitiated, loaded.StopReviewDecision);
        Assert.Equal(ConnectionSessionStatus.Completed, loaded.Status);
    }

    /// <summary>生ログがセッション終了後も残り、車両に紐づくことを確かめます。</summary>
    [Fact]
    public void RawEntriesSurviveSessionFinalizationAndRemainVehicleScoped()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var vehicle = new VehicleProfile("TESTVIN-ZD8", "BRZ");
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100), vehicle: new ConnectionSessionVehicle(vehicle));
        repository.Save(session);

        repository.Append(Observation(101, 4_200_000, 0x0C, [0x1A, 0xF8]), session.Id);
        repository.Append(Observation(102, 5_100_000, 0x05, [0x85]), session.Id);
        session = session with { EndedAt = TestDates.Unix(110), EndReason = ConnectionSessionEndReason.UserDisconnected };
        repository.Save(session);

        var loaded = repository.Sessions("account")[0];
        var entries = repository.Entries(session.Id);

        Assert.Equal("TESTVIN-ZD8", loaded.Vehicle?.DisplayIdentifier);
        Assert.Equal(2, loaded.RawLogSummary.RecordCount);
        Assert.Equal(3, loaded.RawLogSummary.ByteCount);
        Assert.Equal(ConnectionSessionLocalRawState.Available, loaded.RawLogSummary.LocalState);
        Assert.Equal(ConnectionSessionCloudSyncState.Pending, loaded.RawLogSummary.CloudState);
        Assert.Equal([0L, 1L], entries.Select(entry => entry.Sequence));
        Assert.Equal(new byte[] { 0x1A, 0xF8 }, entries[0].Payload);
        Assert.Equal(new byte[] { 0x85 }, entries[1].Payload);
    }

    /// <summary>セッション一覧がローカル生ログの実件数を使うことを確かめます。</summary>
    [Fact]
    public void SessionsUseActualRawRowsForLocalSummary()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var session = new ConnectionSession("account");
        fixture.Repository.Save(session);
        fixture.Repository.Append(Observation(1, 1, 0x0C, [0x01, 0x02, 0x03]), session.Id);
        fixture.Database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            "UPDATE connection_sessions SET rawRecordCount = 99, rawByteCount = 999, localRawState = 'empty' WHERE id = $1",
            Key(session.Id)));

        var loaded = fixture.Repository.Sessions("account")[0];

        Assert.Equal(1, loaded.RawLogSummary.RecordCount);
        Assert.Equal(3, loaded.RawLogSummary.ByteCount);
        Assert.Equal(ConnectionSessionLocalRawState.Available, loaded.RawLogSummary.LocalState);
    }

    /// <summary>セッション削除が所有者のセッションと生ログだけを消すことを確かめます。</summary>
    [Fact]
    public void DeleteSessionRemovesOnlyOwnedSessionAndRawEntries()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var deleted = new ConnectionSession("account");
        var retained = new ConnectionSession("other");
        repository.Save(deleted);
        repository.Save(retained);
        repository.Append(Observation(1, 1, 0x0D, [0x20]), deleted.Id);

        Assert.ThrowsAny<Exception>(() => repository.DeleteSession(deleted.Id, "other"));
        repository.DeleteSession(deleted.Id, "account");

        Assert.Empty(repository.Sessions("account"));
        Assert.Empty(repository.Entries(deleted.Id));
        Assert.Equal([retained.Id], repository.Sessions("other").Select(session => session.Id));
        Assert.False(Directory.Exists(Path.Combine(fixture.LogDirectory, Key(deleted.Id))));
    }

    /// <summary>ローカル除去が概要と一致するMac受領証を残すことを確かめます。</summary>
    [Fact]
    public void LocalRemovalKeepsSummaryAndMatchingMacReceipt()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100));
        repository.Save(session);
        repository.Append(Observation(101, 2_000_000, 0x0D, [0x32]), session.Id);
        session = session with { EndedAt = TestDates.Unix(102), EndReason = ConnectionSessionEndReason.UserDisconnected };
        repository.Save(session);
        repository.MarkCloudUploaded(session.Id, "digest");
        var receipt = new ConnectionSessionMacImportReceipt("mac-installation", "Garage Mac", TestDates.Unix(103), "digest");
        repository.MarkMacImported(receipt, session.Id);

        repository.RemoveLocalEntries(session.Id);

        var loaded = repository.Sessions("account")[0];
        Assert.Equal(1, loaded.RawLogSummary.RecordCount);
        Assert.Equal(1, loaded.RawLogSummary.ByteCount);
        Assert.Equal(ConnectionSessionLocalRawState.Removed, loaded.RawLogSummary.LocalState);
        Assert.Equal("digest", loaded.RawLogSummary.ManifestDigest);
        Assert.Equal(receipt, loaded.RawLogSummary.MacImportReceipt);
        Assert.True(loaded.RawLogSummary.IsDurablyImportedByMac);
        Assert.Empty(repository.Entries(session.Id));
    }

    /// <summary>転送の取込が除去済みの生ログをセッションIDで復元することを確かめます。</summary>
    [Fact]
    public void RawTransferImportRestoresRemovedPayloadBySessionID()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100));
        repository.Save(session);
        repository.Append(Observation(101, 2_000_000, 0x0D, [0x32]), session.Id);
        session = session with { EndedAt = TestDates.Unix(102), EndReason = ConnectionSessionEndReason.UserDisconnected };
        repository.Save(session);
        var uploadedSession = repository.Sessions("account")[0];
        var uploadedEntries = repository.Entries(session.Id);
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(uploadedSession, uploadedEntries), "digest");
        repository.MarkCloudUploaded(session.Id, "digest");
        var receipt = new ConnectionSessionMacImportReceipt("mac-installation", "Garage Mac", TestDates.Unix(103), "digest");
        repository.MarkMacImported(receipt, session.Id);
        repository.RemoveLocalEntries(session.Id);

        repository.ImportVerifiedTransfer(transfer);

        var loaded = repository.Sessions("account")[0];
        Assert.Equal(ConnectionSessionLocalRawState.Available, loaded.RawLogSummary.LocalState);
        Assert.Equal(receipt, loaded.RawLogSummary.MacImportReceipt);
        Assert.Equal(uploadedEntries, repository.Entries(session.Id));

        repository.RemoveLocalEntries(session.Id);
        repository.ImportVerifiedTransfer(transfer);

        var repeatedlyLoaded = repository.Sessions("account")[0];
        Assert.Equal(ConnectionSessionLocalRawState.Available, repeatedlyLoaded.RawLogSummary.LocalState);
        Assert.Equal(receipt, repeatedlyLoaded.RawLogSummary.MacImportReceipt);
        Assert.Equal(uploadedEntries, repository.Entries(session.Id));
    }

    /// <summary>Rawなし転送の繰り返し取込がMac受領証を保つことを確かめます。</summary>
    [Fact]
    public void RepeatedMetadataOnlyTransferImportPreservesMacReceipt()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100))
        {
            EndedAt = TestDates.Unix(102),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        repository.Save(session);
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(session, []), "digest");
        repository.MarkCloudUploaded(session.Id, "digest");
        var receipt = new ConnectionSessionMacImportReceipt("mac-installation", "Garage Mac", TestDates.Unix(103), "digest");
        repository.MarkMacImported(receipt, session.Id);

        repository.ImportVerifiedTransfer(transfer);

        var loaded = repository.Sessions("account")[0];
        Assert.Equal(ConnectionSessionLocalRawState.Empty, loaded.RawLogSummary.LocalState);
        Assert.Equal(receipt, loaded.RawLogSummary.MacImportReceipt);
        Assert.Empty(repository.Entries(session.Id));
    }

    /// <summary>転送の取込がローカルの高精度なセッション日時を保つことを確かめます。</summary>
    [Fact]
    public void RawTransferPreservesHigherPrecisionLocalSessionMetadata()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100.123_456));
        repository.Save(session);
        repository.Append(Observation(101.123_456, 2_000_000, 0x0D, [0x32]), session.Id);
        session = session with { EndedAt = TestDates.Unix(102.123_456), EndReason = ConnectionSessionEndReason.UserDisconnected };
        repository.Save(session);
        var localSession = repository.Sessions("account")[0];
        var localEntries = repository.Entries(session.Id);
        repository.MarkCloudUploaded(session.Id, "same-manifest");
        var roundedSession = localSession with { EndedAt = TestDates.Unix(102.123) };
        var original = localEntries[0];
        var roundedEntry = new ConnectionSessionRawLogEntry(
            original.Sequence,
            TestDates.Unix(101.123),
            original.BatchElapsedNanoseconds,
            original.Service,
            original.Pid,
            original.Payload);
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(roundedSession, [roundedEntry]), "same-manifest");

        repository.ImportVerifiedTransfer(transfer);

        Assert.Equal([roundedEntry], repository.Entries(session.Id));
        Assert.Equal(localSession.EndedAt, repository.Sessions("account")[0].EndedAt);
    }

    /// <summary>CloudKit転送のミリ秒精度の日時を復元できることを確かめます。</summary>
    [Fact]
    public void RawTransferAcceptsMillisecondDateRepresentation()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(1_784_982_574.817))
        {
            EndedAt = TestDates.Unix(1_784_982_588.797),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        repository.Save(session);
        var entry = new ConnectionSessionRawLogEntry(0, DateTimeOffset.FromUnixTimeMilliseconds(1_784_982_575_123), 22_000, 0x01, 0x04, [0x45]);

        repository.RestoreVerifiedTransfer(new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(session, [entry]), "digest"));

        var stored = repository.Entries(session.Id)[0];
        Assert.Equal(entry, stored);
    }

    /// <summary>概要の繰り返し取込が欠けた車両と取得端末を補うことを確かめます。</summary>
    [Fact]
    public void RepeatedTransferImportRepairsMissingVehicleAndAcquisitionDevice()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var vehicle = new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8");
        var device = new ConnectionSessionAcquisitionDevice(ConnectionSessionAcquisitionPlatform.IPhone, "iPhone 17 Pro");
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100), vehicle: vehicle, acquisitionDevice: device)
        {
            EndedAt = TestDates.Unix(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        var metadata = new ConnectionSessionCloudMetadata(session, "digest");
        repository.ImportCloudMetadata(metadata);
        fixture.Database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            "UPDATE connection_sessions SET vehicleID = NULL, vehicleName = NULL, vehicleDisplayIdentifier = NULL, acquisitionPlatform = NULL, acquisitionDeviceName = NULL WHERE id = $1",
            Key(session.Id)));

        repository.ImportCloudMetadata(metadata);

        var repaired = repository.Sessions("account")[0];
        Assert.Equal(vehicle, repaired.Vehicle);
        Assert.Equal(device, repaired.AcquisitionDevice);
        Assert.Equal(ConnectionSessionCloudSyncState.Uploaded, repaired.RawLogSummary.CloudState);
        Assert.Equal("digest", repaired.RawLogSummary.ManifestDigest);
    }

    /// <summary>同じ内容の転送が古いManifestを直し、生ログは置き換えないことを確かめます。</summary>
    [Fact]
    public void EquivalentTransferReconcilesStaleManifestWithoutReplacingRawEntries()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100));
        repository.Save(session);
        repository.Append(Observation(101, 2_000_000, 0x0D, [0x32]), session.Id);
        session = session with { EndedAt = TestDates.Unix(102), EndReason = ConnectionSessionEndReason.UserDisconnected };
        repository.Save(session);
        var transferredSession = repository.Sessions("account")[0];
        var entries = repository.Entries(session.Id);
        repository.MarkCloudUploaded(session.Id, "stale-local-manifest");
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(transferredSession, entries), "verified-remote-manifest");

        repository.ImportVerifiedTransfer(transfer);

        var reconciled = repository.Sessions("account")[0];
        Assert.Equal("verified-remote-manifest", reconciled.RawLogSummary.ManifestDigest);
        Assert.Equal(ConnectionSessionCloudSyncState.Uploaded, reconciled.RawLogSummary.CloudState);
        Assert.Equal(entries, repository.Entries(session.Id));
    }

    /// <summary>転送の取込がセッションの内容を上書きしないことを確かめます。</summary>
    [Fact]
    public void RawTransferDoesNotOverwriteSessionMetadata()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account", startedAt: TestDates.Unix(100))
        {
            EndedAt = TestDates.Unix(102),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        repository.Save(session);
        repository.MarkCloudUploaded(session.Id, "local-manifest");
        var conflicting = session with { EndReason = ConnectionSessionEndReason.ConnectionLost };
        var transfer = new VerifiedConnectionSessionTransfer(new ConnectionSessionTransferPackage(conflicting, []), "remote-manifest");

        repository.ImportVerifiedTransfer(transfer);

        var restored = repository.Sessions("account")[0];
        Assert.Equal(ConnectionSessionEndReason.UserDisconnected, restored.EndReason);
        Assert.Equal("remote-manifest", restored.RawLogSummary.ManifestDigest);
    }

    /// <summary>車両別の抽出が他の車両を含まないことを確かめます。</summary>
    [Fact]
    public void VehicleScopedEntriesExcludeOtherVehicles()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var firstVehicle = new VehicleProfile("FIRST-VIN", "First BRZ");
        var secondVehicle = new VehicleProfile("SECOND-VIN", "Second BRZ");
        var firstSession = new ConnectionSession("account", startedAt: TestDates.Unix(100), vehicle: new ConnectionSessionVehicle(firstVehicle));
        var secondSession = new ConnectionSession("account", startedAt: TestDates.Unix(200), vehicle: new ConnectionSessionVehicle(secondVehicle));
        repository.Save(firstSession);
        repository.Save(secondSession);
        repository.Append(Observation(101, 1_000_000, 0x0C, [0x01]), firstSession.Id);
        repository.Append(Observation(201, 2_000_000, 0x0D, [0x02]), secondSession.Id);
        repository.Save(firstSession with { EndedAt = TestDates.Unix(110), EndReason = ConnectionSessionEndReason.UserDisconnected });
        repository.Save(secondSession with { EndedAt = TestDates.Unix(210), EndReason = ConnectionSessionEndReason.UserDisconnected });

        var extracted = repository.Entries(firstVehicle.Id, "account");

        Assert.Equal([firstVehicle.Id], extracted.Select(entry => entry.VehicleID));
        Assert.Equal([firstSession.Id], extracted.Select(entry => entry.SessionID));
        Assert.Equal([TestDates.Unix(100)], extracted.Select(entry => entry.SessionStartedAt));
        Assert.Equal(new byte[] { 0x01 }, extracted[0].Entry.Payload);
        Assert.Empty(repository.Entries(firstVehicle.Id, "other-account"));
    }

    /// <summary>アカウント単位の削除が履歴と生ログを消すことを確かめます。</summary>
    [Fact]
    public void DeleteSessionsRemovesAccountHistoryAndRawEntries()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var deletedSession = new ConnectionSession("delete-account", startedAt: TestDates.Unix(100));
        var retainedSession = new ConnectionSession("retain-account", startedAt: TestDates.Unix(200));
        repository.Save(deletedSession);
        repository.Save(retainedSession);
        repository.Append(Observation(101, 1_000_000, 0x0C, [0x12, 0x34]), deletedSession.Id);
        repository.Append(Observation(201, 2_000_000, 0x0D, [0x56]), retainedSession.Id);

        repository.DeleteSessions("delete-account");

        Assert.Empty(repository.Sessions("delete-account"));
        Assert.Empty(repository.Entries(deletedSession.Id));
        Assert.Equal([retainedSession.Id], repository.Sessions("retain-account").Select(session => session.Id));
        Assert.Equal(new byte[] { 0x56 }, repository.Entries(retainedSession.Id)[0].Payload);
    }

    /// <summary>終了済みセッションへの追記と、未登録セッションへの追記を拒否することを確かめます。</summary>
    [Fact]
    public void AppendRejectsEndedOrMissingSession()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account") { EndedAt = TestDates.Unix(1), EndReason = ConnectionSessionEndReason.UserDisconnected };
        repository.Save(session);

        Assert.ThrowsAny<Exception>(() => repository.Append(Observation(2, 1, 0x0C, [0x01]), session.Id));
        Assert.ThrowsAny<Exception>(() => repository.Append(Observation(2, 1, 0x0C, [0x01]), ConnectionSessionID.New()));
        Assert.Empty(repository.Entries(session.Id));
    }

    /// <summary>ページ読みと件数がsequence順で動くことを確かめます。</summary>
    [Fact]
    public void PagedEntriesAndCountFollowSequence()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var repository = fixture.Repository;
        var session = new ConnectionSession("account");
        repository.Save(session);
        for (var index = 0; index < 10; index++)
        {
            repository.Append(Observation(index, 1, 0x0C, [(byte)index]), session.Id);
        }

        Assert.Equal(10, repository.EntryCount(session.Id));
        Assert.Equal([0L, 1L, 2L], repository.Entries(session.Id, null, 3).Select(entry => entry.Sequence));
        Assert.Equal([3L, 4L, 5L, 6L], repository.Entries(session.Id, 2, 4).Select(entry => entry.Sequence));
        Assert.Empty(repository.Entries(session.Id, 9, 5));
        Assert.Empty(repository.Entries(session.Id, null, 0));
    }

    /// <summary>テスト用の未デコード応答を作ります。</summary>
    /// <remarks>責務: 観測時刻・経過時間・PID・データを指定してService 01の応答を作ります。</remarks>
    /// <param name="seconds">Unix epochからの観測秒。</param>
    /// <param name="elapsed">batch経過ナノ秒。</param>
    /// <param name="pid">PID。</param>
    /// <param name="payload">応答データ。</param>
    /// <returns>未デコード応答。</returns>
    internal static OBDRawResponseObservation Observation(double seconds, ulong elapsed, byte pid, byte[] payload) =>
        new(TestDates.Unix(seconds), elapsed, new OBDPIDRequest(0x01, pid), payload);

    /// <summary>セッションIDのDBキーを返します。</summary>
    /// <remarks>責務: 小文字UUIDへ変換します。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>DBキー。</returns>
    internal static string Key(ConnectionSessionID sessionID) => sessionID.RawValue.ToString("D");
}
