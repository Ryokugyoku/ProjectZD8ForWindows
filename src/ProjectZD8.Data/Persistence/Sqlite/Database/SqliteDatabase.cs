using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace ProjectZD8.Data.Persistence.Sqlite.Database;

/// <summary>1件のSQLite接続を直列化された読書き境界として提供します。</summary>
/// <remarks>
/// iOS版のGRDB <c>DatabaseQueue</c> に相当します。すべての読書きを1本の接続とロックで直列化し、
/// 書込みは1回のトランザクションで原子的に確定します。
/// </remarks>
public sealed class SqliteDatabase : IDisposable
{
    /// <summary>製品DBのファイル名です。</summary>
    public const string ApplicationDatabaseFileName = "projectzd8.sqlite";

    /// <summary>同じファイルを開いた共有インスタンスです。</summary>
    private static readonly ConcurrentDictionary<string, SqliteDatabase> SharedDatabases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>読書きを直列化するロックです。</summary>
    private readonly object _gate = new();

    /// <summary>開いたままにするSQLite接続です。</summary>
    private readonly SqliteConnection _connection;

    /// <summary>破棄済みかどうかです。</summary>
    private bool _isDisposed;

    /// <summary>接続文字列から接続を開き、接続ごとの設定を適用します。</summary>
    /// <remarks>責務: 1本のSQLite接続を外部キー有効・WAL・待機時間付きの利用可能状態へ固定します。</remarks>
    /// <param name="connectionString">SQLiteの接続文字列。</param>
    /// <param name="isFile">ファイルDBならWALを有効にします。</param>
    private SqliteDatabase(string connectionString, bool isFile)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Execute(_connection, null, "PRAGMA foreign_keys = ON;");
        Execute(_connection, null, "PRAGMA busy_timeout = 5000;");
        if (isFile)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteScalar();
            Execute(_connection, null, "PRAGMA synchronous = NORMAL;");
        }
    }

    /// <summary>メモリ上の新しいDBを開きます。</summary>
    /// <remarks>責務: テストや一時利用のため、破棄で消える独立したSQLite DBを生成します。</remarks>
    /// <returns>メモリ上のDB。</returns>
    public static SqliteDatabase OpenInMemory() => new("Data Source=:memory:", isFile: false);

    /// <summary>指定パスのファイルDBを開きます。</summary>
    /// <remarks>責務: 1件のSQLiteファイルを作成または開いて直列化境界を返します。</remarks>
    /// <param name="path">SQLiteファイルのパス。</param>
    /// <returns>ファイルDB。</returns>
    public static SqliteDatabase OpenFile(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        return new SqliteDatabase(builder.ToString(), isFile: true);
    }

    /// <summary>データディレクトリーの製品DBを、同じパスで共有されるインスタンスとして開きます。</summary>
    /// <remarks>責務: 組み立てが渡したディレクトリーを作成し、全リポジトリーが同じ接続を使えるよう製品DBを返します。</remarks>
    /// <param name="directory">アプリのデータディレクトリー。</param>
    /// <returns>プロセス内で共有される製品DB。</returns>
    public static SqliteDatabase OpenApplicationDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directory, ApplicationDatabaseFileName));
        return SharedDatabases.GetOrAdd(path, OpenFile);
    }

    /// <summary>読取処理をロック内で実行します。</summary>
    /// <remarks>責務: 1件の読取を他の読書きと直列化して実行します。</remarks>
    /// <typeparam name="T">読取結果の型。</typeparam>
    /// <param name="read">接続を受け取る読取処理。</param>
    /// <returns>読取処理の結果。</returns>
    internal T Read<T>(Func<SqliteConnection, T> read)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return read(_connection);
        }
    }

    /// <summary>書込み処理を1回のトランザクションで実行します。</summary>
    /// <remarks>責務: 1件の書込みを直列化し、例外時はすべて取り消し、成功時だけ確定します。</remarks>
    /// <typeparam name="T">書込み結果の型。</typeparam>
    /// <param name="write">接続とトランザクションを受け取る書込み処理。</param>
    /// <returns>書込み処理の結果。</returns>
    internal T Write<T>(Func<SqliteConnection, SqliteTransaction, T> write)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var transaction = _connection.BeginTransaction(deferred: false);
            var value = write(_connection, transaction);
            transaction.Commit();
            return value;
        }
    }

    /// <summary>書込み処理を1回のトランザクションで実行します。</summary>
    /// <remarks>責務: 戻り値のない書込みを <see cref="Write{T}"/> へ委譲します。</remarks>
    /// <param name="write">接続とトランザクションを受け取る書込み処理。</param>
    internal void Write(Action<SqliteConnection, SqliteTransaction> write) =>
        Write<object?>((connection, transaction) =>
        {
            write(connection, transaction);
            return null;
        });

    /// <summary>1件のSQL文を実行し、変更行数を返します。</summary>
    /// <remarks>責務: 引数を位置パラメーター（$1, $2 ...）へ束縛してSQLを実行します。</remarks>
    /// <param name="connection">実行する接続。</param>
    /// <param name="transaction">実行中のトランザクション。ない場合は <see langword="null"/>。</param>
    /// <param name="sql">実行するSQL。</param>
    /// <param name="arguments">位置パラメーターの値。</param>
    /// <returns>変更された行数。</returns>
    internal static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params object?[] arguments)
    {
        using var command = CreateCommand(connection, transaction, sql, arguments);
        return command.ExecuteNonQuery();
    }

    /// <summary>1件のSQLを実行し、先頭行の先頭列を返します。</summary>
    /// <remarks>責務: スカラー照会の結果をDBNullを除いた値へ変換します。</remarks>
    /// <param name="connection">実行する接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="sql">実行するSQL。</param>
    /// <param name="arguments">位置パラメーターの値。</param>
    /// <returns>先頭の値。行がないかNULLなら <see langword="null"/>。</returns>
    internal static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params object?[] arguments)
    {
        using var command = CreateCommand(connection, transaction, sql, arguments);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    /// <summary>1件のSQLを実行し、各行を変換した一覧を返します。</summary>
    /// <remarks>責務: 照会結果の全行を呼出元の変換で値へ写像します。</remarks>
    /// <typeparam name="T">1行の変換結果。</typeparam>
    /// <param name="connection">実行する接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="sql">実行するSQL。</param>
    /// <param name="map">1行を値へ変換する処理。</param>
    /// <param name="arguments">位置パラメーターの値。</param>
    /// <returns>行順の変換結果。</returns>
    internal static List<T> Query<T>(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        Func<SqliteDataReader, T> map,
        params object?[] arguments)
    {
        using var command = CreateCommand(connection, transaction, sql, arguments);
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            results.Add(map(reader));
        }
        return results;
    }

    /// <summary>位置パラメーター付きのコマンドを生成します。</summary>
    /// <remarks>責務: SQLと引数を1件のSQLiteコマンドへ束縛します。</remarks>
    /// <param name="connection">実行する接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="sql">実行するSQL。</param>
    /// <param name="arguments">位置パラメーターの値。</param>
    /// <returns>実行可能なコマンド。</returns>
    private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction? transaction, string sql, object?[] arguments)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        for (var index = 0; index < arguments.Length; index++)
        {
            command.Parameters.AddWithValue("$" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), arguments[index] ?? DBNull.Value);
        }
        return command;
    }

    /// <summary>破棄済みなら例外を投げます。</summary>
    /// <remarks>責務: 破棄後の接続利用を明示的な失敗へ変換します。</remarks>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    /// <summary>接続を閉じます。</summary>
    /// <remarks>責務: 共有表から外し、SQLite接続を解放します。</remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            foreach (var pair in SharedDatabases)
            {
                if (ReferenceEquals(pair.Value, this))
                {
                    SharedDatabases.TryRemove(pair.Key, out _);
                }
            }
            _connection.Dispose();
        }
    }
}
