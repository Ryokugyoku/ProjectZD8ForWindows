using Microsoft.Data.Sqlite;
using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Data.Persistence.LogStore;
using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Records;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>接続セッションの保存一式が共有する、SQLite・LogStore・直列化ロック・セッションごとの実行時状態です。</summary>
/// <remarks>
/// iOS版では同じGRDB <c>DatabaseQueue</c> を共有することで、セッション・Raw・取得証拠の書込みが直列化されていました。
/// Windows版はこのクラスのロックで同じ直列化を行い、SQLite（概要・manifest）とLogStore（生ログ・batch・PID要求）を同じ順序で更新します。
/// </remarks>
internal sealed class ConnectionSessionStorageContext
{
    /// <summary>セッションごとの実行時状態です。</summary>
    private readonly Dictionary<Guid, SessionRuntime> _runtimes = [];

    /// <summary>SQLiteとLogStoreと計測境界を指定して生成します。</summary>
    /// <remarks>責務: 保存一式が共有する依存を固定します。</remarks>
    /// <param name="database">接続セッション概要とmanifestを保存するDB。</param>
    /// <param name="logStore">生ログと取得証拠のストア。</param>
    /// <param name="performanceEvents">取得証拠writeを通知する匿名計測境界。</param>
    internal ConnectionSessionStorageContext(
        SqliteDatabase database,
        ConnectionSessionLogStore logStore,
        IAcquisitionPerformanceEventPort performanceEvents)
    {
        Database = database;
        LogStore = logStore;
        PerformanceEvents = performanceEvents;
    }

    /// <summary>すべての読書きを直列化するロックです。</summary>
    internal object Gate { get; } = new();

    /// <summary>SQLiteの読書き境界です。</summary>
    internal SqliteDatabase Database { get; }

    /// <summary>生ログと取得証拠のストアです。</summary>
    internal ConnectionSessionLogStore LogStore { get; }

    /// <summary>取得証拠writeを通知する匿名計測境界です。</summary>
    internal IAcquisitionPerformanceEventPort PerformanceEvents { get; }

    /// <summary>LogStoreへ追記する直前に呼ぶテスト用の差込み口です（例外を投げると追記を失敗させます）。</summary>
    internal Action<SessionLogRecord>? BeforeLogAppend { get; set; }

    /// <summary>セッションIDをDBキーへ変換します。</summary>
    /// <remarks>責務: UUID形式のsession IDを小文字の安定SQLiteキーへ変換します。</remarks>
    /// <param name="sessionID">変換するセッションID。</param>
    /// <returns>小文字UUID文字列。</returns>
    internal static string Key(ConnectionSessionID sessionID) => SqliteValueConversion.Key(sessionID.RawValue);

    /// <summary>SQLiteから1件のセッションを読み、生ログの件数を実際の値で補います。</summary>
    /// <remarks>
    /// 責務: 保存行をDomainセッションへ復元し、ローカルRawを除去していなければ件数とバイト数をLogStoreの実値にします。
    /// 生ログの追記ごとにSQLiteを書かないため、保存行の件数列は最新とは限りません。
    /// </remarks>
    /// <param name="connection">実行中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="sessionID">読み取るセッションID。</param>
    /// <returns>セッション。未保存なら <see langword="null"/>。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">保存値が不正な場合は <c>IntegrityConflict</c>。</exception>
    internal ConnectionSession? FetchSession(SqliteConnection connection, SqliteTransaction? transaction, ConnectionSessionID sessionID)
    {
        ConnectionSession? session;
        try
        {
            session = ConnectionSessionRecord.Fetch(connection, transaction, sessionID);
        }
        catch (InvalidDataException)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
        }
        return session is null ? null : WithActualCounts(session);
    }

    /// <summary>ローカルRawを除去していないセッションの件数とバイト数をLogStoreの実値にします。</summary>
    /// <remarks>責務: 1件のセッション概要の件数列を現在の生ログの実値へ補正します。</remarks>
    /// <param name="session">補正するセッション。</param>
    /// <returns>補正したセッション。</returns>
    internal ConnectionSession WithActualCounts(ConnectionSession session)
    {
        if (session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
        {
            return session;
        }
        var (count, bytes) = Counts(session.Id);
        return session with { RawLogSummary = session.RawLogSummary with { RecordCount = count, ByteCount = bytes } };
    }

    /// <summary>セッションの生ログの件数とバイト数を返します。</summary>
    /// <remarks>責務: LogStoreにログがあれば実値を、なければ0件を返します。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>(件数, 応答データの合計バイト数)。</returns>
    internal (long Count, long Bytes) Counts(ConnectionSessionID sessionID) =>
        LogStore.Find(sessionID) is { } log ? (log.Count, log.ByteCount) : (0, 0);

    /// <summary>セッションの実行時状態を返します。</summary>
    /// <remarks>責務: 生ログの追記ごとにSQLiteを読まないよう、存在・終了状態・概要の状態を覚えておきます。</remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <returns>実行時状態。</returns>
    internal SessionRuntime Runtime(ConnectionSessionID sessionID)
    {
        if (_runtimes.TryGetValue(sessionID.RawValue, out var runtime))
        {
            return runtime;
        }
        var session = Database.Read(connection => FetchSession(connection, null, sessionID));
        runtime = new SessionRuntime
        {
            Exists = session is not null,
            IsOpen = session is { EndedAt: null },
            SummaryIsInAppendState = session is not null && IsAppendState(session.RawLogSummary),
        };
        _runtimes[sessionID.RawValue] = runtime;
        return runtime;
    }

    /// <summary>セッションの実行時状態を捨てます。</summary>
    /// <remarks>責務: SQLiteのセッション行を変えたあとで、次回に読み直させます。</remarks>
    /// <param name="sessionID">セッションID。</param>
    internal void Invalidate(ConnectionSessionID sessionID) => _runtimes.Remove(sessionID.RawValue);

    /// <summary>生ログ追記で変わる概要列を、追記中の状態にします。</summary>
    /// <remarks>
    /// 責務: iOS版の追記と同じく、ローカルRawあり・転送待ち・Digestと受領証と閲覧日時なしの状態をSQLiteへ保存します。
    /// すでにその状態ならSQLiteを書きません（計測中の追記ごとのSQLite書込みを避けるため）。
    /// </remarks>
    /// <param name="sessionID">セッションID。</param>
    /// <param name="runtime">セッションの実行時状態。</param>
    internal void EnsureAppendSummary(ConnectionSessionID sessionID, SessionRuntime runtime)
    {
        if (runtime.SummaryIsInAppendState)
        {
            return;
        }
        Database.Write((connection, transaction) =>
        {
            var session = FetchSession(connection, transaction, sessionID)
                ?? throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            ConnectionSessionRecord.Save(connection, transaction, session with
            {
                RawLogSummary = session.RawLogSummary with
                {
                    LocalState = ConnectionSessionLocalRawState.Available,
                    CloudState = ConnectionSessionCloudSyncState.Pending,
                    ManifestDigest = null,
                    MacImportReceipt = null,
                    LastAccessedAt = null,
                },
            });
        });
        runtime.SummaryIsInAppendState = true;
    }

    /// <summary>LogStoreへレコードを追記します。</summary>
    /// <remarks>責務: テスト用の差込み口を通してから、セッションのログへ1件のレコードを追記します。</remarks>
    /// <param name="log">追記先のログ。</param>
    /// <param name="record">追記するレコード。</param>
    internal void Append(SessionLog log, SessionLogRecord record)
    {
        BeforeLogAppend?.Invoke(record);
        log.Append(record);
    }

    /// <summary>1回の取得証拠writeを計測区間として実行します。</summary>
    /// <remarks>
    /// 責務: iOS版の <c>measuredWrite</c> と同じく、待機（ロック取得）と処理を含む区間を通知し、
    /// repository error以外の失敗を <c>Unavailable</c> へ変換します。
    /// </remarks>
    /// <typeparam name="T">処理の結果。</typeparam>
    /// <param name="operation">計測する取得性能操作。</param>
    /// <param name="context">個人・端末・車両識別子を含まない取得位置。</param>
    /// <param name="body">ロック内で実行する処理。</param>
    /// <returns>処理の結果。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">処理が失敗した場合。</exception>
    internal T Measured<T>(AcquisitionPerformanceOperation operation, AcquisitionPerformanceContext context, Func<T> body)
    {
        var interval = PerformanceEvents.Begin(operation, context);
        try
        {
            T value;
            lock (Gate)
            {
                PerformanceEvents.QueueDidEnter(interval);
                value = body();
            }
            PerformanceEvents.End(interval, AcquisitionPerformanceOutcome.Succeeded);
            return value;
        }
        catch (ConnectionSessionAcquisitionRepositoryException)
        {
            PerformanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
            throw;
        }
        catch (Exception)
        {
            PerformanceEvents.End(interval, AcquisitionPerformanceOutcome.Failed);
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Unavailable);
        }
        finally
        {
            LogStore.TrimIdle();
        }
    }

    /// <summary>概要が生ログ追記中の状態かどうかを返します。</summary>
    /// <remarks>責務: 追記時にSQLiteを書き換える必要があるかを判定します。</remarks>
    /// <param name="summary">判定する概要。</param>
    /// <returns>追記中の状態なら <see langword="true"/>。</returns>
    private static bool IsAppendState(ConnectionSessionRawLogSummary summary) =>
        summary.LocalState == ConnectionSessionLocalRawState.Available
        && summary.CloudState == ConnectionSessionCloudSyncState.Pending
        && summary.ManifestDigest is null
        && summary.MacImportReceipt is null
        && summary.LastAccessedAt is null;

    /// <summary>セッションごとの実行時状態です。</summary>
    internal sealed class SessionRuntime
    {
        /// <summary>SQLiteにセッション行があるかどうかです。</summary>
        public bool Exists { get; init; }

        /// <summary>未終了（追記できる）かどうかです。</summary>
        public bool IsOpen { get; init; }

        /// <summary>概要がすでに生ログ追記中の状態かどうかです。</summary>
        public bool SummaryIsInAppendState { get; set; }

        /// <summary>確定済みmanifestの要求順PID（一度確定すると変わらないため覚えます）。</summary>
        public OBDPIDRequest[]? SealedOrderedPIDs { get; set; }
    }
}
