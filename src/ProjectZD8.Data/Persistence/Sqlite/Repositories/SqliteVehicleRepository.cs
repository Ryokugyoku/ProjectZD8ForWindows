using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Records;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>登録車両をアカウントスコープでSQLiteへ保存します。</summary>
/// <remarks>
/// iOS版は車両をCloudKitの <c>VehicleCatalog</c>（とUserDefaultsのキャッシュ）だけに保存します。
/// Windows版はSQLiteをマスタとし、CloudKitとの同期はこのクラスを包む別の実装（Data/Network の担当）が行います。
/// 写真は大きくなりうるため、車両行とは別の <c>vehicle_photos</c> 表に保存します。
/// </remarks>
public sealed class SqliteVehicleRepository : IVehicleRepository
{
    /// <summary>車両表の読取列です。</summary>
    private const string Columns =
        "v.id, v.vin, v.obdIdentifier, v.name, v.manufacturer, v.engineModel, v.powertrain, v.energySources, " +
        "v.tankCapacityLiters, v.modelYear, v.note, v.isDefault, v.updatedAt, p.data";

    /// <summary>SQLiteの直列化された読書き境界です。</summary>
    private readonly SqliteDatabase _database;

    /// <summary>指定DBへMigrationを適用して生成します。</summary>
    /// <remarks>責務: 1件のSQLite接続を車両スキーマ利用可能状態へ移行します。</remarks>
    /// <param name="database">車両を保存するDB。</param>
    public SqliteVehicleRepository(SqliteDatabase database)
    {
        _database = database;
        ProjectZD8DatabaseMigrator.Migrate(database);
    }

    /// <summary>データディレクトリーの製品DBを開いて生成します。</summary>
    /// <remarks>責務: 車両の製品保存先を開いてRepositoryを返します。</remarks>
    /// <param name="directory">組み立てが渡すアプリのデータディレクトリー。</param>
    /// <returns>Migration済みの車両Repository。</returns>
    public static SqliteVehicleRepository OpenApplicationRepository(string directory) =>
        new(SqliteDatabase.OpenApplicationDatabase(directory));

    /// <summary>指定アカウントの登録車両を読み込みます。</summary>
    /// <remarks>責務: 1件のアカウントに属する現在の車両一覧を更新日時の新しい順（同時刻はID順）で返します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>更新日時の新しい順に並ぶ登録車両。</returns>
    public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier)
    {
        IReadOnlyList<VehicleProfile> vehicles = _database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            $"SELECT {Columns} FROM vehicles v LEFT JOIN vehicle_photos p ON p.accountIdentifier = v.accountIdentifier AND p.vehicleID = v.id " +
            "WHERE v.accountIdentifier = $1 ORDER BY v.updatedAt DESC, v.id",
            MakeDomainVehicle,
            accountIdentifier));
        return Task.FromResult(vehicles);
    }

    /// <summary>指定アカウントへ車両プロフィールを保存します。</summary>
    /// <remarks>責務: 1件の車両プロフィールを同じアカウントスコープへ作成または更新します。</remarks>
    /// <param name="vehicle">保存する車両プロフィール。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>保存の完了。</returns>
    public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier)
    {
        _database.Write((connection, transaction) => Upsert(connection, transaction, vehicle, accountIdentifier));
        return Task.CompletedTask;
    }

    /// <summary>指定アカウントから車両プロフィールを削除します。</summary>
    /// <remarks>責務: 1件のアカウントスコープから指定車両と写真だけを削除します。</remarks>
    /// <param name="id">削除する車両ID。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>削除の完了。</returns>
    public Task DeleteVehicle(VehicleID id, string accountIdentifier)
    {
        _database.Write((connection, transaction) => SqliteDatabase.Execute(
            connection,
            transaction,
            "DELETE FROM vehicles WHERE accountIdentifier = $1 AND id = $2",
            accountIdentifier,
            SqliteValueConversion.Key(id.RawValue)));
        return Task.CompletedTask;
    }

    /// <summary>同期済み集合で指定アカウントの車両一覧を置き換えます。</summary>
    /// <remarks>責務: CloudKitと同期する実装が、取得したカタログ全体を1トランザクションで反映できるようにします。</remarks>
    /// <param name="vehicles">反映する車両一覧。</param>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    public void ReplaceAll(IReadOnlyList<VehicleProfile> vehicles, string accountIdentifier)
    {
        _database.Write((connection, transaction) =>
        {
            SqliteDatabase.Execute(connection, transaction, "DELETE FROM vehicles WHERE accountIdentifier = $1", accountIdentifier);
            foreach (var vehicle in vehicles)
            {
                Upsert(connection, transaction, vehicle, accountIdentifier);
            }
        });
    }

    /// <summary>指定アカウントのローカル車両IDを返します。</summary>
    /// <remarks>責務: アカウント削除時に車両別PID設定を消すため、保存済み車両の識別子だけを返します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    /// <returns>保存済み車両ID（更新日時の新しい順）。</returns>
    public IReadOnlyList<VehicleID> VehicleIDs(string accountIdentifier) =>
        _database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            "SELECT id FROM vehicles WHERE accountIdentifier = $1 ORDER BY updatedAt DESC, id",
            reader => new VehicleID(Guid.Parse(reader.GetString(0))),
            accountIdentifier));

    /// <summary>指定アカウントの車両をすべて削除します。</summary>
    /// <remarks>責務: アカウント削除時にローカルの車両と写真を原子的に消します。</remarks>
    /// <param name="accountIdentifier">Appleアカウントのアプリ固有識別子。</param>
    public void DeleteVehicles(string accountIdentifier) =>
        _database.Write((connection, transaction) =>
            SqliteDatabase.Execute(connection, transaction, "DELETE FROM vehicles WHERE accountIdentifier = $1", accountIdentifier));

    /// <summary>1件の車両と写真を保存します。</summary>
    /// <remarks>責務: 車両列を作成または更新し、写真を別表へ置き換えます。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="vehicle">保存する車両。</param>
    /// <param name="accountIdentifier">所有アカウント。</param>
    private static void Upsert(SqliteConnection connection, SqliteTransaction transaction, VehicleProfile vehicle, string accountIdentifier)
    {
        var key = SqliteValueConversion.Key(vehicle.Id.RawValue);
        SqliteDatabase.Execute(
            connection,
            transaction,
            """
            INSERT INTO vehicles (accountIdentifier, id, vin, obdIdentifier, name, manufacturer, engineModel, powertrain,
                energySources, tankCapacityLiters, modelYear, note, hasPhoto, isDefault, updatedAt)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15)
            ON CONFLICT (accountIdentifier, id) DO UPDATE SET
                vin = excluded.vin, obdIdentifier = excluded.obdIdentifier, name = excluded.name,
                manufacturer = excluded.manufacturer, engineModel = excluded.engineModel, powertrain = excluded.powertrain,
                energySources = excluded.energySources, tankCapacityLiters = excluded.tankCapacityLiters,
                modelYear = excluded.modelYear, note = excluded.note, hasPhoto = excluded.hasPhoto,
                isDefault = excluded.isDefault, updatedAt = excluded.updatedAt
            """,
            accountIdentifier,
            key,
            vehicle.Vin,
            vehicle.ObdIdentifier,
            vehicle.Name,
            vehicle.Manufacturer,
            vehicle.EngineModel,
            vehicle.Powertrain.ToRawValue(),
            JsonSerializer.Serialize(vehicle.EnergySources.Select(source => source.ToRawValue()).ToArray()),
            vehicle.TankCapacityLiters,
            vehicle.ModelYear,
            vehicle.Note,
            vehicle.PhotoData is null ? 0 : 1,
            vehicle.IsDefault ? 1 : 0,
            SqliteValueConversion.Ticks(vehicle.UpdatedAt));
        SqliteDatabase.Execute(
            connection,
            transaction,
            "DELETE FROM vehicle_photos WHERE accountIdentifier = $1 AND vehicleID = $2",
            accountIdentifier,
            key);
        if (vehicle.PhotoData is { } photo)
        {
            SqliteDatabase.Execute(
                connection,
                transaction,
                "INSERT INTO vehicle_photos (accountIdentifier, vehicleID, data) VALUES ($1, $2, $3)",
                accountIdentifier,
                key,
                photo);
        }
    }

    /// <summary>1行を車両プロフィールへ変換します。</summary>
    /// <remarks>責務: 車両列と写真列を検証してDomain値へ写像します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <returns>車両プロフィール。</returns>
    /// <exception cref="InvalidDataException">識別子や列挙値が不正な場合。</exception>
    private static VehicleProfile MakeDomainVehicle(SqliteDataReader reader)
    {
        if (!Guid.TryParse(reader.GetString(0), out var id)
            || !VehiclePowertrainKindRawValue.TryParse(reader.GetString(6), out var powertrain))
        {
            throw new InvalidDataException("Vehicle contains an invalid identifier or powertrain");
        }
        var rawSources = JsonSerializer.Deserialize<string[]>(reader.GetString(7))
            ?? throw new InvalidDataException("Vehicle energy sources are invalid");
        var sources = rawSources.Select(raw => VehicleEnergySourceRawValue.TryParse(raw, out var source)
            ? source
            : throw new InvalidDataException("Vehicle energy source is unknown")).ToArray();
        var modelYear = SqliteValueConversion.NullableInt64(reader, 9);
        return new VehicleProfile(
            vin: reader.GetString(1),
            name: reader.GetString(3),
            id: new VehicleID(id),
            obdIdentifier: SqliteValueConversion.NullableString(reader, 2),
            manufacturer: reader.GetString(4),
            engineModel: reader.GetString(5),
            powertrain: powertrain,
            energySources: sources,
            tankCapacityLiters: SqliteValueConversion.NullableDouble(reader, 8),
            modelYear: modelYear is { } year ? (int)year : null,
            note: reader.GetString(10),
            photoData: reader.IsDBNull(13) ? null : (byte[])reader.GetValue(13),
            isDefault: reader.GetInt64(11) != 0,
            updatedAt: SqliteValueConversion.Date(reader.GetInt64(12)));
    }
}
