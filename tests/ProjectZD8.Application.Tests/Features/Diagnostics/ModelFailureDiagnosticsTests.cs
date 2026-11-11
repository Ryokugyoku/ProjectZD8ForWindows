using Microsoft.Extensions.Logging;
using ProjectZD8.Application.Tests.TestSupport;
using ProjectZD8.Application.VehicleManagement.Actions;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Diagnostics;

/// <summary>
/// Application のモデルが失敗をエラー状態に変えるとき、原因の例外つきの Error が診断ログに残ることを確かめます。
/// </summary>
[Collection(DiagnosticSinkCollection.Name)]
public sealed class ModelFailureDiagnosticsTests
{
    /// <summary>テスト用の VIN です（診断ログには末尾 4 桁以外を出さない）。</summary>
    private const string Vin = "JF1ZD8A10NG000001";

    /// <summary>テスト用の車両名です（診断ログには出さない）。</summary>
    private const string VehicleName = "私のBRZ-ひみつの名前";

    /// <summary>車両の保存に失敗したとき、操作名と例外つきの Error が残り、VIN と車両名が出ないことを確かめます。</summary>
    [Fact]
    public async Task VehicleSaveFailureWritesErrorWithExceptionAndNoVehicleData()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        var repository = new ThrowingVehicleRepository(saveFails: true);
        var model = NewModel(repository, new ThrowingIdentificationPort());
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        Assert.True(await AsyncWait.UntilAsync(() => model.State.HasLoadedVehicles));

        model.Send(new VehicleManagementAction.VehicleSaved(new VehicleProfile(vin: Vin, name: VehicleName)));

        Assert.True(await AsyncWait.UntilAsync(() => model.State.Phase == VehicleManagementStatePhase.Failed));
        var failed = Assert.Single(sink.Named("ui.vehicle.operation.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal("vehicle.save", failed.Properties.Single(pair => pair.Key == "Operation").Value);
        Assert.Equal("System.InvalidOperationException", failed.Exception!.Type);
        Assert.Contains("save failed", failed.Exception.Message, StringComparison.Ordinal);
        var dump = sink.Dump();
        Assert.DoesNotContain(Vin, dump, StringComparison.Ordinal);
        Assert.DoesNotContain(VehicleName, dump, StringComparison.Ordinal);
    }

    /// <summary>車両の読込に失敗したとき、同期失敗の表示状態へ変わる前に Error が残ることを確かめます。</summary>
    [Fact]
    public async Task VehicleLoadFailureWritesErrorBeforeFailedState()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        var model = NewModel(new ThrowingVehicleRepository(loadFails: true), new ThrowingIdentificationPort());

        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));

        Assert.True(await AsyncWait.UntilAsync(() => model.State.Phase == VehicleManagementStatePhase.Failed));
        Assert.Equal("garage.error.sync", model.State.FailureKey);
        var failed = Assert.Single(sink.Named("ui.vehicle.operation.failed"));
        Assert.Equal("vehicles.load", failed.Properties.Single(pair => pair.Key == "Operation").Value);
        Assert.NotNull(failed.Exception);
        Assert.Equal(DiagnosticCategories.UI, failed.Category);
    }

    /// <summary>車両識別が想定外の例外で失敗したとき、Error と例外が残ることを確かめます。</summary>
    [Fact]
    public async Task VehicleIdentificationFailureWritesError()
    {
        var sink = new RecordingDiagnosticSink();
        using var installed = DiagnosticLog.Install(sink);
        var model = NewModel(new ThrowingVehicleRepository(), new ThrowingIdentificationPort());
        model.Send(new VehicleManagementAction.AccountIdentifierChanged("test-account"));
        Assert.True(await AsyncWait.UntilAsync(() => model.State.HasLoadedVehicles));

        model.Send(new VehicleManagementAction.IdentifyRequested(
            new OBDConnectionEndpoint(OBDConnectionEndpointTransport.Serial, "COM3", "OBDLink EX")));

        Assert.True(await AsyncWait.UntilAsync(() => model.State.Phase == VehicleManagementStatePhase.Failed));
        var failed = Assert.Single(sink.Named("ui.vehicle.operation.failed"));
        Assert.Equal("vehicle.identify", failed.Properties.Single(pair => pair.Key == "Operation").Value);
        Assert.Contains("identification failed", failed.Exception!.Message, StringComparison.Ordinal);
    }

    /// <summary>テスト用のモデルを作ります。</summary>
    /// <param name="repository">車両の保存先。</param>
    /// <param name="identification">車両識別の境界。</param>
    /// <returns>モデル。</returns>
    private static VehicleManagementModel NewModel(IVehicleRepository repository, IVehicleIdentificationPort identification) =>
        new(
            state: new VehicleManagementState(),
            repository: repository,
            identifyForConnection: new IdentifyVehicleForConnectionUseCase(identification),
            photoImporter: new NoPhotoImportPort());

    /// <summary>指定の操作だけ失敗する車両の保存先です。</summary>
    /// <param name="loadFails">読込を失敗させるか。</param>
    /// <param name="saveFails">保存を失敗させるか。</param>
    private sealed class ThrowingVehicleRepository(bool loadFails = false, bool saveFails = false) : IVehicleRepository
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier) =>
            loadFails
                ? Task.FromException<IReadOnlyList<VehicleProfile>>(new InvalidOperationException("load failed"))
                : Task.FromResult<IReadOnlyList<VehicleProfile>>([]);

        /// <inheritdoc />
        public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier) =>
            saveFails ? Task.FromException(new InvalidOperationException("save failed")) : Task.CompletedTask;

        /// <inheritdoc />
        public Task DeleteVehicle(VehicleID id, string accountIdentifier) => Task.CompletedTask;
    }

    /// <summary>常に想定外の例外で失敗する車両識別の境界です。</summary>
    private sealed class ThrowingIdentificationPort : IVehicleIdentificationPort
    {
        /// <inheritdoc />
        public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint) =>
            Task.FromException<VehicleIdentificationSnapshot>(new InvalidOperationException("identification failed"));
    }

    /// <summary>画像取込を使わない境界です。</summary>
    private sealed class NoPhotoImportPort : IVehiclePhotoImportPort
    {
        /// <inheritdoc />
        public Task<byte[]> ImportPhoto(Uri url) => Task.FromResult(Array.Empty<byte>());
    }
}
