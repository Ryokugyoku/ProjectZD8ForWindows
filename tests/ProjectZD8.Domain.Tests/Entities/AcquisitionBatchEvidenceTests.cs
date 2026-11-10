using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Entities;

/// <summary>batch、selection、request outcomeのDomain不変条件を検証します。</summary>
public sealed class AcquisitionBatchEvidenceTests
{
    /// <summary>policy評価完了時だけ非選択PIDを意図的省略として返します。</summary>
    /// <remarks>責務: 選択集合差が未完了batchやmissing値から推測されないことを確認します。</remarks>
    [Fact]
    public void IntentionalOmissionRequiresCompletedPolicyEvaluation()
    {
        var request = PendingRequest(requestOrdinal: 0, manifestPIDOrdinal: 1);
        var completed = Batch(selectionComplete: true, requests: [request]);
        var incomplete = Batch(selectionComplete: false, requests: [request]);

        Assert.Equal(AcquisitionPIDSelection.Requested, completed.Selection(1));
        Assert.Equal(AcquisitionPIDSelection.IntentionalPollingOmission, completed.Selection(0));
        Assert.Null(incomplete.Selection(0));
    }

    /// <summary>respondedだけがRaw sequenceを参照できます。</summary>
    /// <remarks>責務: response有無とRaw参照の双方向不変条件を確認します。</remarks>
    [Fact]
    public void RawReferenceExistsOnlyForRespondedRequest()
    {
        var missing = Assert.Throws<AcquisitionBatchEvidenceException>(
            () => TerminalRequest(PIDRequestTransportOutcome.Responded, rawSequence: null));
        Assert.Equal(AcquisitionBatchEvidenceError.RespondedRawReferenceMissing, missing.Reason);

        var nonResponse = Assert.Throws<AcquisitionBatchEvidenceException>(
            () => TerminalRequest(PIDRequestTransportOutcome.TimedOut, rawSequence: 4));
        Assert.Equal(AcquisitionBatchEvidenceError.NonResponseContainsValue, nonResponse.Reason);

        var exception = Record.Exception(() => TerminalRequest(PIDRequestTransportOutcome.Responded, rawSequence: 4));
        Assert.Null(exception);
    }

    /// <summary>明示cancelとprocess終了後unknownを別値として保持します。</summary>
    /// <remarks>責務: 2種類のterminal transport outcomeがDomain上で混同されないことを確認します。</remarks>
    [Fact]
    public void CancellationAndUnknownAfterTerminationRemainDistinct()
    {
        var cancelled = TerminalRequest(PIDRequestTransportOutcome.Cancelled, rawSequence: null);
        var terminated = TerminalRequest(PIDRequestTransportOutcome.UnknownAfterTermination, rawSequence: null);

        Assert.Equal(PIDRequestTransportOutcome.Cancelled, cancelled.TransportOutcome);
        Assert.Equal(PIDRequestTransportOutcome.UnknownAfterTermination, terminated.TransportOutcome);
        Assert.NotEqual(cancelled, terminated);
    }

    /// <summary>completed batchは全要求のterminal結果を必要とします。</summary>
    /// <remarks>責務: selected-only要求を含むbatchが完全成功へ確定されないことを確認します。</remarks>
    [Fact]
    public void CompletedBatchRejectsPartialRequest()
    {
        var request = PendingRequest(requestOrdinal: 0, manifestPIDOrdinal: 0);

        var error = Assert.Throws<AcquisitionBatchEvidenceException>(() => new AcquisitionBatchEvidence(
            identity: new AcquisitionBatchIdentity(ordinal: 0),
            generation: 1,
            policyTick: 0,
            isSelectionEvaluationComplete: true,
            startedAt: DateTimeOffset.FromUnixTimeSeconds(10),
            completionState: AcquisitionBatchCompletionState.Completed,
            completedAt: DateTimeOffset.FromUnixTimeSeconds(11),
            failure: null,
            requests: [request]));
        Assert.Equal(AcquisitionBatchEvidenceError.InvalidBatchState, error.Reason);
    }

    /// <summary>test用未確定batchを生成します。</summary>
    /// <remarks>責務: selection完了状態と要求列を固定したopen batchを返します。</remarks>
    /// <param name="selectionComplete">policy評価完了の有無。</param>
    /// <param name="requests">選択済み要求列。</param>
    /// <returns>terminal結果を持たないbatch証拠。</returns>
    private static AcquisitionBatchEvidence Batch(bool selectionComplete, IReadOnlyList<PIDRequestEvidence> requests) =>
        new(
            identity: new AcquisitionBatchIdentity(ordinal: 0),
            generation: 1,
            policyTick: 0,
            isSelectionEvaluationComplete: selectionComplete,
            startedAt: DateTimeOffset.FromUnixTimeSeconds(10),
            completionState: null,
            completedAt: null,
            failure: null,
            requests: requests);

    /// <summary>test用selected-only要求を生成します。</summary>
    /// <remarks>責務: 指定ordinalを持つ値未評価の要求証拠を返します。</remarks>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="manifestPIDOrdinal">manifest PID位置。</param>
    /// <returns>dispatch前の要求証拠。</returns>
    private static PIDRequestEvidence PendingRequest(int requestOrdinal, int manifestPIDOrdinal) =>
        new(
            requestOrdinal: requestOrdinal,
            manifestPIDOrdinal: manifestPIDOrdinal,
            dispatchState: PIDRequestDispatchState.SelectedOnly,
            transportOutcome: null,
            valueOutcome: PIDRequestValueOutcome.NotEvaluated,
            rawSequence: null,
            elapsedNanoseconds: null,
            reasonCode: null);

    /// <summary>test用terminal要求を生成します。</summary>
    /// <remarks>責務: 指定transport outcomeとRaw参照を持つ要求証拠を返します。</remarks>
    /// <param name="outcome">terminal transport outcome。</param>
    /// <param name="rawSequence">既存Raw参照候補。</param>
    /// <returns>terminal要求証拠。</returns>
    private static PIDRequestEvidence TerminalRequest(PIDRequestTransportOutcome outcome, long? rawSequence) =>
        new(
            requestOrdinal: 0,
            manifestPIDOrdinal: 0,
            dispatchState: PIDRequestDispatchState.Terminal,
            transportOutcome: outcome,
            valueOutcome: outcome == PIDRequestTransportOutcome.Responded
                ? PIDRequestValueOutcome.DecodedValid
                : PIDRequestValueOutcome.NotEvaluated,
            rawSequence: rawSequence,
            elapsedNanoseconds: 100,
            reasonCode: null);
}
