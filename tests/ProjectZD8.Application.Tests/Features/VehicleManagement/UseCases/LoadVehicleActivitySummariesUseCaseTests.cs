using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.VehicleManagement.UseCases;

/// <summary>Garage用の車両別アクティビティ集約を検証します。</summary>
public sealed class LoadVehicleActivitySummariesUseCaseTests
{
    /// <summary>複数車両と未関連履歴を1回の走査で正しく分離します。</summary>
    /// <remarks>責務: 車両別の件数、最終日時、記録時間、中断、接続中状態が混線しないことを確認します。</remarks>
    [Fact]
    public void ExecuteAggregatesSessionsByRegisteredVehicle()
    {
        var firstVehicleID = VehicleID.New();
        var secondVehicleID = VehicleID.New();
        var completed = Session(firstVehicleID, startedAt: 100) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(160),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        var interrupted = Session(firstVehicleID, startedAt: 200) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(260),
            EndReason = ConnectionSessionEndReason.ConnectionLost,
        };
        var connected = Session(secondVehicleID, startedAt: 300);
        var unassigned = new ConnectionSession(accountIdentifier: "account", startedAt: DateTimeOffset.FromUnixTimeSeconds(400));
        var repository = new ConnectionSessionRepositoryFake([unassigned, connected, interrupted, completed]);

        var result = new LoadVehicleActivitySummariesUseCase(repository).Execute(accountIdentifier: "account");

        Assert.Equal(["account"], repository.RequestedAccountIdentifiers);
        Assert.Equal(2, result.GetValueOrDefault(firstVehicleID)?.SessionCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(260), result.GetValueOrDefault(firstVehicleID)?.LastLoggedAt);
        Assert.Equal(120, result.GetValueOrDefault(firstVehicleID)?.TotalRecordedDuration);
        Assert.Equal(1, result.GetValueOrDefault(firstVehicleID)?.InterruptedCount);
        Assert.Equal(false, result.GetValueOrDefault(firstVehicleID)?.IsConnected);
        Assert.Equal(1, result.GetValueOrDefault(secondVehicleID)?.SessionCount);
        Assert.Equal(true, result.GetValueOrDefault(secondVehicleID)?.IsConnected);
        Assert.Equal(2, result.Count);
    }

    /// <summary>最新セッションのZD8専用走行距離をGarage集計へ公開します。</summary>
    /// <remarks>責務: 古い標準値ではなく新しい車種専用累積走行距離と型式がGarage表示へ渡ることを確認します。</remarks>
    [Fact]
    public void ExecuteUsesLatestZD8OdometerForGarage()
    {
        var vehicleID = VehicleID.New();
        var older = Session(vehicleID, startedAt: 100) with { EndingOdometerKilometers = 30_000 };
        var latest = Session(vehicleID, startedAt: 200) with
        {
            EndingOdometerKilometers = 30_123.4,
            DistanceSourceModelCode = "ZD8",
        };
        var repository = new ConnectionSessionRepositoryFake([older, latest]);

        var summary = new LoadVehicleActivitySummariesUseCase(repository)
            .Execute(accountIdentifier: "account")
            .GetValueOrDefault(vehicleID);

        Assert.NotNull(summary);
        Assert.Equal(30_123.4, summary.LatestOdometerKilometers);
        Assert.Equal("ZD8", summary.OdometerModelCode);
    }

    /// <summary>指定車両へ関連付いたテスト用セッションを生成します。</summary>
    /// <remarks>責務: 1件の車両IDと開始時刻を履歴集約テスト用の未終了セッションへ変換します。</remarks>
    /// <param name="vehicleID">履歴へ関連付ける登録車両ID。</param>
    /// <param name="startedAt">Unix epoch秒による開始日時。</param>
    /// <returns>指定車両へ関連付いた未終了セッション。</returns>
    private static ConnectionSession Session(VehicleID vehicleID, long startedAt) =>
        new(
            accountIdentifier: "account",
            startedAt: DateTimeOffset.FromUnixTimeSeconds(startedAt),
            vehicle: new ConnectionSessionVehicle(id: vehicleID, name: "Vehicle", displayIdentifier: "TEST"));
}

/// <summary>接続履歴集約テストへ固定セッション一覧を提供します。</summary>
file sealed class ConnectionSessionRepositoryFake : IConnectionSessionRepository
{
    /// <summary>取得要求へ返す接続セッションです。</summary>
    private readonly IReadOnlyList<ConnectionSession> storedSessions;

    /// <summary>取得要求で受け取ったアカウント識別子の保存先です。</summary>
    private readonly List<string> requestedAccountIdentifiers = [];

    /// <summary>固定セッション一覧を保持するRepositoryを生成します。</summary>
    /// <remarks>責務: テスト用セッション一覧をアカウント照会可能なRepositoryへ固定します。</remarks>
    /// <param name="sessions">取得要求へ返すセッション一覧。</param>
    public ConnectionSessionRepositoryFake(IReadOnlyList<ConnectionSession> sessions)
    {
        storedSessions = sessions;
    }

    /// <summary>取得要求で受け取ったアカウント識別子です。</summary>
    public IReadOnlyList<string> RequestedAccountIdentifiers => requestedAccountIdentifiers;

    /// <summary>テストでは保存要求を副作用なしで受理します。</summary>
    /// <remarks>責務: 集約テストで使用しない保存操作を無変更で完了します。</remarks>
    /// <param name="session">保存対象として受け取る接続セッション。</param>
    public void Save(ConnectionSession session)
    {
    }

    /// <summary>固定セッション一覧を返します。</summary>
    /// <remarks>責務: 1件のアカウント照会を記録して固定履歴を返します。</remarks>
    /// <param name="accountIdentifier">取得対象として受け取るアカウント識別子。</param>
    /// <returns>初期化時に固定した接続セッション一覧。</returns>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier)
    {
        requestedAccountIdentifiers.Add(accountIdentifier);
        return storedSessions;
    }
}
