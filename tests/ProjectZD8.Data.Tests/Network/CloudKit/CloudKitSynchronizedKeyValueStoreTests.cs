using System.Text;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Network.CloudKit;

/// <summary>
/// 設定とログアウト通知の CloudKit レコード（AccountSettings、AccountSessionRevocation）との対応を確かめます。
/// </summary>
public sealed class CloudKitSynchronizedKeyValueStoreTests
{
    /// <summary>テストのフィンガープリントです。</summary>
    private static readonly string Fingerprint = CloudKitAccountFingerprint.Of("account");

    /// <summary>
    /// 設定の保存が、account-settings-fp の settingsData と updatedAt になることを確かめます。
    /// </summary>
    /// <remarks>責務: 設定のキーとレコードの対応を確かめます。</remarks>
    [Fact]
    public async Task SettingsKeyIsWrittenAsAccountSettingsRecord()
    {
        var database = new InMemoryCloudKitDatabase();
        var store = new CloudKitSynchronizedKeyValueStore(database, time: null, context: null);
        var data = Encoding.UTF8.GetBytes("""{"appearance":"dark","automaticSessionUploadEnabled":false,"language":"english"}""");

        store.SetData($"settings.account.v1.{Fingerprint}", data);
        await store.SynchronizeAsync();

        var record = database.Records[$"account-settings-{Fingerprint}"];
        Assert.Equal("AccountSettings", record.RecordType);
        Assert.Equal(data, record.GetBytes("settingsData"));
        Assert.NotNull(record.GetTimestamp("updatedAt"));
    }

    /// <summary>
    /// ほかの端末が書いた失効の印を取得して、変わったキーを知らせることを確かめます。
    /// </summary>
    /// <remarks>責務: session-revocation-fp の marker の取得と変更の通知を確かめます。</remarks>
    [Fact]
    public async Task RemoteRevocationMarkerRaisesExternalChange()
    {
        var database = new InMemoryCloudKitDatabase();
        var store = new CloudKitSynchronizedKeyValueStore(database, time: null, context: null);
        var key = $"authentication.revocation.v1.{Fingerprint}";
        var changes = new List<string>();
        store.ChangedExternally += keys => changes.AddRange(keys);
        Assert.Null(store.Data(key));
        await store.SynchronizeAsync();

        database.Records[$"session-revocation-{Fingerprint}"] = new CloudKitRecord(
            $"session-revocation-{Fingerprint}",
            "AccountSessionRevocation",
            new Dictionary<string, CloudKitFieldValue>
            {
                ["marker"] = new CloudKitFieldValue.StringValue("2F1A8E2C-0000-4000-8000-000000000001"),
                ["revokedAt"] = new CloudKitFieldValue.TimestampValue(DateTimeOffset.UnixEpoch),
            });
        await store.SynchronizeAsync();

        Assert.Equal([key], changes);
        Assert.Equal("2F1A8E2C-0000-4000-8000-000000000001", Encoding.UTF8.GetString(store.Data(key)!));
    }

    /// <summary>
    /// 送信に失敗した変更が、次の同期で送り直されることを確かめます。
    /// </summary>
    /// <remarks>責務: 保留中の変更の再送を確かめます。</remarks>
    [Fact]
    public async Task FailedWritesAreRetried()
    {
        var database = new InMemoryCloudKitDatabase();
        var store = new CloudKitSynchronizedKeyValueStore(database, time: null, context: null);
        database.NextFailure = new HttpRequestException("offline");

        store.SetData($"authentication.revocation.v1.{Fingerprint}", Encoding.UTF8.GetBytes("M"));
        await store.SynchronizeAsync();
        Assert.Empty(database.Records);
        await store.SynchronizeAsync();

        Assert.Equal("M", database.Records[$"session-revocation-{Fingerprint}"].GetString("marker"));
    }

    /// <summary>
    /// 削除が CloudKit のレコードの削除になり、ほかのキーは送らないことを確かめます。
    /// </summary>
    /// <remarks>責務: RemoveObject と、同期の対象外のキーを確かめます。</remarks>
    [Fact]
    public async Task RemoveDeletesRecordAndUnknownKeysStayLocal()
    {
        var database = new InMemoryCloudKitDatabase();
        var store = new CloudKitSynchronizedKeyValueStore(database, time: null, context: null);
        var key = $"settings.account.v1.{Fingerprint}";
        store.SetData(key, [1]);
        store.SetData("other.key", [2]);
        await store.SynchronizeAsync();

        store.RemoveObject(key);
        await store.SynchronizeAsync();

        Assert.Empty(database.Records);
        Assert.Equal([2], store.Data("other.key"));
    }

    /// <summary>
    /// CloudKit を使えないときもメモリだけで動き、落ちないことを確かめます。
    /// </summary>
    /// <remarks>責務: 利用できない経路を確かめます。</remarks>
    [Fact]
    public async Task WorksInMemoryWhenUnavailable()
    {
        var database = new InMemoryCloudKitDatabase { Availability = CloudKitAvailability.SignedOut };
        var store = new CloudKitSynchronizedKeyValueStore(database, time: null, context: null);
        var key = $"settings.account.v1.{Fingerprint}";

        store.SetData(key, [1]);

        Assert.False(store.Synchronize());
        await store.SynchronizeAsync();
        Assert.Equal([1], store.Data(key));
    }
}
