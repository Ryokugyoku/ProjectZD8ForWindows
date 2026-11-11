using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.LogHistory.UseCases;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LogHistory.State;

/// <summary>アカウント単位の接続セッション照会を履歴表示状態へ変換します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。<see cref="State"/> が変わると <see cref="PropertyChanged"/> を通知します。</remarks>
public sealed class ConnectionHistoryModel : INotifyPropertyChanged
{
    /// <summary>Platformが描画する現在の履歴状態です。</summary>
    private ConnectionHistoryState state;
    /// <summary>セッションの永続化照会境界です。</summary>
    private readonly IConnectionSessionRepository repository;
    /// <summary>セッション単位のCloudKit同期ユースケースです。</summary>
    private readonly SynchronizeConnectionSessionsUseCase? synchronizeSessions;
    /// <summary>現在端末のRawログ容量解放ユースケースです。</summary>
    private readonly ReleaseConnectionSessionRawCacheUseCase? releaseRawCache;
    /// <summary>iCloudを含む全端末セッション削除ユースケースです。</summary>
    private readonly DeleteConnectionSessionEverywhereUseCase? deleteSessionEverywhere;
    /// <summary>自動判別できない停止をユーザー確認済みとして保存するユースケースです。</summary>
    private readonly ReviewInterruptedConnectionSessionUseCase? reviewInterruptedSession;
    /// <summary>期限切れRawログをCloudKit保管へ退避するユースケースです。</summary>
    private readonly EvictStaleConnectionSessionRawLogsUseCase? evictStaleRawLogs;
    /// <summary>現在のAppleアカウント識別子です。</summary>
    private string? accountIdentifier;
    /// <summary>古いアカウント同期完了を現在状態へ反映しないための世代です。</summary>
    private ulong syncGeneration;
    /// <summary>現在進行中のCloudKit同期タスクです。</summary>
    private RunningTask? syncTask;
    /// <summary>現在進行中の単一セッションアップロードタスクです。</summary>
    private RunningTask? uploadTask;
    /// <summary>古いアカウントのアップロード完了を現在状態へ反映しないための世代です。</summary>
    private ulong uploadGeneration;
    /// <summary>現在進行中の全端末削除タスクです。</summary>
    private RunningTask? deletionTask;

    /// <summary>初期状態とセッション照会先を固定して生成します。</summary>
    /// <remarks>責務: 接続履歴表示状態を1件のセッション照会境界へ結び付けます。</remarks>
    /// <param name="state">Platformへ公開する初期状態。</param>
    /// <param name="repository">アカウント単位のセッション取得先。</param>
    /// <param name="synchronizeSessions">セッション単位のCloudKit同期ユースケース。</param>
    /// <param name="releaseRawCache">現在端末のRawログ容量解放ユースケース。</param>
    /// <param name="deleteSessionEverywhere">iCloudを含む全端末セッション削除ユースケース。</param>
    /// <param name="reviewInterruptedSession">ユーザー操作による停止の確認結果を保存するユースケース。</param>
    /// <param name="evictStaleRawLogs">3日以上未閲覧のRawログを端末から退避するユースケース。</param>
    public ConnectionHistoryModel(
        ConnectionHistoryState state,
        IConnectionSessionRepository repository,
        SynchronizeConnectionSessionsUseCase? synchronizeSessions = null,
        ReleaseConnectionSessionRawCacheUseCase? releaseRawCache = null,
        DeleteConnectionSessionEverywhereUseCase? deleteSessionEverywhere = null,
        ReviewInterruptedConnectionSessionUseCase? reviewInterruptedSession = null,
        EvictStaleConnectionSessionRawLogsUseCase? evictStaleRawLogs = null)
    {
        this.state = state;
        this.repository = repository;
        this.synchronizeSessions = synchronizeSessions;
        this.releaseRawCache = releaseRawCache;
        this.deleteSessionEverywhere = deleteSessionEverywhere;
        this.reviewInterruptedSession = reviewInterruptedSession;
        this.evictStaleRawLogs = evictStaleRawLogs;
    }

    /// <summary>空の履歴状態と指定セッション照会先を使って生成します。</summary>
    /// <remarks>責務: 1件のセッション照会境界を標準的な接続履歴モデルへ変換します。</remarks>
    /// <param name="repository">アカウント単位のセッション取得先。</param>
    /// <param name="synchronizeSessions">セッション単位のCloudKit同期ユースケース。</param>
    /// <param name="releaseRawCache">現在端末のRawログ容量解放ユースケース。</param>
    /// <param name="deleteSessionEverywhere">iCloudを含む全端末セッション削除ユースケース。</param>
    /// <param name="reviewInterruptedSession">ユーザー操作による停止の確認結果を保存するユースケース。</param>
    /// <param name="evictStaleRawLogs">3日以上未閲覧のRawログを端末から退避するユースケース。</param>
    public ConnectionHistoryModel(
        IConnectionSessionRepository repository,
        SynchronizeConnectionSessionsUseCase? synchronizeSessions = null,
        ReleaseConnectionSessionRawCacheUseCase? releaseRawCache = null,
        DeleteConnectionSessionEverywhereUseCase? deleteSessionEverywhere = null,
        ReviewInterruptedConnectionSessionUseCase? reviewInterruptedSession = null,
        EvictStaleConnectionSessionRawLogsUseCase? evictStaleRawLogs = null)
        : this(
            new ConnectionHistoryState(),
            repository,
            synchronizeSessions,
            releaseRawCache,
            deleteSessionEverywhere,
            reviewInterruptedSession,
            evictStaleRawLogs)
    {
    }

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Platformが描画する現在の履歴状態です。</summary>
    public ConnectionHistoryState State
    {
        get => state;
        set => SetProperty(ref state, value);
    }

    /// <summary>型付き操作を履歴照会へ変換します。</summary>
    /// <remarks>責務: 1件のLogHistory操作を対応する履歴状態変更またはユースケースへ振り分けます。</remarks>
    /// <param name="action">AppまたはPlatformから通知された操作。</param>
    public void Send(ConnectionHistoryAction action)
    {
        switch (action)
        {
            case ConnectionHistoryAction.AccountIdentifierChanged changed: ActivateAccount(changed.Identifier); break;
            case ConnectionHistoryAction.RefreshRequested: RefreshSessions(); break;
            case ConnectionHistoryAction.LocalDataChanged: LoadSessions(); break;
            case ConnectionHistoryAction.AutomaticUploadChanged changed: State = State with { AutomaticUploadEnabled = changed.IsEnabled }; break;
            case ConnectionHistoryAction.SessionUploadRequested requested: UploadSession(requested.SessionID); break;
            case ConnectionHistoryAction.SessionUploadFailureDismissed: State = State with { UploadFailureSessionID = null }; break;
            case ConnectionHistoryAction.FilterStartDateChanged changed: State = State with { FilterStartDate = changed.Date }; break;
            case ConnectionHistoryAction.FilterEndDateChanged changed: State = State with { FilterEndDate = changed.Date }; break;
            case ConnectionHistoryAction.EndReasonFilterChanged changed: State = State with { EndReasonFilter = changed.Filter }; break;
            case ConnectionHistoryAction.SortOrderChanged changed: State = State with { SortOrder = changed.Order }; break;
            case ConnectionHistoryAction.FiltersReset: ResetFilters(); break;
            case ConnectionHistoryAction.StopReviewRequested requested: PrepareStopReview(requested.SessionID); break;
            case ConnectionHistoryAction.StopReviewConfirmed: ConfirmStopReview(); break;
            case ConnectionHistoryAction.StopReviewCancelled: State = State with { StopReviewPrompt = null }; break;
            case ConnectionHistoryAction.StopReviewFailureDismissed: State = State with { StopReviewFailureKey = null }; break;
            case ConnectionHistoryAction.LocalRawRemovalRequested requested: PrepareRawRemoval(requested.SessionID); break;
            case ConnectionHistoryAction.LocalRawRemovalConfirmed: ConfirmRawRemoval(); break;
            case ConnectionHistoryAction.LocalRawRemovalCancelled: State = State with { RawRemovalPrompt = null }; break;
            case ConnectionHistoryAction.SessionDeletionRequested requested: PrepareSessionDeletion(requested.SessionID); break;
            case ConnectionHistoryAction.SessionDeletionConfirmed: ConfirmSessionDeletion(); break;
            case ConnectionHistoryAction.SessionDeletionCancelled: State = State with { SessionDeletionPrompt = null }; break;
            case ConnectionHistoryAction.SessionDeletionFailureDismissed: State = State with { SessionDeletionFailureKey = null }; break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>指定セッションのユーザー操作停止確認を準備します。</summary>
    /// <remarks>責務: 1件の確認可能なセッションIDを観測済み終了理由を含む確認表示へ変換します。</remarks>
    /// <param name="sessionID">確認対象の接続セッションID。</param>
    private void PrepareStopReview(ConnectionSessionID sessionID)
    {
        if (reviewInterruptedSession is null)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == sessionID);
        if (session is null || !session.NeedsStopReview || session.EndReason is not { } reason)
        {
            return;
        }
        State = State with { StopReviewFailureKey = null };
        State = State with { StopReviewPrompt = new ConnectionSessionStopReviewPrompt(sessionID, reason) };
    }

    /// <summary>表示中の停止確認を正式データ扱いとして保存します。</summary>
    /// <remarks>責務: 現在の停止確認内容をユーザー操作停止の永続化と履歴再読込へ変換します。</remarks>
    private void ConfirmStopReview()
    {
        if (State.StopReviewPrompt is not { } prompt)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == prompt.SessionID);
        if (session is null || reviewInterruptedSession is null)
        {
            return;
        }
        try
        {
            _ = reviewInterruptedSession.Execute(session);
            State = State with { StopReviewPrompt = null };
            LoadSessions();
        }
        catch (Exception exception)
        {
            ConnectionHistoryLog.Failed(exception, "stop-review.save");
            State = State with { StopReviewPrompt = null };
            State = State with { StopReviewFailureKey = "history.stop_review.error" };
        }
    }

    /// <summary>セッション一覧の絞り込み条件を標準値へ戻します。</summary>
    /// <remarks>責務: 現在の履歴状態から日付範囲と終了理由条件だけを初期化します。</remarks>
    private void ResetFilters()
    {
        State = State with { FilterStartDate = null };
        State = State with { FilterEndDate = null };
        State = State with { EndReasonFilter = ConnectionHistoryEndReasonFilter.All };
    }

    /// <summary>新しいアカウントへ履歴読込範囲を切り替えます。</summary>
    /// <remarks>責務: 1件の認証識別子変更を履歴状態の初期化と再読込へ反映します。</remarks>
    /// <param name="identifier">新しいAppleアカウント識別子。</param>
    private void ActivateAccount(string? identifier)
    {
        if (string.Equals(identifier, accountIdentifier, StringComparison.Ordinal))
        {
            return;
        }
        accountIdentifier = identifier;
        syncTask?.Cancel();
        syncTask = null;
        uploadTask?.Cancel();
        uploadTask = null;
        deletionTask?.Cancel();
        deletionTask = null;
        unchecked
        {
            syncGeneration++;
            uploadGeneration++;
        }
        State = new ConnectionHistoryState();
        if (string.IsNullOrEmpty(identifier))
        {
            return;
        }
        RefreshSessions();
    }

    /// <summary>指定セッションの全端末物理削除確認を準備します。</summary>
    /// <remarks>責務: 1件の終了済みセッションIDを全端末削除警告の表示状態へ変換します。</remarks>
    /// <param name="sessionID">削除候補の接続セッションID。</param>
    private void PrepareSessionDeletion(ConnectionSessionID sessionID)
    {
        if (deleteSessionEverywhere is null || State.DeletingSessionID is not null)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == sessionID);
        if (session is null || session.EndedAt is null)
        {
            return;
        }
        State = State with
        {
            SessionDeletionPrompt = new ConnectionSessionDeletionPrompt(
                sessionID,
                session.RawLogSummary.RecordCount,
                session.RawLogSummary.ByteCount),
        };
    }

    /// <summary>確認済みセッションを全端末削除処理へ渡します。</summary>
    /// <remarks>責務: 現在の削除確認状態をCloudKit削除伝播とローカル物理削除へ変換します。</remarks>
    private void ConfirmSessionDeletion()
    {
        if (State.SessionDeletionPrompt is not { } prompt)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == prompt.SessionID);
        if (session is null || deleteSessionEverywhere is not { } deleteUseCase || State.DeletingSessionID is not null)
        {
            return;
        }
        State = State with { SessionDeletionPrompt = null };
        State = State with { SessionDeletionFailureKey = null };
        State = State with { DeletingSessionID = session.Id };
        var precedingSyncTask = syncTask;
        var precedingUploadTask = uploadTask;
        syncTask?.Cancel();
        syncTask = null;
        uploadTask?.Cancel();
        uploadTask = null;
        unchecked
        {
            syncGeneration++;
            uploadGeneration++;
        }
        State = State with { SyncPhase = ConnectionHistorySyncPhase.Idle };
        State = State with { UploadingSessionID = null };
        State = State with { UploadProgress = null };
        var handle = new RunningTask();
        deletionTask = handle;
        handle.Start(async _ =>
        {
            if (precedingSyncTask is not null)
            {
                await precedingSyncTask.Completion;
            }
            if (precedingUploadTask is not null)
            {
                await precedingUploadTask.Completion;
            }
            try
            {
                await deleteUseCase.Execute(session);
                State = State with { DeletingSessionID = null };
                LoadSessions();
            }
            catch (OperationCanceledException)
            {
                ConnectionHistoryLog.Canceled("session.delete");
                return;
            }
            catch (Exception exception)
            {
                ConnectionHistoryLog.Failed(exception, "session.delete");
                State = State with { DeletingSessionID = null };
                State = State with { SessionDeletionFailureKey = "history.delete.error" };
            }
        });
    }

    /// <summary>ローカル履歴を即時反映してからCloudKit同期を開始します。</summary>
    /// <remarks>責務: 現在アカウントの更新要求をローカル表示と最新世代の端末間同期へ変換します。</remarks>
    private void RefreshSessions()
    {
        LoadSessions();
        if (accountIdentifier is not { } account || synchronizeSessions is not { } synchronize)
        {
            return;
        }
        if (syncTask is not null)
        {
            return;
        }
        unchecked
        {
            syncGeneration++;
        }
        var generation = syncGeneration;
        var evict = evictStaleRawLogs;
        State = State with { SyncPhase = ConnectionHistorySyncPhase.Syncing };
        var handle = new RunningTask();
        syncTask = handle;
        handle.Start(async _ =>
        {
            try
            {
                await synchronize.Execute(account, uploadsPendingSessions: false);
                evict?.Execute(account);
                if (syncGeneration != generation || !string.Equals(accountIdentifier, account, StringComparison.Ordinal))
                {
                    return;
                }
                syncTask = null;
                LoadSessions();
                State = State with { SyncPhase = ConnectionHistorySyncPhase.Synchronized };
            }
            catch (OperationCanceledException)
            {
                ConnectionHistoryLog.Canceled("sessions.synchronize");
                return;
            }
            catch (Exception exception)
            {
                ConnectionHistoryLog.Failed(exception, "sessions.synchronize");
                if (syncGeneration != generation || !string.Equals(accountIdentifier, account, StringComparison.Ordinal))
                {
                    return;
                }
                syncTask = null;
                State = State with { SyncPhase = ConnectionHistorySyncPhase.Failed };
            }
        });
    }

    /// <summary>指定された未送信セッションのiCloudアップロードを開始します。</summary>
    /// <remarks>責務: 1件の送信可能な終了済みセッションIDを重複しない個別CloudKit転送へ変換します。</remarks>
    /// <param name="sessionID">アップロード対象の接続セッションID。</param>
    private void UploadSession(ConnectionSessionID sessionID)
    {
        if (accountIdentifier is not { } account || synchronizeSessions is not { } synchronize || uploadTask is not null)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == sessionID);
        if (session is null
            || session.EndedAt is null
            || session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed
            || session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded)
        {
            return;
        }
        unchecked
        {
            uploadGeneration++;
        }
        var generation = uploadGeneration;
        State = State with { UploadingSessionID = sessionID };
        State = State with { UploadProgress = 0 };
        State = State with { UploadFailureSessionID = null };
        var handle = new RunningTask();
        uploadTask = handle;
        handle.Start(async _ =>
        {
            try
            {
                await synchronize.Upload(
                    sessionID,
                    account,
                    progress =>
                    {
                        if (uploadGeneration != generation || !string.Equals(accountIdentifier, account, StringComparison.Ordinal))
                        {
                            return;
                        }
                        State = State with { UploadProgress = Math.Min(Math.Max(progress, 0), 1) };
                    });
                if (uploadGeneration != generation || !string.Equals(accountIdentifier, account, StringComparison.Ordinal))
                {
                    return;
                }
                uploadTask = null;
                State = State with { UploadingSessionID = null };
                State = State with { UploadProgress = null };
                LoadSessions();
            }
            catch (OperationCanceledException)
            {
                ConnectionHistoryLog.Canceled("session.upload");
                return;
            }
            catch (Exception exception)
            {
                ConnectionHistoryLog.Failed(exception, "session.upload");
                if (uploadGeneration != generation || !string.Equals(accountIdentifier, account, StringComparison.Ordinal))
                {
                    return;
                }
                uploadTask = null;
                State = State with { UploadingSessionID = null };
                State = State with { UploadProgress = null };
                State = State with { UploadFailureSessionID = sessionID };
                LoadSessions();
            }
        });
    }

    /// <summary>指定セッションのローカルRawログ除去確認を準備します。</summary>
    /// <remarks>責務: 1件のセッションIDをiCloud保管証跡付きの端末容量解放確認状態へ変換します。</remarks>
    /// <param name="sessionID">除去候補の接続セッションID。</param>
    private void PrepareRawRemoval(ConnectionSessionID sessionID)
    {
        if (releaseRawCache is null)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == sessionID);
        if (session is null)
        {
            return;
        }
        var decision = releaseRawCache.Decision(session);
        if (decision == ConnectionSessionLocalRemovalDecision.Unavailable)
        {
            return;
        }
        State = State with
        {
            RawRemovalPrompt = new ConnectionSessionRawRemovalPrompt(
                sessionID,
                decision,
                session.RawLogSummary.RecordCount,
                session.RawLogSummary.ByteCount),
        };
    }

    /// <summary>ユーザー確認済みのRawログを現在端末から除去します。</summary>
    /// <remarks>責務: 現在の除去確認状態をローカルRawログ削除と履歴再読込へ変換します。</remarks>
    private void ConfirmRawRemoval()
    {
        if (State.RawRemovalPrompt is not { } prompt)
        {
            return;
        }
        var session = State.Sessions.FirstOrDefault(candidate => candidate.Id == prompt.SessionID);
        if (session is null || releaseRawCache is null)
        {
            return;
        }
        try
        {
            releaseRawCache.Execute(session);
            State = State with { RawRemovalPrompt = null };
            LoadSessions();
        }
        catch (Exception exception)
        {
            ConnectionHistoryLog.Failed(exception, "raw-cache.release");
            State = State with { FailureKey = "history.error.local_removal" };
        }
    }

    /// <summary>現在アカウントの接続履歴を保存先から読み込みます。</summary>
    /// <remarks>責務: 現在の1件のアカウントスコープへ接続履歴照会結果を反映します。</remarks>
    private void LoadSessions()
    {
        if (accountIdentifier is not { } account)
        {
            return;
        }
        State = State with { Phase = ConnectionHistoryStatePhase.Loading };
        try
        {
            var sessions = repository.Sessions(account);
            State = State with { Sessions = sessions.ToArray() };
            State = State with { Phase = ConnectionHistoryStatePhase.Loaded };
            State = State with { FailureKey = null };
        }
        catch (Exception exception)
        {
            ConnectionHistoryLog.Failed(exception, "sessions.load");
            State = State with { Phase = ConnectionHistoryStatePhase.Failed };
            State = State with { FailureKey = "history.error.storage" };
        }
    }

    /// <summary>値が変わったときだけフィールドを更新して変更を通知します。</summary>
    /// <remarks>責務: 1件の観測対象プロパティの更新を <see cref="PropertyChanged"/> 通知へ変換します。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">通知するプロパティ名。</param>
    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>保持してキャンセルできる進行中の非同期処理です。</summary>
    /// <remarks>Swift の保持された <c>Task</c> に対応します。ポートに CancellationToken が無いため、キャンセルは世代の比較と <see cref="OperationCanceledException"/> で表します。</remarks>
    private sealed class RunningTask
    {
        /// <summary>処理のキャンセル要求元です。</summary>
        private readonly CancellationTokenSource cancellation = new();

        /// <summary>処理の完了を待つためのタスクです。</summary>
        public Task Completion { get; private set; } = Task.CompletedTask;

        /// <summary>処理を開始して完了タスクを保持します。</summary>
        /// <remarks>責務: 1件の非同期処理をキャンセルトークン付きで開始します。</remarks>
        /// <param name="operation">開始する非同期処理。</param>
        public void Start(Func<CancellationToken, Task> operation)
        {
            Completion = operation(cancellation.Token);
        }

        /// <summary>処理へキャンセルを要求します。</summary>
        /// <remarks>責務: 保持中のキャンセル要求元を取り消し状態にします。</remarks>
        public void Cancel()
        {
            cancellation.Cancel();
        }
    }
}
