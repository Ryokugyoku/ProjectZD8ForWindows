using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.UseCases;

/// <summary>接続履歴をGarage表示用の車両別アクティビティへ縮約します。</summary>
public sealed class LoadVehicleActivitySummariesUseCase
{
    /// <summary>アカウント単位の接続履歴取得境界です。</summary>
    private readonly IConnectionSessionRepository repository;

    /// <summary>セッション取得先を固定してユースケースを生成します。</summary>
    /// <remarks>責務: 1件の接続履歴Repositoryを車両別アクティビティ集約へ結び付けます。</remarks>
    /// <param name="repository">アカウント単位の接続履歴取得先。</param>
    public LoadVehicleActivitySummariesUseCase(IConnectionSessionRepository repository)
    {
        this.repository = repository;
    }

    /// <summary>指定アカウントの履歴を車両IDごとの軽量集計へ変換します。</summary>
    /// <remarks>責務: 接続履歴を1回だけ走査して登録車両別のセッション統計を返します。</remarks>
    /// <param name="accountIdentifier">集計対象のAppleアカウント識別子。</param>
    /// <returns>登録車両IDをキーとするGarage表示用アクティビティ。</returns>
    /// <exception cref="Exception">接続履歴を取得できない場合のRepositoryエラー。</exception>
    public IReadOnlyDictionary<VehicleID, VehicleActivitySummary> Execute(string accountIdentifier)
    {
        var summaries = new Dictionary<VehicleID, VehicleActivitySummary>();
        var latestOdometerDateByVehicleID = new Dictionary<VehicleID, DateTimeOffset>();
        foreach (var session in repository.Sessions(accountIdentifier))
        {
            if (session.Vehicle is null)
            {
                continue;
            }

            var vehicleID = session.Vehicle.Id;
            var previous = summaries.GetValueOrDefault(vehicleID) ?? new VehicleActivitySummary();
            var sessionDate = session.EndedAt ?? session.StartedAt;
            var shouldUseOdometer = session.EndingOdometerKilometers is not null
                && sessionDate >= latestOdometerDateByVehicleID.GetValueOrDefault(vehicleID, DateTimeOffset.MinValue);
            if (shouldUseOdometer)
            {
                latestOdometerDateByVehicleID[vehicleID] = sessionDate;
            }

            summaries[vehicleID] = new VehicleActivitySummary(
                sessionCount: previous.SessionCount + 1,
                lastLoggedAt: Latest(previous.LastLoggedAt, sessionDate),
                totalRecordedDuration: previous.TotalRecordedDuration + RecordedDuration(session),
                interruptedCount: previous.InterruptedCount + (session.Status == ConnectionSessionStatus.Interrupted ? 1 : 0),
                isConnected: previous.IsConnected || session.Status == ConnectionSessionStatus.Connected,
                latestOdometerKilometers: shouldUseOdometer ? session.EndingOdometerKilometers : previous.LatestOdometerKilometers,
                odometerModelCode: shouldUseOdometer ? session.DistanceSourceModelCode : previous.OdometerModelCode);
        }

        return summaries;
    }

    /// <summary>2件の任意日時から新しい日時を返します。</summary>
    /// <remarks>責務: 車両別アクティビティに保持する最終ログ日時を単一値へ縮約します。</remarks>
    /// <param name="lhs">既に集約済みの日時。</param>
    /// <param name="rhs">新しく比較する日時。</param>
    /// <returns>2件のうち新しい日時。</returns>
    private static DateTimeOffset Latest(DateTimeOffset? lhs, DateTimeOffset rhs)
    {
        var left = lhs ?? DateTimeOffset.MinValue;
        return left > rhs ? left : rhs;
    }

    /// <summary>1件の終了済みセッションから非負の記録時間を返します。</summary>
    /// <remarks>責務: セッションの開始・終了日時をGarage集計用の非負秒数へ変換します。</remarks>
    /// <param name="session">記録時間を求める接続セッション。</param>
    /// <returns>未終了時は0、終了済みの場合は非負の経過秒数。</returns>
    private static double RecordedDuration(ConnectionSession session) =>
        Math.Max(0, session.EndedAt is { } endedAt ? (endedAt - session.StartedAt).TotalSeconds : 0);
}
