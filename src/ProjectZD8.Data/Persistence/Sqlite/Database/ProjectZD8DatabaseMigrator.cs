using Microsoft.Data.Sqlite;

namespace ProjectZD8.Data.Persistence.Sqlite.Database;

/// <summary>Windows版製品DBの現行SQLiteスキーマを版付きMigrationで定義します。</summary>
/// <remarks>
/// iOS版 <c>ProjectZD8DatabaseMigrator</c> に相当します。Windows版は旧開発DBを持たないため、
/// Migration名は <c>windows_v1_*</c> から始めます。日時列はすべてUTCのTicks（100ナノ秒単位の整数）です。
/// 生ログ・取得batch・PID要求証拠はSQLiteではなくLogStore（<c>Persistence/LogStore</c>）に保存するため、
/// iOS版の <c>connection_session_raw_logs</c>・<c>connection_session_acquisition_batches</c>・
/// <c>connection_session_acquisition_pid_requests</c> の3表はここにはありません。
/// </remarks>
public static class ProjectZD8DatabaseMigrator
{
    /// <summary>適用済みMigrationを記録する表名です。</summary>
    internal const string MigrationTableName = "schema_migrations";

    /// <summary>登録順のMigration識別子と処理です。</summary>
    private static readonly (string Identifier, Action<SqliteConnection, SqliteTransaction> Apply)[] RegisteredMigrations =
    [
        ("windows_v1_create_schema", CreateBaseline),
        ("windows_v1_add_connection_session_acquisition_evidence", CreateConnectionSessionAcquisitionEvidence),
    ];

    /// <summary>登録順のMigration識別子です。</summary>
    public static IReadOnlyList<string> Migrations { get; } = RegisteredMigrations.Select(migration => migration.Identifier).ToArray();

    /// <summary>未適用のMigrationを登録順に1件ずつトランザクションで適用します。</summary>
    /// <remarks>責務: 1件のSQLite DBを製品スキーマ利用可能状態へ前方向だけ移行します。</remarks>
    /// <param name="database">移行するDB。</param>
    public static void Migrate(SqliteDatabase database) => Migrate(database, upTo: null);

    /// <summary>指定Migrationまでを登録順に適用します。</summary>
    /// <remarks>責務: 途中版のスキーマを再現するため、指定識別子までの未適用Migrationを適用します。</remarks>
    /// <param name="database">移行するDB。</param>
    /// <param name="upTo">最後に適用するMigration識別子。<see langword="null"/>なら全件。</param>
    internal static void Migrate(SqliteDatabase database, string? upTo)
    {
        database.Write((connection, transaction) =>
            SqliteDatabase.Execute(
                connection,
                transaction,
                $"CREATE TABLE IF NOT EXISTS {MigrationTableName} (identifier TEXT NOT NULL PRIMARY KEY);"));
        foreach (var (identifier, apply) in RegisteredMigrations)
        {
            database.Write((connection, transaction) =>
            {
                var applied = SqliteDatabase.Scalar(
                    connection,
                    transaction,
                    $"SELECT identifier FROM {MigrationTableName} WHERE identifier = $1",
                    identifier);
                if (applied is not null)
                {
                    return;
                }
                apply(connection, transaction);
                SqliteDatabase.Execute(
                    connection,
                    transaction,
                    $"INSERT INTO {MigrationTableName} (identifier) VALUES ($1)",
                    identifier);
            });
            if (string.Equals(identifier, upTo, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    /// <summary>マスタ表（PID定義、車両別PID、接続セッション、整備記録、車両）を作成します。</summary>
    /// <remarks>責務: Windows版1.0のマスタデータ列・制約・索引を固定します。</remarks>
    /// <param name="connection">作成先の接続。</param>
    /// <param name="transaction">Migrationのトランザクション。</param>
    private static void CreateBaseline(SqliteConnection connection, SqliteTransaction transaction)
    {
        SqliteDatabase.Execute(connection, transaction, """
            CREATE TABLE obd_pid_definitions (
                service INTEGER NOT NULL CHECK (service >= 0 AND service <= 255),
                pid INTEGER NOT NULL CHECK (pid >= 0 AND pid <= 255),
                header INTEGER,
                vehicleModelCode TEXT,
                nameKey TEXT NOT NULL CHECK (length(nameKey) > 0),
                requiredByteCount INTEGER CHECK (requiredByteCount >= 1 AND requiredByteCount <= 8),
                formula TEXT,
                unit TEXT NOT NULL,
                minimumValue REAL,
                maximumValue REAL,
                sourceURI TEXT NOT NULL CHECK (length(sourceURI) > 0),
                revision INTEGER NOT NULL CHECK (revision >= 1),
                summaryKey TEXT NOT NULL,
                highValueKey TEXT NOT NULL,
                lowValueKey TEXT NOT NULL,
                correlationKey TEXT NOT NULL,
                PRIMARY KEY (service, pid),
                CHECK ((header IS NULL AND vehicleModelCode IS NULL) OR (header IS NOT NULL AND vehicleModelCode IS NOT NULL)),
                CHECK ((requiredByteCount IS NULL AND formula IS NULL) OR (requiredByteCount IS NOT NULL AND formula IS NOT NULL AND length(formula) > 0)),
                CHECK ((minimumValue IS NULL AND maximumValue IS NULL) OR (minimumValue IS NOT NULL AND maximumValue IS NOT NULL AND minimumValue <= maximumValue))
            );

            CREATE TABLE vehicle_pid_capabilities (
                vehicleID TEXT NOT NULL COLLATE NOCASE,
                service INTEGER NOT NULL CHECK (service >= 0 AND service <= 255),
                pid INTEGER NOT NULL CHECK (pid >= 0 AND pid <= 255),
                isCollectionEnabled INTEGER NOT NULL DEFAULT 1,
                discoveredAt INTEGER NOT NULL,
                PRIMARY KEY (vehicleID, service, pid)
            );

            CREATE TABLE connection_sessions (
                id TEXT PRIMARY KEY NOT NULL CHECK (length(id) > 0),
                accountIdentifier TEXT NOT NULL CHECK (length(accountIdentifier) > 0),
                startedAt INTEGER NOT NULL,
                endedAt INTEGER,
                endReason TEXT,
                stopReviewDecision TEXT,
                vehicleID TEXT,
                vehicleName TEXT,
                vehicleDisplayIdentifier TEXT,
                acquisitionPlatform TEXT,
                acquisitionDeviceName TEXT,
                startingOdometerKilometers REAL,
                endingOdometerKilometers REAL,
                distanceSourceModelCode TEXT,
                rawRecordCount INTEGER NOT NULL DEFAULT 0,
                rawByteCount INTEGER NOT NULL DEFAULT 0,
                localRawState TEXT NOT NULL DEFAULT 'empty',
                cloudSyncState TEXT NOT NULL DEFAULT 'notUploaded',
                manifestDigest TEXT,
                macImportedDeviceID TEXT,
                macImportedDeviceName TEXT,
                macImportedAt INTEGER,
                macImportedManifestDigest TEXT,
                rawLastAccessedAt INTEGER,
                CHECK ((endedAt IS NULL AND endReason IS NULL) OR (endedAt IS NOT NULL AND endReason IS NOT NULL)),
                CHECK ((vehicleID IS NULL AND vehicleName IS NULL AND vehicleDisplayIdentifier IS NULL) OR (vehicleID IS NOT NULL AND vehicleName IS NOT NULL AND vehicleDisplayIdentifier IS NOT NULL))
            );
            CREATE INDEX connection_sessions_account_started_at ON connection_sessions(accountIdentifier, startedAt);
            CREATE INDEX connection_sessions_vehicle_started_at ON connection_sessions(accountIdentifier, vehicleID, startedAt);

            CREATE TABLE maintenance_records (
                id TEXT PRIMARY KEY NOT NULL CHECK (length(id) > 0),
                accountIdentifier TEXT NOT NULL CHECK (length(accountIdentifier) > 0),
                vehicleID TEXT NOT NULL CHECK (length(vehicleID) > 0),
                kind TEXT NOT NULL CHECK (kind = 'light' OR kind = 'heavy'),
                performedAt INTEGER NOT NULL,
                updatedAt INTEGER NOT NULL,
                deletedAt INTEGER,
                payload BLOB NOT NULL
            );
            CREATE INDEX maintenance_records_account_vehicle_performed_at ON maintenance_records(accountIdentifier, vehicleID, performedAt);
            CREATE INDEX maintenance_records_account_updated_at ON maintenance_records(accountIdentifier, updatedAt);

            CREATE TABLE vehicles (
                accountIdentifier TEXT NOT NULL CHECK (length(accountIdentifier) > 0),
                id TEXT NOT NULL CHECK (length(id) > 0),
                vin TEXT NOT NULL,
                obdIdentifier TEXT,
                name TEXT NOT NULL,
                manufacturer TEXT NOT NULL,
                engineModel TEXT NOT NULL,
                powertrain TEXT NOT NULL,
                energySources TEXT NOT NULL,
                tankCapacityLiters REAL,
                modelYear INTEGER,
                note TEXT NOT NULL,
                hasPhoto INTEGER NOT NULL CHECK (hasPhoto IN (0, 1)),
                isDefault INTEGER NOT NULL CHECK (isDefault IN (0, 1)),
                updatedAt INTEGER NOT NULL,
                PRIMARY KEY (accountIdentifier, id)
            );
            CREATE INDEX vehicles_account_updated_at ON vehicles(accountIdentifier, updatedAt);

            CREATE TABLE vehicle_photos (
                accountIdentifier TEXT NOT NULL,
                vehicleID TEXT NOT NULL,
                data BLOB NOT NULL,
                PRIMARY KEY (accountIdentifier, vehicleID),
                FOREIGN KEY (accountIdentifier, vehicleID) REFERENCES vehicles(accountIdentifier, id) ON DELETE CASCADE
            );
            """);
    }

    /// <summary>取得manifest、PID snapshot、要求順、Raw境界の正規化表を追加します。</summary>
    /// <remarks>
    /// 責務: 既存セッションを変更せず取得証拠用の4表と不変性triggerを追加します。
    /// iOS版と同じ制約とtriggerです。ただし取得platformの値は今後のWindows追加に備えてCHECKで限定しません。
    /// </remarks>
    /// <param name="connection">作成先の接続。</param>
    /// <param name="transaction">Migrationのトランザクション。</param>
    private static void CreateConnectionSessionAcquisitionEvidence(SqliteConnection connection, SqliteTransaction transaction)
    {
        SqliteDatabase.Execute(connection, transaction, """
            CREATE TABLE connection_session_acquisition_manifests (
                sessionID TEXT PRIMARY KEY NOT NULL
                    REFERENCES connection_sessions(id) ON DELETE CASCADE,
                manifestVersion INTEGER NOT NULL CHECK (manifestVersion > 0),
                applicationMarketingVersion TEXT NOT NULL CHECK (length(applicationMarketingVersion) > 0),
                applicationBuildVersion TEXT NOT NULL CHECK (length(applicationBuildVersion) > 0),
                schemaContractVersion INTEGER NOT NULL CHECK (schemaContractVersion > 0),
                pollingPolicyVersion INTEGER NOT NULL CHECK (pollingPolicyVersion > 0),
                acquisitionPlatform TEXT NOT NULL CHECK (length(acquisitionPlatform) > 0),
                modelInputManifestVersion INTEGER NOT NULL CHECK (modelInputManifestVersion > 0),
                isSealed INTEGER NOT NULL CHECK (isSealed IN (0, 1))
            );

            CREATE TABLE connection_session_acquisition_pid_definitions (
                sessionID TEXT NOT NULL
                    REFERENCES connection_session_acquisition_manifests(sessionID) ON DELETE CASCADE,
                service INTEGER NOT NULL CHECK (service BETWEEN 0 AND 255),
                pid INTEGER NOT NULL CHECK (pid BETWEEN 0 AND 255),
                capabilitySupport TEXT NOT NULL CHECK (capabilitySupport IN ('supported', 'unsupported', 'indeterminate')),
                isCollectionEnabled INTEGER NOT NULL CHECK (isCollectionEnabled IN (0, 1)),
                definitionRevision INTEGER NOT NULL CHECK (definitionRevision > 0),
                requiredByteCount INTEGER NOT NULL CHECK (requiredByteCount > 0),
                formulaCanonicalizationVersion INTEGER NOT NULL CHECK (formulaCanonicalizationVersion > 0),
                formulaExpression TEXT NOT NULL CHECK (length(formulaExpression) > 0),
                unit TEXT NOT NULL CHECK (length(unit) > 0),
                validityRangeKind TEXT NOT NULL CHECK (validityRangeKind IN ('notDeclared', 'inclusive')),
                minimumValue REAL,
                maximumValue REAL,
                PRIMARY KEY (sessionID, service, pid),
                CHECK (
                    (validityRangeKind = 'notDeclared' AND minimumValue IS NULL AND maximumValue IS NULL)
                    OR
                    (validityRangeKind = 'inclusive'
                        AND minimumValue IS NOT NULL AND maximumValue IS NOT NULL
                        AND typeof(minimumValue) IN ('real', 'integer')
                        AND typeof(maximumValue) IN ('real', 'integer')
                        AND minimumValue BETWEEN -1.7976931348623157e308 AND 1.7976931348623157e308
                        AND maximumValue BETWEEN -1.7976931348623157e308 AND 1.7976931348623157e308
                        AND minimumValue <= maximumValue)
                )
            );

            CREATE TABLE connection_session_acquisition_ordered_pids (
                sessionID TEXT NOT NULL,
                ordinal INTEGER NOT NULL CHECK (ordinal >= 0),
                service INTEGER NOT NULL CHECK (service BETWEEN 0 AND 255),
                pid INTEGER NOT NULL CHECK (pid BETWEEN 0 AND 255),
                PRIMARY KEY (sessionID, ordinal),
                UNIQUE (sessionID, service, pid),
                FOREIGN KEY (sessionID, service, pid)
                    REFERENCES connection_session_acquisition_pid_definitions(sessionID, service, pid)
                    ON DELETE CASCADE
            );

            CREATE TABLE connection_session_acquisition_raw_boundaries (
                sessionID TEXT NOT NULL
                    REFERENCES connection_session_acquisition_manifests(sessionID) ON DELETE CASCADE,
                eventKind TEXT NOT NULL CHECK (eventKind IN ('started', 'ended')),
                occurredAtMicroseconds INTEGER NOT NULL,
                endReason TEXT,
                PRIMARY KEY (sessionID, eventKind),
                CHECK (
                    (eventKind = 'started' AND endReason IS NULL)
                    OR
                    (eventKind = 'ended' AND endReason IN (
                        'userDisconnected', 'vehicleNoResponse', 'connectionLost', 'acquisitionFailed',
                        'superseded', 'accountSignedOut', 'unexpectedTermination'
                    ))
                )
            );

            CREATE TRIGGER acquisition_manifest_update_guard
            BEFORE UPDATE ON connection_session_acquisition_manifests
            WHEN NOT (
                OLD.isSealed = 0 AND NEW.isSealed = 1
                AND OLD.sessionID = NEW.sessionID
                AND OLD.manifestVersion = NEW.manifestVersion
                AND OLD.applicationMarketingVersion = NEW.applicationMarketingVersion
                AND OLD.applicationBuildVersion = NEW.applicationBuildVersion
                AND OLD.schemaContractVersion = NEW.schemaContractVersion
                AND OLD.pollingPolicyVersion = NEW.pollingPolicyVersion
                AND OLD.acquisitionPlatform = NEW.acquisitionPlatform
                AND OLD.modelInputManifestVersion = NEW.modelInputManifestVersion
                AND EXISTS (
                    SELECT 1 FROM connection_session_acquisition_raw_boundaries
                    WHERE sessionID = OLD.sessionID AND eventKind = 'started'
                )
                AND (
                    NOT EXISTS (
                        SELECT 1 FROM connection_session_acquisition_ordered_pids
                        WHERE sessionID = OLD.sessionID
                    )
                    OR (
                        (SELECT MIN(ordinal) FROM connection_session_acquisition_ordered_pids WHERE sessionID = OLD.sessionID) = 0
                        AND (SELECT MAX(ordinal) FROM connection_session_acquisition_ordered_pids WHERE sessionID = OLD.sessionID)
                            = (SELECT COUNT(*) - 1 FROM connection_session_acquisition_ordered_pids WHERE sessionID = OLD.sessionID)
                    )
                )
            )
            BEGIN
                SELECT RAISE(ABORT, 'acquisition manifest is immutable');
            END;

            CREATE TRIGGER acquisition_pid_definition_insert_guard
            BEFORE INSERT ON connection_session_acquisition_pid_definitions
            WHEN COALESCE((SELECT isSealed FROM connection_session_acquisition_manifests WHERE sessionID = NEW.sessionID), 1) != 0
            BEGIN
                SELECT RAISE(ABORT, 'sealed acquisition manifest rejects PID definition insert');
            END;

            CREATE TRIGGER acquisition_pid_definition_update_guard
            BEFORE UPDATE ON connection_session_acquisition_pid_definitions
            BEGIN
                SELECT RAISE(ABORT, 'acquisition PID definition is immutable');
            END;

            CREATE TRIGGER acquisition_ordered_pid_insert_guard
            BEFORE INSERT ON connection_session_acquisition_ordered_pids
            WHEN COALESCE((SELECT isSealed FROM connection_session_acquisition_manifests WHERE sessionID = NEW.sessionID), 1) != 0
            BEGIN
                SELECT RAISE(ABORT, 'sealed acquisition manifest rejects ordered PID insert');
            END;

            CREATE TRIGGER acquisition_ordered_pid_update_guard
            BEFORE UPDATE ON connection_session_acquisition_ordered_pids
            BEGIN
                SELECT RAISE(ABORT, 'acquisition ordered PID is immutable');
            END;

            CREATE TRIGGER acquisition_raw_boundary_update_guard
            BEFORE UPDATE ON connection_session_acquisition_raw_boundaries
            BEGIN
                SELECT RAISE(ABORT, 'acquisition Raw boundary is immutable');
            END;

            CREATE TRIGGER acquisition_started_insert_guard
            BEFORE INSERT ON connection_session_acquisition_raw_boundaries
            WHEN NEW.eventKind = 'started'
                AND COALESCE((SELECT isSealed FROM connection_session_acquisition_manifests WHERE sessionID = NEW.sessionID), 1) != 0
            BEGIN
                SELECT RAISE(ABORT, 'sealed acquisition manifest rejects started boundary');
            END;

            CREATE TRIGGER acquisition_ended_insert_guard
            BEFORE INSERT ON connection_session_acquisition_raw_boundaries
            WHEN NEW.eventKind = 'ended' AND (
                NOT EXISTS (
                    SELECT 1 FROM connection_session_acquisition_raw_boundaries
                    WHERE sessionID = NEW.sessionID AND eventKind = 'started'
                )
                OR NEW.occurredAtMicroseconds < (
                    SELECT occurredAtMicroseconds FROM connection_session_acquisition_raw_boundaries
                    WHERE sessionID = NEW.sessionID AND eventKind = 'started'
                )
            )
            BEGIN
                SELECT RAISE(ABORT, 'ended boundary requires an earlier started boundary');
            END;
            """);
    }
}
