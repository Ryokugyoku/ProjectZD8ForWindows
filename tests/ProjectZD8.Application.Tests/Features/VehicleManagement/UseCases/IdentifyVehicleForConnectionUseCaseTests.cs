using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.VehicleManagement.UseCases;

/// <summary>OBD車両識別結果を登録済み車両と新規登録へ振り分ける規則を検証します。</summary>
public sealed class IdentifyVehicleForConnectionUseCaseTests
{
    /// <summary>大文字小文字だけが異なるVINを同じ登録車両として扱うことを検証します。</summary>
    /// <remarks>責務: 1件の既存VIN観測が新規登録を作らず登録済み結果になることを確認します。</remarks>
    [Fact]
    public async Task ExecuteReturnsRegisteredVehicleForExistingVIN()
    {
        var vehicle = new VehicleProfile(vin: "JF1ZD8A10NG000001", name: "ZD8");
        var snapshot = MakeSnapshot(vin: "jf1zd8a10ng000001");
        var useCase = new IdentifyVehicleForConnectionUseCase(new VehicleIdentificationPortFake(snapshot));

        var outcome = await useCase.Execute(MakeEndpoint(), [vehicle]);

        Assert.Equal(new IdentifyVehicleForConnectionUseCase.Outcome.Registered(vehicle, snapshot), outcome);
    }

    /// <summary>未登録VINで全観測値を保持した登録確認へ進むことを検証します。</summary>
    /// <remarks>責務: 1件の未知VIN観測がフィールドを失わず新規登録候補になることを確認します。</remarks>
    [Fact]
    public async Task ExecutePreservesAllFieldsForUnknownVIN()
    {
        var snapshot = MakeSnapshot(vin: "UNKNOWN-VIN");
        var useCase = new IdentifyVehicleForConnectionUseCase(new VehicleIdentificationPortFake(snapshot));

        var outcome = await useCase.Execute(MakeEndpoint(), []);

        Assert.Equal(new IdentifyVehicleForConnectionUseCase.Outcome.RequiresRegistration(snapshot), outcome);
    }

    /// <summary>非VINのOBD由来識別子で登録済み車両を照合します。</summary>
    /// <remarks>責務: VINがない車両同士をOBD由来識別子の同種比較だけで一致させることを確認します。</remarks>
    [Fact]
    public async Task ExecuteReturnsRegisteredVehicleForExistingOBDIdentifier()
    {
        var vehicle = new VehicleProfile(vin: "", obdIdentifier: "ZD81234567", name: "ZD8");
        var snapshot = MakeSnapshot(vin: null, obdIdentifier: "zd81234567");
        var useCase = new IdentifyVehicleForConnectionUseCase(new VehicleIdentificationPortFake(snapshot));

        var outcome = await useCase.Execute(MakeEndpoint(), [vehicle]);

        Assert.Equal(new IdentifyVehicleForConnectionUseCase.Outcome.Registered(vehicle, snapshot), outcome);
    }

    /// <summary>車両識別子未取得を登録可能な空文字へ変換しないことを検証します。</summary>
    /// <remarks>責務: VINと非VIN識別子の両方を含まない観測が明示的な失敗として拒否されることを確認します。</remarks>
    [Fact]
    public async Task ExecuteRejectsSnapshotWithoutVehicleIdentifier()
    {
        var useCase = new IdentifyVehicleForConnectionUseCase(new VehicleIdentificationPortFake(MakeSnapshot(vin: null)));

        var error = await Assert.ThrowsAsync<VehicleIdentificationException>(
            () => useCase.Execute(MakeEndpoint(), []));

        Assert.Equal(new VehicleIdentificationError.VinUnavailable(), error.Reason);
    }

    /// <summary>テスト用の車両識別子と取得元付き全フィールドを生成します。</summary>
    /// <remarks>責務: 指定識別子を持つ決定的なOBD識別観測を1件生成します。</remarks>
    /// <param name="vin">観測へ設定するVIN候補。</param>
    /// <param name="obdIdentifier">観測へ設定する非VIN識別子。</param>
    /// <returns>メーカーとECU識別値を含む観測。</returns>
    private static VehicleIdentificationSnapshot MakeSnapshot(string? vin, string? obdIdentifier = null) =>
        new(
            vin: vin,
            obdIdentifier: obdIdentifier,
            fields:
            [
                new VehicleIdentificationSnapshot.Field(id: "manufacturer", label: "Manufacturer", value: "Example", source: "ECU A"),
                new VehicleIdentificationSnapshot.Field(id: "ecu", label: "ECU", value: "RAW-01", source: "ECU B"),
            ],
            observedAt: DateTimeOffset.FromUnixTimeSeconds(100));

    /// <summary>テスト用のEXシリアル終端を生成します。</summary>
    /// <remarks>責務: 全車両識別照合テストへ同一の通信非依存接続情報を提供します。</remarks>
    /// <returns>決定的なシリアル接続終端。</returns>
    private static OBDConnectionEndpoint MakeEndpoint() =>
        new(OBDConnectionEndpointTransport.Serial, "/dev/cu.test", "OBDLink EX");
}

/// <summary>車両識別照合テストへ固定のOBD識別観測を返します。</summary>
file sealed class VehicleIdentificationPortFake : IVehicleIdentificationPort
{
    /// <summary>取得結果として返す固定観測です。</summary>
    private readonly VehicleIdentificationSnapshot snapshot;

    /// <summary>固定観測を保持して生成します。</summary>
    /// <remarks>責務: 1件の識別観測を応答として固定します。</remarks>
    /// <param name="snapshot">返す識別観測。</param>
    public VehicleIdentificationPortFake(VehicleIdentificationSnapshot snapshot)
    {
        this.snapshot = snapshot;
    }

    /// <summary>固定観測をテスト対象へ返します。</summary>
    /// <remarks>責務: 1回の識別要求へ注入済み観測を返します。</remarks>
    /// <param name="endpoint">テストでは参照しない接続終端。</param>
    /// <returns>初期化時に保持した識別観測。</returns>
    public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint) => Task.FromResult(snapshot);
}
