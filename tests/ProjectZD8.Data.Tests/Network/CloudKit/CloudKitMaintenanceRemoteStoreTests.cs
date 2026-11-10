using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Network.CloudKit;

/// <summary>
/// 整備カタログの CloudKit への保存と取得を確かめます。
/// </summary>
public sealed class CloudKitMaintenanceRemoteStoreTests
{
    /// <summary>
    /// 保存した整備記録を読み戻せ、レコード名が iOS と同じであることを確かめます。
    /// </summary>
    /// <remarks>責務: MaintenanceCatalog の往復を確かめます。</remarks>
    [Fact]
    public async Task SaveThenFetchRoundTrips()
    {
        var database = new InMemoryCloudKitDatabase();
        var store = new CloudKitMaintenanceRemoteStore(database);
        var record = new MaintenanceRecord(
            VehicleID.New(),
            MaintenanceKind.Heavy,
            "タイミングベルト",
            performedAt: DateTimeOffset.FromUnixTimeSeconds(1000),
            createdAt: DateTimeOffset.FromUnixTimeSeconds(1000),
            updatedAt: DateTimeOffset.FromUnixTimeSeconds(1000));

        Assert.Empty(await store.FetchRecords("account"));
        await store.SaveRecords([record], "account");
        await store.SaveRecords([record], "account");

        Assert.Equal([record], await store.FetchRecords("account"));
        Assert.Equal(
            "maintenance-catalog-" + CloudKitAccountFingerprint.Of("account"),
            Assert.Single(database.Records.Keys));
    }

    /// <summary>
    /// ローカルモードでは空と何もしない、サインインしていなければ Unavailable になることを確かめます。
    /// </summary>
    /// <remarks>責務: 利用できない経路を確かめます。</remarks>
    [Fact]
    public async Task UnavailablePaths()
    {
        var database = new InMemoryCloudKitDatabase { Availability = CloudKitAvailability.NotConfigured };
        var store = new CloudKitMaintenanceRemoteStore(database);

        Assert.Empty(await store.FetchRecords("account"));
        await store.SaveRecords([], "account");

        database.Availability = CloudKitAvailability.SignedOut;
        var error = await Assert.ThrowsAsync<MaintenanceRepositoryException>(() => store.FetchRecords("account"));
        Assert.Equal(MaintenanceRepositoryError.Unavailable, error.Reason);
    }

    /// <summary>
    /// 既知のアカウント識別子の SHA-256 が、iOS の CryptoKit と同じ小文字 16 進になることを確かめます。
    /// </summary>
    /// <remarks>責務: フィンガープリントの計算を既知の値で確かめます。</remarks>
    [Fact]
    public void FingerprintMatchesKnownSha256()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            CloudKitAccountFingerprint.Of("abc"));
    }
}
