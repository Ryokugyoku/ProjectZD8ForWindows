using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>車両リポジトリ（Windows版で追加）を検証します。</summary>
/// <remarks>iOS版にはSQLiteの車両保存がないため、<c>VehicleRepository</c> の契約（アカウントスコープ、更新日時の新しい順）を確かめます。</remarks>
public sealed class SqliteVehicleRepositoryTests
{
    /// <summary>全項目と写真が往復することを確かめます。</summary>
    [Fact]
    public async Task SaveAndLoadRoundTripsAllFieldsAndPhoto()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehicleRepository(database);
        var vehicle = new VehicleProfile(
            vin: "TESTVIN",
            name: "BRZ",
            obdIdentifier: "OBD-ID",
            manufacturer: "Subaru",
            engineModel: "FA24",
            powertrain: VehiclePowertrainKind.Hybrid,
            energySources: [VehicleEnergySource.GasolinePremium, VehicleEnergySource.Electricity],
            tankCapacityLiters: 50,
            modelYear: 2024,
            note: "note",
            photoData: [0x01, 0x02, 0x03],
            isDefault: true,
            updatedAt: TestDates.Unix(100));

        await repository.SaveVehicle(vehicle, "account");

        Assert.Equal([vehicle], await repository.LoadVehicles("account"));
        Assert.Empty(await repository.LoadVehicles("other"));
    }

    /// <summary>保存が同じIDを更新し、写真の削除も反映し、更新日時の新しい順で返ることを確かめます。</summary>
    [Fact]
    public async Task SaveUpdatesExistingVehicleAndOrdersByUpdatedAtDescending()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehicleRepository(database);
        var first = new VehicleProfile("VIN-1", "First", photoData: [0x01], updatedAt: TestDates.Unix(100));
        var second = new VehicleProfile("VIN-2", "Second", updatedAt: TestDates.Unix(200));
        await repository.SaveVehicle(first, "account");
        await repository.SaveVehicle(second, "account");
        var renamed = first with { Name = "Renamed", PhotoData = null, UpdatedAt = TestDates.Unix(300) };

        await repository.SaveVehicle(renamed, "account");

        Assert.Equal([renamed, second], await repository.LoadVehicles("account"));
        Assert.Equal([renamed.Id, second.Id], repository.VehicleIDs("account"));
    }

    /// <summary>削除が指定アカウントの指定車両だけを消すことを確かめます。</summary>
    [Fact]
    public async Task DeleteVehicleRemovesOnlyThatAccountVehicle()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehicleRepository(database);
        var vehicle = new VehicleProfile("VIN", "Car", photoData: [0x09]);
        await repository.SaveVehicle(vehicle, "account");
        await repository.SaveVehicle(vehicle, "other");

        await repository.DeleteVehicle(vehicle.Id, "account");

        Assert.Empty(await repository.LoadVehicles("account"));
        Assert.Equal([vehicle], await repository.LoadVehicles("other"));
        Assert.Equal(1L, database.Read(connection => SqliteDatabase.Scalar(connection, null, "SELECT COUNT(*) FROM vehicle_photos")));
    }

    /// <summary>アカウントの全車両の置換と削除を確かめます。</summary>
    [Fact]
    public async Task ReplaceAllAndDeleteVehiclesAreAccountScoped()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehicleRepository(database);
        await repository.SaveVehicle(new VehicleProfile("OLD", "Old"), "account");
        await repository.SaveVehicle(new VehicleProfile("KEEP", "Keep"), "other");
        var replacement = new VehicleProfile("NEW", "New", photoData: [0x05]);

        repository.ReplaceAll([replacement], "account");
        Assert.Equal([replacement], await repository.LoadVehicles("account"));

        repository.DeleteVehicles("account");
        Assert.Empty(await repository.LoadVehicles("account"));
        Assert.Single(await repository.LoadVehicles("other"));
    }
}
