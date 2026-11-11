using System.Net;
using Microsoft.Extensions.Logging;
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Configuration;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Persistence.LogStore;
using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Diagnostics;

/// <summary>
/// 保存（SQLite・生ログストア）・認証・同期の失敗が、診断ログに固定のイベント名と例外つきで残り、
/// 伏せるべき値が出ていないことを確かめます。
/// </summary>
[Collection(DiagnosticSinkCollection.Name)]
public sealed class StoreSyncDiagnosticsTests
{
    /// <summary>テスト用のセッション ID です。</summary>
    private static readonly ConnectionSessionID SessionID = new(Guid.Parse("40000000-0000-0000-0000-000000000004"));

    /// <summary>テスト用の CloudKit の設定です。</summary>
    private static readonly CloudServiceConfiguration Configuration = new()
    {
        AppleServicesId = "com.example.web",
        AppleRedirectUri = new Uri("https://example.com/apple-callback.html"),
        CloudKitContainerId = "iCloud.Example.Container",
        CloudKitEnvironment = "development",
        CloudKitApiToken = "secret-api-token",
    };

    /// <summary>存在しないフォルダーの SQLite を開けないとき、Error と例外が残り、パスが伏せられることを確かめます。</summary>
    [Fact]
    public void SqliteOpenFailureWritesErrorWithExceptionAndRedactedPath()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "missing-folder", "projectzd8.sqlite");

        Assert.ThrowsAny<Exception>(() => SqliteDatabase.OpenFile(path));

        var failed = Assert.Single(sink.Named("store.sqlite.database.open.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal(DiagnosticCategories.StoreSqlite, failed.Category);
        Assert.NotNull(failed.Exception);
        Assert.Contains("SqliteException", failed.Exception.Type, StringComparison.Ordinal);
        Assert.DoesNotContain(directory.Path, sink.Dump(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("projectzd8.sqlite", sink.Dump(), StringComparison.Ordinal);
    }

    /// <summary>スキーマの移行が、適用した 1 件ごとに Debug で残ることを確かめます。</summary>
    [Fact]
    public void MigrationWritesOneDebugEventPerAppliedMigration()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        using var database = SqliteDatabase.OpenInMemory();

        ProjectZD8DatabaseMigrator.Migrate(database);
        ProjectZD8DatabaseMigrator.Migrate(database);

        var applied = sink.Named("store.sqlite.migration.applied");
        Assert.Equal(ProjectZD8DatabaseMigrator.Migrations.Count, applied.Count);
        Assert.All(applied, item => Assert.Equal(LogLevel.Debug, item.Level));
        Assert.Empty(sink.Named("store.sqlite.migration.failed"));
    }

    /// <summary>書きかけの末尾を切り詰めて開き直すと、切った長さつきの Warning が残ることを確かめます。</summary>
    [Fact]
    public void LogStoreTornTailTruncationWritesWarningWithLengths()
    {
        using var directory = new TemporaryDirectory();
        var segment = WriteAndAbandon(directory.Path, 20, out var lastEntry);
        var validLength = new FileInfo(segment).Length;
        var torn = SessionLogRecordCodec.EncodeFrame(new SessionLogRecord.Raw(lastEntry));
        using (var stream = new FileStream(segment, FileMode.Append, FileAccess.Write))
        {
            stream.Write(torn, 0, torn.Length / 2);
        }
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);

        using (var store = ConnectionSessionLogStore.Open(directory.Path))
        {
            _ = store.Get(SessionID);
        }

        var truncated = Assert.Single(sink.Named("store.log.tail.truncated"));
        Assert.Equal(LogLevel.Warning, truncated.Level);
        Assert.Equal(DiagnosticCategories.StoreLog, truncated.Category);
        Assert.Equal(validLength + (torn.Length / 2), Property<long>(truncated, "FromBytes"));
        Assert.Equal(validLength, Property<long>(truncated, "ToBytes"));
        Assert.Equal(SessionID.RawValue.ToString("D"), Property<string>(truncated, "Session"));
        Assert.Single(sink.Named("store.log.open.scanned"));
        Assert.DoesNotContain(directory.Path, sink.Dump(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>きれいに閉じたログを開き直すと、索引ファイルから開いたことが Debug で残ることを確かめます。</summary>
    [Fact]
    public void LogStoreCleanReopenWritesIndexedDebugEvent()
    {
        using var directory = new TemporaryDirectory();
        using (var store = ConnectionSessionLogStore.Open(directory.Path))
        {
            var log = store.Get(SessionID);
            log.AppendRaw(Observation(1));
            log.WaitDurable();
        }
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);

        using (var store = ConnectionSessionLogStore.Open(directory.Path))
        {
            _ = store.Get(SessionID);
        }

        var opened = Assert.Single(sink.Named("store.log.open.indexed"));
        Assert.Equal(LogLevel.Debug, opened.Level);
        Assert.Empty(sink.Named("store.log.tail.truncated"));
    }

    /// <summary>CloudKit の HTTP エラーが、操作・レコード種別・状態つきの Error と例外で残り、トークンが出ないことを確かめます。</summary>
    [Fact]
    public async Task CloudKitHttpErrorWritesRequestFailedWithoutTokens()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.InternalServerError,
            """{"serverErrorCode":"INTERNAL_ERROR","reason":"boom"}"""));
        var tokens = new MemoryTokenStore { Token = "web-auth-token+/=" };
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, tokens);

        await Assert.ThrowsAsync<CloudKitException>(() => client.QueryRecords("VehicleCatalog", "accountFingerprint", "fp"));

        var failed = Assert.Single(sink.Named("sync.request.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal(DiagnosticCategories.Sync, failed.Category);
        Assert.Equal("private/records/query", Property<string>(failed, "Operation"));
        Assert.Equal("VehicleCatalog", Property<string>(failed, "RecordType"));
        Assert.Equal(500, Property<int>(failed, "Status"));
        Assert.Equal("INTERNAL_ERROR", Property<string>(failed, "ServerErrorCode"));
        Assert.Contains("CloudKitException", failed.Exception!.Type, StringComparison.Ordinal);
        var dump = sink.Dump();
        Assert.DoesNotContain("secret-api-token", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("web-auth-token", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("ckWebAuthToken", dump, StringComparison.Ordinal);
    }

    /// <summary>CloudKit の要求が成功すると、操作・件数・状態・所要時間が Debug で残ることを確かめます。</summary>
    [Fact]
    public async Task CloudKitSuccessWritesDebugWithCount()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"records":[{"recordName":"r1","recordType":"VehicleCatalog","fields":{}},{"recordName":"r2","recordType":"VehicleCatalog","fields":{}}]}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore { Token = "t" });

        await client.QueryRecords("VehicleCatalog", "accountFingerprint", "fp");

        var completed = Assert.Single(sink.Named("sync.request.completed"));
        Assert.Equal(LogLevel.Debug, completed.Level);
        Assert.Equal(2, Property<int>(completed, "Count"));
        Assert.Equal(200, Property<int>(completed, "Status"));
        Assert.True(Property<long>(completed, "ElapsedMs") >= 0);
    }

    /// <summary>ローカルモードのデータを引き継げなかったとき、Warning と例外が残り、アカウント ID が伏せられることを確かめます。</summary>
    [Fact]
    public void LocalDataAdoptionFailureWritesWarningWithRedactedAccountIds()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        using var directory = new TemporaryDirectory();
        var defaults = new JsonFileLocalKeyValueStore(Path.Combine(directory.Path, "preferences.json"));
        defaults.SetString(LocalAccountAuthorizationAdapter.IdentifierKey, "local-account-identifier");
        var port = new LocalAccountDataAdoptingAuthorizationPort(
            inner: null!,
            defaults,
            reassign: (_, _) => throw new InvalidOperationException("reassign failed"),
            settings: null!);

        var adopted = port.AdoptLocalDataIfNeeded("apple-subject-identifier");

        Assert.False(adopted);
        var failed = Assert.Single(sink.Named("auth.local-data.adopt.failed"));
        Assert.Equal(LogLevel.Warning, failed.Level);
        Assert.Equal(DiagnosticCategories.Auth, failed.Category);
        Assert.Equal("System.InvalidOperationException", failed.Exception!.Type);
        Assert.Equal(Redact.AccountId("local-account-identifier"), Property<string>(failed, "FromAccount"));
        Assert.Equal(Redact.AccountId("apple-subject-identifier"), Property<string>(failed, "ToAccount"));
        var dump = sink.Dump();
        Assert.DoesNotContain("local-account-identifier", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("apple-subject-identifier", dump, StringComparison.Ordinal);
    }

    /// <summary>端末内の設定ファイルが壊れているとき、Warning が残りパスが伏せられることを確かめます。</summary>
    [Fact]
    public void CorruptPreferencesFileWritesWarning()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "preferences.json");
        File.WriteAllText(file, "{ not json");

        var store = new JsonFileLocalKeyValueStore(file);
        _ = store.GetString("anything");

        var unreadable = Assert.Single(sink.Named("app.preferences.local-store.unreadable"));
        Assert.Equal(LogLevel.Warning, unreadable.Level);
        Assert.NotNull(unreadable.Exception);
        Assert.DoesNotContain(directory.Path, sink.Dump(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>イベントの値を型つきで取り出します。</summary>
    /// <typeparam name="T">値の型。</typeparam>
    /// <param name="item">イベント。</param>
    /// <param name="name">値の名前。</param>
    /// <returns>値。</returns>
    private static T Property<T>(DiagnosticEvent item, string name) =>
        (T)Convert.ChangeType(item.Properties.Single(pair => pair.Key == name).Value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>生ログを書いて、索引ファイルを書かずにストアを捨てます。</summary>
    /// <param name="directory">ストアのディレクトリー。</param>
    /// <param name="count">書く件数。</param>
    /// <param name="last">最後に書いたエントリ。</param>
    /// <returns>最初のセグメントのファイルのパス。</returns>
    private static string WriteAndAbandon(string directory, int count, out ConnectionSessionRawLogEntry last)
    {
        var store = ConnectionSessionLogStore.Open(directory);
        var log = store.Get(SessionID);
        last = null!;
        for (var index = 0; index < count; index++)
        {
            last = log.AppendRaw(Observation(index));
        }
        log.WaitDurable();
        store.Abandon();
        return SegmentFileFormat.PlainPath(Path.Combine(directory, SessionID.RawValue.ToString("D"), "g000001"), 0);
    }

    /// <summary>テスト用の未デコード応答を作ります。</summary>
    /// <param name="index">番号。</param>
    /// <returns>未デコード応答。</returns>
    private static OBDRawResponseObservation Observation(int index) =>
        new(TestDates.Unix(1_000 + index), (ulong)index, new OBDPIDRequest(0x01, 0x0C), [(byte)index, (byte)(index >> 8)]);

    /// <summary>メモリ上のトークンの保存先です。</summary>
    private sealed class MemoryTokenStore : ICloudKitWebAuthTokenStore
    {
        /// <summary>保存中のトークンです。</summary>
        public string? Token { get; set; }

        /// <inheritdoc />
        public string? Load() => Token;

        /// <inheritdoc />
        public void Save(string token) => Token = token;

        /// <inheritdoc />
        public void Remove() => Token = null;
    }
}
