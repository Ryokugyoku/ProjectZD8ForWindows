namespace ProjectZD8.Domain.Entities;

/// <summary>policy選択後の1要求が物理通信境界をどこまで進んだかを表します。</summary>
public enum PIDRequestDispatchState
{
    /// <summary>policyで選択されたが送信処理は開始されていません。</summary>
    SelectedOnly,
    /// <summary>送信処理へ進むことを永続的に許可しました。</summary>
    DispatchBegun,
    /// <summary>transport結果が排他的に確定しました。</summary>
    Terminal,
}

/// <summary><see cref="PIDRequestDispatchState"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class PIDRequestDispatchStateRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this PIDRequestDispatchState value) => value switch
    {
        PIDRequestDispatchState.SelectedOnly => "selectedOnly",
        PIDRequestDispatchState.DispatchBegun => "dispatchBegun",
        PIDRequestDispatchState.Terminal => "terminal",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out PIDRequestDispatchState value)
    {
        switch (rawValue)
        {
            case "selectedOnly": value = PIDRequestDispatchState.SelectedOnly; return true;
            case "dispatchBegun": value = PIDRequestDispatchState.DispatchBegun; return true;
            case "terminal": value = PIDRequestDispatchState.Terminal; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>1件のPID要求について通信境界が確定できた排他結果です。</summary>
public enum PIDRequestTransportOutcome
{
    /// <summary>要求と一致する正応答payloadを取得しました。</summary>
    Responded,
    /// <summary>protocol上の明示証拠により非対応を確認しました。</summary>
    Unsupported,
    /// <summary>対象要求の期限内にterminal応答を取得できませんでした。</summary>
    TimedOut,
    /// <summary>明示的なtask取消しを対象要求で観測しました。</summary>
    Cancelled,
    /// <summary>対象要求中に通信境界を失いました。</summary>
    TransportFailure,
    /// <summary>永続化済みdispatch開始後にprocess終了して結果を確定できませんでした。</summary>
    UnknownAfterTermination,
    /// <summary>応答文字列は観測したものの承認済み結果へ分類できませんでした。</summary>
    UnclassifiedResponse,
}

/// <summary><see cref="PIDRequestTransportOutcome"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class PIDRequestTransportOutcomeRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this PIDRequestTransportOutcome value) => value switch
    {
        PIDRequestTransportOutcome.Responded => "responded",
        PIDRequestTransportOutcome.Unsupported => "unsupported",
        PIDRequestTransportOutcome.TimedOut => "timedOut",
        PIDRequestTransportOutcome.Cancelled => "cancelled",
        PIDRequestTransportOutcome.TransportFailure => "transportFailure",
        PIDRequestTransportOutcome.UnknownAfterTermination => "unknownAfterTermination",
        PIDRequestTransportOutcome.UnclassifiedResponse => "unclassifiedResponse",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out PIDRequestTransportOutcome value)
    {
        switch (rawValue)
        {
            case "responded": value = PIDRequestTransportOutcome.Responded; return true;
            case "unsupported": value = PIDRequestTransportOutcome.Unsupported; return true;
            case "timedOut": value = PIDRequestTransportOutcome.TimedOut; return true;
            case "cancelled": value = PIDRequestTransportOutcome.Cancelled; return true;
            case "transportFailure": value = PIDRequestTransportOutcome.TransportFailure; return true;
            case "unknownAfterTermination": value = PIDRequestTransportOutcome.UnknownAfterTermination; return true;
            case "unclassifiedResponse": value = PIDRequestTransportOutcome.UnclassifiedResponse; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>取得時PID definition snapshotによる値評価結果です。</summary>
public enum PIDRequestValueOutcome
{
    /// <summary>payloadを値として評価していません。</summary>
    NotEvaluated,
    /// <summary>finite値へdecodeでき取得時の宣言済み範囲を満たしました。</summary>
    DecodedValid,
    /// <summary>取得時definitionでpayloadをfinite値へdecodeできませんでした。</summary>
    DecodeFailure,
    /// <summary>finite値へdecodeできましたが取得時の宣言済み範囲外でした。</summary>
    InvalidValue,
}

/// <summary><see cref="PIDRequestValueOutcome"/> と Swift の raw value 文字列を相互変換します。</summary>
public static class PIDRequestValueOutcomeRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件のケースを永続化で使う安定文字列へ写します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift の raw value と一致する文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値が渡された場合。</exception>
    public static string ToRawValue(this PIDRequestValueOutcome value) => value switch
    {
        PIDRequestValueOutcome.NotEvaluated => "notEvaluated",
        PIDRequestValueOutcome.DecodedValid => "decodedValid",
        PIDRequestValueOutcome.DecodeFailure => "decodeFailure",
        PIDRequestValueOutcome.InvalidValue => "invalidValue",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 1件の安定文字列を対応するケースへ完全一致で写します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できたケース。失敗時は既定値。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out PIDRequestValueOutcome value)
    {
        switch (rawValue)
        {
            case "notEvaluated": value = PIDRequestValueOutcome.NotEvaluated; return true;
            case "decodedValid": value = PIDRequestValueOutcome.DecodedValid; return true;
            case "decodeFailure": value = PIDRequestValueOutcome.DecodeFailure; return true;
            case "invalidValue": value = PIDRequestValueOutcome.InvalidValue; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>1件の選択済みPID要求に対する永続的な進行証拠です。</summary>
public sealed record PIDRequestEvidence
{
    /// <summary>batch内で0から連続する要求順です。</summary>
    public int RequestOrdinal { get; }
    /// <summary>session manifestのordered PID集合を参照する位置です。</summary>
    public int ManifestPIDOrdinal { get; }
    /// <summary>要求が物理通信境界をどこまで進んだかを示します。</summary>
    public PIDRequestDispatchState DispatchState { get; }
    /// <summary>terminal時の通信結果です。</summary>
    public PIDRequestTransportOutcome? TransportOutcome { get; }
    /// <summary>取得時definitionによる値評価結果です。</summary>
    public PIDRequestValueOutcome ValueOutcome { get; }
    /// <summary><c>responded</c>時だけ参照する既存Raw sequenceです。</summary>
    public long? RawSequence { get; }
    /// <summary>要求開始からterminal観測までの単調経過時間です。</summary>
    public ulong? ElapsedNanoseconds { get; }
    /// <summary>transcript原文を保存せず承認済み分類理由を識別するcodeです。</summary>
    public string? ReasonCode { get; }

    /// <summary>要求進行と排他結果を整合する不変証拠へ固定します。</summary>
    /// <remarks>責務: 1件の選択済み要求についてdispatch、transport、value、Raw参照の整合性を検証します。</remarks>
    /// <param name="requestOrdinal">batch内で0から連続する要求順。</param>
    /// <param name="manifestPIDOrdinal">manifest ordered PID集合の非負位置。</param>
    /// <param name="dispatchState">要求の進行段階。</param>
    /// <param name="transportOutcome">terminal時の排他通信結果。</param>
    /// <param name="valueOutcome">取得時definitionによる値評価結果。</param>
    /// <param name="rawSequence"><c>responded</c>時だけ指定する非負Raw sequence。</param>
    /// <param name="elapsedNanoseconds">terminal観測までの任意の単調経過時間。</param>
    /// <param name="reasonCode">承認済み分類理由code。</param>
    /// <exception cref="AcquisitionBatchEvidenceException">ordinalまたは結果の組が不正な場合。</exception>
    public PIDRequestEvidence(
        int requestOrdinal,
        int manifestPIDOrdinal,
        PIDRequestDispatchState dispatchState,
        PIDRequestTransportOutcome? transportOutcome,
        PIDRequestValueOutcome valueOutcome,
        long? rawSequence,
        ulong? elapsedNanoseconds,
        string? reasonCode)
    {
        if (requestOrdinal < 0 || manifestPIDOrdinal < 0)
        {
            throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidRequestOrdinal);
        }
        if (reasonCode is { Length: 0 })
        {
            throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.EmptyReasonCode);
        }
        switch (dispatchState)
        {
            case PIDRequestDispatchState.SelectedOnly:
            case PIDRequestDispatchState.DispatchBegun:
                if (transportOutcome is not null || valueOutcome != PIDRequestValueOutcome.NotEvaluated
                    || rawSequence is not null || elapsedNanoseconds is not null)
                {
                    throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidRequestState);
                }
                break;
            case PIDRequestDispatchState.Terminal:
                if (transportOutcome is not { } outcome)
                {
                    throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidRequestState);
                }
                if (outcome == PIDRequestTransportOutcome.Responded)
                {
                    if (rawSequence is not >= 0)
                    {
                        throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.RespondedRawReferenceMissing);
                    }
                }
                else if (rawSequence is not null || valueOutcome != PIDRequestValueOutcome.NotEvaluated)
                {
                    throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.NonResponseContainsValue);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dispatchState), dispatchState, null);
        }
        RequestOrdinal = requestOrdinal;
        ManifestPIDOrdinal = manifestPIDOrdinal;
        DispatchState = dispatchState;
        TransportOutcome = transportOutcome;
        ValueOutcome = valueOutcome;
        RawSequence = rawSequence;
        ElapsedNanoseconds = elapsedNanoseconds;
        ReasonCode = reasonCode;
    }
}
