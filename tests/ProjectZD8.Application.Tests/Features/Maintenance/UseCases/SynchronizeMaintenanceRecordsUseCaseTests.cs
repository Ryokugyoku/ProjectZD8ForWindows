using ProjectZD8.Application.Maintenance.Ports;
using ProjectZD8.Application.Maintenance.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Maintenance.UseCases;

/// <summary>車両別整備記録の双方向統合規則を検証します。</summary>
public sealed class SynchronizeMaintenanceRecordsUseCaseTests
{
    /// <summary>同じ記録IDでは更新日時が新しい側を採用します。</summary>
    /// <remarks>責務: iPhoneとMacで同一記録を編集した場合に新しい変更だけが残ることを確認します。</remarks>
    [Fact]
    public void MergeUsesNewestVersionForSameRecord()
    {
        var id = MaintenanceRecordID.New();
        var vehicleID = VehicleID.New();
        var older = new MaintenanceRecord(
            id: id,
            vehicleID: vehicleID,
            kind: MaintenanceKind.Light,
            title: "Older",
            updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        var newer = new MaintenanceRecord(
            id: id,
            vehicleID: vehicleID,
            kind: MaintenanceKind.Light,
            title: "Newer",
            updatedAt: DateTimeOffset.FromUnixTimeSeconds(200));

        var merged = SynchronizeMaintenanceRecordsUseCase.Merge(local: [newer], remote: [older]);

        Assert.Equal(["Newer"], merged.Select(record => record.Title));
    }

    /// <summary>新しい削除墓石が古い実体より優先されます。</summary>
    /// <remarks>責務: 一方の端末で削除した記録が他端末の古い内容から復活しないことを確認します。</remarks>
    [Fact]
    public void MergePreservesNewerDeletionTombstone()
    {
        var id = MaintenanceRecordID.New();
        var vehicleID = VehicleID.New();
        var active = new MaintenanceRecord(
            id: id,
            vehicleID: vehicleID,
            kind: MaintenanceKind.Heavy,
            title: "Engine overhaul",
            updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
        var deletedAt = DateTimeOffset.FromUnixTimeSeconds(300);
        var deleted = active with { UpdatedAt = deletedAt, DeletedAt = deletedAt };

        var merged = SynchronizeMaintenanceRecordsUseCase.Merge(local: [active], remote: [deleted]);

        Assert.NotEmpty(merged);
        Assert.Equal(deletedAt, merged[0].DeletedAt);
    }

    /// <summary>異なる車両の記録をID単位で混在させず保持します。</summary>
    /// <remarks>責務: 複数車両の軽整備と重整備が双方向統合後も各車両IDへ紐づくことを確認します。</remarks>
    [Fact]
    public void MergeKeepsRecordsForEveryVehicle()
    {
        var firstVehicle = VehicleID.New();
        var secondVehicle = VehicleID.New();
        var light = new MaintenanceRecord(vehicleID: firstVehicle, kind: MaintenanceKind.Light, title: "Oil");
        var heavy = new MaintenanceRecord(vehicleID: secondVehicle, kind: MaintenanceKind.Heavy, title: "Gearbox");

        var merged = SynchronizeMaintenanceRecordsUseCase.Merge(local: [light], remote: [heavy]);

        Assert.Equal(
            new HashSet<VehicleID> { firstVehicle, secondVehicle },
            merged.Select(record => record.VehicleID).ToHashSet());
    }

    /// <summary>双方向統合結果をローカルと遠隔の両方へ同じ内容で保存します。</summary>
    /// <remarks>責務: 1回の同期がiPhone側記録とMac側記録を両保存先へ反映することを確認します。</remarks>
    [Fact]
    public async Task ExecutePersistsMergedRecordsToBothStores()
    {
        var localRecord = new MaintenanceRecord(vehicleID: VehicleID.New(), kind: MaintenanceKind.Light, title: "Oil");
        var remoteRecord = new MaintenanceRecord(vehicleID: VehicleID.New(), kind: MaintenanceKind.Heavy, title: "Engine");
        var local = new MaintenanceRecordRepositoryFake([localRecord]);
        var remote = new MaintenanceRemoteStoreFake([remoteRecord]);
        var useCase = new SynchronizeMaintenanceRecordsUseCase(local, remote);

        var visible = await useCase.Execute(accountIdentifier: "account");

        var expected = new HashSet<MaintenanceRecordID> { localRecord.Id, remoteRecord.Id };
        Assert.Equal(expected, visible.Select(record => record.Id).ToHashSet());
        Assert.Equal(expected, local.StoredRecords.Select(record => record.Id).ToHashSet());
        Assert.Equal(expected, remote.StoredRecords.Select(record => record.Id).ToHashSet());
    }

    /// <summary>同期テスト用の端末内整備保存先です。</summary>
    private sealed class MaintenanceRecordRepositoryFake : IMaintenanceRecordRepository
    {
        /// <summary>初期の端末内記録を保持します。</summary>
        /// <remarks>責務: 同期テストのローカル初期集合を固定します。</remarks>
        /// <param name="records">初期整備記録集合。</param>
        public MaintenanceRecordRepositoryFake(IReadOnlyList<MaintenanceRecord> records)
        {
            StoredRecords = records;
        }

        /// <summary>現在保持する整備記録です。</summary>
        public IReadOnlyList<MaintenanceRecord> StoredRecords { get; private set; }

        /// <summary>固定した端末内記録を返します。</summary>
        /// <remarks>責務: 同期要求へ現在のローカル集合を返します。</remarks>
        /// <param name="accountIdentifier">テストで受け取るアカウント識別子。</param>
        /// <returns>現在の端末内記録集合。</returns>
        public Task<IReadOnlyList<MaintenanceRecord>> Records(string accountIdentifier) => Task.FromResult(StoredRecords);

        /// <summary>1件の端末内記録をupsertします。</summary>
        /// <remarks>責務: 保存要求1件をテスト用ローカル集合へ反映します。</remarks>
        /// <param name="record">保存する整備記録。</param>
        /// <param name="accountIdentifier">テストで受け取るアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task Save(MaintenanceRecord record, string accountIdentifier)
        {
            StoredRecords = [.. StoredRecords.Where(stored => stored.Id != record.Id), record];
            return Task.CompletedTask;
        }

        /// <summary>同期済み集合で端末内記録を置き換えます。</summary>
        /// <remarks>責務: 同期結果をテスト用ローカル集合へそのまま保存します。</remarks>
        /// <param name="records">同期済み整備記録集合。</param>
        /// <param name="accountIdentifier">テストで受け取るアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task ReplaceAll(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier)
        {
            StoredRecords = records;
            return Task.CompletedTask;
        }
    }

    /// <summary>同期テスト用の遠隔整備保存先です。</summary>
    private sealed class MaintenanceRemoteStoreFake : IMaintenanceRemoteStore
    {
        /// <summary>初期の遠隔記録を保持します。</summary>
        /// <remarks>責務: 同期テストの遠隔初期集合を固定します。</remarks>
        /// <param name="records">初期整備記録集合。</param>
        public MaintenanceRemoteStoreFake(IReadOnlyList<MaintenanceRecord> records)
        {
            StoredRecords = records;
        }

        /// <summary>現在保持する遠隔整備記録です。</summary>
        public IReadOnlyList<MaintenanceRecord> StoredRecords { get; private set; }

        /// <summary>固定した遠隔記録を返します。</summary>
        /// <remarks>責務: 同期要求へ現在の遠隔集合を返します。</remarks>
        /// <param name="accountIdentifier">テストで受け取るアカウント識別子。</param>
        /// <returns>現在の遠隔記録集合。</returns>
        public Task<IReadOnlyList<MaintenanceRecord>> FetchRecords(string accountIdentifier) => Task.FromResult(StoredRecords);

        /// <summary>同期済み集合で遠隔記録を置き換えます。</summary>
        /// <remarks>責務: 同期結果をテスト用遠隔集合へそのまま保存します。</remarks>
        /// <param name="records">同期済み整備記録集合。</param>
        /// <param name="accountIdentifier">テストで受け取るアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task SaveRecords(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier)
        {
            StoredRecords = records;
            return Task.CompletedTask;
        }
    }
}
