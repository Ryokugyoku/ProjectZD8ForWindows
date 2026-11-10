using ProjectZD8.Data.Mapping;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Network.CloudKit;

/// <summary>
/// 端末内を正とした車両カタログの同期（3 者の比較）を確かめます。
/// </summary>
public sealed class CloudKitVehicleRepositoryTests
{
    /// <summary>テストのアカウントです。</summary>
    private const string Account = "account";

    /// <summary>
    /// 端末内で追加した車両が CloudKit のカタログ（iOS と同じ JSON）として送られることを確かめます。
    /// </summary>
    /// <remarks>責務: 保存の後に VehicleCatalog の catalogAsset が更新されることを確かめます。</remarks>
    [Fact]
    public async Task SaveUploadsCatalogInSwiftFormat()
    {
        var (repository, local, database) = Make();
        var vehicle = Vehicle("A", 100);

        await repository.SaveVehicle(vehicle, Account);

        var record = database.Records[CloudKitVehicleRepository.RecordName(Account)];
        Assert.Equal("VehicleCatalog", record.RecordType);
        var catalog = CloudKitPayloadJsonCodec.DecodeVehicleCatalog(database.Assets[record.GetAsset("catalogAsset")!.DownloadUrl!]);
        Assert.Equal([vehicle], catalog);
        Assert.Equal([vehicle], await local.LoadVehicles(Account));
    }

    /// <summary>
    /// ほかの端末で追加・更新された車両を取り込み、新しい方を採ることを確かめます。
    /// </summary>
    /// <remarks>責務: CloudKit にだけある車両の取り込みと、更新日時での選択を確かめます。</remarks>
    [Fact]
    public async Task LoadAdoptsRemoteAdditionsAndNewerEdits()
    {
        var (repository, local, database) = Make();
        var shared = Vehicle("Shared", 100);
        await repository.SaveVehicle(shared, Account);
        var remoteEdit = shared with { Name = "Edited on iPhone", UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(200) };
        var remoteNew = Vehicle("From iPhone", 150);
        PutRemoteCatalog(database, [remoteEdit, remoteNew]);

        var loaded = await repository.LoadVehicles(Account);

        Assert.Equal([remoteEdit, remoteNew], loaded);
        Assert.Equal(2, (await local.LoadVehicles(Account)).Count);
        Assert.Contains(remoteEdit, await local.LoadVehicles(Account));
    }

    /// <summary>
    /// ほかの端末で消された車両をこの端末からも消し、この端末で消した車両を CloudKit からも消すことを確かめます。
    /// </summary>
    /// <remarks>責務: 前回同期した ID の一覧を使った削除の判定を確かめます。</remarks>
    [Fact]
    public async Task DeletionsPropagateBothWays()
    {
        var (repository, local, database) = Make();
        var keep = Vehicle("Keep", 100);
        var deletedRemotely = Vehicle("Deleted on iPhone", 110);
        var deletedLocally = Vehicle("Deleted here", 120);
        foreach (var vehicle in new[] { keep, deletedRemotely, deletedLocally })
        {
            await repository.SaveVehicle(vehicle, Account);
        }
        PutRemoteCatalog(database, [keep, deletedLocally]);
        await local.DeleteVehicle(deletedLocally.Id, Account);

        var loaded = await repository.LoadVehicles(Account);

        Assert.Equal([keep], loaded);
        Assert.Equal([keep], await local.LoadVehicles(Account));
        var record = database.Records[CloudKitVehicleRepository.RecordName(Account)];
        Assert.Equal([keep], CloudKitPayloadJsonCodec.DecodeVehicleCatalog(database.Assets[record.GetAsset("catalogAsset")!.DownloadUrl!]));
    }

    /// <summary>
    /// CloudKit を使えない・通信に失敗したときは、端末内の一覧だけを返し、落ちないことを確かめます。
    /// </summary>
    /// <remarks>責務: ローカルだけの経路を確かめます。</remarks>
    [Fact]
    public async Task FallsBackToLocalWhenCloudUnavailableOrFailing()
    {
        var (repository, local, database) = Make();
        database.Availability = CloudKitAvailability.NotConfigured;
        var vehicle = Vehicle("Offline", 100);

        await repository.SaveVehicle(vehicle, Account);
        Assert.Empty(database.Records);
        Assert.Equal([vehicle], await repository.LoadVehicles(Account));

        database.Availability = CloudKitAvailability.Available;
        database.NextFailure = new HttpRequestException("offline");
        Assert.Equal([vehicle], await repository.LoadVehicles(Account));
        Assert.Equal([vehicle.Id], repository.LocalVehicleIDs(Account));
    }

    /// <summary>
    /// 端末内の車両を消すと、その端末のアカウントの車両がなくなることを確かめます。
    /// </summary>
    /// <remarks>責務: RemoveLocalVehicleCache と DeleteAllVehicleData を確かめます。</remarks>
    [Fact]
    public async Task ErasureRemovesLocalAndRemoteCatalog()
    {
        var (repository, local, database) = Make();
        await repository.SaveVehicle(Vehicle("A", 1), Account);

        repository.RemoveLocalVehicleCache(Account);
        await repository.DeleteAllVehicleData(Account);

        Assert.Empty(await local.LoadVehicles(Account));
        Assert.Empty(database.Records);
    }

    /// <summary>
    /// テストの対象と依存を作ります。
    /// </summary>
    /// <remarks>責務: メモリ上の端末内リポジトリと CloudKit で対象を組み立てます。</remarks>
    /// <returns>対象、端末内、CloudKit。</returns>
    private static (CloudKitVehicleRepository Repository, InMemoryVehicleRepository Local, InMemoryCloudKitDatabase Database) Make()
    {
        var local = new InMemoryVehicleRepository();
        var database = new InMemoryCloudKitDatabase();
        return (new CloudKitVehicleRepository(local, database, new InMemoryLocalKeyValueStore()), local, database);
    }

    /// <summary>
    /// ほかの端末が書いたカタログを置きます。
    /// </summary>
    /// <remarks>責務: CloudKit のカタログを直接書き換えます。</remarks>
    /// <param name="database">CloudKit。</param>
    /// <param name="vehicles">車両。</param>
    private static void PutRemoteCatalog(InMemoryCloudKitDatabase database, IReadOnlyList<VehicleProfile> vehicles)
    {
        var url = $"https://assets.test/remote/{Guid.NewGuid():N}";
        database.Assets[url] = CloudKitPayloadJsonCodec.EncodeVehicleCatalog(vehicles);
        var name = CloudKitVehicleRepository.RecordName(Account);
        database.Records[name] = new CloudKitRecord(
            name,
            "VehicleCatalog",
            new Dictionary<string, CloudKitFieldValue> { ["catalogAsset"] = new CloudKitFieldValue.AssetValue(url, null, null) },
            "remote");
    }

    /// <summary>
    /// テスト用の車両を作ります。
    /// </summary>
    /// <remarks>責務: 名前と更新日時の決まった車両を作ります。</remarks>
    /// <param name="name">名前。</param>
    /// <param name="updatedAtSeconds">更新日時（1970 年からの秒）。</param>
    /// <returns>車両。</returns>
    private static VehicleProfile Vehicle(string name, long updatedAtSeconds) =>
        new(vin: "", name: name, updatedAt: DateTimeOffset.FromUnixTimeSeconds(updatedAtSeconds));

    /// <summary>
    /// メモリ上の端末内の車両リポジトリです（SQLite の代わり）。
    /// </summary>
    private sealed class InMemoryVehicleRepository : IVehicleRepository
    {
        /// <summary>アカウントごとの車両です。</summary>
        private readonly Dictionary<string, List<VehicleProfile>> _vehicles = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<VehicleProfile>>(
                (_vehicles.TryGetValue(accountIdentifier, out var list) ? list : []).OrderByDescending(v => v.UpdatedAt).ToArray());

        /// <inheritdoc />
        public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier)
        {
            var list = _vehicles.TryGetValue(accountIdentifier, out var existing) ? existing : _vehicles[accountIdentifier] = [];
            list.RemoveAll(v => v.Id == vehicle.Id);
            list.Add(vehicle);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteVehicle(VehicleID id, string accountIdentifier)
        {
            if (_vehicles.TryGetValue(accountIdentifier, out var list))
            {
                list.RemoveAll(v => v.Id == id);
            }
            return Task.CompletedTask;
        }
    }
}
