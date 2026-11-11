using Microsoft.Data.Sqlite;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Data.Persistence.LogStore;
using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Records;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>取得manifest・Raw境界（SQLite）と、取得batch・PID要求証拠（LogStore）を保存します。</summary>
/// <remarks>
/// iOS版 <c>GRDBConnectionSessionAcquisitionRepository</c> の移植です。状態遷移・エラー・canonical readbackの規則は同じです。
/// iOS版でSQLiteのtriggerと外部キーが担っていたbatch・requestの不変条件（append-only、Raw参照の存在と一意性、
/// manifest PID位置の存在）は、LogStoreへ追記する前にこのクラスがメモリ上の状態で検証します。
/// iOS版と違い、batch・requestの日時はmicrosecondへ丸めずUTCのTicksのまま保存します（C#の日時を損失なく往復させるため）。
/// </remarks>
internal sealed class SqliteConnectionSessionAcquisitionRepository :
    IConnectionSessionAcquisitionRepository,
    IConnectionSessionAcquisitionBatchRepository,
    IConnectionSessionAcquisitionTerminationRepository
{
    /// <summary>プロセス終了後にdispatch済みrequestへ付ける理由codeです。</summary>
    private const string ProcessTerminatedAfterDispatch = "process_terminated_after_dispatch";

    /// <summary>保存一式の共有状態です。</summary>
    private readonly ConnectionSessionStorageContext _context;

    /// <summary>共有状態を指定して生成します。</summary>
    /// <remarks>責務: 同じSQLiteとLogStoreを使う取得証拠repositoryを作ります。</remarks>
    /// <param name="context">保存一式の共有状態。</param>
    internal SqliteConnectionSessionAcquisitionRepository(ConnectionSessionStorageContext context) => _context = context;

    /// <summary>未登録sessionへmanifestと開始境界を1回だけ保存します。</summary>
    /// <remarks>責務: canonical manifest aggregateと開始境界を1回のSQLiteトランザクションで保存し、完全readback時だけ確定します。</remarks>
    /// <param name="manifest">全semantic fieldを持つ新規取得manifest。</param>
    /// <param name="startedAt">最初のRaw要求直前の開始境界時刻。</param>
    /// <param name="sessionID">親接続セッションID。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">重複、競合、保存先失敗の場合。</exception>
    public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID)
    {
        CanonicalAcquisitionStart expected;
        try
        {
            expected = ConnectionSessionAcquisitionManifestRecord.Canonicalize(manifest, startedAt);
        }
        catch (ConnectionSessionAcquisitionRepositoryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SqliteStoreLog.OperationFailed(SqliteStoreLog.Logger, exception, "ConnectionSessionAcquisition", "SaveStartOnce.Canonicalize");
            throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
        _context.Measured(AcquisitionPerformanceOperation.ManifestPersistence, new AcquisitionPerformanceContext(), () =>
            _context.Database.Write<object?>((connection, transaction) =>
            {
                var key = ConnectionSessionStorageContext.Key(sessionID);
                if (ConnectionSessionAcquisitionManifestRecord.ReadCanonicalStart(connection, transaction, key) is { } stored)
                {
                    throw Error(stored == expected
                        ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                        : ConnectionSessionAcquisitionRepositoryError.Conflict);
                }
                if (!ConnectionSessionAcquisitionManifestRecord.SessionExists(connection, transaction, key))
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                ConnectionSessionAcquisitionManifestRecord.InsertStart(connection, transaction, expected, key);
                if (ConnectionSessionAcquisitionManifestRecord.ReadCanonicalStart(connection, transaction, key) != expected)
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                return null;
            }));
    }

    /// <summary>開始済みsessionへ終了境界を1回だけ追記します。</summary>
    /// <remarks>責務: canonical終了境界を開始境界との時系列検証後に1回のSQLiteトランザクションで追記します。</remarks>
    /// <param name="endedAt">Raw取得終了境界時刻。</param>
    /// <param name="reason">Raw取得停止の直接原因。</param>
    /// <param name="sessionID">親接続セッションID。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">重複、競合、開始欠落、時系列違反、保存先失敗の場合。</exception>
    public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID)
    {
        var endedMicroseconds = AcquisitionMicroseconds.From(endedAt);
        _context.Measured(AcquisitionPerformanceOperation.SessionTerminationPersistence, new AcquisitionPerformanceContext(), () =>
            _context.Database.Write<object?>((connection, transaction) =>
            {
                var key = ConnectionSessionStorageContext.Key(sessionID);
                var boundaries = ConnectionSessionAcquisitionManifestRecord.BoundaryRows(connection, transaction, key);
                var started = boundaries.FirstOrDefault(boundary => boundary.EventKind == "started");
                if (started is null)
                {
                    throw Error(boundaries.Count == 0
                        ? ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing
                        : ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                if (boundaries.Count(boundary => boundary.EventKind == "started") != 1 || started.EndReason is not null)
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                if (boundaries.FirstOrDefault(boundary => boundary.EventKind == "ended") is { } existingEnd)
                {
                    if (boundaries.Count(boundary => boundary.EventKind == "ended") != 1
                        || existingEnd.EndReason is not { } storedReason
                        || !ConnectionSessionEndReasonRawValue.TryParse(storedReason, out _))
                    {
                        throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                    }
                    throw Error(existingEnd.OccurredAtMicroseconds == endedMicroseconds && storedReason == reason.ToRawValue()
                        ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                        : ConnectionSessionAcquisitionRepositoryError.Conflict);
                }
                if (endedMicroseconds < started.OccurredAtMicroseconds)
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.EndBeforeStart);
                }
                ConnectionSessionAcquisitionManifestRecord.InsertEnd(connection, transaction, key, endedMicroseconds, reason);
                var readback = ConnectionSessionAcquisitionManifestRecord.BoundaryRows(connection, transaction, key)
                    .FirstOrDefault(boundary => boundary.EventKind == "ended");
                if (readback is null || readback.OccurredAtMicroseconds != endedMicroseconds || readback.EndReason != reason.ToRawValue())
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                return null;
            }));
    }

    /// <summary>sessionのimmutable取得manifestを読み取ります。</summary>
    /// <remarks>責務: 正規化4表の完全なsealed aggregateをDomain manifestへ復元します。</remarks>
    /// <param name="sessionID">読取対象の接続セッションID。</param>
    /// <returns>保存済み取得manifest。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">未登録は <c>NotFound</c>、不完全または読取失敗は <c>Unavailable</c>。</exception>
    public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID) =>
        Read(() => _context.Database.Read(connection =>
            ConnectionSessionAcquisitionManifestRecord.ReadCanonicalStart(connection, null, ConnectionSessionStorageContext.Key(sessionID))?.Manifest
                ?? throw Error(ConnectionSessionAcquisitionRepositoryError.NotFound)));

    /// <summary>sessionのRaw取得境界を開始、終了順で読み取ります。</summary>
    /// <remarks>責務: 保存済みmicrosecond境界行を変更せずDomain event列へ復元します。</remarks>
    /// <param name="sessionID">読取対象の接続セッションID。</param>
    /// <returns>開始から終了の順に並ぶ境界event。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">保存値破損または読取失敗は <c>Unavailable</c>。</exception>
    public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID) =>
        Read(() => _context.Database.Read(connection =>
        {
            var rows = ConnectionSessionAcquisitionManifestRecord.BoundaryRows(connection, null, ConnectionSessionStorageContext.Key(sessionID));
            ConnectionSessionAcquisitionManifestRecord.ValidateBoundarySequence(rows);
            return ConnectionSessionAcquisitionManifestRecord.MakeBoundaryEvidence(rows);
        }));

    /// <summary>未確定batchとpolicy選択済みrequest列を原子的に保存します。</summary>
    /// <remarks>責務: 物理要求前のopen batch aggregateをcanonical readback付きで1回だけ作成します。</remarks>
    /// <param name="evidence">terminal結果を持たないbatch開始証拠。</param>
    /// <param name="sessionID">batchを所有するsession。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">重複、競合、親manifest欠落、保存失敗の場合。</exception>
    public void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID)
    {
        if (evidence.CompletionState is not null
            || !evidence.IsSelectionEvaluationComplete
            || evidence.Requests.Any(request => request.DispatchState != PIDRequestDispatchState.SelectedOnly))
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        var context = new AcquisitionPerformanceContext(
            generation: evidence.Generation,
            batchOrdinal: evidence.Identity.Ordinal,
            policyTick: evidence.PolicyTick);
        _context.Measured<object?>(AcquisitionPerformanceOperation.BatchOpenPersistence, context, () =>
        {
            if (_context.LogStore.Find(sessionID)?.Evidence.Batch(evidence.Identity.Ordinal) is { } stored)
            {
                throw Error(stored == evidence
                    ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                    : ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            var ordered = SealedOrderedPIDs(sessionID) ?? throw Error(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
            if (evidence.Generation > long.MaxValue
                || evidence.PolicyTick > long.MaxValue
                || evidence.Requests.Any(request => request.ManifestPIDOrdinal >= ordered.Length))
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            var log = _context.LogStore.Get(sessionID);
            var record = new SessionLogRecord.BatchBegun(
                evidence.Identity.Ordinal,
                evidence.Generation,
                evidence.PolicyTick,
                evidence.IsSelectionEvaluationComplete,
                evidence.StartedAt.UtcTicks,
                evidence.Requests.Select(request => request.ManifestPIDOrdinal).ToArray());
            if (log.Evidence.Preview(record) != evidence)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            PinIfOpen(sessionID, log);
            _context.Append(log, record);
            return null;
        });
    }

    /// <summary>selected-only requestをdispatch開始へ単調遷移させます。</summary>
    /// <remarks>責務: 1件の要求が物理送信処理へ進む直前のdurable境界を冪等に保存します。</remarks>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">親batch identity。</param>
    /// <param name="sessionID">親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">request欠落、terminal済み競合、保存失敗の場合。</exception>
    public void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID)
    {
        var context = new AcquisitionPerformanceContext(batchOrdinal: batchIdentity.Ordinal, requestOrdinal: requestOrdinal);
        _context.Measured<object?>(AcquisitionPerformanceOperation.RequestDispatchPersistence, context, () =>
        {
            var log = _context.LogStore.Find(sessionID) ?? throw Error(ConnectionSessionAcquisitionRepositoryError.NotFound);
            var batch = log.Evidence.Batch(batchIdentity.Ordinal);
            if (batch is null || requestOrdinal < 0 || requestOrdinal >= batch.Requests.Count)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.NotFound);
            }
            switch (batch.Requests[requestOrdinal].DispatchState)
            {
                case PIDRequestDispatchState.DispatchBegun:
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Duplicate);
                case PIDRequestDispatchState.SelectedOnly:
                    _context.Append(log, new SessionLogRecord.DispatchBegun(batchIdentity.Ordinal, requestOrdinal));
                    return null;
                default:
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
        });
    }

    /// <summary>正応答Rawと対応するrequest terminal証拠を1レコードで保存します。</summary>
    /// <remarks>責務: dispatch開始済み要求へRawを採番追記し、responded terminalを原子的に確定します。</remarks>
    /// <param name="observation">追加する未デコード正応答。</param>
    /// <param name="valueOutcome">取得時定義による値評価結果。</param>
    /// <param name="elapsedNanoseconds">要求開始から正応答観測までの単調経過時間。</param>
    /// <param name="reasonCode">transcript原文を含まない承認済み分類理由code。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">親batch identity。</param>
    /// <param name="sessionID">batchとRawを所有するsession。</param>
    /// <returns>採番済みRaw sequenceを参照するcanonical request evidence。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">dispatch前、sealed、重複、競合、所有関係不一致、保存失敗の場合。</exception>
    public PIDRequestEvidence SaveRespondedRequest(
        OBDRawResponseObservation observation,
        PIDRequestValueOutcome valueOutcome,
        ulong elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID)
    {
        if (!Enum.IsDefined(valueOutcome) || reasonCode is { Length: 0 })
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        var context = new AcquisitionPerformanceContext(batchOrdinal: batchIdentity.Ordinal, requestOrdinal: requestOrdinal);
        return _context.Measured(AcquisitionPerformanceOperation.RespondedPersistence, context, () =>
        {
            var (log, stored, ordered) = RequestForTerminalTransition(requestOrdinal, batchIdentity, sessionID);
            if (stored.ManifestPIDOrdinal >= ordered.Length || ordered[stored.ManifestPIDOrdinal] != observation.Request)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            if (stored.DispatchState == PIDRequestDispatchState.Terminal)
            {
                ClassifyRespondedRetry(log, stored, observation, valueOutcome, elapsedNanoseconds, reasonCode);
            }
            if (stored.DispatchState != PIDRequestDispatchState.DispatchBegun)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            var runtime = _context.Runtime(sessionID);
            if (!runtime.IsOpen || observation.BatchElapsedNanoseconds > long.MaxValue || elapsedNanoseconds > long.MaxValue)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            _context.EnsureAppendSummary(sessionID, runtime);
            var entry = log.PeekNextEntry(observation);
            var expected = new PIDRequestEvidence(
                requestOrdinal,
                stored.ManifestPIDOrdinal,
                PIDRequestDispatchState.Terminal,
                PIDRequestTransportOutcome.Responded,
                valueOutcome,
                entry.Sequence,
                elapsedNanoseconds,
                reasonCode);
            var record = new SessionLogRecord.Responded(entry, batchIdentity.Ordinal, expected);
            if (log.Evidence.Preview(record).Requests[requestOrdinal] != expected)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            log.IsPinned = true;
            _context.Append(log, record);
            return expected;
        });
    }

    /// <summary>Rawを持たないrequest terminal証拠を保存します。</summary>
    /// <remarks>責務: dispatch開始済み要求を確認済み非responded結果へcanonical readback付きで確定します。</remarks>
    /// <param name="outcome">Rawを生成しない確認済みtransport結果。</param>
    /// <param name="elapsedNanoseconds">要求開始からterminal観測までの単調経過時間。回復時に不明なら <see langword="null"/>。</param>
    /// <param name="reasonCode">transcript原文を含まない承認済み分類理由code。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">親batch identity。</param>
    /// <param name="sessionID">batchを所有するsession。</param>
    /// <returns>Raw参照を持たないcanonical request evidence。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">responded指定、dispatch前、sealed、重複、競合、保存失敗の場合。</exception>
    public PIDRequestEvidence SaveNonRespondedRequest(
        PIDRequestTransportOutcome outcome,
        ulong? elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID)
    {
        if (outcome == PIDRequestTransportOutcome.Responded || reasonCode is { Length: 0 })
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        var context = new AcquisitionPerformanceContext(batchOrdinal: batchIdentity.Ordinal, requestOrdinal: requestOrdinal);
        return _context.Measured(AcquisitionPerformanceOperation.NonRespondedPersistence, context, () =>
        {
            var (log, stored, _) = RequestForTerminalTransition(requestOrdinal, batchIdentity, sessionID);
            var expected = new PIDRequestEvidence(
                requestOrdinal,
                stored.ManifestPIDOrdinal,
                PIDRequestDispatchState.Terminal,
                outcome,
                PIDRequestValueOutcome.NotEvaluated,
                rawSequence: null,
                elapsedNanoseconds,
                reasonCode);
            if (stored.DispatchState == PIDRequestDispatchState.Terminal)
            {
                throw Error(stored == expected
                    ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                    : ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            if (stored.DispatchState != PIDRequestDispatchState.DispatchBegun)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            if (elapsedNanoseconds > long.MaxValue)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            var record = new SessionLogRecord.RequestTerminal(batchIdentity.Ordinal, expected);
            if (log.Evidence.Preview(record).Requests[requestOrdinal] != expected)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            _context.Append(log, record);
            return expected;
        });
    }

    /// <summary>terminal request列とbatch終端を1レコードで確定します。</summary>
    /// <remarks>責務: 既存Raw参照を検証しながら、partialを含むrequest結果とbatch sealを原子的に追記します。</remarks>
    /// <param name="evidence">terminal状態を持つbatch証拠。</param>
    /// <param name="sessionID">親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">開始欠落、Raw参照欠落、重複、競合、保存失敗の場合。</exception>
    public void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID)
    {
        if (evidence.CompletionState is not { } completionState || evidence.CompletedAt is not { } completedAt)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        var context = new AcquisitionPerformanceContext(
            generation: evidence.Generation,
            batchOrdinal: evidence.Identity.Ordinal,
            policyTick: evidence.PolicyTick);
        _context.Measured<object?>(AcquisitionPerformanceOperation.BatchSealPersistence, context, () =>
        {
            var log = _context.LogStore.Find(sessionID) ?? throw Error(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
            var state = log.Evidence;
            var stored = state.Batch(evidence.Identity.Ordinal) ?? throw Error(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
            if (stored.CompletionState is not null)
            {
                throw Error(stored == evidence
                    ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                    : ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            if (stored.Identity != evidence.Identity
                || stored.Generation != evidence.Generation
                || stored.PolicyTick != evidence.PolicyTick
                || stored.IsSelectionEvaluationComplete != evidence.IsSelectionEvaluationComplete
                || stored.StartedAt != evidence.StartedAt
                || stored.Requests.Count != evidence.Requests.Count)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            var updates = new List<PIDRequestEvidence>();
            var newlyReferenced = new HashSet<long>();
            foreach (var request in evidence.Requests.Where(request => request.DispatchState == PIDRequestDispatchState.Terminal))
            {
                var current = stored.Requests[request.RequestOrdinal];
                if (current.DispatchState == PIDRequestDispatchState.Terminal)
                {
                    if (current != request)
                    {
                        throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
                    }
                    continue;
                }
                if (current.DispatchState != PIDRequestDispatchState.DispatchBegun)
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
                }
                if (request.RawSequence is { } sequence
                    && (sequence < 0 || sequence >= log.NextSequence || log.Count == 0
                        || state.IsRawReferenced(sequence) || !newlyReferenced.Add(sequence)))
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                if (request.ElapsedNanoseconds > long.MaxValue)
                {
                    throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                }
                updates.Add(request);
            }
            var record = new SessionLogRecord.BatchSealed(
                evidence.Identity.Ordinal,
                completionState,
                completedAt.UtcTicks,
                evidence.Failure,
                updates.ToArray());
            if (state.Preview(record) != evidence)
            {
                throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
            }
            _context.Append(log, record);
            return null;
        });
    }

    /// <summary>sessionの全batch evidenceをordinal順で復元します。</summary>
    /// <remarks>責務: LogStoreの証拠状態をpartial状態を保持したDomain証拠列へ変換します。</remarks>
    /// <param name="sessionID">読取対象session。</param>
    /// <returns>batch ordinal昇順の証拠列。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">保存値破損または読取失敗は <c>Unavailable</c>。</exception>
    public IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID) =>
        Read(() =>
        {
            lock (_context.Gate)
            {
                try
                {
                    return _context.LogStore.Find(sessionID)?.Evidence.Batches.ToList() ?? [];
                }
                finally
                {
                    _context.LogStore.TrimIdle();
                }
            }
        });

    /// <summary>現在session、open batch、取得終了境界を1回で確定します。</summary>
    /// <remarks>
    /// 責務: 1件の接続sessionを、未確定batchの失敗sealを含む原子的な終了状態へ変換します。
    /// SQLiteのトランザクション内で境界とsessionを書き、LogStoreのseal追記をディスクへ確定してからSQLiteを確定します。
    /// </remarks>
    /// <param name="session">終了直前の現在session。</param>
    /// <param name="endedAt">sessionと取得境界へ共通で使用する終了日時。</param>
    /// <param name="reason">接続と取得が停止した直接原因。</param>
    /// <returns>トランザクション内でcanonical readbackした終了済みsession。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">session不在、競合、取得境界不正、保存失敗の場合。</exception>
    public ConnectionSession FinishSessionAcquisition(ConnectionSession session, DateTimeOffset endedAt, ConnectionSessionEndReason reason) =>
        _context.Measured(AcquisitionPerformanceOperation.SessionTerminationPersistence, new AcquisitionPerformanceContext(), () =>
        {
            var ended = _context.Database.Write((connection, transaction) =>
            {
                ConnectionSessionAcquisitionManifestRecord.AppendEndIfAcquisitionStarted(
                    connection,
                    transaction,
                    ConnectionSessionStorageContext.Key(session.Id),
                    endedAt,
                    reason);
                var finished = FinishSessionRecord(connection, transaction, session, endedAt, reason);
                if (_context.LogStore.Find(session.Id) is { } log)
                {
                    SealOpenBatchesAfterAcquisitionFailure(log, endedAt);
                    log.WaitDurable();
                    log.IsPinned = false;
                }
                return finished;
            });
            _context.Invalidate(session.Id);
            return ended;
        });

    /// <summary>アカウントに残る未終了sessionとopen取得証拠を1回で回復します。</summary>
    /// <remarks>責務: process終了後に残った全sessionを決定的な異常終了状態へ変換します。</remarks>
    /// <param name="accountIdentifier">回復対象sessionの所有アカウント。</param>
    /// <param name="recoveredAt">回復結果へ固定する終了日時。</param>
    /// <returns>今回回復した終了済みsession。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">保存値不正または回復失敗の場合。</exception>
    public IReadOnlyList<ConnectionSession> RecoverInterruptedSessionAcquisitions(string accountIdentifier, DateTimeOffset recoveredAt) =>
        _context.Measured(AcquisitionPerformanceOperation.TerminationRecoveryPersistence, new AcquisitionPerformanceContext(), () =>
        {
            var recovered = _context.Database.Write((connection, transaction) =>
            {
                var sessions = SqliteDatabase.Query(
                    connection,
                    transaction,
                    $"SELECT {ConnectionSessionRecord.Columns} FROM connection_sessions WHERE accountIdentifier = $1 AND endedAt IS NULL ORDER BY startedAt",
                    ConnectionSessionRecord.MakeDomainSession,
                    accountIdentifier);
                var results = new List<ConnectionSession>();
                var touched = new List<SessionLog>();
                foreach (var candidate in sessions)
                {
                    var session = candidate ?? throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
                    ConnectionSessionAcquisitionManifestRecord.AppendEndIfAcquisitionStarted(
                        connection,
                        transaction,
                        ConnectionSessionStorageContext.Key(session.Id),
                        recoveredAt,
                        ConnectionSessionEndReason.UnexpectedTermination);
                    results.Add(FinishSessionRecord(connection, transaction, session, recoveredAt, ConnectionSessionEndReason.UnexpectedTermination));
                    if (_context.LogStore.Find(session.Id) is { } log)
                    {
                        RecoverOpenBatchesAfterProcessTermination(log, recoveredAt);
                        touched.Add(log);
                    }
                }
                foreach (var log in touched)
                {
                    log.WaitDurable();
                    log.IsPinned = false;
                }
                return results;
            });
            foreach (var session in recovered)
            {
                _context.Invalidate(session.Id);
            }
            return (IReadOnlyList<ConnectionSession>)recovered;
        });

    /// <summary>graceful終了時に残ったopen batchをpersistence failureとしてsealします。</summary>
    /// <remarks>責務: session終了直前の未確定batchを、新しい物理結果を推測せず失敗状態へ閉じます。</remarks>
    /// <param name="log">batchを所有するセッションのログ。</param>
    /// <param name="completedAt">batch失敗を確定する日時。</param>
    private void SealOpenBatchesAfterAcquisitionFailure(SessionLog log, DateTimeOffset completedAt)
    {
        foreach (var open in log.Evidence.OpenBatches())
        {
            var terminal = new AcquisitionBatchEvidence(
                open.Identity,
                open.Generation,
                open.PolicyTick,
                open.IsSelectionEvaluationComplete,
                open.StartedAt,
                AcquisitionBatchCompletionState.Failed,
                completedAt > open.StartedAt ? completedAt : open.StartedAt,
                AcquisitionBatchFailure.PersistenceFailure,
                open.Requests);
            SealBatch(log, terminal, []);
        }
    }

    /// <summary>process終了後のdispatch済み要求をunknownへ変換してopen batchをsealします。</summary>
    /// <remarks>責務: 1件のsessionに残る全open batchを再送なしのtermination証拠へ変換します。</remarks>
    /// <param name="log">batchを所有するセッションのログ。</param>
    /// <param name="completedAt">回復を確定する日時。</param>
    private void RecoverOpenBatchesAfterProcessTermination(SessionLog log, DateTimeOffset completedAt)
    {
        foreach (var open in log.Evidence.OpenBatches())
        {
            var requests = open.Requests.ToArray();
            var updates = new List<PIDRequestEvidence>();
            foreach (var request in open.Requests.Where(request => request.DispatchState == PIDRequestDispatchState.DispatchBegun))
            {
                var terminal = new PIDRequestEvidence(
                    request.RequestOrdinal,
                    request.ManifestPIDOrdinal,
                    PIDRequestDispatchState.Terminal,
                    PIDRequestTransportOutcome.UnknownAfterTermination,
                    PIDRequestValueOutcome.NotEvaluated,
                    rawSequence: null,
                    elapsedNanoseconds: null,
                    reasonCode: ProcessTerminatedAfterDispatch);
                requests[request.RequestOrdinal] = terminal;
                updates.Add(terminal);
            }
            var sealedBatch = new AcquisitionBatchEvidence(
                open.Identity,
                open.Generation,
                open.PolicyTick,
                open.IsSelectionEvaluationComplete,
                open.StartedAt,
                AcquisitionBatchCompletionState.TerminatedUnknown,
                completedAt > open.StartedAt ? completedAt : open.StartedAt,
                AcquisitionBatchFailure.ProcessTerminated,
                requests);
            SealBatch(log, sealedBatch, updates.ToArray());
        }
    }

    /// <summary>1件のterminal batchをcanonical readback付きでsealします。</summary>
    /// <remarks>責務: 回復済みbatch終端を既存request列との整合確認後に追記します。</remarks>
    /// <param name="log">batchを所有するセッションのログ。</param>
    /// <param name="evidence">保存するterminal batch証拠。</param>
    /// <param name="updates">同時に終端へ更新するrequest証拠。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">readback不一致の場合は <c>Unavailable</c>。</exception>
    private void SealBatch(SessionLog log, AcquisitionBatchEvidence evidence, PIDRequestEvidence[] updates)
    {
        var record = new SessionLogRecord.BatchSealed(
            evidence.Identity.Ordinal,
            evidence.CompletionState!.Value,
            evidence.CompletedAt!.Value.UtcTicks,
            evidence.Failure,
            updates);
        if (log.Evidence.Preview(record) != evidence)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
        _context.Append(log, record);
    }

    /// <summary>現在sessionを親トランザクション内で終了しcanonical値を返します。</summary>
    /// <remarks>責務: 1件の未終了sessionへ終了日時と理由を保存して同じ行を復元します。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="session">終了直前のApplication保持値。</param>
    /// <param name="endedAt">保存する終了日時。</param>
    /// <param name="reason">保存する終了理由。</param>
    /// <returns>保存済み終了session。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">session不在、既終了は <c>Conflict</c>、readback不正は <c>Unavailable</c>。</exception>
    private ConnectionSession FinishSessionRecord(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ConnectionSession session,
        DateTimeOffset endedAt,
        ConnectionSessionEndReason reason)
    {
        ConnectionSession? stored;
        try
        {
            stored = _context.FetchSession(connection, transaction, session.Id);
        }
        catch (ConnectionSessionRepositoryException)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        if (stored is not { EndedAt: null })
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        var ended = session with { RawLogSummary = stored.RawLogSummary, EndedAt = endedAt, EndReason = reason };
        ConnectionSessionRecord.Save(connection, transaction, ended);
        if (_context.FetchSession(connection, transaction, session.Id) is not { } readback || readback != ended)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
        return readback;
    }

    /// <summary>terminal遷移対象の親manifest、open batch、requestを読み取ります。</summary>
    /// <remarks>責務: 1件のrequest terminal書込前提を保存済み所有関係とseal状態から検証します。</remarks>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">親batch identity。</param>
    /// <param name="sessionID">親session。</param>
    /// <returns>(ログ, 保存済みrequest, manifestの要求順PID)。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">manifestやbatchの欠落・sealed batchは <c>Conflict</c>、request欠落は <c>NotFound</c>。</exception>
    private (SessionLog Log, PIDRequestEvidence Request, OBDPIDRequest[] Ordered) RequestForTerminalTransition(
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID)
    {
        var ordered = SealedOrderedPIDs(sessionID);
        var log = _context.LogStore.Find(sessionID);
        var batch = log?.Evidence.Batch(batchIdentity.Ordinal);
        if (ordered is null || log is null || batch is null)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        if (requestOrdinal < 0 || requestOrdinal >= batch.Requests.Count)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.NotFound);
        }
        var request = batch.Requests[requestOrdinal];
        if (batch.CompletionState is not null && request.DispatchState != PIDRequestDispatchState.Terminal)
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        return (log, request, ordered);
    }

    /// <summary>既存responded terminalへのretryをduplicateまたはconflictへ分類します。</summary>
    /// <remarks>責務: 保存済みRawとrequest evidenceをretry入力の全semantic fieldへ照合します。</remarks>
    /// <param name="log">Rawを所有するセッションのログ。</param>
    /// <param name="stored">保存済みterminal request evidence。</param>
    /// <param name="observation">retryされたRaw observation。</param>
    /// <param name="valueOutcome">retryされた値評価結果。</param>
    /// <param name="elapsedNanoseconds">retryされたterminal経過時間。</param>
    /// <param name="reasonCode">retryされた分類理由code。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">完全一致は <c>Duplicate</c>、差異または欠損は <c>Conflict</c>。</exception>
    private static void ClassifyRespondedRetry(
        SessionLog log,
        PIDRequestEvidence stored,
        OBDRawResponseObservation observation,
        PIDRequestValueOutcome valueOutcome,
        ulong elapsedNanoseconds,
        string? reasonCode)
    {
        if (stored.TransportOutcome != PIDRequestTransportOutcome.Responded
            || stored.ValueOutcome != valueOutcome
            || stored.ElapsedNanoseconds != elapsedNanoseconds
            || !string.Equals(stored.ReasonCode, reasonCode, StringComparison.Ordinal)
            || stored.RawSequence is not { } sequence
            || log.ReadEntry(sequence) is not { } raw
            || raw.ObservedAt != observation.ObservedAt
            || raw.BatchElapsedNanoseconds != observation.BatchElapsedNanoseconds
            || raw.Service != observation.Request.Service
            || raw.Pid != observation.Request.Pid
            || !raw.Payload.SequenceEqual(observation.Payload))
        {
            throw Error(ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        throw Error(ConnectionSessionAcquisitionRepositoryError.Duplicate);
    }

    /// <summary>確定済みmanifestの要求順PIDを返します。</summary>
    /// <remarks>責務: SQLiteのsealed manifestを1回だけ読み、実行時状態に覚えます。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>要求順PID。sealed manifestがなければ <see langword="null"/>。</returns>
    private OBDPIDRequest[]? SealedOrderedPIDs(ConnectionSessionID sessionID)
    {
        var runtime = _context.Runtime(sessionID);
        if (runtime.SealedOrderedPIDs is { } cached)
        {
            return cached;
        }
        var key = ConnectionSessionStorageContext.Key(sessionID);
        var ordered = _context.Database.Read(connection =>
            ConnectionSessionAcquisitionManifestRecord.IsSealed(connection, null, key)
                ? ConnectionSessionAcquisitionManifestRecord.OrderedPIDs(connection, null, key).Select(row => row.Request).ToArray()
                : null);
        runtime.SealedOrderedPIDs = ordered;
        return ordered;
    }

    /// <summary>計測中のセッションならログをメモリに残す印を付けます。</summary>
    /// <remarks>責務: 未終了セッションのログを追い出し対象から外します。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <param name="log">セッションのログ。</param>
    private void PinIfOpen(ConnectionSessionID sessionID, SessionLog log)
    {
        if (_context.Runtime(sessionID).IsOpen)
        {
            log.IsPinned = true;
        }
    }

    /// <summary>読取処理の失敗を取得repository errorへ変換します。</summary>
    /// <remarks>責務: repository error以外の失敗を <c>Unavailable</c> として返します。</remarks>
    /// <typeparam name="T">読取結果。</typeparam>
    /// <param name="read">読取処理。</param>
    /// <returns>読取結果。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">読取が失敗した場合。</exception>
    private T Read<T>(Func<T> read)
    {
        try
        {
            lock (_context.Gate)
            {
                return read();
            }
        }
        catch (ConnectionSessionAcquisitionRepositoryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SqliteStoreLog.OperationFailed(SqliteStoreLog.Logger, exception, "ConnectionSessionAcquisition", "Read");
            throw Error(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
    }

    /// <summary>取得repository errorを生成します。</summary>
    /// <remarks>責務: エラー理由を例外へ包みます。</remarks>
    /// <param name="reason">エラー理由。</param>
    /// <returns>例外。</returns>
    private static ConnectionSessionAcquisitionRepositoryException Error(ConnectionSessionAcquisitionRepositoryError reason) => new(reason);
}
