using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// 遠隔の失効でも使う、端末内のアカウントデータの消去を検証します（iOS の ProjectZD8AccountDataEraserTests）。
/// </summary>
public sealed class ProjectZD8AccountDataEraserTests
{
    /// <summary>
    /// 運転履歴、車両、設定、アダプターの選択を、同じアカウントの削除で消すことを確かめます。
    /// </summary>
    /// <remarks>責務: ほかの端末の失効を見つけた後、この端末にアカウント固有のデータが残らないことを確かめます。</remarks>
    [Fact]
    public void TestEraseAllDataRemovesEveryAccountScopedLocalStore()
    {
        var defaults = new InMemoryLocalKeyValueStore();
        var cloud = new FakeSynchronizedKeyValueStore();
        var settings = new SynchronizedAccountSettingsStore(defaults, cloud);
        var adapterRemoved = 0;
        var sessions = new AccountConnectionSessionErasureRepositoryFake();
        var vehicles = new AccountVehicleDataErasurePortFake();
        var capabilities = new AccountVehiclePIDCapabilityErasureRepositoryFake();
        settings.Save(new AccountSettings(SettingsLanguage.English, SettingsAppearance.Dark), "account");
        var eraser = new ProjectZD8AccountDataEraser(
            settings,
            () => adapterRemoved++,
            sessions,
            vehicles,
            capabilities);

        eraser.EraseAllData("account");

        Assert.Equal(["account"], sessions.DeletedAccountIdentifiers);
        Assert.Equal(["account"], vehicles.RemovedLocalCacheIdentifiers);
        Assert.Equal(vehicles.LocalIDs, capabilities.DeletedVehicleIDs);
        Assert.Null(settings.Load("account"));
        Assert.Equal(1, adapterRemoved);
        Assert.Empty(cloud.SavedData);
        Assert.Empty(defaults.Data);
    }

    /// <summary>
    /// 端末内の接続履歴のアカウント削除要求を記録します。
    /// </summary>
    private sealed class AccountConnectionSessionErasureRepositoryFake : IAccountConnectionSessionErasureRepository
    {
        /// <summary>削除されたアカウント識別子です。</summary>
        public List<string> DeletedAccountIdentifiers { get; } = [];

        /// <inheritdoc />
        public void DeleteSessions(string accountIdentifier) => DeletedAccountIdentifiers.Add(accountIdentifier);
    }

    /// <summary>
    /// 車両の端末内の削除要求を記録します。
    /// </summary>
    private sealed class AccountVehicleDataErasurePortFake : IAccountVehicleDataErasurePort
    {
        /// <summary>端末内を消されたアカウント識別子です。</summary>
        public List<string> RemovedLocalCacheIdentifiers { get; } = [];

        /// <summary>端末内にある固定の車両 ID です。</summary>
        public IReadOnlyList<VehicleID> LocalIDs { get; } = [VehicleID.New(), VehicleID.New()];

        /// <inheritdoc />
        public Task DeleteAllVehicleData(string accountIdentifier) => Task.CompletedTask;

        /// <inheritdoc />
        public IReadOnlyList<VehicleID> LocalVehicleIDs(string accountIdentifier) => LocalIDs;

        /// <inheritdoc />
        public void RemoveLocalVehicleCache(string accountIdentifier) => RemovedLocalCacheIdentifiers.Add(accountIdentifier);
    }

    /// <summary>
    /// 車両別の PID 設定の削除要求を記録します。
    /// </summary>
    private sealed class AccountVehiclePIDCapabilityErasureRepositoryFake : IAccountVehiclePIDCapabilityErasureRepository
    {
        /// <summary>削除された車両 ID です。</summary>
        public IReadOnlyList<VehicleID> DeletedVehicleIDs { get; private set; } = [];

        /// <inheritdoc />
        public void DeleteCapabilities(IReadOnlyList<VehicleID> vehicleIDs) => DeletedVehicleIDs = vehicleIDs;
    }
}
