using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>車両別対応PIDリポジトリを検証します。</summary>
/// <remarks>iOS版 <c>GRDBVehiclePIDCapabilityRepositoryTests</c> の移植です。</remarks>
public sealed class SqliteVehiclePIDCapabilityRepositoryTests
{
    /// <summary>テスト用の車両IDです。</summary>
    private static readonly VehicleID VehicleID = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    /// <summary>別の車両IDです。</summary>
    private static readonly VehicleID OtherVehicleID = new(Guid.Parse("20000000-0000-0000-0000-000000000002"));

    /// <summary>車両ごとに保存し、指定PIDだけを更新することを確かめます。</summary>
    [Fact]
    public void PersistsPerVehicleAndUpdatesOnlyRequestedPID()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehiclePIDCapabilityRepository(database);
        var first = Capability(0x0C);
        var second = Capability(0x0D);
        repository.InsertInitial([first, second], VehicleID);

        repository.SetCollectionEnabled(false, second.Id.Request, VehicleID);

        var loaded = repository.Capabilities(VehicleID);
        Assert.Equal(new byte[] { 0x0C, 0x0D }, loaded.Select(capability => capability.Id.Request.Pid));
        Assert.Equal([true, false], loaded.Select(capability => capability.IsCollectionEnabled));
        Assert.Empty(repository.Capabilities(OtherVehicleID));
    }

    /// <summary>2回目の初期登録を拒否することを確かめます。</summary>
    [Fact]
    public void RejectsSecondInitialInsert()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehiclePIDCapabilityRepository(database);
        repository.InsertInitial([Capability(0x0C)], VehicleID);
        Assert.ThrowsAny<Exception>(() => repository.InsertInitial([Capability(0x0D)], VehicleID));
    }

    /// <summary>追加登録が既存の選択を保ち、車種専用PIDを加えることを確かめます。</summary>
    [Fact]
    public void MergeDiscoveredPreservesExistingSelectionAndAddsVehicleSpecificPID()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehiclePIDCapabilityRepository(database);
        var standard = Capability(0x0D);
        repository.InsertInitial([standard], VehicleID);
        repository.SetCollectionEnabled(false, standard.Id.Request, VehicleID);
        var discovered = new VehiclePIDCapability(VehicleID, new OBDPIDRequest(0x21, 0x17), true, TestDates.Unix(200));

        repository.MergeDiscovered([standard, discovered], VehicleID);

        var loaded = repository.Capabilities(VehicleID);
        Assert.Equal([standard.Id.Request, discovered.Id.Request], loaded.Select(capability => capability.Id.Request));
        Assert.Equal([false, true], loaded.Select(capability => capability.IsCollectionEnabled));
    }

    /// <summary>指定した車両だけを削除することを確かめます。</summary>
    [Fact]
    public void DeleteCapabilitiesRemovesOnlyRequestedVehicles()
    {
        using var database = SqliteDatabase.OpenInMemory();
        var repository = new SqliteVehiclePIDCapabilityRepository(database);
        repository.InsertInitial([Capability(0x0C)], VehicleID);
        var retained = new VehiclePIDCapability(OtherVehicleID, new OBDPIDRequest(0x01, 0x0D), true, TestDates.Unix(100));
        repository.InsertInitial([retained], OtherVehicleID);

        repository.DeleteCapabilities([VehicleID]);

        Assert.Empty(repository.Capabilities(VehicleID));
        Assert.Equal([retained], repository.Capabilities(OtherVehicleID));
    }

    /// <summary>テスト用の対応PIDを作ります。</summary>
    /// <remarks>責務: Service 01の収集有効な対応PIDを作ります。</remarks>
    /// <param name="pid">PID。</param>
    /// <returns>対応PID。</returns>
    private static VehiclePIDCapability Capability(byte pid) =>
        new(VehicleID, new OBDPIDRequest(0x01, pid), true, TestDates.Unix(100));
}
