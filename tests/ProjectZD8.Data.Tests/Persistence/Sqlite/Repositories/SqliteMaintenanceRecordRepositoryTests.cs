using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>整備記録リポジトリを検証します。</summary>
/// <remarks>iOS版 <c>GRDBMaintenanceRecordRepositoryTests</c> の移植と、置換・並び順の追加テストです。</remarks>
public sealed class SqliteMaintenanceRecordRepositoryTests
{
    /// <summary>重整備の写真と締結証跡が往復することを確かめます。</summary>
    [Fact]
    public async Task HeavyRecordRoundTripsPhotoAndFastenerEvidence()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteMaintenanceRecordRepository(database);
        var photo = new MaintenancePhoto([0x01, 0x02], caption: "Before");
        var evidence = new FastenerInstallationEvidence(
            position: "Cylinder head A1",
            torqueNewtonMeters: 30,
            tool: "Torque wrench #1",
            tightenedBy: "Technician",
            tightenedAt: TestDates.Unix(500),
            photoIDs: [photo.Id]);
        var record = new MaintenanceRecord(
            vehicleID: VehicleID.New(),
            kind: MaintenanceKind.Heavy,
            title: "Engine overhaul",
            workItems: [new MaintenanceWorkItem(MaintenanceComponent.EngineAssembly, MaintenanceOperation.Overhaul)],
            photos: [photo],
            fastenerGroups:
            [
                new MaintenanceFastenerGroup(
                    name: "Cylinder head",
                    location: "Bank A",
                    expectedCount: 10,
                    removedCount: 10,
                    installations: [evidence]),
            ]);

        await repository.Save(record, "account-a");
        var records = await repository.Records("account-a");
        var restored = Assert.Single(records);

        Assert.Equal(record, restored);
        Assert.Equal(30, restored.FastenerGroups[0].Installations[0].TorqueNewtonMeters);
        Assert.Equal([photo.Id], restored.FastenerGroups[0].Installations[0].PhotoIDs);
    }

    /// <summary>記録がアカウントごとに分かれることを確かめます。</summary>
    [Fact]
    public async Task RecordsAreAccountScoped()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteMaintenanceRecordRepository(database);
        var first = new MaintenanceRecord(VehicleID.New(), MaintenanceKind.Light, "First");
        var second = new MaintenanceRecord(VehicleID.New(), MaintenanceKind.Heavy, "Second");

        await repository.Save(first, "account-a");
        await repository.Save(second, "account-b");

        Assert.Equal(["First"], (await repository.Records("account-a")).Select(record => record.Title));
        Assert.Equal(["Second"], (await repository.Records("account-b")).Select(record => record.Title));
    }

    /// <summary>置換が同じアカウントの全件を入れ替え、更新日時の新しい順で返ることを確かめます。</summary>
    [Fact]
    public async Task ReplaceAllReplacesAccountRecordsAndOrdersByUpdatedAtDescending()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteMaintenanceRecordRepository(database);
        await repository.Save(new MaintenanceRecord(VehicleID.New(), MaintenanceKind.Light, "Old"), "account-a");
        await repository.Save(new MaintenanceRecord(VehicleID.New(), MaintenanceKind.Light, "Other"), "account-b");
        var older = new MaintenanceRecord(VehicleID.New(), MaintenanceKind.Light, "Older", updatedAt: TestDates.Unix(100));
        var newer = new MaintenanceRecord(VehicleID.New(), MaintenanceKind.Heavy, "Newer", updatedAt: TestDates.Unix(200), deletedAt: TestDates.Unix(200));

        await repository.ReplaceAll([older, newer], "account-a");

        Assert.Equal([newer, older], await repository.Records("account-a"));
        Assert.Equal(["Other"], (await repository.Records("account-b")).Select(record => record.Title));
    }
}
