using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】接続セッション、Raw ログ、取得証拠をメモリだけに保存する代用の保存先です。
/// </summary>
/// <remarks>
/// iOS 版の <c>GRDBConnectionSessionRepository</c> と同じ一式のインターフェースを 1 つのクラスで実装します。
/// Data 層（永続化担当）の SQLite 実装ができたら、<see cref="WindowsApplicationComposition"/> の生成箇所だけを差し替えます。
/// アプリを終了すると内容は消えます。終了時に未封止の batch を失敗で封止する処理は省いています。
/// </remarks>
public sealed class InterimConnectionSessionStore :
    IConnectionSessionRepository,
    IConnectionSessionRawLogRepository,
    IConnectionSessionErasureRepository,
    IAccountConnectionSessionErasureRepository,
    IConnectionSessionAcquisitionRepository,
    IConnectionSessionAcquisitionBatchRepository,
    IConnectionSessionAcquisitionTerminationRepository
{
    /// <summary>保存操作を直列化する lock です。</summary>
    private readonly Lock _gate = new();

    /// <summary>取得区間を通知する計測境界です。</summary>
    private readonly IAcquisitionPerformanceEventPort _performanceEvents;

    /// <summary>セッション ID ごとの概要です。</summary>
    private readonly Dictionary<ConnectionSessionID, ConnectionSession> _sessions = [];

    /// <summary>セッション ID ごとの Raw 観測（保存順）です。</summary>
    private readonly Dictionary<ConnectionSessionID, List<OBDRawResponseObservation>> _entries = [];

    /// <summary>セッション ID ごとの取得マニフェストです。</summary>
    private readonly Dictionary<ConnectionSessionID, ConnectionSessionAcquisitionManifest> _manifests = [];

    /// <summary>セッション ID ごとの取得境界です。</summary>
    private readonly Dictionary<ConnectionSessionID, List<AcquisitionRawBoundaryEvidence>> _boundaries = [];

    /// <summary>セッション ID ごとの batch 証拠です。</summary>
    private readonly Dictionary<ConnectionSessionID, List<AcquisitionBatchEvidence>> _batches = [];

    /// <summary>計測境界を指定して空の保存先を作ります。</summary>
    /// <param name="performanceEvents">取得区間を通知する計測境界。</param>
    public InterimConnectionSessionStore(IAcquisitionPerformanceEventPort performanceEvents)
    {
        _performanceEvents = performanceEvents;
    }

    /// <summary>セッションを保存します。既存の Raw 概要は保ちます。</summary>
    /// <remarks>責務: 1 件のセッションを ID 単位で新規保存または更新します。</remarks>
    /// <param name="session">保存するセッション。</param>
    public void Save(ConnectionSession session)
    {
        lock (_gate)
        {
            _sessions[session.Id] = _sessions.TryGetValue(session.Id, out var existing)
                ? session with { RawLogSummary = existing.RawLogSummary }
                : session;
        }
    }

    /// <summary>アカウントのセッションを開始日時の新しい順に返します。</summary>
    /// <remarks>責務: 1 件のアカウントのセッションへ、実際の Raw 件数とバイト数を反映して返します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>新しい順のセッション。</returns>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier)
    {
        lock (_gate)
        {
            return _sessions.Values
                .Where(session => string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
                .OrderByDescending(session => session.StartedAt)
                .Select(WithActualLocalRawSummary)
                .ToList();
        }
    }

    /// <summary>Raw 観測を 1 件追記します。</summary>
    /// <remarks>責務: セッションへ Raw 観測を保存順で追加します。</remarks>
    /// <param name="observation">Raw 観測。</param>
    /// <param name="sessionID">所有セッション。</param>
    public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            EntryList(sessionID).Add(observation);
        }
    }

    /// <summary>セッションの Raw ログを保存順に返します。</summary>
    /// <remarks>責務: 保存済み Raw 観測を連番付きの Raw ログ行へ変換します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <returns>Raw ログ行。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(sessionID, out var entries)
                ? entries.Select((observation, index) => new ConnectionSessionRawLogEntry(
                    sequence: index,
                    observedAt: observation.ObservedAt,
                    batchElapsedNanoseconds: observation.BatchElapsedNanoseconds,
                    service: observation.Request.Service,
                    pid: observation.Request.Pid,
                    payload: observation.Payload.ToArray())).ToList()
                : [];
        }
    }

    /// <summary>車両に関連付いたセッションの Raw ログを返します。</summary>
    /// <remarks>責務: アカウントと車両で絞ったセッションの Raw ログ行を、セッション情報付きで返します。</remarks>
    /// <param name="vehicleID">対象車両。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>車両の Raw ログ行。</returns>
    public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier)
    {
        List<ConnectionSession> sessions;
        lock (_gate)
        {
            sessions = _sessions.Values
                .Where(session => session.Vehicle?.Id == vehicleID
                    && string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
                .OrderBy(session => session.StartedAt)
                .ToList();
        }

        return sessions
            .SelectMany(session => Entries(session.Id).Select(entry => new VehicleConnectionSessionRawLogEntry(
                vehicleID: vehicleID,
                sessionID: session.Id,
                sessionStartedAt: session.StartedAt,
                entry: entry)))
            .ToList();
    }

    /// <summary>クラウドへ保存できたことを記録します。</summary>
    /// <remarks>責務: セッションの Raw 概要をアップロード済みへ更新します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <param name="manifestDigest">保存した Raw のダイジェスト。</param>
    public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest) =>
        UpdateSummary(sessionID, summary => summary with
        {
            CloudState = ConnectionSessionCloudSyncState.Uploaded,
            ManifestDigest = manifestDigest,
            MacImportReceipt = null,
        });

    /// <summary>クラウドへの保存に失敗したことを記録します。</summary>
    /// <remarks>責務: 端末に Raw があるセッションだけを失敗状態へ更新します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    public void MarkCloudUploadFailed(ConnectionSessionID sessionID) =>
        UpdateSummary(sessionID, summary => summary.LocalState == ConnectionSessionLocalRawState.Available
            ? summary with { CloudState = ConnectionSessionCloudSyncState.Failed }
            : summary);

    /// <summary>Mac 受領証を記録します。</summary>
    /// <remarks>責務: ダイジェストが一致するときだけ受領証を保存します。</remarks>
    /// <param name="receipt">受領証。</param>
    /// <param name="sessionID">対象セッション。</param>
    public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID) =>
        UpdateSummary(sessionID, summary => string.Equals(summary.ManifestDigest, receipt.ManifestDigest, StringComparison.Ordinal)
            ? summary with { MacImportReceipt = receipt }
            : throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict));

    /// <summary>検証済みの転送を取り込みます（暫定版では未対応）。</summary>
    /// <remarks>責務: 暫定の保存先ではクラウド転送を扱わないため、利用不能を返します。</remarks>
    /// <param name="transfer">検証済みの転送。</param>
    public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) =>
        throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.Unavailable);

    /// <summary>端末の Raw ログを消します。</summary>
    /// <remarks>責務: Raw がある セッションの Raw 行を消し、概要を「端末から削除済み」にします。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    public void RemoveLocalEntries(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionID, out var session)
                || WithActualLocalRawSummary(session).RawLogSummary.LocalState != ConnectionSessionLocalRawState.Available)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }

            _entries.Remove(sessionID);
            _sessions[sessionID] = session with
            {
                RawLogSummary = session.RawLogSummary with { LocalState = ConnectionSessionLocalRawState.Removed },
            };
        }
    }

    /// <summary>所有者を確かめてセッションと子データを消します。</summary>
    /// <remarks>責務: 1 件のセッションと Raw・取得証拠を物理削除します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <param name="accountIdentifier">所有アカウント。</param>
    public void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionID, out var session)
                || !string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }

            RemoveAll(sessionID);
        }
    }

    /// <summary>アカウントのセッションを全部消します。</summary>
    /// <remarks>責務: アカウント削除時に、そのアカウントの全セッションと子データを消します。</remarks>
    /// <param name="accountIdentifier">対象アカウント。</param>
    public void DeleteSessions(string accountIdentifier)
    {
        lock (_gate)
        {
            foreach (var id in _sessions.Values
                .Where(session => string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
                .Select(session => session.Id)
                .ToList())
            {
                RemoveAll(id);
            }
        }
    }

    /// <summary>マニフェストと開始境界を 1 回だけ保存します。</summary>
    /// <remarks>責務: 同じ組の重複と異なる組の競合を区別して保存します。</remarks>
    /// <param name="manifest">取得マニフェスト。</param>
    /// <param name="startedAt">開始日時。</param>
    /// <param name="sessionID">所有セッション。</param>
    public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.ManifestPersistence, () =>
        {
            if (_manifests.TryGetValue(sessionID, out var stored))
            {
                var storedStart = _boundaries[sessionID].OfType<AcquisitionRawBoundaryEvidence.Started>().First();
                throw new ConnectionSessionAcquisitionRepositoryException(
                    stored.Equals(manifest) && storedStart.At == startedAt
                        ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                        : ConnectionSessionAcquisitionRepositoryError.Conflict);
            }

            _manifests[sessionID] = manifest;
            _boundaries[sessionID] = [new AcquisitionRawBoundaryEvidence.Started(startedAt)];
            return 0;
        });

    /// <summary>終了境界を追記します。</summary>
    /// <remarks>責務: 開始済みのセッションへ終了境界を 1 件追記します。</remarks>
    /// <param name="endedAt">終了日時。</param>
    /// <param name="reason">終了理由。</param>
    /// <param name="sessionID">所有セッション。</param>
    public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.SessionTerminationPersistence, () =>
        {
            if (!_boundaries.TryGetValue(sessionID, out var boundaries))
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
            }

            boundaries.Add(new AcquisitionRawBoundaryEvidence.Ended(endedAt, reason));
            return 0;
        });

    /// <summary>取得マニフェストを返します。</summary>
    /// <remarks>責務: 保存済みのマニフェストを変更せずに返します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <returns>マニフェスト。</returns>
    public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            return _manifests.TryGetValue(sessionID, out var manifest)
                ? manifest
                : throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
        }
    }

    /// <summary>取得境界を追記順に返します。</summary>
    /// <remarks>責務: 開始と終了の境界を変更せずに返します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <returns>境界の一覧。</returns>
    public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            return _boundaries.TryGetValue(sessionID, out var boundaries) ? boundaries.ToArray() : [];
        }
    }

    /// <summary>未封止の batch を保存します。</summary>
    /// <remarks>責務: 同じ識別子の重複を拒んで batch を追加します。</remarks>
    /// <param name="evidence">batch 証拠。</param>
    /// <param name="sessionID">所有セッション。</param>
    public void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.BatchOpenPersistence, () =>
        {
            var batches = BatchList(sessionID);
            if (batches.Any(batch => batch.Identity == evidence.Identity))
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Duplicate);
            }

            batches.Add(evidence);
            return 0;
        });

    /// <summary>要求を送信開始へ進めます。</summary>
    /// <remarks>責務: 未封止 batch 内の要求 1 件を送信開始済みにします。</remarks>
    /// <param name="requestOrdinal">batch 内の要求順。</param>
    /// <param name="batchIdentity">親 batch。</param>
    /// <param name="sessionID">所有セッション。</param>
    public void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.RequestDispatchPersistence, () =>
            ReplaceRequest(sessionID, batchIdentity, requestOrdinal, stored => new PIDRequestEvidence(
                stored.RequestOrdinal,
                stored.ManifestPIDOrdinal,
                PIDRequestDispatchState.DispatchBegun,
                null,
                PIDRequestValueOutcome.NotEvaluated,
                null,
                null,
                null)));

    /// <summary>応答があった要求と Raw を一緒に保存します。</summary>
    /// <remarks>責務: Raw に連番を振り、終端状態の要求証拠を返します。</remarks>
    /// <returns>連番付きの要求証拠。</returns>
    public PIDRequestEvidence SaveRespondedRequest(
        OBDRawResponseObservation observation,
        PIDRequestValueOutcome valueOutcome,
        ulong elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.RespondedPersistence, () =>
        {
            var entries = EntryList(sessionID);
            var sequence = (long)entries.Count;
            var terminal = ReplaceRequest(sessionID, batchIdentity, requestOrdinal, stored => new PIDRequestEvidence(
                stored.RequestOrdinal,
                stored.ManifestPIDOrdinal,
                PIDRequestDispatchState.Terminal,
                PIDRequestTransportOutcome.Responded,
                valueOutcome,
                sequence,
                elapsedNanoseconds,
                reasonCode));
            entries.Add(observation);
            return terminal;
        });

    /// <summary>応答がなかった要求を保存します。</summary>
    /// <remarks>責務: Raw を作らずに終端状態の要求証拠を返します。</remarks>
    /// <returns>要求証拠。</returns>
    public PIDRequestEvidence SaveNonRespondedRequest(
        PIDRequestTransportOutcome outcome,
        ulong? elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.NonRespondedPersistence, () =>
            ReplaceRequest(sessionID, batchIdentity, requestOrdinal, stored => new PIDRequestEvidence(
                stored.RequestOrdinal,
                stored.ManifestPIDOrdinal,
                PIDRequestDispatchState.Terminal,
                outcome,
                PIDRequestValueOutcome.NotEvaluated,
                null,
                elapsedNanoseconds,
                reasonCode)));

    /// <summary>batch を封止します。</summary>
    /// <remarks>責務: 同じ識別子の未封止 batch を封止済みの証拠へ置き換えます。</remarks>
    /// <param name="evidence">封止済みの batch 証拠。</param>
    /// <param name="sessionID">所有セッション。</param>
    public void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) =>
        Write(AcquisitionPerformanceOperation.BatchSealPersistence, () =>
        {
            var batches = BatchList(sessionID);
            var index = batches.FindIndex(batch => batch.Identity == evidence.Identity);
            if (index < 0)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
            }

            batches[index] = evidence;
            return 0;
        });

    /// <summary>batch 証拠を保存順に返します。</summary>
    /// <remarks>責務: セッションの batch 証拠を変更せずに返します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <returns>batch の一覧。</returns>
    public IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID)
    {
        lock (_gate)
        {
            return _batches.TryGetValue(sessionID, out var batches) ? batches.ToArray() : [];
        }
    }

    /// <summary>セッションと取得終了境界をまとめて確定します。</summary>
    /// <remarks>責務: 未終了のセッションを終了済みへ更新し、開始済みなら終了境界を追記します。</remarks>
    /// <param name="session">終了直前のセッション。</param>
    /// <param name="endedAt">終了日時。</param>
    /// <param name="reason">終了理由。</param>
    /// <returns>終了済みのセッション。</returns>
    public ConnectionSession FinishSessionAcquisition(ConnectionSession session, DateTimeOffset endedAt, ConnectionSessionEndReason reason) =>
        Write(AcquisitionPerformanceOperation.SessionTerminationPersistence, () => FinishLocked(session, endedAt, reason));

    /// <summary>未終了のまま残ったセッションを異常終了として確定します。</summary>
    /// <remarks>責務: アカウントの未終了セッションを開始順にすべて「予期しない終了」で閉じます。</remarks>
    /// <param name="accountIdentifier">対象アカウント。</param>
    /// <param name="recoveredAt">回復日時。</param>
    /// <returns>今回閉じたセッション。</returns>
    public IReadOnlyList<ConnectionSession> RecoverInterruptedSessionAcquisitions(string accountIdentifier, DateTimeOffset recoveredAt) =>
        Write(AcquisitionPerformanceOperation.TerminationRecoveryPersistence, () => _sessions.Values
            .Where(session => session.EndedAt is null
                && string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
            .OrderBy(session => session.StartedAt)
            .ToList()
            .Select(session => FinishLocked(session, recoveredAt, ConnectionSessionEndReason.UnexpectedTermination))
            .ToList());

    /// <summary>lock の中でセッションを終了させます。</summary>
    /// <remarks>責務: 保存済みの未終了セッションへ終了日時・理由を書き、取得境界を閉じます。</remarks>
    /// <param name="session">終了させるセッション。</param>
    /// <param name="endedAt">終了日時。</param>
    /// <param name="reason">終了理由。</param>
    /// <returns>終了済みのセッション。</returns>
    private ConnectionSession FinishLocked(ConnectionSession session, DateTimeOffset endedAt, ConnectionSessionEndReason reason)
    {
        if (!_sessions.TryGetValue(session.Id, out var stored) || stored.EndedAt is not null)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }

        if (_boundaries.TryGetValue(session.Id, out var boundaries))
        {
            boundaries.Add(new AcquisitionRawBoundaryEvidence.Ended(endedAt, reason));
        }

        var ended = session with { RawLogSummary = stored.RawLogSummary, EndedAt = endedAt, EndReason = reason };
        _sessions[session.Id] = ended;
        return ended;
    }

    /// <summary>計測区間付きで 1 件の書き込みを実行します。</summary>
    /// <remarks>責務: 書き込みを lock の中で行い、成功か失敗で区間を閉じます。</remarks>
    /// <typeparam name="T">戻り値の型。</typeparam>
    /// <param name="operation">計測する操作。</param>
    /// <param name="body">書き込み処理。</param>
    /// <returns>書き込み処理の戻り値。</returns>
    private T Write<T>(AcquisitionPerformanceOperation operation, Func<T> body)
    {
        var interval = _performanceEvents.Begin(operation, new AcquisitionPerformanceContext());
        try
        {
            T result;
            lock (_gate)
            {
                _performanceEvents.QueueDidEnter(interval);
                result = body();
            }

            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Succeeded);
            return result;
        }
        catch (Exception)
        {
            _performanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
            throw;
        }
    }

    /// <summary>Raw の実件数で概要を補正したセッションを返します。</summary>
    /// <remarks>責務: 端末に Raw があるセッションの件数・バイト数・状態を実データに合わせます。</remarks>
    /// <param name="session">補正するセッション。</param>
    /// <returns>補正したセッション。</returns>
    private ConnectionSession WithActualLocalRawSummary(ConnectionSession session)
    {
        if (session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
        {
            return session;
        }

        var entries = _entries.TryGetValue(session.Id, out var list) ? list : [];
        var summary = session.RawLogSummary with
        {
            RecordCount = entries.Count,
            ByteCount = entries.Sum(entry => (long)entry.Payload.Count),
        };
        if (summary.RecordCount > 0)
        {
            summary = summary with { LocalState = ConnectionSessionLocalRawState.Available };
        }

        return session with { RawLogSummary = summary };
    }

    /// <summary>Raw 概要を書き換えます。</summary>
    /// <remarks>責務: 1 件のセッションの Raw 概要へ変換を適用します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <param name="transform">概要の変換。</param>
    private void UpdateSummary(ConnectionSessionID sessionID, Func<ConnectionSessionRawLogSummary, ConnectionSessionRawLogSummary> transform)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionID, out var session))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }

            _sessions[sessionID] = session with { RawLogSummary = transform(WithActualLocalRawSummary(session).RawLogSummary) };
        }
    }

    /// <summary>セッションの子データを全部消します（lock の中で呼ぶ）。</summary>
    /// <remarks>責務: セッション・Raw・マニフェスト・境界・batch を 1 件分消します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    private void RemoveAll(ConnectionSessionID sessionID)
    {
        _sessions.Remove(sessionID);
        _entries.Remove(sessionID);
        _manifests.Remove(sessionID);
        _boundaries.Remove(sessionID);
        _batches.Remove(sessionID);
    }

    /// <summary>Raw 観測の一覧を返し、なければ作ります（lock の中で呼ぶ）。</summary>
    /// <remarks>責務: セッションごとの可変 Raw 一覧を取得します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <returns>可変の一覧。</returns>
    private List<OBDRawResponseObservation> EntryList(ConnectionSessionID sessionID)
    {
        if (!_entries.TryGetValue(sessionID, out var entries))
        {
            entries = [];
            _entries[sessionID] = entries;
        }

        return entries;
    }

    /// <summary>batch の一覧を返し、なければ作ります（lock の中で呼ぶ）。</summary>
    /// <remarks>責務: セッションごとの可変 batch 一覧を取得します。</remarks>
    /// <param name="sessionID">対象セッション。</param>
    /// <returns>可変の一覧。</returns>
    private List<AcquisitionBatchEvidence> BatchList(ConnectionSessionID sessionID)
    {
        if (!_batches.TryGetValue(sessionID, out var batches))
        {
            batches = [];
            _batches[sessionID] = batches;
        }

        return batches;
    }

    /// <summary>未封止 batch 内の要求 1 件を置き換えます（lock の中で呼ぶ）。</summary>
    /// <remarks>責務: 要求の状態遷移を batch 証拠へ反映します。</remarks>
    /// <param name="sessionID">所有セッション。</param>
    /// <param name="batchIdentity">親 batch。</param>
    /// <param name="ordinal">要求順。</param>
    /// <param name="transform">要求の変換。</param>
    /// <returns>置き換えた要求。</returns>
    private PIDRequestEvidence ReplaceRequest(
        ConnectionSessionID sessionID,
        AcquisitionBatchIdentity batchIdentity,
        int ordinal,
        Func<PIDRequestEvidence, PIDRequestEvidence> transform)
    {
        var batches = BatchList(sessionID);
        var index = batches.FindIndex(batch => batch.Identity == batchIdentity && batch.CompletionState is null);
        if (index < 0)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
        }

        var batch = batches[index];
        var requests = batch.Requests.ToArray();
        requests[ordinal] = transform(requests[ordinal]);
        batches[index] = new AcquisitionBatchEvidence(
            batch.Identity,
            batch.Generation,
            batch.PolicyTick,
            batch.IsSelectionEvaluationComplete,
            batch.StartedAt,
            null,
            null,
            null,
            requests);
        return requests[ordinal];
    }
}
