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

/// <summary>接続セッション概要と取得manifest（SQLite）、生ログと取得batch・PID要求証拠（LogStore）を1つの保存先として扱います。</summary>
/// <remarks>
/// <para>
/// iOS版 <c>GRDBConnectionSessionRepository</c> に相当し、同じ一式のインターフェースを実装します。組み立てはこの1つを保存先として渡せます。
/// </para>
/// <para>
/// iOS版との違い: 生ログはSQLiteの表ではなくLogStore（セッションごとの追記専用ジャーナル）に保存します。生ログの追記ごとに
/// SQLiteの件数列は更新せず、セッションを読むときにLogStoreの実件数で補います（iOS版の <c>sessions(for:)</c> と同じ結果になります）。
/// 計測中の追記はメモリ上の検証とキューへの投入だけで戻り、ディスクへの確定は背景スレッドがまとめて行います。
/// セッション終了・回復・生ログの置換では、LogStoreの変更をディスクへ確定してからSQLiteを確定します。
/// </para>
/// </remarks>
public sealed class SqliteConnectionSessionRepository :
    IConnectionSessionRepository,
    IConnectionSessionRawLogRepository,
    IConnectionSessionRawLogRangeRepository,
    IConnectionSessionErasureRepository,
    IAccountConnectionSessionErasureRepository,
    IConnectionSessionAcquisitionRepository,
    IConnectionSessionAcquisitionBatchRepository,
    IConnectionSessionAcquisitionTerminationRepository,
    IDisposable
{
    /// <summary>LogStoreのディレクトリー名です。</summary>
    public const string LogStoreDirectoryName = "logs";

    /// <summary>保存一式の共有状態です。</summary>
    private readonly ConnectionSessionStorageContext _context;

    /// <summary>同じ保存先で取得証拠を保存する実装です。</summary>
    private readonly SqliteConnectionSessionAcquisitionRepository _acquisition;

    /// <summary>指定DBへMigrationを適用し、LogStoreと組み合わせて生成します。</summary>
    /// <remarks>
    /// 責務: SQLiteを製品スキーマ利用可能状態へ移行し、SQLiteに行がないセッションのログ（削除途中で残ったもの）を片付けます。
    /// LogStoreはこのインスタンスの破棄時に閉じます。
    /// </remarks>
    /// <param name="database">接続セッション概要とmanifestを保存するDB。</param>
    /// <param name="logStore">生ログと取得証拠のストア。</param>
    /// <param name="performanceEvents">取得証拠writeを通知する匿名計測境界。省略時は何もしません。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">Migrationに失敗した場合は <c>Unavailable</c>。</exception>
    public SqliteConnectionSessionRepository(
        SqliteDatabase database,
        ConnectionSessionLogStore logStore,
        IAcquisitionPerformanceEventPort? performanceEvents = null)
    {
        try
        {
            ProjectZD8DatabaseMigrator.Migrate(database);
        }
        catch (Exception)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
        _context = new ConnectionSessionStorageContext(database, logStore, performanceEvents ?? new NoOpAcquisitionPerformanceEventPort());
        _acquisition = new SqliteConnectionSessionAcquisitionRepository(_context);
        RemoveOrphanLogs();
    }

    /// <summary>データディレクトリーの製品DBとLogStoreを開いて生成します。</summary>
    /// <remarks>責務: 組み立てが渡したディレクトリーの <c>projectzd8.sqlite</c> と <c>logs/</c> を保存先として開きます。</remarks>
    /// <param name="directory">アプリのデータディレクトリー（組み立てが <c>ApplicationData.Current.LocalFolder</c> などを渡します）。</param>
    /// <param name="performanceEvents">取得証拠writeを通知する匿名計測境界。</param>
    /// <param name="options">LogStoreの設定。省略時は既定。</param>
    /// <returns>Migration済みの接続セッションリポジトリ。</returns>
    public static SqliteConnectionSessionRepository OpenApplicationRepository(
        string directory,
        IAcquisitionPerformanceEventPort? performanceEvents = null,
        LogStoreOptions? options = null) =>
        new(
            SqliteDatabase.OpenApplicationDatabase(directory),
            ConnectionSessionLogStore.Open(Path.Combine(directory, LogStoreDirectoryName), options),
            performanceEvents);

    /// <summary>LogStoreへ追記する直前に呼ぶテスト用の差込み口です（例外を投げると追記を失敗させます）。</summary>
    internal Action<SessionLogRecord>? BeforeLogAppend
    {
        get => _context.BeforeLogAppend;
        set => _context.BeforeLogAppend = value;
    }

    /// <summary>テストが直接SQLを実行するためのDBです。</summary>
    internal SqliteDatabase Database => _context.Database;

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.SaveStartOnce"/>
    public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID) =>
        _acquisition.SaveStartOnce(manifest, startedAt, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.AppendEnd"/>
    public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID) =>
        _acquisition.AppendEnd(endedAt, reason, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.Manifest"/>
    public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID) => _acquisition.Manifest(sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.BoundaryEvidence"/>
    public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID) =>
        _acquisition.BoundaryEvidence(sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.BeginBatch"/>
    public void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) =>
        _acquisition.BeginBatch(evidence, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.MarkRequestDispatchBegun"/>
    public void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID) =>
        _acquisition.MarkRequestDispatchBegun(requestOrdinal, batchIdentity, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.SaveRespondedRequest"/>
    public PIDRequestEvidence SaveRespondedRequest(
        OBDRawResponseObservation observation,
        PIDRequestValueOutcome valueOutcome,
        ulong elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) =>
        _acquisition.SaveRespondedRequest(observation, valueOutcome, elapsedNanoseconds, reasonCode, requestOrdinal, batchIdentity, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.SaveNonRespondedRequest"/>
    public PIDRequestEvidence SaveNonRespondedRequest(
        PIDRequestTransportOutcome outcome,
        ulong? elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) =>
        _acquisition.SaveNonRespondedRequest(outcome, elapsedNanoseconds, reasonCode, requestOrdinal, batchIdentity, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.FinishBatch"/>
    public void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) =>
        _acquisition.FinishBatch(evidence, sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.Batches"/>
    public IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID) => _acquisition.Batches(sessionID);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.FinishSessionAcquisition"/>
    public ConnectionSession FinishSessionAcquisition(ConnectionSession session, DateTimeOffset endedAt, ConnectionSessionEndReason reason) =>
        _acquisition.FinishSessionAcquisition(session, endedAt, reason);

    /// <inheritdoc cref="SqliteConnectionSessionAcquisitionRepository.RecoverInterruptedSessionAcquisitions"/>
    public IReadOnlyList<ConnectionSession> RecoverInterruptedSessionAcquisitions(string accountIdentifier, DateTimeOffset recoveredAt) =>
        _acquisition.RecoverInterruptedSessionAcquisitions(accountIdentifier, recoveredAt);

    /// <summary>セッションの現在内容を安定ID単位で保存します。</summary>
    /// <remarks>責務: 1件のDomain接続セッションを新規保存または更新します。保存済みのRaw概要は上書きしません。</remarks>
    /// <param name="session">保存する接続セッション。</param>
    public void Save(ConnectionSession session)
    {
        lock (_context.Gate)
        {
            _context.Database.Write((connection, transaction) =>
            {
                var preserving = session;
                if (_context.FetchSession(connection, transaction, session.Id) is { } existing)
                {
                    preserving = session with { RawLogSummary = existing.RawLogSummary };
                }
                ConnectionSessionRecord.Save(connection, transaction, preserving);
            });
            _context.Invalidate(session.Id);
            if (session.EndedAt is not null && _context.LogStore.Find(session.Id) is { } log)
            {
                log.IsPinned = false;
            }
            _context.LogStore.TrimIdle();
        }
    }

    /// <summary>指定アカウントの接続セッションを新しい順で復元します。</summary>
    /// <remarks>責務: アカウントに一致する行をDomain履歴へ復元し、ローカルRawの件数を実際の生ログから補います。</remarks>
    /// <param name="accountIdentifier">取得対象のAppleアカウント識別子。</param>
    /// <returns>開始日時が新しい順の接続セッション一覧。</returns>
    /// <exception cref="InvalidDataException">不正な保存値の場合。</exception>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier)
    {
        lock (_context.Gate)
        {
            try
            {
                var records = _context.Database.Read(connection => SqliteDatabase.Query(
                    connection,
                    null,
                    $"SELECT {ConnectionSessionRecord.Columns} FROM connection_sessions WHERE accountIdentifier = $1 ORDER BY startedAt DESC",
                    ConnectionSessionRecord.MakeDomainSession,
                    accountIdentifier));
                return records.Select(record =>
                {
                    var session = record ?? throw new InvalidDataException("Connection session contains an invalid identifier or end reason");
                    var corrected = _context.WithActualCounts(session);
                    return corrected.RawLogSummary.RecordCount > 0
                        ? corrected with { RawLogSummary = corrected.RawLogSummary with { LocalState = ConnectionSessionLocalRawState.Available } }
                        : corrected;
                }).ToList();
            }
            finally
            {
                _context.LogStore.TrimIdle();
            }
        }
    }

    /// <summary>指定アカウントが所有するセッションと子の生ログ・取得証拠を物理削除します。</summary>
    /// <remarks>責務: 所有者確認済みのセッション行を削除し（取得manifestは外部キーCascade）、LogStoreのログも消します。</remarks>
    /// <param name="sessionID">削除対象の接続セッションID。</param>
    /// <param name="accountIdentifier">削除対象を所有するAppleアカウント識別子。</param>
    /// <exception cref="ConnectionSessionRepositoryException">所有関係不一致またはセッション不在は <c>InvalidState</c>。</exception>
    public void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
    {
        lock (_context.Gate)
        {
            _context.Database.Write((connection, transaction) =>
            {
                var deleted = SqliteDatabase.Execute(
                    connection,
                    transaction,
                    "DELETE FROM connection_sessions WHERE id = $1 AND accountIdentifier = $2",
                    ConnectionSessionStorageContext.Key(sessionID),
                    accountIdentifier);
                if (deleted != 1)
                {
                    throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
                }
            });
            _context.Invalidate(sessionID);
            _context.LogStore.Delete(sessionID);
        }
    }

    /// <summary>指定アカウントの接続履歴と子の生ログ・取得証拠を削除します。</summary>
    /// <remarks>責務: アカウントに属する全セッション行を原子的に削除し、LogStoreのログも消します。</remarks>
    /// <param name="accountIdentifier">削除対象のAppleアカウント識別子。</param>
    public void DeleteSessions(string accountIdentifier)
    {
        lock (_context.Gate)
        {
            var identifiers = _context.Database.Write((connection, transaction) =>
            {
                var ids = SqliteDatabase.Query(
                    connection,
                    transaction,
                    "SELECT id FROM connection_sessions WHERE accountIdentifier = $1",
                    reader => reader.GetString(0),
                    accountIdentifier);
                SqliteDatabase.Execute(connection, transaction, "DELETE FROM connection_sessions WHERE accountIdentifier = $1", accountIdentifier);
                return ids;
            });
            foreach (var identifier in identifiers)
            {
                if (Guid.TryParse(identifier, out var guid))
                {
                    var sessionID = new ConnectionSessionID(guid);
                    _context.Invalidate(sessionID);
                    _context.LogStore.Delete(sessionID);
                }
            }
        }
    }

    /// <summary>未デコードOBD応答をセッションの次の順序へ追記します。</summary>
    /// <remarks>
    /// 責務: 1件のRaw応答を未終了セッションへ追加します。概要を追記中の状態にするSQLite更新は、状態が変わるときだけ行います。
    /// ディスクへの確定は待ちません。
    /// </remarks>
    /// <param name="observation">取得境界から受け取った未デコード応答。</param>
    /// <param name="sessionID">追記対象の接続セッションID。</param>
    /// <exception cref="ConnectionSessionRepositoryException">セッション不在、終了済みは <c>InvalidState</c>、保存先失敗は <c>Unavailable</c>。</exception>
    public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID)
    {
        lock (_context.Gate)
        {
            var runtime = _context.Runtime(sessionID);
            if (!runtime.IsOpen || observation.BatchElapsedNanoseconds > long.MaxValue)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            _context.EnsureAppendSummary(sessionID, runtime);
            var log = _context.LogStore.Get(sessionID);
            log.IsPinned = true;
            try
            {
                var entry = log.PeekNextEntry(observation);
                _context.Append(log, new SessionLogRecord.Raw(entry));
            }
            catch (ConnectionSessionAcquisitionRepositoryException)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.Unavailable);
            }
        }
    }

    /// <summary>セッション内のRaw応答を記録順で復元します。</summary>
    /// <remarks>責務: 1件のセッションの生ログをsequence昇順で返します。ファイルの読取はロックの外で行います。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <returns>sequence昇順の未デコードRawログ。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) =>
        FindLog(sessionID)?.ReadAll() ?? [];

    /// <summary>指定セッションのRawログ件数を返します。</summary>
    /// <remarks>責務: 1件のセッションIDを保存済み生ログの総件数へ変換します。</remarks>
    /// <param name="sessionID">件数を取得する接続セッションID。</param>
    /// <returns>保存済みRawログ件数。</returns>
    public long EntryCount(ConnectionSessionID sessionID) => FindLog(sessionID)?.Count ?? 0;

    /// <summary>指定セッションのRawログを記録順カーソルで分割して返します。</summary>
    /// <remarks>責務: 1件のセッションIDと記録順カーソルを上限付きの次ページへ変換します。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <param name="sequence">直前に読み込んだ記録順序。先頭ページでは <see langword="null"/>。</param>
    /// <param name="limit">1回で返す最大件数。</param>
    /// <returns>sequence昇順の次ページRawログ。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID, long? sequence, int limit)
    {
        if (sequence == long.MaxValue)
        {
            return [];
        }
        var from = sequence is { } after ? after + 1 : long.MinValue;
        return FindLog(sessionID)?.ReadSequenceRange(from, long.MaxValue, Math.Max(0, limit)) ?? [];
    }

    /// <summary>sequence範囲の生ログを返します。</summary>
    /// <remarks>責務: 疎な索引で開始位置を引き、範囲内の生ログだけを読みます。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <param name="fromInclusive">範囲の開始（含む）。</param>
    /// <param name="toExclusive">範囲の終了（含まない）。</param>
    /// <param name="limit">最大件数。</param>
    /// <returns>sequence昇順の生ログ。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> EntriesInSequenceRange(
        ConnectionSessionID sessionID,
        long fromInclusive,
        long toExclusive,
        int limit = int.MaxValue) =>
        FindLog(sessionID)?.ReadSequenceRange(fromInclusive, toExclusive, limit) ?? [];

    /// <summary>観測日時の範囲の生ログを返します。</summary>
    /// <remarks>責務: 疎な索引の日時の最小・最大で読むブロックを絞り、範囲内の生ログをsequence順で返します。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <param name="from">範囲の開始（含む）。</param>
    /// <param name="to">範囲の終了（含む）。</param>
    /// <returns>sequence昇順の生ログ。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> EntriesObserved(ConnectionSessionID sessionID, DateTimeOffset from, DateTimeOffset to) =>
        FindLog(sessionID)?.ReadTimeRange(from, to) ?? [];

    /// <summary>指定車両に属する全セッションのRaw応答を時系列で復元します。</summary>
    /// <remarks>責務: 1件のアカウントと車両IDを、学習抽出可能なセッション境界付きRawログへ変換します。</remarks>
    /// <param name="vehicleID">抽出対象の登録車両ID。</param>
    /// <param name="accountIdentifier">車両とセッションを所有するAppleアカウント識別子。</param>
    /// <returns>セッション開始日時、セッションID、sequenceの安定順で並ぶRawログ。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">保存値が不正な場合は <c>IntegrityConflict</c>。</exception>
    public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier)
    {
        List<(ConnectionSession Session, SessionLog? Log)> sessions;
        lock (_context.Gate)
        {
            var records = _context.Database.Read(connection => SqliteDatabase.Query(
                connection,
                null,
                $"SELECT {ConnectionSessionRecord.Columns} FROM connection_sessions WHERE accountIdentifier = $1 AND vehicleID = $2 ORDER BY startedAt, id",
                ConnectionSessionRecord.MakeDomainSession,
                accountIdentifier,
                SqliteValueConversion.Key(vehicleID.RawValue)));
            sessions = records.Select(record =>
            {
                if (record is not { } session || session.Vehicle?.Id != vehicleID)
                {
                    throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
                }
                return (session, _context.LogStore.Find(session.Id));
            }).ToList();
        }
        return sessions
            .SelectMany(pair => (pair.Log?.ReadAll() ?? []).Select(entry =>
                new VehicleConnectionSessionRawLogEntry(vehicleID, pair.Session.Id, pair.Session.StartedAt, entry)))
            .ToList();
    }

    /// <summary>CloudKit保存済みManifestをセッションへ記録します。</summary>
    /// <remarks>責務: 1件の終了済みセッションを指定DigestのCloudKit保存済み状態へ遷移させます。</remarks>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <param name="manifestDigest">保存済みAssetバイトのSHA-256。</param>
    /// <exception cref="ConnectionSessionRepositoryException">セッション状態不正は <c>InvalidState</c>。</exception>
    public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest) =>
        UpdateSession(sessionID, session =>
        {
            if (session.EndedAt is null || manifestDigest.Length == 0)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            return session with
            {
                RawLogSummary = session.RawLogSummary with
                {
                    CloudState = ConnectionSessionCloudSyncState.Uploaded,
                    ManifestDigest = manifestDigest,
                    MacImportReceipt = null,
                },
            };
        });

    /// <summary>CloudKit転送失敗を再試行可能状態として記録します。</summary>
    /// <remarks>責務: 1件のRawログ保有セッションをCloudKit転送失敗状態へ遷移させます。</remarks>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <exception cref="ConnectionSessionRepositoryException">ローカルRawがない場合は <c>InvalidState</c>。</exception>
    public void MarkCloudUploadFailed(ConnectionSessionID sessionID) =>
        UpdateSession(sessionID, session =>
        {
            if (session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Available)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            return session with { RawLogSummary = session.RawLogSummary with { CloudState = ConnectionSessionCloudSyncState.Failed } };
        });

    /// <summary>Mac取込受領証をセッションへ保存します。</summary>
    /// <remarks>責務: 1件のMac受領証を同じDigestのセッションManifestへ関連付けます。</remarks>
    /// <param name="receipt">Macが発行した永続取込受領証。</param>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <exception cref="ConnectionSessionRepositoryException">Manifest不一致は <c>IntegrityConflict</c>。</exception>
    public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID) =>
        UpdateSession(sessionID, session =>
        {
            if (!string.Equals(session.RawLogSummary.ManifestDigest, receipt.ManifestDigest, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            return session with { RawLogSummary = session.RawLogSummary with { MacImportReceipt = receipt } };
        });

    /// <summary>検証済みRaw転送Payloadを既存ローカル接続履歴へ取り込みます。</summary>
    /// <remarks>責務: 1件の検証済みRaw Payloadを同じ安定セッションIDへ冪等に再結合します。</remarks>
    /// <param name="transfer">CloudKitから取得した検証済み転送Payload。</param>
    public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) => RestoreVerifiedTransfer(transfer);

    /// <summary>CloudKitセッション概要をRaw Payloadなしでローカル履歴へ取り込みます。</summary>
    /// <remarks>責務: 1件の検証済み概要を、端末固有Raw状態を保持したセッション一覧へ反映します。</remarks>
    /// <param name="metadata">Raw Payloadを含まないセッション概要とManifest。</param>
    /// <exception cref="ConnectionSessionRepositoryException">既存内容との不一致や状態不正は <c>IntegrityConflict</c>。</exception>
    public void ImportCloudMetadata(ConnectionSessionCloudMetadata metadata)
    {
        lock (_context.Gate)
        {
            _context.Database.Write((connection, transaction) =>
            {
                var transferred = metadata.Session;
                if (transferred.EndedAt is null || transferred.AccountIdentifier.Length == 0 || metadata.ManifestDigest.Length == 0)
                {
                    throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
                }
                if (_context.FetchSession(connection, transaction, transferred.Id) is { } existing)
                {
                    if (!SessionsHaveCompatibleArchivedMetadata(existing, transferred))
                    {
                        throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
                    }
                    if (existing.RawLogSummary.ManifestDigest is { } digest
                        && !string.Equals(digest, metadata.ManifestDigest, StringComparison.Ordinal)
                        && existing.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded)
                    {
                        throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
                    }
                    var reconciled = SessionByFillingMissingTransferredMetadata(existing, transferred);
                    var receipt = reconciled.RawLogSummary.MacImportReceipt;
                    reconciled = reconciled with
                    {
                        RawLogSummary = reconciled.RawLogSummary with
                        {
                            RecordCount = transferred.RawLogSummary.RecordCount,
                            ByteCount = transferred.RawLogSummary.ByteCount,
                            CloudState = ConnectionSessionCloudSyncState.Uploaded,
                            ManifestDigest = metadata.ManifestDigest,
                            MacImportReceipt = string.Equals(receipt?.ManifestDigest, metadata.ManifestDigest, StringComparison.Ordinal) ? receipt : null,
                        },
                    };
                    ConnectionSessionRecord.Save(connection, transaction, reconciled);
                    return;
                }
                var imported = transferred with
                {
                    RawLogSummary = new ConnectionSessionRawLogSummary(
                        recordCount: transferred.RawLogSummary.RecordCount,
                        byteCount: transferred.RawLogSummary.ByteCount,
                        localState: transferred.RawLogSummary.RecordCount == 0
                            ? ConnectionSessionLocalRawState.Empty
                            : ConnectionSessionLocalRawState.Removed,
                        cloudState: ConnectionSessionCloudSyncState.Uploaded,
                        manifestDigest: metadata.ManifestDigest,
                        macImportReceipt: null,
                        lastAccessedAt: null),
                };
                ConnectionSessionRecord.Save(connection, transaction, imported);
            });
            _context.Invalidate(metadata.Session.Id);
        }
    }

    /// <summary>明示要求されたセッションID検証済みRaw Payloadをローカルキャッシュへ復元します。</summary>
    /// <remarks>
    /// 責務: 1件のオンデマンド転送を安定セッションIDへ原子的に再結合します。
    /// LogStoreに新しい世代を書いて読み戻しで検証し、SQLiteの確定後に切り替えます（SQLiteが失敗したら新しい世代を捨てます）。
    /// 取得証拠（batch・PID要求）はそのまま残します。
    /// </remarks>
    /// <param name="transfer">CloudKitから取得した検証済みセッション転送。</param>
    /// <exception cref="ConnectionSessionRepositoryException">アカウント不一致、順序欠損、未終了、読み戻し不一致は <c>IntegrityConflict</c>。</exception>
    public void RestoreVerifiedTransfer(VerifiedConnectionSessionTransfer transfer)
    {
        lock (_context.Gate)
        {
            var package = transfer.Package;
            var contiguous = package.Entries.Select((entry, offset) => entry.Sequence == offset).All(matches => matches);
            var restored = _context.Database.Read(connection => _context.FetchSession(connection, null, package.SessionID));
            if (!contiguous
                || restored is null
                || !string.Equals(restored.AccountIdentifier, package.AccountIdentifier, StringComparison.Ordinal)
                || restored.EndedAt is null
                || package.Entries.Any(entry => entry.BatchElapsedNanoseconds > long.MaxValue))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            var existing = restored.RawLogSummary;
            var summary = new ConnectionSessionRawLogSummary(
                recordCount: package.Entries.Count,
                byteCount: package.Entries.Sum(entry => (long)entry.Payload.Count),
                localState: package.Entries.Count == 0 ? ConnectionSessionLocalRawState.Empty : ConnectionSessionLocalRawState.Available,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: transfer.ManifestDigest,
                macImportReceipt: string.Equals(existing.MacImportReceipt?.ManifestDigest, transfer.ManifestDigest, StringComparison.Ordinal)
                    ? existing.MacImportReceipt
                    : null,
                lastAccessedAt: existing.LastAccessedAt);
            ReplaceRawLog(package.SessionID, package.Entries, restored with { RawLogSummary = summary });
        }
    }

    /// <summary>Rawログの最終閲覧日時を現在端末へ保存します。</summary>
    /// <remarks>責務: 1件のローカルRawキャッシュを指定閲覧日時へ更新します。</remarks>
    /// <param name="date">Rawログを閲覧可能状態へした日時。</param>
    /// <param name="sessionID">更新するセッションID。</param>
    /// <exception cref="ConnectionSessionRepositoryException">Raw除去済みまたはセッション不在は <c>InvalidState</c>。</exception>
    public void MarkRawLogAccessed(DateTimeOffset date, ConnectionSessionID sessionID) =>
        UpdateSession(sessionID, session =>
        {
            if (session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            return session with { RawLogSummary = session.RawLogSummary with { LastAccessedAt = date } };
        });

    /// <summary>セッション概要を残して現在端末のRawログだけを除去します。</summary>
    /// <remarks>
    /// 責務: 1件の終了済みセッションから生ログだけを消してローカル除去状態へ遷移させます。件数とバイト数は概要に残します。
    /// iOS版はRaw行を参照するPID要求証拠があると外部キーで失敗しますが、Windows版は取得証拠を残したまま生ログだけを消します。
    /// </remarks>
    /// <param name="sessionID">RawログPayloadを除去する接続セッションID。</param>
    /// <exception cref="ConnectionSessionRepositoryException">取得中またはRawログ不在は <c>InvalidState</c>。</exception>
    public void RemoveLocalEntries(ConnectionSessionID sessionID)
    {
        lock (_context.Gate)
        {
            var session = _context.Database.Read(connection => _context.FetchSession(connection, null, sessionID));
            if (session is null
                || session.EndedAt is null
                || session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Available)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            ReplaceRawLog(
                sessionID,
                [],
                session with { RawLogSummary = session.RawLogSummary with { LocalState = ConnectionSessionLocalRawState.Removed } });
        }
    }

    /// <summary>生ログを置き換え、同時にセッション概要を保存します。</summary>
    /// <remarks>責務: LogStoreの新しい世代を用意し、SQLiteを確定してから切り替えます。SQLiteが失敗したら新しい世代を捨てます。</remarks>
    /// <param name="sessionID">対象のセッションID。</param>
    /// <param name="entries">新しい生ログ。</param>
    /// <param name="session">保存するセッション概要。</param>
    private void ReplaceRawLog(ConnectionSessionID sessionID, IReadOnlyList<ConnectionSessionRawLogEntry> entries, ConnectionSession session)
    {
        var log = entries.Count == 0 ? _context.LogStore.Find(sessionID) : _context.LogStore.Get(sessionID);
        PreparedGeneration? prepared;
        try
        {
            prepared = log?.PrepareReplacement(entries);
        }
        catch (ConnectionSessionAcquisitionRepositoryException)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.Unavailable);
        }
        try
        {
            _context.Database.Write((connection, transaction) => ConnectionSessionRecord.Save(connection, transaction, session));
        }
        catch
        {
            prepared?.Rollback();
            throw;
        }
        prepared?.Commit();
        _context.Invalidate(sessionID);
        _context.LogStore.TrimIdle();
    }

    /// <summary>指定セッションを読み込み、変更処理を同じSQLite書込み内で保存します。</summary>
    /// <remarks>責務: 1件のセッション状態更新を読込と保存の原子的境界へ変換します。</remarks>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <param name="transform">読み込んだDomainセッションから新しい値を作る処理。</param>
    /// <exception cref="ConnectionSessionRepositoryException">セッション不在は <c>InvalidState</c>。</exception>
    private void UpdateSession(ConnectionSessionID sessionID, Func<ConnectionSession, ConnectionSession> transform)
    {
        lock (_context.Gate)
        {
            _context.Database.Write((connection, transaction) =>
            {
                var session = _context.FetchSession(connection, transaction, sessionID)
                    ?? throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
                ConnectionSessionRecord.Save(connection, transaction, transform(session));
            });
            _context.Invalidate(sessionID);
        }
    }

    /// <summary>ログがあれば開いて返します。</summary>
    /// <remarks>責務: ロック内でログを引き、ファイルの読取は呼出元がロックの外で行えるようにします。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>ログ。ない場合は <see langword="null"/>。</returns>
    private SessionLog? FindLog(ConnectionSessionID sessionID)
    {
        lock (_context.Gate)
        {
            return _context.LogStore.Find(sessionID);
        }
    }

    /// <summary>SQLiteに行がないセッションのログを消します。</summary>
    /// <remarks>責務: 削除の途中で落ちて残ったログを起動時に片付けます。</remarks>
    private void RemoveOrphanLogs()
    {
        var known = _context.Database.Read(connection => SqliteDatabase.Query(
            connection,
            null,
            "SELECT id FROM connection_sessions",
            reader => reader.GetString(0)).ToHashSet(StringComparer.Ordinal));
        foreach (var sessionID in _context.LogStore.StoredSessionIDs())
        {
            if (!known.Contains(ConnectionSessionStorageContext.Key(sessionID)))
            {
                _context.LogStore.Delete(sessionID);
            }
        }
    }

    /// <summary>2件のセッションが同じ保存済み運転を表現できるかを確認します。</summary>
    /// <remarks>責務: セッション識別情報と両側に存在する取得メタデータを同内容転送判定へ変換します。</remarks>
    /// <param name="existing">現在端末に保存済みのセッション。</param>
    /// <param name="transferred">CloudKitから復元したセッション。</param>
    /// <returns>必須情報が一致し、任意情報に矛盾がない場合は <see langword="true"/>。</returns>
    private static bool SessionsHaveCompatibleArchivedMetadata(ConnectionSession existing, ConnectionSession transferred) =>
        existing.Id == transferred.Id
        && string.Equals(existing.AccountIdentifier, transferred.AccountIdentifier, StringComparison.Ordinal)
        && DatesMatchTransferPrecision(existing.StartedAt, transferred.StartedAt)
        && OptionalDatesMatchTransferPrecision(existing.EndedAt, transferred.EndedAt)
        && existing.EndReason == transferred.EndReason
        && ValuesAreCompatible(existing.Vehicle, transferred.Vehicle)
        && ValuesAreCompatible(existing.AcquisitionDevice, transferred.AcquisitionDevice)
        && ValuesAreCompatible(existing.StartingOdometerKilometers, transferred.StartingOdometerKilometers)
        && ValuesAreCompatible(existing.EndingOdometerKilometers, transferred.EndingOdometerKilometers)
        && ValuesAreCompatible(existing.DistanceSourceModelCode, transferred.DistanceSourceModelCode);

    /// <summary>2件の日時がCloudKit転送Codecのミリ秒精度で一致するかを返します。</summary>
    /// <remarks>責務: ローカル日時と転送日時をミリ秒単位の同一時刻判定へ変換します。</remarks>
    /// <param name="lhs">ローカル永続化から復元した日時。</param>
    /// <param name="rhs">CloudKit Assetから復号した日時。</param>
    /// <returns>差が1.001ミリ秒以内の場合は <see langword="true"/>。</returns>
    private static bool DatesMatchTransferPrecision(DateTimeOffset lhs, DateTimeOffset rhs) =>
        Math.Abs((lhs - rhs).Ticks) <= 10_010;

    /// <summary>2件の任意日時が欠落状態を含めてCloudKit転送精度で一致するかを返します。</summary>
    /// <remarks>責務: 任意のローカル日時と転送日時をミリ秒単位の同一状態判定へ変換します。</remarks>
    /// <param name="lhs">ローカル永続化から復元した任意日時。</param>
    /// <param name="rhs">CloudKit Assetから復号した任意日時。</param>
    /// <returns>両方欠落するか、両方の日時差が許容範囲内の場合は <see langword="true"/>。</returns>
    private static bool OptionalDatesMatchTransferPrecision(DateTimeOffset? lhs, DateTimeOffset? rhs) =>
        (lhs, rhs) switch
        {
            ({ } left, { } right) => DatesMatchTransferPrecision(left, right),
            (null, null) => true,
            _ => false,
        };

    /// <summary>2件の任意値が欠落補完可能または同値かを返します。</summary>
    /// <remarks>責務: 任意の同型値2件を非破壊なメタデータ補完可否へ変換します。</remarks>
    /// <typeparam name="T">値の型。</typeparam>
    /// <param name="lhs">現在端末の任意値。</param>
    /// <param name="rhs">転送Payloadの任意値。</param>
    /// <returns>片方が欠落しているか両方が同値の場合は <see langword="true"/>。</returns>
    private static bool ValuesAreCompatible<T>(T? lhs, T? rhs) =>
        lhs is null || rhs is null || EqualityComparer<T>.Default.Equals(lhs, rhs);

    /// <summary>同じManifestの転送情報でローカル欠落メタデータだけを補完します。</summary>
    /// <remarks>責務: 1件の既存セッションへ同一転送Payloadが保持する欠落中の表示情報を非破壊で反映します。</remarks>
    /// <param name="existing">端末固有の保管状態と後続確認結果を保持する既存セッション。</param>
    /// <param name="transferred">同じManifestから復元した転送時点のセッション。</param>
    /// <returns>ローカル値を優先し、欠落項目だけを転送値で補完したセッション。</returns>
    private static ConnectionSession SessionByFillingMissingTransferredMetadata(ConnectionSession existing, ConnectionSession transferred) =>
        existing with
        {
            Vehicle = existing.Vehicle ?? transferred.Vehicle,
            AcquisitionDevice = existing.AcquisitionDevice ?? transferred.AcquisitionDevice,
            StartingOdometerKilometers = existing.StartingOdometerKilometers ?? transferred.StartingOdometerKilometers,
            EndingOdometerKilometers = existing.EndingOdometerKilometers ?? transferred.EndingOdometerKilometers,
            DistanceSourceModelCode = existing.DistanceSourceModelCode ?? transferred.DistanceSourceModelCode,
        };

    /// <summary>LogStoreを閉じます。</summary>
    /// <remarks>責務: 全ログの書込みを確定して索引ファイルを書き、背景書込みを止めます。</remarks>
    public void Dispose() => _context.LogStore.Dispose();
}
