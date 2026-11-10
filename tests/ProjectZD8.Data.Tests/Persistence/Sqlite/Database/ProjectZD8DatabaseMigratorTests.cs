using ProjectZD8.Data.Persistence.Sqlite.Database;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Database;

/// <summary>Windows版製品DBのMigrationを検証します。</summary>
/// <remarks>
/// iOS版 <c>ProjectZD8DatabaseMigratorTests</c> の移植です。Windows版は旧開発DB（v2〜v8）を持たないため、
/// 旧DBからの移行テスト（testLegacyV8FixtureMigration...、testReleaseBaselinePreservesLegacySessionSchema...）は対象外です。
/// 生ログ・batch・PID要求はLogStoreに保存するため、それらの表とindexはSQLiteにありません。
/// </remarks>
public sealed class ProjectZD8DatabaseMigratorTests
{
    /// <summary>取得証拠の4表です。</summary>
    private static readonly string[] AcquisitionTables =
    [
        "connection_session_acquisition_manifests",
        "connection_session_acquisition_pid_definitions",
        "connection_session_acquisition_ordered_pids",
        "connection_session_acquisition_raw_boundaries",
    ];

    /// <summary>初期スキーマが全製品表を作ることを確かめます。</summary>
    [Fact]
    public void ReleaseBaselineCreatesAllProductTables()
    {
        using var database = SqliteDatabase.OpenInMemory();

        ProjectZD8DatabaseMigrator.Migrate(database);

        foreach (var table in new[] { "obd_pid_definitions", "vehicle_pid_capabilities", "connection_sessions", "maintenance_records", "vehicles", "vehicle_photos" })
        {
            Assert.True(TableExists(database, table), table);
        }
    }

    /// <summary>Migrationの登録順を確かめます。</summary>
    [Fact]
    public void MigratorRegistersBaselineThenAcquisitionEvidence()
    {
        Assert.Equal(
            ["windows_v1_create_schema", "windows_v1_add_connection_session_acquisition_evidence"],
            ProjectZD8DatabaseMigrator.Migrations);
    }

    /// <summary>完了後の再適用が既存行を変えないことを確かめます。</summary>
    [Fact]
    public void ReleaseBaselineIsIdempotentAfterCompletion()
    {
        using var database = SqliteDatabase.OpenInMemory();
        ProjectZD8DatabaseMigrator.Migrate(database);
        database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            "INSERT INTO vehicle_pid_capabilities (vehicleID, service, pid, isCollectionEnabled, discoveredAt) VALUES ('vehicle', 1, 12, 1, 100)"));

        ProjectZD8DatabaseMigrator.Migrate(database);

        Assert.Equal(1L, database.Read(connection => SqliteDatabase.Scalar(connection, null, "SELECT COUNT(*) FROM vehicle_pid_capabilities")));
        Assert.Equal(2L, database.Read(connection => SqliteDatabase.Scalar(connection, null, "SELECT COUNT(*) FROM schema_migrations")));
    }

    /// <summary>新規DBが取得証拠の空の表・trigger・外部キーを持つことを確かめます。</summary>
    [Fact]
    public void FreshDatabaseCreatesAcquisitionEvidenceSchemaWithoutRows()
    {
        using var database = SqliteDatabase.OpenInMemory();

        ProjectZD8DatabaseMigrator.Migrate(database);

        database.Read(connection =>
        {
            foreach (var table in AcquisitionTables)
            {
                Assert.True(TableExists(database, table, connection), table);
                Assert.Equal(0L, SqliteDatabase.Scalar(connection, null, $"SELECT COUNT(*) FROM {table}"));
            }
            Assert.Equal(8, SqliteDatabase.Query(
                connection,
                null,
                "SELECT name FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'acquisition_%' ORDER BY name",
                reader => reader.GetString(0)).Count);
            Assert.Empty(SqliteDatabase.Query(
                connection,
                null,
                "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name LIKE 'connection_session_acquisition_%' AND name NOT LIKE 'sqlite_autoindex_%'",
                reader => reader.GetString(0)));
            Assert.Empty(SqliteDatabase.Query(connection, null, "PRAGMA foreign_key_check", reader => reader.GetString(0)));
            Assert.Equal("ok", SqliteDatabase.Scalar(connection, null, "PRAGMA integrity_check"));
            var manifestForeignKeys = SqliteDatabase.Query(
                connection,
                null,
                "SELECT \"table\", on_delete FROM pragma_foreign_key_list('connection_session_acquisition_manifests')",
                reader => (Table: reader.GetString(0), OnDelete: reader.GetString(1)));
            Assert.Single(manifestForeignKeys);
            Assert.Equal(("connection_sessions", "CASCADE"), manifestForeignKeys[0]);
            Assert.Equal(3, SqliteDatabase.Query(
                connection,
                null,
                "SELECT \"from\" FROM pragma_foreign_key_list('connection_session_acquisition_ordered_pids')",
                reader => reader.GetString(0)).Count);
            return 0;
        });
    }

    /// <summary>初期スキーマの後の取得証拠Migrationが既存セッションを保つことを確かめます。</summary>
    [Fact]
    public void AcquisitionEvidenceMigrationPreservesExistingSessions()
    {
        using var database = SqliteDatabase.OpenInMemory();
        ProjectZD8DatabaseMigrator.Migrate(database, upTo: "windows_v1_create_schema");
        Assert.False(TableExists(database, "connection_session_acquisition_manifests"));
        database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            """
            INSERT INTO connection_sessions (id, accountIdentifier, startedAt, endedAt, endReason, rawRecordCount, rawByteCount, localRawState, cloudSyncState, manifestDigest)
            VALUES ('fixture-session', 'fixture-account', 100, 200, 'userDisconnected', 2, 3, 'available', 'uploaded', 'fixture-manifest-digest')
            """));

        ProjectZD8DatabaseMigrator.Migrate(database);

        database.Read(connection =>
        {
            Assert.Equal("fixture-manifest-digest", SqliteDatabase.Scalar(connection, null, "SELECT manifestDigest FROM connection_sessions WHERE id = 'fixture-session'"));
            Assert.Equal(0L, SqliteDatabase.Scalar(connection, null, "SELECT COUNT(*) FROM connection_session_acquisition_manifests"));
            Assert.Equal("ok", SqliteDatabase.Scalar(connection, null, "PRAGMA integrity_check"));
            return 0;
        });
    }

    /// <summary>表があるかを返します。</summary>
    /// <remarks>責務: sqlite_masterで表の有無を確かめます。</remarks>
    /// <param name="database">確かめるDB。</param>
    /// <param name="table">表名。</param>
    /// <param name="connection">すでに読取中の接続（ある場合）。</param>
    /// <returns>表があれば <see langword="true"/>。</returns>
    private static bool TableExists(SqliteDatabase database, string table, Microsoft.Data.Sqlite.SqliteConnection? connection = null)
    {
        const string sql = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $1";
        return connection is null
            ? database.Read(open => (long)SqliteDatabase.Scalar(open, null, sql, table)!) == 1
            : (long)SqliteDatabase.Scalar(connection, null, sql, table)! == 1;
    }
}
