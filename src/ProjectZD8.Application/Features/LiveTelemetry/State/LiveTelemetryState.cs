using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.State;

/// <summary>PID読取処理の現在段階です。</summary>
/// <remarks>Swift の <c>LiveTelemetryState.Phase</c> です（プロパティ名とぶつかるため外に出しました）。</remarks>
public enum LiveTelemetryStatePhase
{
    /// <summary>読取操作を待っています。</summary>
    Idle,

    /// <summary>DB登録済みPIDの対応状況を探索しています。</summary>
    Reading,

    /// <summary>応答済みPIDを継続取得しています。</summary>
    Loaded,

    /// <summary>取得停止と通信資源の解放完了を待っています。</summary>
    Stopping,

    /// <summary>PID読取または数値化に失敗しました。</summary>
    Failed,
}

/// <summary>リアルタイムPID取得と表示に必要な現在状態です。</summary>
public sealed record LiveTelemetryState
{
    /// <summary>現在の処理段階です。</summary>
    public LiveTelemetryStatePhase Phase { get; init; } = LiveTelemetryStatePhase.Idle;

    /// <summary>PIDごとの最新数値観測です。</summary>
    public IReadOnlyList<OBDPIDSample> Samples { get; init; } = [];

    /// <summary>初回探索で応答が確認できたPID数です。</summary>
    public int SupportedPIDCount { get; init; }

    /// <summary>読取りまたは再試行に使用するOBD終端です。</summary>
    public OBDConnectionEndpoint? Endpoint { get; init; }

    /// <summary>現在監視する車両のアプリ内識別子です。</summary>
    public VehicleID? VehicleID { get; init; }

    /// <summary>車種専用PIDの適用に使用する確認済み型式コードです。</summary>
    public string? VehicleModelCode { get; init; }

    /// <summary>直近の失敗を表示するローカライズキーです。</summary>
    public string? FailureKey { get; init; }

    /// <summary>HOMEで切断操作を提供する接続中段階かどうかです。</summary>
    public bool IsConnectionActive =>
        Phase is LiveTelemetryStatePhase.Reading or LiveTelemetryStatePhase.Loaded or LiveTelemetryStatePhase.Stopping;

    /// <summary>通信資源の安全な終了完了を待っているかどうかです。</summary>
    public bool IsDisconnecting => Phase == LiveTelemetryStatePhase.Stopping;

    /// <summary>観測一覧を内容で比較して等価性を判定します。</summary>
    /// <remarks>責務: Swift の Equatable と同じく全プロパティを値で比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全プロパティが等しければ <c>true</c>。</returns>
    public bool Equals(LiveTelemetryState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && Phase == other.Phase
            && Samples.SequenceEqual(other.Samples)
            && SupportedPIDCount == other.SupportedPIDCount
            && Equals(Endpoint, other.Endpoint)
            && VehicleID == other.VehicleID
            && string.Equals(VehicleModelCode, other.VehicleModelCode, StringComparison.Ordinal)
            && string.Equals(FailureKey, other.FailureKey, StringComparison.Ordinal);
    }

    /// <summary>内容からハッシュ値を計算します。</summary>
    /// <remarks>責務: 内容比較の等価性と矛盾しないハッシュ値を返します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Phase);
        foreach (var sample in Samples)
        {
            hash.Add(sample);
        }

        hash.Add(SupportedPIDCount);
        hash.Add(Endpoint);
        hash.Add(VehicleID);
        hash.Add(VehicleModelCode, StringComparer.Ordinal);
        hash.Add(FailureKey, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
