using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>履歴画面が表示するセッション状態です。</summary>
/// <remarks>Swift の <c>ConnectionSession.Status</c> に対応します。C# では型と同名のプロパティ <see cref="ConnectionSession.Status"/> を入れ子にできないため外へ出しています。</remarks>
public enum ConnectionSessionStatus
{
    /// <summary>現在も接続と取得が継続しています。</summary>
    Connected,
    /// <summary>ユーザー操作によって正常に終了しました。</summary>
    Completed,
    /// <summary>通信または処理上の理由で中断しました。</summary>
    Interrupted,
}

/// <summary>HOME接続開始から通信終了までの1回の接続履歴です。</summary>
public sealed record ConnectionSession
{
    /// <summary>セッションの安定識別子です。</summary>
    public ConnectionSessionID Id { get; }
    /// <summary>セッションを所有するAppleアカウント識別子です。</summary>
    public string AccountIdentifier { get; }
    /// <summary>HOMEで接続開始を押した日時です。</summary>
    public DateTimeOffset StartedAt { get; }
    /// <summary>終了が確定した日時です。</summary>
    public DateTimeOffset? EndedAt { get; init; }
    /// <summary>終了が確定した直接原因です。</summary>
    public ConnectionSessionEndReason? EndReason { get; init; }
    /// <summary>自動判別できない終了に対するユーザー確認結果です。</summary>
    public ConnectionSessionStopReviewDecision? StopReviewDecision { get; init; }
    /// <summary>接続時点の登録車両表示情報です。</summary>
    public ConnectionSessionVehicle? Vehicle { get; init; }
    /// <summary>ログ取得を開始した端末の表示情報です。</summary>
    public ConnectionSessionAcquisitionDevice? AcquisitionDevice { get; init; }
    /// <summary>セッション内で最初に取得できた累積走行距離です。</summary>
    public double? StartingOdometerKilometers { get; init; }
    /// <summary>セッション内で最後に取得できた累積走行距離です。</summary>
    public double? EndingOdometerKilometers { get; init; }
    /// <summary>累積走行距離を車種専用PIDから取得した場合の型式です。</summary>
    public string? DistanceSourceModelCode { get; init; }
    /// <summary>未デコードRawログの件数と端末間保管状態です。</summary>
    public ConnectionSessionRawLogSummary RawLogSummary { get; init; }

    /// <summary>終了情報から履歴表示用の現在状態を返します。</summary>
    public ConnectionSessionStatus Status
    {
        get
        {
            if (EndReason is null)
            {
                return ConnectionSessionStatus.Connected;
            }
            return EndReason == ConnectionSessionEndReason.UserDisconnected
                || StopReviewDecision == ConnectionSessionStopReviewDecision.UserInitiated
                ? ConnectionSessionStatus.Completed
                : ConnectionSessionStatus.Interrupted;
        }
    }

    /// <summary>ユーザー操作の可能性があるため確認を提示できる終了かを示します。</summary>
    public bool NeedsStopReview
    {
        get
        {
            if (StopReviewDecision is not null)
            {
                return false;
            }
            return EndReason == ConnectionSessionEndReason.VehicleNoResponse
                || EndReason == ConnectionSessionEndReason.ConnectionLost;
        }
    }

    /// <summary>セッション中の累積走行距離差分です。</summary>
    public double? RecordedDistanceKilometers
    {
        get
        {
            if (StartingOdometerKilometers is not { } starting || EndingOdometerKilometers is not { } ending
                || !double.IsFinite(starting) || !double.IsFinite(ending)
                || !(starting >= 0) || !(ending >= starting))
            {
                return null;
            }
            return ending - starting;
        }
    }

    /// <summary>セッションの所有者と開始時刻を固定して生成します。</summary>
    /// <remarks>責務: 1回のHOME接続開始を未終了の接続セッションへ変換します。</remarks>
    /// <param name="accountIdentifier">セッションを所有するAppleアカウント識別子。</param>
    /// <param name="id">セッションへ割り当てる安定識別子。<see langword="null"/> なら新規に採番します。</param>
    /// <param name="startedAt">HOMEで接続開始を押した日時。<see langword="null"/> なら現在時刻です。</param>
    /// <param name="vehicle">開始時点で確定済みの場合に保持する車両情報。</param>
    /// <param name="acquisitionDevice">ログ取得を開始した端末の表示情報。</param>
    public ConnectionSession(
        string accountIdentifier,
        ConnectionSessionID? id = null,
        DateTimeOffset? startedAt = null,
        ConnectionSessionVehicle? vehicle = null,
        ConnectionSessionAcquisitionDevice? acquisitionDevice = null)
    {
        Id = id ?? ConnectionSessionID.New();
        AccountIdentifier = accountIdentifier;
        StartedAt = startedAt ?? DateTimeOffset.UtcNow;
        EndedAt = null;
        EndReason = null;
        StopReviewDecision = null;
        Vehicle = vehicle;
        AcquisitionDevice = acquisitionDevice;
        StartingOdometerKilometers = null;
        EndingOdometerKilometers = null;
        DistanceSourceModelCode = null;
        RawLogSummary = ConnectionSessionRawLogSummary.Empty;
    }
}
