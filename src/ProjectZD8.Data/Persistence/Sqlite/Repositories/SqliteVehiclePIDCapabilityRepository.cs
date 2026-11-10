using Microsoft.Data.Sqlite;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Records;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>車両別対応PIDと収集選択をSQLiteへ保存します。</summary>
/// <remarks>iOS版 <c>GRDBVehiclePIDCapabilityRepository</c> に相当します。</remarks>
public sealed class SqliteVehiclePIDCapabilityRepository : IVehiclePIDCapabilityRepository, IAccountVehiclePIDCapabilityErasureRepository
{
    /// <summary>SQLiteの直列化された読書き境界です。</summary>
    private readonly SqliteDatabase _database;

    /// <summary>指定DBへ製品版Migrationを適用して生成します。</summary>
    /// <remarks>責務: 1件のSQLite接続を製品版スキーマ利用可能状態へ移行して車両別PID Repositoryを生成します。</remarks>
    /// <param name="database">車両別PID設定を保存するDB。</param>
    public SqliteVehiclePIDCapabilityRepository(SqliteDatabase database)
    {
        _database = database;
        ProjectZD8DatabaseMigrator.Migrate(database);
    }

    /// <summary>データディレクトリーの製品DBを開きます。</summary>
    /// <remarks>責務: 製品DBの車両別対応PID Repositoryを生成します。</remarks>
    /// <param name="directory">組み立てが渡すアプリのデータディレクトリー。</param>
    /// <returns>Migration済みRepository。</returns>
    public static SqliteVehiclePIDCapabilityRepository OpenApplicationRepository(string directory) =>
        new(SqliteDatabase.OpenApplicationDatabase(directory));

    /// <summary>指定車両で確認済みの対応PIDを取得します。</summary>
    /// <remarks>責務: 1件の車両IDに属する対応PIDをService/PID順で復元します。</remarks>
    /// <param name="vehicleID">照会する車両ID。</param>
    /// <returns>永続化済み対応PID設定。</returns>
    /// <exception cref="InvalidDataException">不正レコードの場合。</exception>
    public IReadOnlyList<VehiclePIDCapability> Capabilities(VehicleID vehicleID) =>
        _database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            "SELECT vehicleID, service, pid, isCollectionEnabled, discoveredAt FROM vehicle_pid_capabilities WHERE vehicleID = $1 ORDER BY service, pid",
            MakeDomainCapability,
            SqliteValueConversion.Key(vehicleID.RawValue)));

    /// <summary>未収集車両へ対応PIDを全件収集有効で登録します。</summary>
    /// <remarks>責務: 0件の車両スコープへ探索結果を原子的に初期登録します。</remarks>
    /// <param name="capabilities">登録する対応PID。</param>
    /// <param name="vehicleID">登録先の車両ID。</param>
    /// <exception cref="InvalidOperationException">既存レコードがある、または車両不一致・収集無効の場合。</exception>
    public void InsertInitial(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID)
    {
        _database.Write((connection, transaction) =>
        {
            var count = (long)(SqliteDatabase.Scalar(
                connection,
                transaction,
                "SELECT COUNT(*) FROM vehicle_pid_capabilities WHERE vehicleID = $1",
                SqliteValueConversion.Key(vehicleID.RawValue)) ?? 0L);
            if (count != 0)
            {
                throw new InvalidOperationException("Vehicle PID capabilities already exist");
            }
            foreach (var capability in capabilities)
            {
                if (capability.Id.VehicleID != vehicleID || !capability.IsCollectionEnabled)
                {
                    throw new InvalidOperationException("Initial vehicle PID capabilities must match the vehicle and be enabled");
                }
                Insert(connection, transaction, capability, ignoreConflict: false);
            }
        });
    }

    /// <summary>応答確認済みPIDを既存選択へ追加します。</summary>
    /// <remarks>責務: 1台の車両へ未登録の対応PIDだけを収集有効で追加します。</remarks>
    /// <param name="capabilities">新たに応答したPID設定。</param>
    /// <param name="vehicleID">登録先の車両ID。</param>
    /// <exception cref="InvalidOperationException">車両不一致または収集無効の場合。</exception>
    public void MergeDiscovered(IReadOnlyList<VehiclePIDCapability> capabilities, VehicleID vehicleID)
    {
        _database.Write((connection, transaction) =>
        {
            foreach (var capability in capabilities)
            {
                if (capability.Id.VehicleID != vehicleID || !capability.IsCollectionEnabled)
                {
                    throw new InvalidOperationException("Discovered vehicle PID capabilities must match the vehicle and be enabled");
                }
                Insert(connection, transaction, capability, ignoreConflict: true);
            }
        });
    }

    /// <summary>1件の対応PIDの収集選択を更新します。</summary>
    /// <remarks>責務: 指定車両の指定PIDだけの収集有効状態を変更します。</remarks>
    /// <param name="isEnabled">新しい収集有効状態。</param>
    /// <param name="request">更新するService/PID。</param>
    /// <param name="vehicleID">更新対象の車両ID。</param>
    /// <exception cref="InvalidOperationException">対象が存在しない場合。</exception>
    public void SetCollectionEnabled(bool isEnabled, OBDPIDRequest request, VehicleID vehicleID)
    {
        _database.Write((connection, transaction) =>
        {
            var changes = SqliteDatabase.Execute(
                connection,
                transaction,
                "UPDATE vehicle_pid_capabilities SET isCollectionEnabled = $1 WHERE vehicleID = $2 AND service = $3 AND pid = $4",
                isEnabled ? 1 : 0,
                SqliteValueConversion.Key(vehicleID.RawValue),
                (int)request.Service,
                (int)request.Pid);
            if (changes != 1)
            {
                throw new InvalidOperationException("Vehicle PID capability was not found");
            }
        });
    }

    /// <summary>指定車両群のPID対応情報と収集選択を削除します。</summary>
    /// <remarks>責務: 登録車両ID群を車両別PID設定の原子的な一括削除へ変換します。</remarks>
    /// <param name="vehicleIDs">削除対象アカウントに登録されていた車両ID群。</param>
    public void DeleteCapabilities(IReadOnlyList<VehicleID> vehicleIDs)
    {
        if (vehicleIDs.Count == 0)
        {
            return;
        }
        _database.Write((connection, transaction) =>
        {
            foreach (var vehicleID in vehicleIDs)
            {
                SqliteDatabase.Execute(
                    connection,
                    transaction,
                    "DELETE FROM vehicle_pid_capabilities WHERE vehicleID = $1",
                    SqliteValueConversion.Key(vehicleID.RawValue));
            }
        });
    }

    /// <summary>1件の対応PIDを挿入します。</summary>
    /// <remarks>責務: 1件のDomain設定をSQLite列型へ写像して保存します。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="capability">保存する対応PID設定。</param>
    /// <param name="ignoreConflict">既存キーを無視する場合は <see langword="true"/>。</param>
    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, VehiclePIDCapability capability, bool ignoreConflict)
    {
        SqliteDatabase.Execute(
            connection,
            transaction,
            $"INSERT {(ignoreConflict ? "OR IGNORE " : "")}INTO vehicle_pid_capabilities (vehicleID, service, pid, isCollectionEnabled, discoveredAt) VALUES ($1, $2, $3, $4, $5)",
            SqliteValueConversion.Key(capability.Id.VehicleID.RawValue),
            (int)capability.Id.Request.Service,
            (int)capability.Id.Request.Pid,
            capability.IsCollectionEnabled ? 1 : 0,
            SqliteValueConversion.Ticks(capability.DiscoveredAt));
    }

    /// <summary>1行をDomain設定へ変換します。</summary>
    /// <remarks>責務: 検証済みSQLite列を車両別対応PID設定へ写像します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <returns>Domain設定。</returns>
    /// <exception cref="InvalidDataException">識別子が不正な場合。</exception>
    private static VehiclePIDCapability MakeDomainCapability(SqliteDataReader reader)
    {
        var service = reader.GetInt64(1);
        var pid = reader.GetInt64(2);
        if (!Guid.TryParse(reader.GetString(0), out var vehicle)
            || service is < 0 or > byte.MaxValue || pid is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException("Vehicle PID capability contains an invalid identifier");
        }
        return new VehiclePIDCapability(
            vehicleID: new VehicleID(vehicle),
            request: new OBDPIDRequest((byte)service, (byte)pid),
            isCollectionEnabled: reader.GetInt64(3) != 0,
            discoveredAt: SqliteValueConversion.Date(reader.GetInt64(4)));
    }
}
