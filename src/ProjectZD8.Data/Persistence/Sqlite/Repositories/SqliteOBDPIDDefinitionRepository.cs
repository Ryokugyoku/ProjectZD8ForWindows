using Microsoft.Data.Sqlite;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Records;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>SQLiteへPID変換定義を保存します。</summary>
/// <remarks>iOS版 <c>GRDBOBDPIDDefinitionRepository</c> に相当します。</remarks>
public sealed class SqliteOBDPIDDefinitionRepository : IOBDPIDDefinitionRepository
{
    /// <summary>PID定義表の全列です（読取順）。</summary>
    private const string Columns =
        "service, pid, header, vehicleModelCode, nameKey, requiredByteCount, formula, unit, minimumValue, maximumValue, " +
        "sourceURI, revision, summaryKey, highValueKey, lowValueKey, correlationKey";

    /// <summary>SQLiteの直列化された読書き境界です。</summary>
    private readonly SqliteDatabase _database;

    /// <summary>指定DBへ製品版Migrationを適用して生成します。</summary>
    /// <remarks>責務: 1件のSQLite接続を製品版スキーマ利用可能状態へ移行してPID定義Repositoryを生成します。</remarks>
    /// <param name="database">PID定義を保存するDB。</param>
    public SqliteOBDPIDDefinitionRepository(SqliteDatabase database)
    {
        _database = database;
        ProjectZD8DatabaseMigrator.Migrate(database);
    }

    /// <summary>データディレクトリーの製品DBを開いて生成します。</summary>
    /// <remarks>責務: PID定義DBの製品保存先を開いてリポジトリを返します。</remarks>
    /// <param name="directory">組み立てが渡すアプリのデータディレクトリー。</param>
    /// <returns>Migration済みのPID定義リポジトリ。</returns>
    public static SqliteOBDPIDDefinitionRepository OpenApplicationRepository(string directory) =>
        new(SqliteDatabase.OpenApplicationDatabase(directory));

    /// <summary>登録済みの現在PID定義を識別子順で取得します。</summary>
    /// <remarks>責務: PIDテーブルの全レコードをService/PIDの昇順でDomain定義へ復元します。</remarks>
    /// <returns>登録済みPID定義の一覧。</returns>
    /// <exception cref="InvalidDataException">保存値が範囲外の場合。</exception>
    public IReadOnlyList<OBDPIDDefinition> Definitions() =>
        _database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            $"SELECT {Columns} FROM obd_pid_definitions ORDER BY service, pid",
            MakeDomainDefinition));

    /// <summary>定義の改訂番号が新しい場合だけ登録または更新します。</summary>
    /// <remarks>責務: 1件のPID定義を既存改訂を後退させずSQLiteへ保存します。</remarks>
    /// <param name="definition">保存するPID定義。</param>
    public void Upsert(OBDPIDDefinition definition)
    {
        _database.Write((connection, transaction) =>
        {
            var currentRevision = SqliteDatabase.Scalar(
                connection,
                transaction,
                "SELECT revision FROM obd_pid_definitions WHERE service = $1 AND pid = $2",
                (int)definition.Service,
                (int)definition.Pid);
            if (currentRevision is long revision && definition.Revision <= revision)
            {
                return;
            }
            SqliteDatabase.Execute(
                connection,
                transaction,
                $"INSERT OR REPLACE INTO obd_pid_definitions ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16)",
                (int)definition.Service,
                (int)definition.Pid,
                definition.Header is { } header ? (int)header : null,
                definition.VehicleModelCode,
                definition.NameKey,
                definition.RequiredByteCount,
                definition.Formula,
                definition.Unit,
                definition.MinimumValue,
                definition.MaximumValue,
                definition.SourceURI,
                definition.Revision,
                definition.SummaryKey,
                definition.HighValueKey,
                definition.LowValueKey,
                definition.CorrelationKey);
        });
    }

    /// <summary>指定Service/PIDの現在定義を取得します。</summary>
    /// <remarks>責務: 1件の複合識別子に一致するPID定義をSQLiteから復元します。</remarks>
    /// <param name="service">OBD Service番号。</param>
    /// <param name="pid">Service内PID番号。</param>
    /// <returns>登録済みDomain定義。未登録の場合は <see langword="null"/>。</returns>
    /// <exception cref="InvalidDataException">保存値が範囲外の場合。</exception>
    public OBDPIDDefinition? Definition(byte service, byte pid) =>
        _database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            $"SELECT {Columns} FROM obd_pid_definitions WHERE service = $1 AND pid = $2",
            MakeDomainDefinition,
            (int)service,
            (int)pid).FirstOrDefault());

    /// <summary>1行をDomain定義へ変換します。</summary>
    /// <remarks>責務: 検証済みDB列を1件のPID Domain定義へ写像します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <returns>Domain定義。</returns>
    /// <exception cref="InvalidDataException">Service、PID、ヘッダーが表現範囲外の場合。</exception>
    private static OBDPIDDefinition MakeDomainDefinition(SqliteDataReader reader)
    {
        var service = reader.GetInt64(0);
        var pid = reader.GetInt64(1);
        var header = SqliteValueConversion.NullableInt64(reader, 2);
        if (service is < 0 or > byte.MaxValue || pid is < 0 or > byte.MaxValue
            || header is < 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException("PID definition contains an out-of-range identifier");
        }
        return new OBDPIDDefinition(
            service: (byte)service,
            pid: (byte)pid,
            header: header is { } value ? (ushort)value : null,
            vehicleModelCode: SqliteValueConversion.NullableString(reader, 3),
            nameKey: reader.GetString(4),
            requiredByteCount: SqliteValueConversion.NullableInt64(reader, 5) is { } count ? (int)count : null,
            formula: SqliteValueConversion.NullableString(reader, 6),
            unit: reader.GetString(7),
            minimumValue: SqliteValueConversion.NullableDouble(reader, 8),
            maximumValue: SqliteValueConversion.NullableDouble(reader, 9),
            sourceURI: reader.GetString(10),
            revision: reader.GetInt32(11),
            summaryKey: reader.GetString(12),
            highValueKey: reader.GetString(13),
            lowValueKey: reader.GetString(14),
            correlationKey: reader.GetString(15));
    }
}
