using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>ローカルモードのデータを Apple のアカウントへ付け替える処理（Windows 版で追加）を検証します。</summary>
public sealed class SqliteAccountDataReassignmentTests
{
    /// <summary>車両・写真・セッション・整備記録が新しい持ち主へ移り、元の持ち主には残らないことを確かめます。</summary>
    [Fact]
    public async Task ReassignMovesAllLocalData()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var vehicles = new SqliteVehicleRepository(fixture.Database);
        var maintenance = new SqliteMaintenanceRecordRepository(fixture.Database);
        var vehicle = new VehicleProfile("VIN-LOCAL", "Local car", photoData: [0x01, 0x02]);
        await vehicles.SaveVehicle(vehicle, "local-1");
        fixture.Repository.Save(new ConnectionSession("local-1", startedAt: TestDates.Unix(100), vehicle: new ConnectionSessionVehicle(vehicle)));
        await maintenance.Save(new MaintenanceRecord(vehicle.Id, MaintenanceKind.Light, "Oil"), "local-1");

        var result = new SqliteAccountDataReassignment(fixture.Database).Reassign("local-1", "apple-sub");

        Assert.Equal(new AccountDataReassignmentResult(1, 1, 1, 1), result);
        var moved = Assert.Single(await vehicles.LoadVehicles("apple-sub"));
        Assert.Equal(vehicle.Id, moved.Id);
        Assert.Equal(new byte[] { 0x01, 0x02 }, moved.PhotoData);
        Assert.Single(fixture.Repository.Sessions("apple-sub"));
        Assert.Single(await maintenance.Records("apple-sub"));
        Assert.Empty(await vehicles.LoadVehicles("local-1"));
        Assert.Empty(fixture.Repository.Sessions("local-1"));
        Assert.Empty(await maintenance.Records("local-1"));
    }

    /// <summary>付け替え先に同じ ID の車両があれば、上書きせず元の持ち主に残すことを確かめます。</summary>
    [Fact]
    public async Task ReassignKeepsConflictingVehicleWithOriginalOwner()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        var vehicles = new SqliteVehicleRepository(fixture.Database);
        var shared = new VehicleProfile("VIN-SHARED", "Local version", photoData: [0x0A]);
        await vehicles.SaveVehicle(shared, "local-1");
        await vehicles.SaveVehicle(shared with { Name = "Apple version" }, "apple-sub");
        await vehicles.SaveVehicle(new VehicleProfile("VIN-ONLY-LOCAL", "Only local"), "local-1");

        var result = new SqliteAccountDataReassignment(fixture.Database).Reassign("local-1", "apple-sub");

        Assert.Equal(1, result.Vehicles);
        Assert.Equal(0, result.VehiclePhotos);
        var appleVehicles = await vehicles.LoadVehicles("apple-sub");
        Assert.Equal(2, appleVehicles.Count);
        Assert.Equal("Apple version", appleVehicles.Single(item => item.Id == shared.Id).Name);
        var left = Assert.Single(await vehicles.LoadVehicles("local-1"));
        Assert.Equal(new byte[] { 0x0A }, left.PhotoData);
    }

    /// <summary>データがなければ何も変えず、空の結果を返すことを確かめます。</summary>
    [Fact]
    public void ReassignWithoutDataIsEmpty()
    {
        using var fixture = new ConnectionSessionStorageFixture();

        var result = new SqliteAccountDataReassignment(fixture.Database).Reassign("local-1", "apple-sub");

        Assert.True(result.IsEmpty);
    }

    /// <summary>空の識別子や同じ識別子を受け付けないことを確かめます。</summary>
    [Theory]
    [InlineData("", "apple-sub")]
    [InlineData("local-1", " ")]
    [InlineData("same", "same")]
    public void ReassignRejectsInvalidIdentifiers(string from, string to)
    {
        using var fixture = new ConnectionSessionStorageFixture();

        Assert.Throws<ArgumentException>(() => new SqliteAccountDataReassignment(fixture.Database).Reassign(from, to));
    }
}
