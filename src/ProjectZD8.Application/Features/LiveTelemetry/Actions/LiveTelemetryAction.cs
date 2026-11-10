using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.Actions;

/// <summary>リアルタイムPID取得をApplicationへ通知する操作です。</summary>
public abstract record LiveTelemetryAction
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: ケースをこのファイルの入れ子レコードだけに限定します。</remarks>
    private LiveTelemetryAction()
    {
    }

    /// <summary>指定OBD終端から車種適用済みの継続取得開始を要求します。</summary>
    /// <param name="Endpoint">OBDアダプターの物理終端。</param>
    /// <param name="VehicleID">対応PID設定を参照する車両ID。</param>
    /// <param name="VehicleModelCode">確認済み車種専用PIDを適用する型式コード。</param>
    public sealed record StartRequested(OBDConnectionEndpoint Endpoint, VehicleID VehicleID, string? VehicleModelCode)
        : LiveTelemetryAction;

    /// <summary>直近の接続終端で継続取得の再開を要求します。</summary>
    public sealed record RetryRequested : LiveTelemetryAction;

    /// <summary>現在の継続取得を終了します。</summary>
    public sealed record StopRequested : LiveTelemetryAction;
}
