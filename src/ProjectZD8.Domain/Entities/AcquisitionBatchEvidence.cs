using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>policy evaluation完了後に導出できるPID選択状態です。</summary>
public enum AcquisitionPIDSelection
{
    /// <summary>batchの送信対象として選択されました。</summary>
    Requested,
    /// <summary>完了済みpolicy evaluationにより意図的に選択されませんでした。</summary>
    IntentionalPollingOmission,
}

/// <summary>batchが永続的に確定した終端状態です。</summary>
public enum AcquisitionBatchCompletionState
{
    /// <summary>全選択要求のterminal結果を保存して完了しました。</summary>
    Completed,
    /// <summary>観測可能な失敗により一部または全部の要求を完了できませんでした。</summary>
    Failed,
    /// <summary>process終了後の回復でbatch終端だけを確定しました。</summary>
    TerminatedUnknown,
}

/// <summary><see cref="AcquisitionBatchCompletionState"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class AcquisitionBatchCompletionStateRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this AcquisitionBatchCompletionState value) => value switch
    {
        AcquisitionBatchCompletionState.Completed => "completed",
        AcquisitionBatchCompletionState.Failed => "failed",
        AcquisitionBatchCompletionState.TerminatedUnknown => "terminatedUnknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionBatchCompletionState value)
    {
        switch (rawValue)
        {
            case "completed": value = AcquisitionBatchCompletionState.Completed; return true;
            case "failed": value = AcquisitionBatchCompletionState.Failed; return true;
            case "terminatedUnknown": value = AcquisitionBatchCompletionState.TerminatedUnknown; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>batch全体を完了できなかったApplication上の分類です。</summary>
public enum AcquisitionBatchFailure
{
    /// <summary>通信境界をbatch途中で利用できなくなりました。</summary>
    TransportUnavailable,
    /// <summary>明示取消しによりbatchを停止しました。</summary>
    Cancelled,
    /// <summary>Rawまたはrequest evidenceを完全に保存できませんでした。</summary>
    PersistenceFailure,
    /// <summary>queue飽和により新規物理要求を開始せず停止しました。</summary>
    BackpressureStopped,
    /// <summary>process終了後の回復まで終端を観測できませんでした。</summary>
    ProcessTerminated,
    /// <summary>承認済み分類へ変換できない結果がありました。</summary>
    UnclassifiedResult,
}

/// <summary><see cref="AcquisitionBatchFailure"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class AcquisitionBatchFailureRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this AcquisitionBatchFailure value) => value switch
    {
        AcquisitionBatchFailure.TransportUnavailable => "transportUnavailable",
        AcquisitionBatchFailure.Cancelled => "cancelled",
        AcquisitionBatchFailure.PersistenceFailure => "persistenceFailure",
        AcquisitionBatchFailure.BackpressureStopped => "backpressureStopped",
        AcquisitionBatchFailure.ProcessTerminated => "processTerminated",
        AcquisitionBatchFailure.UnclassifiedResult => "unclassifiedResult",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionBatchFailure value)
    {
        switch (rawValue)
        {
            case "transportUnavailable": value = AcquisitionBatchFailure.TransportUnavailable; return true;
            case "cancelled": value = AcquisitionBatchFailure.Cancelled; return true;
            case "persistenceFailure": value = AcquisitionBatchFailure.PersistenceFailure; return true;
            case "backpressureStopped": value = AcquisitionBatchFailure.BackpressureStopped; return true;
            case "processTerminated": value = AcquisitionBatchFailure.ProcessTerminated; return true;
            case "unclassifiedResult": value = AcquisitionBatchFailure.UnclassifiedResult; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>1回のpolling policy evaluationとPID要求列の永続証拠です。</summary>
public sealed record AcquisitionBatchEvidence
{
    /// <summary>session内のbatch identityです。</summary>
    public AcquisitionBatchIdentity Identity { get; }
    /// <summary>stale callback拒否に使用した取得世代です。</summary>
    public ulong Generation { get; }
    /// <summary>policyが評価した単調tickです。</summary>
    public ulong PolicyTick { get; }
    /// <summary>policy evaluationが完了して選択集合を確定できたかを示します。</summary>
    public bool IsSelectionEvaluationComplete { get; }
    /// <summary>batch開始を観測した実時間です。</summary>
    public DateTimeOffset StartedAt { get; }
    /// <summary>batchのterminal状態です。<see langword="null"/> は未確定batchを表します。</summary>
    public AcquisitionBatchCompletionState? CompletionState { get; }
    /// <summary>terminal状態を確定した実時間です。</summary>
    public DateTimeOffset? CompletedAt { get; }
    /// <summary>batch全体の失敗分類です。</summary>
    public AcquisitionBatchFailure? Failure { get; }
    /// <summary>policyで選択された要求の順序付き証拠です。</summary>
    public IReadOnlyList<PIDRequestEvidence> Requests { get; }

    /// <summary>batch metadataと要求列の整合を検証して固定します。</summary>
    /// <remarks>責務: 1回のpolicy tickをpartialとterminationを保持できる不変batch証拠へ変換します。</remarks>
    /// <param name="identity">session内batch identity。</param>
    /// <param name="generation">取得世代。</param>
    /// <param name="policyTick">policyの単調tick。</param>
    /// <param name="isSelectionEvaluationComplete">policy評価完了の有無。</param>
    /// <param name="startedAt">batch開始実時間。</param>
    /// <param name="completionState">terminal状態、または未確定を示す <see langword="null"/>。</param>
    /// <param name="completedAt">terminal確定実時間。</param>
    /// <param name="failure">batch-level failure。</param>
    /// <param name="requests">policy選択順の要求証拠。</param>
    /// <exception cref="AcquisitionBatchEvidenceException">terminal、時系列、ordinal、不変条件が不正な場合。</exception>
    public AcquisitionBatchEvidence(
        AcquisitionBatchIdentity identity,
        ulong generation,
        ulong policyTick,
        bool isSelectionEvaluationComplete,
        DateTimeOffset startedAt,
        AcquisitionBatchCompletionState? completionState,
        DateTimeOffset? completedAt,
        AcquisitionBatchFailure? failure,
        IReadOnlyList<PIDRequestEvidence> requests)
    {
        var copied = requests.ToArray();
        var ordinalsAreSequential = copied.Select((request, offset) => offset == request.RequestOrdinal).All(isMatch => isMatch);
        if (!ordinalsAreSequential
            || copied.Select(request => request.ManifestPIDOrdinal).Distinct().Count() != copied.Length)
        {
            throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidRequestOrdinal);
        }
        switch (completionState)
        {
            case null:
                if (completedAt is not null || failure is not null)
                {
                    throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidBatchState);
                }
                break;
            case AcquisitionBatchCompletionState.Completed:
                if (completedAt is not { } completed || completed < startedAt || failure is not null
                    || !isSelectionEvaluationComplete
                    || !copied.All(request => request.DispatchState == PIDRequestDispatchState.Terminal))
                {
                    throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidBatchState);
                }
                break;
            default:
                if (completedAt is not { } terminated || terminated < startedAt || failure is null)
                {
                    throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidBatchState);
                }
                break;
        }
        Identity = identity;
        Generation = generation;
        PolicyTick = policyTick;
        IsSelectionEvaluationComplete = isSelectionEvaluationComplete;
        StartedAt = startedAt;
        CompletionState = completionState;
        CompletedAt = completedAt;
        Failure = failure;
        Requests = copied;
    }

    /// <summary>manifest PID位置がこのbatchで選択されたかを返します。</summary>
    /// <remarks>責務: 完了済みpolicy evaluationと選択要求集合を要求または意図的省略へ変換します。</remarks>
    /// <param name="manifestPIDOrdinal">判定するmanifest ordered PID位置。</param>
    /// <returns>policy評価完了時の選択状態。未完了または負数では <see langword="null"/>。</returns>
    public AcquisitionPIDSelection? Selection(int manifestPIDOrdinal)
    {
        if (!IsSelectionEvaluationComplete || manifestPIDOrdinal < 0)
        {
            return null;
        }
        return Requests.Any(request => request.ManifestPIDOrdinal == manifestPIDOrdinal)
            ? AcquisitionPIDSelection.Requested
            : AcquisitionPIDSelection.IntentionalPollingOmission;
    }

    /// <summary>要求列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じく要求列を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(AcquisitionBatchEvidence? other) =>
        other is not null
        && Identity.Equals(other.Identity)
        && Generation == other.Generation
        && PolicyTick == other.PolicyTick
        && IsSelectionEvaluationComplete == other.IsSelectionEvaluationComplete
        && StartedAt == other.StartedAt
        && CompletionState == other.CompletionState
        && CompletedAt == other.CompletedAt
        && Failure == other.Failure
        && Requests.SequenceEqual(other.Requests);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: 要求列の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        hash.Add(Generation);
        hash.Add(PolicyTick);
        hash.Add(IsSelectionEvaluationComplete);
        hash.Add(StartedAt);
        hash.Add(CompletionState);
        hash.Add(CompletedAt);
        hash.Add(Failure);
        foreach (var request in Requests)
        {
            hash.Add(request);
        }
        return hash.ToHashCode();
    }
}

/// <summary>batchまたは要求証拠の不変条件違反です。</summary>
public enum AcquisitionBatchEvidenceError
{
    /// <summary>batch ordinalが負数です。</summary>
    InvalidBatchOrdinal,
    /// <summary>requestまたはmanifest PID ordinalが負数、重複、非連続です。</summary>
    InvalidRequestOrdinal,
    /// <summary>dispatch段階とterminal結果の組が不正です。</summary>
    InvalidRequestState,
    /// <summary><c>responded</c>に既存Raw sequence参照がありません。</summary>
    RespondedRawReferenceMissing,
    /// <summary>非応答結果が値評価またはRaw参照を保持しています。</summary>
    NonResponseContainsValue,
    /// <summary>batch terminal状態、時刻、failureの組が不正です。</summary>
    InvalidBatchState,
    /// <summary>空のreason codeが指定されました。</summary>
    EmptyReasonCode,
}

/// <summary>batchまたは要求証拠の不変条件違反を表す例外です。</summary>
public sealed class AcquisitionBatchEvidenceException : Exception
{
    /// <summary>違反の理由です。</summary>
    public AcquisitionBatchEvidenceError Reason { get; }

    /// <summary>理由を指定して例外を生成します。</summary>
    /// <remarks>責務: Swift の <c>AcquisitionBatchEvidenceError</c> のケースを例外として運びます。</remarks>
    /// <param name="reason">違反の理由。</param>
    public AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError reason)
        : base(reason.ToString())
    {
        Reason = reason;
    }
}
