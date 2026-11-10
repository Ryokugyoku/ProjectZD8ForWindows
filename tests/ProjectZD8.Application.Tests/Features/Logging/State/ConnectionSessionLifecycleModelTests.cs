using ProjectZD8.Application.Logging.Actions;
using ProjectZD8.Application.Logging.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Logging.State;

/// <summary>接続セッションの開始、車両関連付け、終了を検証します。</summary>
public sealed class ConnectionSessionLifecycleModelTests
{
    /// <summary>HOME開始から車両確定と無応答終了までを同じセッションIDへ保存します。</summary>
    /// <remarks>責務: 1回の接続ライフサイクルが安定IDを維持して終端されることを確認します。</remarks>
    [Fact]
    public void LifecycleKeepsSessionIDWhileBindingVehicleAndEnding()
    {
        var repository = new RecordingConnectionSessionRepository();
        var sessionID = new ConnectionSessionID(Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));
        var startedAt = DateTimeOffset.FromUnixTimeSeconds(100);
        var endedAt = DateTimeOffset.FromUnixTimeSeconds(200);
        var dates = new Queue<DateTimeOffset>([startedAt, endedAt]);
        var historyChangeCount = 0;
        var acquisitionDevice = new ConnectionSessionAcquisitionDevice(ConnectionSessionAcquisitionPlatform.IPad, "iPad Pro");
        var model = new ConnectionSessionLifecycleModel(
            repository: repository,
            now: dates.Dequeue,
            makeID: () => sessionID,
            acquisitionDevice: acquisitionDevice,
            historyDidChange: () => historyChangeCount += 1);
        var vehicle = new VehicleProfile(
            vin: "TESTVIN",
            name: "BRZ",
            id: new VehicleID(Guid.Parse("11111111-2222-3333-4444-555555555555")));

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.VehicleResolved(vehicle));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, 12_345.6)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, 12_346.8)));
        model.Send(new ConnectionSessionLifecycleAction.EndRequested(ConnectionSessionEndReason.VehicleNoResponse));

        var saved = repository.Storage[sessionID.RawValue];
        Assert.Equal(sessionID, saved.Id);
        Assert.Equal(vehicle.Id, saved.Vehicle?.Id);
        Assert.Equal(startedAt, saved.StartedAt);
        Assert.Equal(endedAt, saved.EndedAt);
        Assert.Equal(ConnectionSessionEndReason.VehicleNoResponse, saved.EndReason);
        Assert.Equal(acquisitionDevice, saved.AcquisitionDevice);
        Assert.Equal(12_345.6, saved.StartingOdometerKilometers);
        Assert.Equal(12_346.8, saved.EndingOdometerKilometers);
        Assert.Equal(1.2, Assert.NotNull(saved.RecordedDistanceKilometers), 0.000_1);
        Assert.Equal(ConnectionSessionStatus.Interrupted, saved.Status);
        Assert.Null(model.ActiveSession);
        Assert.Equal(5, historyChangeCount);
    }

    /// <summary>無効な累積走行距離をセッションへ保存しません。</summary>
    /// <remarks>責務: 非有限値と負値が走行距離差分へ混入しないことを確認します。</remarks>
    [Fact]
    public void InvalidOdometerObservationsRemainUnsupported()
    {
        var repository = new RecordingConnectionSessionRepository();
        var model = new ConnectionSessionLifecycleModel(repository);

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, double.NegativeInfinity)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, -1)));

        var session = Assert.IsType<ConnectionSession>(model.ActiveSession);
        Assert.Null(session.StartingOdometerKilometers);
        Assert.Null(session.EndingOdometerKilometers);
        Assert.Null(session.RecordedDistanceKilometers);
    }

    /// <summary>コード消去後走行距離を代替記録し、総走行距離取得後は優先元を固定します。</summary>
    /// <remarks>責務: PID 31からPID A6へ昇格したセッションが低優先PIDへ戻らないことを確認します。</remarks>
    [Fact]
    public void OdometerSupersedesDistanceSinceCodesCleared()
    {
        var repository = new RecordingConnectionSessionRepository();
        var model = new ConnectionSessionLifecycleModel(repository);

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, 1_000)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, 1_001.5)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, 30_000)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, 1_002)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, 30_000.8)));

        var session = Assert.IsType<ConnectionSession>(model.ActiveSession);
        Assert.Equal(30_000, session.StartingOdometerKilometers);
        Assert.Equal(30_000.8, session.EndingOdometerKilometers);
        Assert.Equal(0.8, Assert.NotNull(session.RecordedDistanceKilometers), 0.000_1);
    }

    /// <summary>ZD8専用走行距離の取得元と型式をセッションへ保持します。</summary>
    /// <remarks>責務: 専用PIDの累積距離が標準PIDより優先され表示用型式とともに保存されることを確認します。</remarks>
    [Fact]
    public void VehicleSpecificOdometerPersistsModelCodeAndSupersedesStandardOdometer()
    {
        var repository = new RecordingConnectionSessionRepository();
        var model = new ConnectionSessionLifecycleModel(repository);

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, 30_000)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.VehicleSpecificOdometer, 30_100, "ZD8")));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.Odometer, 30_001)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.VehicleSpecificOdometer, 30_101.2, "ZD8")));

        var session = Assert.IsType<ConnectionSession>(model.ActiveSession);
        Assert.Equal(30_100, session.StartingOdometerKilometers);
        Assert.Equal(30_101.2, session.EndingOdometerKilometers);
        Assert.Equal(1.2, Assert.NotNull(session.RecordedDistanceKilometers), 0.000_1);
        Assert.Equal("ZD8", session.DistanceSourceModelCode);
    }

    /// <summary>総走行距離を取得できないセッションではコード消去後走行距離の差分を保持します。</summary>
    /// <remarks>責務: PID 31だけの有効な観測列を接続履歴の走行距離差分へ変換できることを確認します。</remarks>
    [Fact]
    public void DistanceSinceCodesClearedProvidesFallbackDistance()
    {
        var repository = new RecordingConnectionSessionRepository();
        var model = new ConnectionSessionLifecycleModel(repository);

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, 500)));
        model.Send(new ConnectionSessionLifecycleAction.DistanceObserved(new(ConnectionSessionDistanceSource.DistanceSinceCodesCleared, 502.4)));

        var session = Assert.IsType<ConnectionSession>(model.ActiveSession);
        Assert.Equal(2.4, Assert.NotNull(session.RecordedDistanceKilometers), 0.000_1);
    }

    /// <summary>アプリ再開時に残った未終了セッションを接続中のまま復元しません。</summary>
    /// <remarks>責務: 以前の未終了セッションが異常終了へ回復されることを確認します。</remarks>
    [Fact]
    public void AccountActivationClosesPersistedUnfinishedSession()
    {
        var repository = new RecordingConnectionSessionRepository();
        var session = new ConnectionSession(accountIdentifier: "account", startedAt: DateTimeOffset.FromUnixTimeSeconds(50));
        repository.Storage[session.Id.RawValue] = session;
        var recoveryDate = DateTimeOffset.FromUnixTimeSeconds(75);
        var model = new ConnectionSessionLifecycleModel(repository, now: () => recoveryDate);

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));

        var recovered = repository.Storage[session.Id.RawValue];
        Assert.Equal(recoveryDate, recovered.EndedAt);
        Assert.Equal(ConnectionSessionEndReason.UnexpectedTermination, recovered.EndReason);
        Assert.Equal(ConnectionSessionStatus.Interrupted, recovered.Status);
    }

    /// <summary>自動アップロード設定がONの終了だけをアップロード要求として通知します。</summary>
    /// <remarks>責務: OFFとONで終了した2件のセッションからON時の1件だけを自動アップロード対象へ変換することを確認します。</remarks>
    [Fact]
    public void SessionEndRequestsUploadOnlyWhileAutomaticSettingIsEnabled()
    {
        var repository = new RecordingConnectionSessionRepository();
        var uploadRequests = new List<ConnectionSessionID>();
        var model = new ConnectionSessionLifecycleModel(
            repository,
            endedSessionUploadRequested: session => uploadRequests.Add(session.Id));
        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));

        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.EndRequested(ConnectionSessionEndReason.UserDisconnected));
        Assert.Empty(uploadRequests);

        model.Send(new ConnectionSessionLifecycleAction.AutomaticUploadChanged(true));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        var automaticallyUploadedID = model.ActiveSession?.Id;
        model.Send(new ConnectionSessionLifecycleAction.EndRequested(ConnectionSessionEndReason.UserDisconnected));

        Assert.Equal(new[] { automaticallyUploadedID }.OfType<ConnectionSessionID>(), uploadRequests);
    }

    /// <summary>テスト用に接続セッションをメモリへ記録します。</summary>
    private sealed class RecordingConnectionSessionRepository : IConnectionSessionRepository
    {
        /// <summary>セッションID単位の保存内容です。</summary>
        public Dictionary<Guid, ConnectionSession> Storage { get; } = [];

        /// <summary>指定セッションをメモリへ保存します。</summary>
        /// <remarks>責務: 1件の接続セッションをテスト用辞書へ記録します。</remarks>
        /// <param name="session">記録する接続セッション。</param>
        public void Save(ConnectionSession session) => Storage[session.Id.RawValue] = session;

        /// <summary>指定アカウントのセッションを新しい順で返します。</summary>
        /// <remarks>責務: テスト用辞書から1件のアカウントに属する履歴を抽出します。</remarks>
        /// <param name="accountIdentifier">取得対象のアカウント識別子。</param>
        /// <returns>開始日時が新しい順の接続セッション。</returns>
        public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) =>
            Storage.Values
                .Where(session => string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
                .OrderByDescending(session => session.StartedAt)
                .ToArray();
    }
}
