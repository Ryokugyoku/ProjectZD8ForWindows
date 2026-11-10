using ProjectZD8.Data.Persistence.LogStore;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;

namespace ProjectZD8.Data.Tests.TestSupport;

/// <summary>メモリ上のSQLiteと一時ディレクトリーのLogStoreを使う接続セッション保存一式です。</summary>
/// <remarks>iOS版テストの <c>DatabaseQueue()</c>（メモリ上のDB）に相当します。</remarks>
internal sealed class ConnectionSessionStorageFixture : IDisposable
{
    /// <summary>一時ディレクトリーです。</summary>
    private readonly TemporaryDirectory _directory = new();

    /// <summary>保存一式を開きます。</summary>
    /// <remarks>責務: メモリ上のDBと一時LogStoreで保存一式を作ります。</remarks>
    /// <param name="options">LogStoreの設定。省略時は既定。</param>
    /// <param name="fileDatabase">ファイルDBを使う場合は <see langword="true"/>。</param>
    public ConnectionSessionStorageFixture(LogStoreOptions? options = null, bool fileDatabase = false)
    {
        Options = options ?? LogStoreOptions.Default;
        Database = fileDatabase
            ? SqliteDatabase.OpenFile(System.IO.Path.Combine(_directory.Path, "test.sqlite"))
            : SqliteDatabase.OpenInMemory();
        Repository = Open();
    }

    /// <summary>LogStoreの設定です。</summary>
    public LogStoreOptions Options { get; }

    /// <summary>SQLiteです。</summary>
    public SqliteDatabase Database { get; }

    /// <summary>保存一式です。</summary>
    public SqliteConnectionSessionRepository Repository { get; private set; }

    /// <summary>LogStoreのディレクトリーです。</summary>
    public string LogDirectory => System.IO.Path.Combine(_directory.Path, "logs");

    /// <summary>保存一式を閉じて開き直します（アプリの再起動に相当）。</summary>
    /// <remarks>責務: LogStoreを確定して閉じ、同じSQLiteと同じディレクトリーで開き直します。</remarks>
    /// <param name="close">閉じる前に索引ファイルを書く通常終了なら <see langword="true"/>、異常終了の再現なら <see langword="false"/>。</param>
    public void Reopen(bool close = true)
    {
        if (close)
        {
            Repository.Dispose();
        }
        else
        {
            Repository.AbandonForTesting();
        }
        Repository = Open();
    }

    /// <summary>保存一式を開きます。</summary>
    /// <remarks>責務: 同じDBとディレクトリーで新しい保存一式を作ります。</remarks>
    /// <returns>保存一式。</returns>
    private SqliteConnectionSessionRepository Open() =>
        new(Database, ConnectionSessionLogStore.Open(LogDirectory, Options));

    /// <summary>保存一式とDBを閉じ、ディレクトリーを消します。</summary>
    /// <remarks>責務: テストの後片付けをします。</remarks>
    public void Dispose()
    {
        Repository.Dispose();
        Database.Dispose();
        _directory.Dispose();
    }
}
