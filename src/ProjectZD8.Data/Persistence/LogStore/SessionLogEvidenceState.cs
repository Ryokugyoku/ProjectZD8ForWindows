using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>1セッションの取得batchとPID要求証拠の現在状態をメモリ上に保持します。</summary>
/// <remarks>
/// ジャーナルのレコードを順に適用して作ります（追記時も再起動時の再生も同じ処理）。
/// iOS版のbatch表・request表とその制約（Raw sequenceの一意性など）の判定に使います。
/// 呼出元（<see cref="SessionLog"/>）のロック内で使います。
/// </remarks>
internal sealed class SessionLogEvidenceState
{
    /// <summary>batch番号ごとの状態です。</summary>
    private readonly SortedDictionary<long, AcquisitionBatchEvidence> _batches = [];

    /// <summary>request証拠から参照されているRaw sequenceです（iOS版の <c>UNIQUE (sessionID, rawSequence)</c>）。</summary>
    private readonly HashSet<long> _referencedRawSequences = [];

    /// <summary>再生中に矛盾したレコードがあったかどうかです。</summary>
    internal bool IsCorrupt { get; private set; }

    /// <summary>batch数です。</summary>
    internal int BatchCount => _batches.Count;

    /// <summary>batch番号順の全batch証拠です。</summary>
    internal IEnumerable<AcquisitionBatchEvidence> Batches => _batches.Values;

    /// <summary>指定batchの証拠を返します。</summary>
    /// <remarks>責務: batch番号で現在状態を引きます。</remarks>
    /// <param name="ordinal">batch番号。</param>
    /// <returns>batch証拠。未登録なら <see langword="null"/>。</returns>
    internal AcquisitionBatchEvidence? Batch(long ordinal) => _batches.GetValueOrDefault(ordinal);

    /// <summary>未sealのbatchをbatch番号順に返します。</summary>
    /// <remarks>責務: 終了・回復処理の対象となるopen batchだけを選びます。</remarks>
    /// <returns>open batchの一覧。</returns>
    internal List<AcquisitionBatchEvidence> OpenBatches() =>
        _batches.Values.Where(batch => batch.CompletionState is null).ToList();

    /// <summary>Raw sequenceがすでにrequest証拠から参照されているかを返します。</summary>
    /// <remarks>責務: 1件のRawを2件のrequestが参照しないことを確かめます。</remarks>
    /// <param name="sequence">Raw sequence。</param>
    /// <returns>参照済みなら <see langword="true"/>。</returns>
    internal bool IsRawReferenced(long sequence) => _referencedRawSequences.Contains(sequence);

    /// <summary>1件のレコードを状態へ適用します。</summary>
    /// <remarks>責務: 証拠レコードをbatch状態の変更へ変換します。Rawだけのレコードは何もしません。</remarks>
    /// <param name="record">適用するレコード。</param>
    internal void Apply(SessionLogRecord record)
    {
        try
        {
            switch (record)
            {
                case SessionLogRecord.Raw:
                    return;
                case SessionLogRecord.BatchBegun begun:
                    if (_batches.ContainsKey(begun.Ordinal))
                    {
                        IsCorrupt = true;
                        return;
                    }
                    _batches[begun.Ordinal] = new AcquisitionBatchEvidence(
                        new AcquisitionBatchIdentity(begun.Ordinal),
                        begun.Generation,
                        begun.PolicyTick,
                        begun.IsSelectionEvaluationComplete,
                        new DateTimeOffset(begun.StartedTicks, TimeSpan.Zero),
                        completionState: null,
                        completedAt: null,
                        failure: null,
                        begun.ManifestOrdinals.Select((manifestOrdinal, requestOrdinal) => new PIDRequestEvidence(
                            requestOrdinal,
                            manifestOrdinal,
                            PIDRequestDispatchState.SelectedOnly,
                            transportOutcome: null,
                            PIDRequestValueOutcome.NotEvaluated,
                            rawSequence: null,
                            elapsedNanoseconds: null,
                            reasonCode: null)).ToArray());
                    return;
                case SessionLogRecord.DispatchBegun dispatch:
                    ReplaceRequest(dispatch.Ordinal, dispatch.RequestOrdinal, current => new PIDRequestEvidence(
                        current.RequestOrdinal,
                        current.ManifestPIDOrdinal,
                        PIDRequestDispatchState.DispatchBegun,
                        transportOutcome: null,
                        PIDRequestValueOutcome.NotEvaluated,
                        rawSequence: null,
                        elapsedNanoseconds: null,
                        reasonCode: current.ReasonCode));
                    return;
                case SessionLogRecord.RequestTerminal terminal:
                    ReplaceRequest(terminal.Ordinal, terminal.Request.RequestOrdinal, _ => terminal.Request);
                    return;
                case SessionLogRecord.Responded responded:
                    ReplaceRequest(responded.Ordinal, responded.Request.RequestOrdinal, _ => responded.Request);
                    return;
                case SessionLogRecord.BatchSealed sealedBatch:
                    Seal(sealedBatch);
                    return;
                case SessionLogRecord.BatchSnapshot snapshot:
                    if (_batches.TryGetValue(snapshot.Batch.Identity.Ordinal, out var previous))
                    {
                        Unreference(previous);
                    }
                    _batches[snapshot.Batch.Identity.Ordinal] = snapshot.Batch;
                    Reference(snapshot.Batch);
                    return;
            }
        }
        catch (Exception)
        {
            IsCorrupt = true;
        }
    }

    /// <summary>1件のレコードを適用した結果のbatchを、状態を変えずに計算します。</summary>
    /// <remarks>責務: iOS版のcanonical readbackと同じく、確定前に結果を検証できるようにします。</remarks>
    /// <param name="record">適用を試すレコード。</param>
    /// <returns>適用後のbatch証拠。</returns>
    /// <exception cref="Exception">適用できない場合（Domain不変条件違反など）。</exception>
    internal AcquisitionBatchEvidence Preview(SessionLogRecord record)
    {
        var shadow = new SessionLogEvidenceState();
        long ordinal = record switch
        {
            SessionLogRecord.BatchBegun begun => begun.Ordinal,
            SessionLogRecord.DispatchBegun dispatch => dispatch.Ordinal,
            SessionLogRecord.RequestTerminal terminal => terminal.Ordinal,
            SessionLogRecord.Responded responded => responded.Ordinal,
            SessionLogRecord.BatchSealed sealedBatch => sealedBatch.Ordinal,
            SessionLogRecord.BatchSnapshot snapshot => snapshot.Batch.Identity.Ordinal,
            _ => throw new ArgumentOutOfRangeException(nameof(record)),
        };
        if (_batches.TryGetValue(ordinal, out var current))
        {
            shadow._batches[ordinal] = current;
        }
        shadow.Apply(record);
        if (shadow.IsCorrupt || shadow.Batch(ordinal) is not { } result)
        {
            throw new InvalidOperationException("Log record cannot be applied");
        }
        return result;
    }

    /// <summary>batch内の1件のrequestを置き換えます。</summary>
    /// <remarks>責務: 1件のrequest証拠を更新し、Raw参照の集合を同期します。</remarks>
    /// <param name="ordinal">batch番号。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="update">現在値から新しい値を作る処理。</param>
    private void ReplaceRequest(long ordinal, int requestOrdinal, Func<PIDRequestEvidence, PIDRequestEvidence> update)
    {
        if (!_batches.TryGetValue(ordinal, out var batch) || requestOrdinal < 0 || requestOrdinal >= batch.Requests.Count)
        {
            IsCorrupt = true;
            return;
        }
        var requests = batch.Requests.ToArray();
        var previous = requests[requestOrdinal];
        var replaced = update(previous);
        requests[requestOrdinal] = replaced;
        _batches[ordinal] = WithRequests(batch, requests, batch.CompletionState, batch.CompletedAt, batch.Failure);
        if (previous.RawSequence is { } oldSequence)
        {
            _referencedRawSequences.Remove(oldSequence);
        }
        if (replaced.RawSequence is { } newSequence)
        {
            _referencedRawSequences.Add(newSequence);
        }
    }

    /// <summary>batchを終端へ確定します。</summary>
    /// <remarks>責務: request終端の更新を反映してからbatchの終端状態を設定します。</remarks>
    /// <param name="record">batch終端レコード。</param>
    private void Seal(SessionLogRecord.BatchSealed record)
    {
        if (!_batches.TryGetValue(record.Ordinal, out var batch))
        {
            IsCorrupt = true;
            return;
        }
        var requests = batch.Requests.ToArray();
        foreach (var update in record.Updates)
        {
            if (update.RequestOrdinal < 0 || update.RequestOrdinal >= requests.Length)
            {
                IsCorrupt = true;
                return;
            }
            if (requests[update.RequestOrdinal].RawSequence is { } oldSequence)
            {
                _referencedRawSequences.Remove(oldSequence);
            }
            requests[update.RequestOrdinal] = update;
            if (update.RawSequence is { } newSequence)
            {
                _referencedRawSequences.Add(newSequence);
            }
        }
        _batches[record.Ordinal] = WithRequests(
            batch,
            requests,
            record.CompletionState,
            new DateTimeOffset(record.CompletedTicks, TimeSpan.Zero),
            record.Failure);
    }

    /// <summary>batchの参照中Raw sequenceを集合へ加えます。</summary>
    /// <remarks>責務: 写しで置き換えたbatchのRaw参照を登録します。</remarks>
    /// <param name="batch">登録するbatch。</param>
    private void Reference(AcquisitionBatchEvidence batch)
    {
        foreach (var request in batch.Requests)
        {
            if (request.RawSequence is { } sequence)
            {
                _referencedRawSequences.Add(sequence);
            }
        }
    }

    /// <summary>batchの参照中Raw sequenceを集合から外します。</summary>
    /// <remarks>責務: 写しで置き換える前のbatchのRaw参照を取り消します。</remarks>
    /// <param name="batch">取り消すbatch。</param>
    private void Unreference(AcquisitionBatchEvidence batch)
    {
        foreach (var request in batch.Requests)
        {
            if (request.RawSequence is { } sequence)
            {
                _referencedRawSequences.Remove(sequence);
            }
        }
    }

    /// <summary>request列と終端状態を差し替えたbatchを作ります。</summary>
    /// <remarks>責務: Domain不変条件を検証しながらbatch証拠を作り直します。</remarks>
    /// <param name="batch">元のbatch。</param>
    /// <param name="requests">新しいrequest列。</param>
    /// <param name="state">終端状態。</param>
    /// <param name="completedAt">終端確定日時。</param>
    /// <param name="failure">失敗理由。</param>
    /// <returns>新しいbatch証拠。</returns>
    private static AcquisitionBatchEvidence WithRequests(
        AcquisitionBatchEvidence batch,
        PIDRequestEvidence[] requests,
        AcquisitionBatchCompletionState? state,
        DateTimeOffset? completedAt,
        AcquisitionBatchFailure? failure) =>
        new(
            batch.Identity,
            batch.Generation,
            batch.PolicyTick,
            batch.IsSelectionEvaluationComplete,
            batch.StartedAt,
            state,
            completedAt,
            failure,
            requests);
}
