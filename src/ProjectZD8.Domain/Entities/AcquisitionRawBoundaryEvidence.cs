using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>Raw取得の進行に合わせて追記する不変境界eventです。</summary>
public abstract record AcquisitionRawBoundaryEvidence
{
    /// <summary>外部からのケース追加を防ぎます。</summary>
    /// <remarks>責務: ケースを <see cref="Started"/> と <see cref="Ended"/> に限定します。</remarks>
    private AcquisitionRawBoundaryEvidence()
    {
    }

    /// <summary>最初のRaw要求を許可する直前の開始境界です。</summary>
    /// <param name="At">開始境界の時刻。</param>
    public sealed record Started(DateTimeOffset At) : AcquisitionRawBoundaryEvidence;

    /// <summary>Raw取得が停止した時刻と直接原因を保持する終了境界です。</summary>
    /// <param name="At">終了境界の時刻。</param>
    /// <param name="Reason">Raw取得が停止した直接原因。</param>
    public sealed record Ended(DateTimeOffset At, ConnectionSessionEndReason Reason) : AcquisitionRawBoundaryEvidence;

    /// <summary>eventが示す境界時刻です。</summary>
    public DateTimeOffset OccurredAt => this switch
    {
        Started started => started.At,
        Ended ended => ended.At,
        _ => throw new InvalidOperationException("未知の境界eventです。"),
    };
}
