using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>取得manifestとRaw境界の保存を検証します。</summary>
/// <remarks>
/// iOS版 <c>GRDBConnectionSessionAcquisitionRepositoryTests</c> の移植です。
/// <c>testUnrepresentableTimestampMapsToUnavailable</c>（Dateの無限大）は、C#の日時に無限大がないため対象外です。
/// </remarks>
public sealed class SqliteConnectionSessionAcquisitionRepositoryTests
{
    /// <summary>1件目の要求（rpm）です。</summary>
    private static readonly OBDPIDRequest FirstRequest = new(1, 12);
    /// <summary>2件目の要求（水温）です。</summary>
    private static readonly OBDPIDRequest SecondRequest = new(1, 5);
    /// <summary>3件目の要求です。</summary>
    private static readonly OBDPIDRequest ThirdRequest = new(1, 17);
    /// <summary>テスト用のセッションIDです。</summary>
    private static readonly ConnectionSessionID SessionID = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    /// <summary>完全なcanonical manifestと要求順が往復することを確かめます。</summary>
    [Fact]
    public void RoundTripPreservesCompleteCanonicalManifestAndOrderedPIDs()
    {
        using var fixture = MakeFixture();
        var repository = fixture.Repository;

        repository.SaveStartOnce(MakeManifest(definitionsReversed: true), TestDates.UnixTicks(1_001_234_564), SessionID);

        var stored = repository.Manifest(SessionID);
        Assert.Equal(11, stored.ManifestVersion);
        Assert.Equal("fixture-marketing", stored.ApplicationVersion?.MarketingVersion);
        Assert.Equal("fixture-build", stored.ApplicationVersion?.BuildVersion);
        Assert.Equal(31, stored.SchemaContractVersion);
        Assert.Equal(21, stored.PollingPolicyVersion);
        Assert.Equal(ConnectionSessionAcquisitionPlatform.IPad, stored.AcquisitionPlatform);
        Assert.Equal(41, stored.ModelInputManifestVersion);
        Assert.Equal([SecondRequest, FirstRequest], stored.OrderedRequestedPIDs!.Requests);
        Assert.Equal([SecondRequest, FirstRequest], stored.PidDefinitions.Select(definition => definition.Request));
        Assert.Equal(AcquisitionPIDCapabilitySupport.Indeterminate, stored.PidDefinitions[0].CapabilitySupport);
        Assert.Equal(true, stored.PidDefinitions[0].IsCollectionEnabled);
        Assert.Equal(-40, stored.PidDefinitions[0].ValidityRange?.Minimum);
        Assert.Equal(215, stored.PidDefinitions[0].ValidityRange?.Maximum);
        Assert.Equal(AcquisitionPIDCapabilitySupport.Supported, stored.PidDefinitions[1].CapabilitySupport);
        Assert.Equal(false, stored.PidDefinitions[1].IsCollectionEnabled);
        Assert.Equal(AcquisitionPIDValidityRange.NotDeclared, stored.PidDefinitions[1].ValidityRange);
        var started = Assert.IsType<AcquisitionRawBoundaryEvidence.Started>(repository.BoundaryEvidence(SessionID)[0]);
        Assert.Equal(TestDates.UnixTicks(1_001_234_560), started.At);
    }

    /// <summary>exact retryが定義の配列順を無視し、microsecond未満の差を同じとみなすことを確かめます。</summary>
    [Fact]
    public void ExactRetryIgnoresDefinitionArrayOrderAndDoesNotTrustCallerDigest()
    {
        using var fixture = MakeFixture(manifestDigest: "caller-digest-a");
        fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.UnixTicks(1_000_000_004), SessionID);

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(() =>
            fixture.Repository.SaveStartOnce(MakeManifest(definitionsReversed: true), TestDates.UnixTicks(1_000_000_001), SessionID));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.Duplicate, error.Reason);
    }

    /// <summary>意味の違いはすべて競合になることを確かめます。</summary>
    [Fact]
    public void SemanticDifferencesAreConflicts()
    {
        using var fixture = MakeFixture();
        var startedAt = TestDates.Unix(100);
        fixture.Repository.SaveStartOnce(MakeManifest(), startedAt, SessionID);

        ConnectionSessionAcquisitionManifest[] conflicts =
        [
            MakeManifest(manifestVersion: 12),
            MakeManifest(marketingVersion: "different"),
            MakeManifest(buildVersion: "different"),
            MakeManifest(schemaContractVersion: 32),
            MakeManifest(pollingPolicyVersion: 22),
            MakeManifest(platform: ConnectionSessionAcquisitionPlatform.MacOS),
            MakeManifest(modelInputManifestVersion: 42),
            MakeManifest(secondSupport: AcquisitionPIDCapabilitySupport.Unsupported),
            MakeManifest(secondEnabled: false),
            MakeManifest(secondRevision: 9),
            MakeManifest(secondRequiredByteCount: 2),
            MakeManifest(secondCanonicalizationVersion: 53),
            MakeManifest(secondFormula: "A + 1"),
            MakeManifest(secondUnit: "kelvin"),
            MakeManifest(secondMinimum: -39),
            MakeManifest(includeThirdDefinition: true),
            MakeManifest(includeSecondDefinition: false, orderedRequests: [FirstRequest]),
            MakeManifest(orderedRequests: [FirstRequest, SecondRequest]),
        ];
        foreach (var manifest in conflicts)
        {
            AssertReason(ConnectionSessionAcquisitionRepositoryError.Conflict, () => fixture.Repository.SaveStartOnce(manifest, startedAt, SessionID));
        }
        AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Conflict,
            () => fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.UnixTicks(1_000_000_020), SessionID));
        Assert.Equal(MakeManifest(definitionsReversed: true), fixture.Repository.Manifest(SessionID));
    }

    /// <summary>終了境界の追記がDomainの全結果を区別することを確かめます。</summary>
    [Fact]
    public void AppendEndDistinguishesAllDomainOutcomes()
    {
        using (var missing = MakeFixture())
        {
            AssertReason(
                ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing,
                () => missing.Repository.AppendEnd(TestDates.Unix(200), ConnectionSessionEndReason.ConnectionLost, SessionID));
        }

        using var fixture = MakeFixture();
        var repository = fixture.Repository;
        repository.SaveStartOnce(MakeManifest(), TestDates.UnixTicks(1_001_234_564), SessionID);
        AssertReason(
            ConnectionSessionAcquisitionRepositoryError.EndBeforeStart,
            () => repository.AppendEnd(TestDates.UnixTicks(1_001_234_554), ConnectionSessionEndReason.ConnectionLost, SessionID));

        repository.AppendEnd(TestDates.UnixTicks(2_006_543_214), ConnectionSessionEndReason.UserDisconnected, SessionID);
        AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Duplicate,
            () => repository.AppendEnd(TestDates.UnixTicks(2_006_543_211), ConnectionSessionEndReason.UserDisconnected, SessionID));
        AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Conflict,
            () => repository.AppendEnd(TestDates.Unix(50), ConnectionSessionEndReason.ConnectionLost, SessionID));
        Assert.Equal(MakeManifest(definitionsReversed: true), repository.Manifest(SessionID));
        Assert.Equal(2, repository.BoundaryEvidence(SessionID).Count);
    }

    /// <summary>開始保存の各段階の失敗がすべてを取り消すことを確かめます。</summary>
    [Theory]
    [InlineData("manifest", "BEFORE INSERT ON connection_session_acquisition_manifests")]
    [InlineData("definition", "BEFORE INSERT ON connection_session_acquisition_pid_definitions")]
    [InlineData("ordered", "BEFORE INSERT ON connection_session_acquisition_ordered_pids")]
    [InlineData("started", "BEFORE INSERT ON connection_session_acquisition_raw_boundaries WHEN NEW.eventKind = 'started'")]
    [InlineData("seal", "BEFORE UPDATE ON connection_session_acquisition_manifests WHEN NEW.isSealed = 1")]
    [InlineData("readback", "AFTER UPDATE ON connection_session_acquisition_manifests WHEN NEW.isSealed = 1")]
    public void EveryStartPersistenceStageFailureRollsBack(string name, string timing)
    {
        using var fixture = MakeFixture();
        var body = name == "readback"
            ? "DELETE FROM connection_session_acquisition_manifests WHERE sessionID = NEW.sessionID;"
            : "SELECT RAISE(ABORT, 'injected failure');";
        Execute(fixture.Database, $"CREATE TRIGGER injected_{name} {timing} BEGIN {body} END;");

        AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Unavailable,
            () => fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.Unix(100), SessionID));
        AssertAcquisitionTablesAreEmpty(fixture.Database);
    }

    /// <summary>DBの制約が変更と不正な追記を拒否することを確かめます。</summary>
    [Fact]
    public void DatabaseConstraintsRejectMutationAndInvalidAppend()
    {
        using var fixture = MakeFixture();
        fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.Unix(100), SessionID);
        var key = Key(SessionID);
        var database = fixture.Database;

        Assert.ThrowsAny<Exception>(() => Execute(database, "UPDATE connection_session_acquisition_manifests SET pollingPolicyVersion = 99 WHERE sessionID = $1", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "UPDATE connection_session_acquisition_pid_definitions SET unit = 'x' WHERE sessionID = $1", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "UPDATE connection_session_acquisition_ordered_pids SET ordinal = 5 WHERE sessionID = $1 AND ordinal = 0", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "UPDATE connection_session_acquisition_raw_boundaries SET occurredAtMicroseconds = 1 WHERE sessionID = $1", key));
        Assert.ThrowsAny<Exception>(() => Execute(
            database,
            """
            INSERT INTO connection_session_acquisition_pid_definitions
                (sessionID, service, pid, capabilitySupport, isCollectionEnabled, definitionRevision, requiredByteCount,
                 formulaCanonicalizationVersion, formulaExpression, unit, validityRangeKind, minimumValue, maximumValue)
            VALUES ($1, 1, 13, 'supported', 1, 1, 1, 1, 'A', 'raw', 'notDeclared', NULL, NULL)
            """,
            key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_ordered_pids VALUES ($1, 2, 1, 12)", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_raw_boundaries VALUES ($1, 'started', 100000000, NULL)", key));
        Execute(database, "INSERT INTO connection_session_acquisition_raw_boundaries VALUES ($1, 'ended', 200000000, 'userDisconnected')", key);
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_raw_boundaries VALUES ($1, 'ended', 200000000, 'userDisconnected')", key));
    }

    /// <summary>親セッションの削除が取得証拠の全表へ連鎖することを確かめます。</summary>
    [Fact]
    public void ParentSessionDeletionCascadesAllAcquisitionTables()
    {
        using var fixture = MakeFixture();
        fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.Unix(100), SessionID);

        fixture.Repository.DeleteSession(SessionID, "fixture-account");

        AssertAcquisitionTablesAreEmpty(fixture.Database);
    }

    /// <summary>要求順の欠番と重複がseal前に拒否されることを確かめます。</summary>
    [Fact]
    public void OrdinalGapAndDuplicatesAreRejectedBeforeSeal()
    {
        using var fixture = MakeFixture();
        var key = Key(SessionID);
        var database = fixture.Database;
        Execute(database, "INSERT INTO connection_session_acquisition_manifests VALUES ($1, 1, 'fixture', '1', 1, 1, 'macOS', 1, 0)", key);
        Execute(database, "INSERT INTO connection_session_acquisition_pid_definitions VALUES ($1, 1, 12, 'supported', 1, 1, 2, 1, 'A', 'rpm', 'notDeclared', NULL, NULL)", key);
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_pid_definitions VALUES ($1, 1, 13, 'supported', 1, 1, 1, 1, 'A', 'unit', 'inclusive', NULL, NULL)", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_pid_definitions VALUES ($1, 1, 14, 'supported', 1, 1, 1, 1, 'A', 'unit', 'notDeclared', 0, 1)", key));
        Execute(database, "INSERT INTO connection_session_acquisition_ordered_pids VALUES ($1, 1, 1, 12)", key);
        Execute(database, "INSERT INTO connection_session_acquisition_raw_boundaries VALUES ($1, 'started', 100000000, NULL)", key);
        Assert.ThrowsAny<Exception>(() => Execute(database, "UPDATE connection_session_acquisition_manifests SET isSealed = 1 WHERE sessionID = $1", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_ordered_pids VALUES ($1, 1, 1, 12)", key));
        Assert.ThrowsAny<Exception>(() => Execute(database, "INSERT INTO connection_session_acquisition_ordered_pids VALUES ($1, 2, 1, 12)", key));
    }

    /// <summary>親セッションがない場合は部分行を残さず利用不能になることを確かめます。</summary>
    [Fact]
    public void MissingParentMapsToUnavailableWithoutPartialRows()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var missingID = new ConnectionSessionID(Guid.Parse("00000000-0000-0000-0000-000000000009"));

        AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Unavailable,
            () => fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.Unix(100), missingID));
        AssertAcquisitionTablesAreEmpty(fixture.Database);
    }

    /// <summary>取得証拠のないセッションはnotFoundになることを確かめます。</summary>
    [Fact]
    public void LegacySessionWithoutEvidenceReadsAsNotFound()
    {
        using var fixture = MakeFixture();

        AssertReason(ConnectionSessionAcquisitionRepositoryError.NotFound, () => fixture.Repository.Manifest(SessionID));
        Assert.Empty(fixture.Repository.BoundaryEvidence(SessionID));
    }

    /// <summary>終了境界だけが残る破損は利用不能になることを確かめます。</summary>
    [Fact]
    public void CorruptEndedOnlyBoundaryMapsToUnavailable()
    {
        using var fixture = MakeFixture();
        fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.Unix(100), SessionID);
        fixture.Repository.AppendEnd(TestDates.Unix(200), ConnectionSessionEndReason.UserDisconnected, SessionID);
        Execute(fixture.Database, "DELETE FROM connection_session_acquisition_raw_boundaries WHERE sessionID = $1 AND eventKind = 'started'", Key(SessionID));

        AssertReason(ConnectionSessionAcquisitionRepositoryError.Unavailable, () => fixture.Repository.Manifest(SessionID));
        AssertReason(ConnectionSessionAcquisitionRepositoryError.Unavailable, () => fixture.Repository.BoundaryEvidence(SessionID));
    }

    /// <summary>同時のexact retryが部分状態を作らないことを確かめます。</summary>
    [Fact]
    public async Task ConcurrentExactRetryDoesNotCreatePartialState()
    {
        using var fixture = MakeFixture();
        var manifest = MakeManifest();
        var startedAt = TestDates.Unix(100);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try
            {
                fixture.Repository.SaveStartOnce(manifest, startedAt, SessionID);
                return (ConnectionSessionAcquisitionRepositoryError?)null;
            }
            catch (ConnectionSessionAcquisitionRepositoryException error)
            {
                return error.Reason;
            }
        })));

        Assert.Equal([ConnectionSessionAcquisitionRepositoryError.Duplicate], outcomes.Where(outcome => outcome is not null).Select(outcome => outcome!.Value));
        Assert.Single(fixture.Repository.BoundaryEvidence(SessionID));
        Assert.Equal(1L, fixture.Database.Read(connection => SqliteDatabase.Scalar(connection, null, "SELECT COUNT(*) FROM connection_session_acquisition_manifests")));
        Assert.Equal(1L, fixture.Database.Read(connection => SqliteDatabase.Scalar(connection, null, "SELECT COUNT(*) FROM connection_session_acquisition_raw_boundaries")));
    }

    /// <summary>テスト用のセッションを保存した保存一式を作ります。</summary>
    /// <remarks>責務: 固定IDの未終了セッションを1件保存します。</remarks>
    /// <param name="manifestDigest">セッションへ付けるDigest（呼出元のDigestを信頼しないことの確認用）。</param>
    /// <returns>保存一式。</returns>
    private static ConnectionSessionStorageFixture MakeFixture(string? manifestDigest = null)
    {
        var fixture = new ConnectionSessionStorageFixture();
        var session = new ConnectionSession("fixture-account", id: SessionID, startedAt: TestDates.Unix(90));
        session = session with { RawLogSummary = session.RawLogSummary with { ManifestDigest = manifestDigest } };
        fixture.Repository.Save(session);
        return fixture;
    }

    /// <summary>テスト用のmanifestを作ります。</summary>
    /// <remarks>責務: 引数で1項目だけ変えたmanifestを作ります。</remarks>
    /// <returns>manifest。</returns>
    internal static ConnectionSessionAcquisitionManifest MakeManifest(
        int manifestVersion = 11,
        string marketingVersion = "fixture-marketing",
        string buildVersion = "fixture-build",
        int schemaContractVersion = 31,
        int pollingPolicyVersion = 21,
        ConnectionSessionAcquisitionPlatform platform = ConnectionSessionAcquisitionPlatform.IPad,
        int modelInputManifestVersion = 41,
        AcquisitionPIDCapabilitySupport secondSupport = AcquisitionPIDCapabilitySupport.Indeterminate,
        bool secondEnabled = true,
        int secondRevision = 8,
        int secondRequiredByteCount = 1,
        int secondCanonicalizationVersion = 52,
        string secondFormula = "A - 40",
        string secondUnit = "celsius",
        double secondMinimum = -40,
        bool includeThirdDefinition = false,
        bool includeSecondDefinition = true,
        OBDPIDRequest[]? orderedRequests = null,
        bool definitionsReversed = false)
    {
        var first = new AcquisitionPIDDefinitionSnapshot(
            FirstRequest,
            AcquisitionPIDCapabilitySupport.Supported,
            false,
            7,
            2,
            new AcquisitionPIDDefinitionIdentity(51, "(A * 256 + B) / 4"),
            "rpm",
            AcquisitionPIDValidityRange.NotDeclared);
        var second = new AcquisitionPIDDefinitionSnapshot(
            SecondRequest,
            secondSupport,
            secondEnabled,
            secondRevision,
            secondRequiredByteCount,
            new AcquisitionPIDDefinitionIdentity(secondCanonicalizationVersion, secondFormula),
            secondUnit,
            AcquisitionPIDValidityRange.Inclusive(secondMinimum, 215));
        var definitions = includeSecondDefinition ? new List<AcquisitionPIDDefinitionSnapshot> { first, second } : [first];
        if (includeThirdDefinition)
        {
            definitions.Add(new AcquisitionPIDDefinitionSnapshot(
                ThirdRequest,
                AcquisitionPIDCapabilitySupport.Unsupported,
                false,
                1,
                1,
                new AcquisitionPIDDefinitionIdentity(1, "A"),
                "percent",
                AcquisitionPIDValidityRange.NotDeclared));
        }
        if (definitionsReversed)
        {
            definitions.Reverse();
        }
        return new ConnectionSessionAcquisitionManifest(
            manifestVersion,
            new AcquisitionApplicationVersion(marketingVersion, buildVersion),
            schemaContractVersion,
            pollingPolicyVersion,
            new OrderedAcquisitionPIDSet(orderedRequests ?? [SecondRequest, FirstRequest]),
            definitions,
            platform,
            modelInputManifestVersion);
    }

    /// <summary>取得証拠の4表が空であることを確かめます。</summary>
    /// <remarks>責務: 失敗時に部分行が残っていないことを確認します。</remarks>
    /// <param name="database">確かめるDB。</param>
    private static void AssertAcquisitionTablesAreEmpty(SqliteDatabase database)
    {
        foreach (var table in new[]
        {
            "connection_session_acquisition_manifests",
            "connection_session_acquisition_pid_definitions",
            "connection_session_acquisition_ordered_pids",
            "connection_session_acquisition_raw_boundaries",
        })
        {
            Assert.Equal(0L, database.Read(connection => SqliteDatabase.Scalar(connection, null, $"SELECT COUNT(*) FROM {table}")));
        }
    }

    /// <summary>取得repository errorの理由を確かめます。</summary>
    /// <remarks>責務: 処理が指定理由の例外を投げることを確認します。</remarks>
    /// <param name="reason">期待する理由。</param>
    /// <param name="action">実行する処理。</param>
    internal static void AssertReason(ConnectionSessionAcquisitionRepositoryError reason, Action action) =>
        Assert.Equal(reason, Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(action).Reason);

    /// <summary>SQLを1回のトランザクションで実行します。</summary>
    /// <remarks>責務: テストから直接SQLを実行します。</remarks>
    /// <param name="database">DB。</param>
    /// <param name="sql">SQL。</param>
    /// <param name="arguments">位置パラメーター。</param>
    internal static void Execute(SqliteDatabase database, string sql, params object?[] arguments) =>
        database.Write((connection, transaction) => SqliteDatabase.Execute(connection, transaction, sql, arguments));

    /// <summary>セッションIDのDBキーを返します。</summary>
    /// <remarks>責務: 小文字UUIDへ変換します。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>DBキー。</returns>
    private static string Key(ConnectionSessionID sessionID) => sessionID.RawValue.ToString("D");
}
