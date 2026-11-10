using ProjectZD8.Data.Persistence.Sqlite.Database;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>
/// 端末内のデータの持ち主（アカウント識別子）を付け替えた件数です。
/// </summary>
/// <param name="Vehicles">付け替えた車両の件数。</param>
/// <param name="VehiclePhotos">付け替えた車両写真の件数。</param>
/// <param name="ConnectionSessions">付け替えた接続セッションの件数（生ログ・取得証跡はセッションに付いて一緒に移る）。</param>
/// <param name="MaintenanceRecords">付け替えた整備記録の件数。</param>
public sealed record AccountDataReassignmentResult(
    int Vehicles,
    int VehiclePhotos,
    int ConnectionSessions,
    int MaintenanceRecords)
{
    /// <summary>何も付け替えなかったなら true。</summary>
    public bool IsEmpty => Vehicles == 0 && VehiclePhotos == 0 && ConnectionSessions == 0 && MaintenanceRecords == 0;
}

/// <summary>
/// 端末内の SQLite に保存した車両・写真・接続セッション・整備記録の持ち主を、別のアカウントへ付け替えます。
/// </summary>
/// <remarks>
/// ローカルモード（アカウントなし）で貯めたデータを、あとで Sign in with Apple のアカウントへ引き継ぐときに使います。
/// 生ログと取得の証跡（ログストア）はセッションの ID で引くので、セッションの持ち主を変えれば一緒に移ります。
/// 付け替え先に同じ ID の車両がすでにあれば、その車両（と写真）は付け替えずに元の持ち主に残します（上書きしない）。
/// </remarks>
public sealed class SqliteAccountDataReassignment
{
    /// <summary>共有の SQLite 接続です。</summary>
    private readonly SqliteDatabase _database;

    /// <summary>
    /// 共有の SQLite 接続を受け取ります。
    /// </summary>
    /// <remarks>責務: 付け替えに使う接続を保持します。</remarks>
    /// <param name="database">アプリの SQLite。</param>
    public SqliteAccountDataReassignment(SqliteDatabase database) => _database = database;

    /// <summary>
    /// アプリのデータのフォルダの SQLite を開いて作ります。
    /// </summary>
    /// <remarks>責務: ほかのリポジトリと同じ共有接続で付け替えの実装を作ります。</remarks>
    /// <param name="directory">データのフォルダ。</param>
    /// <returns>付け替えの実装。</returns>
    public static SqliteAccountDataReassignment OpenApplicationRepository(string directory) =>
        new(SqliteDatabase.OpenApplicationDatabase(directory));

    /// <summary>
    /// 1 回のトランザクションで、データの持ち主を付け替えます。
    /// </summary>
    /// <remarks>
    /// 責務: 4 つの表の accountIdentifier を from から to へ書き換えます。
    /// 車両と写真は (accountIdentifier, id) の外部キーで結ばれているので、外部キーの検査をトランザクションの終わりまで遅らせます。
    /// 途中で失敗したら何も変えません。
    /// </remarks>
    /// <param name="fromAccountIdentifier">今の持ち主（ローカルモードのアカウント）。</param>
    /// <param name="toAccountIdentifier">新しい持ち主（Apple のアカウント）。</param>
    /// <returns>付け替えた件数。</returns>
    /// <exception cref="ArgumentException">どちらかが空か、同じ場合。</exception>
    public AccountDataReassignmentResult Reassign(string fromAccountIdentifier, string toAccountIdentifier)
    {
        if (string.IsNullOrWhiteSpace(fromAccountIdentifier) || string.IsNullOrWhiteSpace(toAccountIdentifier))
        {
            throw new ArgumentException("アカウント識別子が空です。");
        }

        if (string.Equals(fromAccountIdentifier, toAccountIdentifier, StringComparison.Ordinal))
        {
            throw new ArgumentException("付け替え元と付け替え先が同じです。");
        }

        return _database.Write((connection, transaction) =>
        {
            SqliteDatabase.Execute(connection, transaction, "PRAGMA defer_foreign_keys = ON;");
            var vehicles = SqliteDatabase.Execute(
                connection,
                transaction,
                "UPDATE OR IGNORE vehicles SET accountIdentifier = $1 WHERE accountIdentifier = $2;",
                toAccountIdentifier,
                fromAccountIdentifier);
            var photos = SqliteDatabase.Execute(
                connection,
                transaction,
                """
                UPDATE OR IGNORE vehicle_photos SET accountIdentifier = $1
                WHERE accountIdentifier = $2
                  AND EXISTS (SELECT 1 FROM vehicles WHERE vehicles.accountIdentifier = $1 AND vehicles.id = vehicle_photos.vehicleID)
                  AND NOT EXISTS (SELECT 1 FROM vehicles WHERE vehicles.accountIdentifier = $2 AND vehicles.id = vehicle_photos.vehicleID);
                """,
                toAccountIdentifier,
                fromAccountIdentifier);
            var sessions = SqliteDatabase.Execute(
                connection,
                transaction,
                "UPDATE connection_sessions SET accountIdentifier = $1 WHERE accountIdentifier = $2;",
                toAccountIdentifier,
                fromAccountIdentifier);
            var maintenance = SqliteDatabase.Execute(
                connection,
                transaction,
                "UPDATE maintenance_records SET accountIdentifier = $1 WHERE accountIdentifier = $2;",
                toAccountIdentifier,
                fromAccountIdentifier);
            return new AccountDataReassignmentResult(vehicles, photos, sessions, maintenance);
        });
    }
}
