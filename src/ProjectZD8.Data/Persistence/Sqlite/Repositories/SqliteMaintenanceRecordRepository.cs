using Microsoft.Data.Sqlite;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Records;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>SQLiteへ写真を含む車両別整備記録を保存します。</summary>
/// <remarks>iOS版 <c>GRDBMaintenanceRecordRepository</c> に相当します。検索列と完全な内部JSONを1行に保存します。</remarks>
public sealed class SqliteMaintenanceRecordRepository : IMaintenanceRecordRepository
{
    /// <summary>SQLiteの直列化された読書き境界です。</summary>
    private readonly SqliteDatabase _database;

    /// <summary>指定DBへMigrationを適用して生成します。</summary>
    /// <remarks>責務: 1件のSQLite接続を整備記録スキーマ利用可能状態へ移行します。</remarks>
    /// <param name="database">整備記録を保存するDB。</param>
    public SqliteMaintenanceRecordRepository(SqliteDatabase database)
    {
        _database = database;
        ProjectZD8DatabaseMigrator.Migrate(database);
    }

    /// <summary>データディレクトリーの製品DBを開いて生成します。</summary>
    /// <remarks>責務: 整備記録の製品保存先を開いてRepositoryを返します。</remarks>
    /// <param name="directory">組み立てが渡すアプリのデータディレクトリー。</param>
    /// <returns>Migration済みの整備記録Repository。</returns>
    public static SqliteMaintenanceRecordRepository OpenApplicationRepository(string directory) =>
        new(SqliteDatabase.OpenApplicationDatabase(directory));

    /// <summary>指定アカウントの全整備記録を更新日時順で取得します。</summary>
    /// <remarks>責務: 1件のアカウントに属するSQLite行をDomain整備記録へ復元します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>墓石を含む全整備記録（更新日時の新しい順）。</returns>
    public Task<IReadOnlyList<MaintenanceRecord>> Records(string accountIdentifier)
    {
        IReadOnlyList<MaintenanceRecord> records = _database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            "SELECT payload FROM maintenance_records WHERE accountIdentifier = $1 ORDER BY updatedAt DESC",
            reader => MaintenanceRecordPayloadCodec.Decode((byte[])reader.GetValue(0)),
            accountIdentifier));
        return Task.FromResult(records);
    }

    /// <summary>指定アカウントへ整備記録を作成または更新します。</summary>
    /// <remarks>責務: 1件のDomain整備記録をSQLiteへupsertします。</remarks>
    /// <param name="record">保存する整備記録。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>保存の完了。</returns>
    public Task Save(MaintenanceRecord record, string accountIdentifier)
    {
        _database.Write((connection, transaction) => Insert(connection, transaction, record, accountIdentifier, replace: true));
        return Task.CompletedTask;
    }

    /// <summary>同期済み集合で指定アカウントの整備記録を置き換えます。</summary>
    /// <remarks>責務: 1件のアカウントに属する全整備行を1トランザクションで更新します。</remarks>
    /// <param name="records">墓石を含む同期済み記録集合。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>置換の完了。</returns>
    public Task ReplaceAll(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier)
    {
        _database.Write((connection, transaction) =>
        {
            SqliteDatabase.Execute(connection, transaction, "DELETE FROM maintenance_records WHERE accountIdentifier = $1", accountIdentifier);
            foreach (var record in records)
            {
                Insert(connection, transaction, record, accountIdentifier, replace: false);
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>1件の整備記録を行として保存します。</summary>
    /// <remarks>責務: 整備記録を索引列と復元可能な内部JSONへ符号化して書き込みます。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="record">保存する整備記録。</param>
    /// <param name="accountIdentifier">所有アカウント。</param>
    /// <param name="replace">既存行を置き換える場合は <see langword="true"/>。</param>
    private static void Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MaintenanceRecord record,
        string accountIdentifier,
        bool replace)
    {
        SqliteDatabase.Execute(
            connection,
            transaction,
            $"INSERT {(replace ? "OR REPLACE " : "")}INTO maintenance_records (id, accountIdentifier, vehicleID, kind, performedAt, updatedAt, deletedAt, payload) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
            SqliteValueConversion.Key(record.Id.RawValue),
            accountIdentifier,
            SqliteValueConversion.Key(record.VehicleID.RawValue),
            record.Kind.ToRawValue(),
            SqliteValueConversion.Ticks(record.PerformedAt),
            SqliteValueConversion.Ticks(record.UpdatedAt),
            SqliteValueConversion.Ticks(record.DeletedAt),
            MaintenanceRecordPayloadCodec.Encode(record));
    }
}
