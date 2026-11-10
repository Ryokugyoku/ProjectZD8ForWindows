using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>保存先を準備できない場合の代替実装が明示的に失敗することを検証します。</summary>
/// <remarks>iOS版にテストはありません。Unavailable実装の契約（常に利用不能）を確かめます。</remarks>
public sealed class UnavailableRepositoryTests
{
    /// <summary>接続セッションの代替実装がそれぞれの利用不能エラーを返すことを確かめます。</summary>
    [Fact]
    public void ConnectionSessionRepositoryFailsWithUnavailable()
    {
        var repository = new UnavailableConnectionSessionRepository();
        var session = new ConnectionSession("account");

        Assert.Equal(ConnectionSessionRepositoryError.Unavailable, Assert.Throws<ConnectionSessionRepositoryException>(() => repository.Sessions("account")).Reason);
        Assert.Equal(ConnectionSessionRepositoryError.Unavailable, Assert.Throws<ConnectionSessionRepositoryException>(() => repository.Save(session)).Reason);
        Assert.Equal(ConnectionSessionRepositoryError.Unavailable, Assert.Throws<ConnectionSessionRepositoryException>(() => ((IConnectionSessionRawLogRepository)repository).ImportCloudMetadata(new ConnectionSessionCloudMetadata(session, "d"))).Reason);
        Assert.Equal(
            ConnectionSessionAcquisitionRepositoryError.Unavailable,
            Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(() => repository.Batches(session.Id)).Reason);
        Assert.Equal(
            ConnectionSessionAcquisitionRepositoryError.Unavailable,
            Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(() => repository.RecoverInterruptedSessionAcquisitions("account", DateTimeOffset.UnixEpoch)).Reason);
    }

    /// <summary>その他の代替実装が失敗することを確かめます。</summary>
    [Fact]
    public async Task OtherRepositoriesFail()
    {
        await Assert.ThrowsAsync<MaintenanceRepositoryException>(() => new UnavailableMaintenanceRecordRepository().Records("account"));
        Assert.Throws<InvalidOperationException>(() => new UnavailableOBDPIDDefinitionRepository().Definitions());
        Assert.Throws<InvalidOperationException>(() => new UnavailableVehiclePIDCapabilityRepository().Capabilities(VehicleID.New()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnavailableVehicleRepository().LoadVehicles("account"));
    }
}
