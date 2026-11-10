using ProjectZD8.Application.LogHistory.Actions;
using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Application.LogHistory.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.LogHistory.State;

/// <summary>アカウント単位の接続履歴表示状態を検証します。</summary>
public sealed class ConnectionHistoryModelTests
{
    /// <summary>全終了理由と並び順がString Catalogの固定キーへ対応します。</summary>
    /// <remarks>責務: 動的補間キーを使わず履歴表示の全選択肢を登録済み固定キーへ変換できることを確認します。</remarks>
    [Fact]
    public void HistoryLocalizationKeysAreFixedCatalogEntries()
    {
        Assert.Equal(
            [
                "history.reason.all",
                "history.reason.userDisconnected",
                "history.reason.vehicleNoResponse",
                "history.reason.connectionLost",
                "history.reason.acquisitionFailed",
                "history.reason.superseded",
                "history.reason.accountSignedOut",
                "history.reason.unexpectedTermination",
            ],
            ConnectionHistoryEndReasonFilterRawValue.AllCases.Select(filter => filter.HistoryLocalizationKey()));
        Assert.Equal(
            [
                "history.sort.newest",
                "history.sort.oldest",
                "history.sort.longestDuration",
                "history.sort.shortestDuration",
            ],
            ConnectionHistorySortOrderRawValue.AllCases.Select(order => order.HistoryLocalizationKey()));
        Assert.Equal(
            "history.reason.unexpectedTermination",
            ConnectionSessionEndReason.UnexpectedTermination.HistoryLocalizationKey());
    }

    /// <summary>有効化したアカウントの履歴だけを新しい順で公開します。</summary>
    /// <remarks>責務: 1件のアカウント変更が対応する履歴一覧へ変換されることを確認します。</remarks>
    [Fact]
    public void AccountActivationLoadsScopedSessions()
    {
        var older = new ConnectionSession("account", startedAt: Unix(10));
        var newer = new ConnectionSession("account", startedAt: Unix(20));
        var repository = new FixedConnectionSessionRepository([newer, older]);
        var model = new ConnectionHistoryModel(repository);

        model.Send(new ConnectionHistoryAction.AccountIdentifierChanged("account"));

        Assert.Equal(ConnectionHistoryStatePhase.Loaded, model.State.Phase);
        Assert.Equal([newer.Id, older.Id], model.State.Sessions.Select(session => session.Id));
        Assert.Equal(2, model.State.ActiveSessions.Count);
    }

    /// <summary>実行中の同期へ重複更新要求が届いても同じ同期を再起動しません。</summary>
    /// <remarks>責務: 長時間Raw取込中の画面再表示が進行中Taskをキャンセルしないことを確認します。</remarks>
    [Fact]
    public async Task RepeatedRefreshKeepsSingleSynchronizationInFlight()
    {
        var repository = new MutableConnectionSessionRepository([]);
        var transferRepository = new PausingConnectionSessionTransferRepository();
        var synchronization = new SynchronizeConnectionSessionsUseCase(
            sessionRepository: repository,
            rawLogRepository: new EmptyConnectionSessionRawLogRepository(),
            sessionErasureRepository: new NoOpConnectionSessionErasureRepository(),
            transferRepository: transferRepository,
            role: ConnectionSessionSyncDeviceRole.IPhone);
        var model = new ConnectionHistoryModel(repository, synchronizeSessions: synchronization);

        model.Send(new ConnectionHistoryAction.AccountIdentifierChanged("account"));
        await transferRepository.WaitForDeletionRequest();
        model.Send(new ConnectionHistoryAction.RefreshRequested());
        model.Send(new ConnectionHistoryAction.RefreshRequested());
        await Task.Yield();

        Assert.Equal(1, transferRepository.DeletionRequestCount);
        transferRepository.ResumeDeletionRequest();
    }

    /// <summary>個別アップロード中はCloudKit Assetの実進捗を履歴状態へ反映します。</summary>
    /// <remarks>責務: 手動アップロードの中間進捗を百分率表示可能な履歴状態として保持することを確認します。</remarks>
    [Fact]
    public async Task ManualUploadPublishesTransferProgress()
    {
        var session = MakeClosedSession(
            startedAt: 100,
            duration: 60,
            reason: ConnectionSessionEndReason.UserDisconnected,
            vehicle: new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8")) with
        {
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 1,
                localState: ConnectionSessionLocalRawState.Available,
                cloudState: ConnectionSessionCloudSyncState.Pending,
                manifestDigest: null,
                macImportReceipt: null),
        };
        var repository = new MutableConnectionSessionRepository([session]);
        var transferRepository = new PausingConnectionSessionTransferRepository();
        var synchronization = new SynchronizeConnectionSessionsUseCase(
            sessionRepository: repository,
            rawLogRepository: new EmptyConnectionSessionRawLogRepository(),
            sessionErasureRepository: new NoOpConnectionSessionErasureRepository(),
            transferRepository: transferRepository,
            role: ConnectionSessionSyncDeviceRole.IPhone);
        var model = new ConnectionHistoryModel(repository, synchronizeSessions: synchronization);
        model.Send(new ConnectionHistoryAction.AccountIdentifierChanged("account"));
        await transferRepository.WaitForDeletionRequest();

        model.Send(new ConnectionHistoryAction.SessionUploadRequested(session.Id));
        await transferRepository.WaitForUploadProgress();

        Assert.Equal(session.Id, model.State.UploadingSessionID);
        Assert.Equal(0.42, model.State.UploadProgress ?? double.NegativeInfinity, 0.0001);

        transferRepository.ResumeUpload();
        transferRepository.ResumeDeletionRequest();
    }

    /// <summary>自動アップロードが有効でも失敗したセッションだけは手動再試行可能です。</summary>
    /// <remarks>責務: 自動アップロード設定とCloudKit転送状態を履歴画面の手動再試行可否へ変換できることを確認します。</remarks>
    [Fact]
    public void FailedUploadRemainsManuallyAvailableWhenAutomaticUploadIsEnabled()
    {
        var failed = MakeClosedSession(
            startedAt: 100,
            duration: 60,
            reason: ConnectionSessionEndReason.UserDisconnected,
            vehicle: new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8")) with
        {
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 1,
                localState: ConnectionSessionLocalRawState.Available,
                cloudState: ConnectionSessionCloudSyncState.Failed,
                manifestDigest: null,
                macImportReceipt: null),
        };
        var pending = failed with
        {
            RawLogSummary = failed.RawLogSummary with { CloudState = ConnectionSessionCloudSyncState.Pending },
        };
        var state = new ConnectionHistoryState { AutomaticUploadEnabled = true };

        Assert.True(state.IsManualUploadAvailable(failed));
        Assert.False(state.IsManualUploadAvailable(pending));

        state = state with { AutomaticUploadEnabled = false };

        Assert.True(state.IsManualUploadAvailable(pending));
    }

    /// <summary>終了済み履歴が登録車両単位に集約され、中断件数と総時間を保持することを検証します。</summary>
    /// <remarks>責務: 複数セッションを車両ID単位の履歴集計へ変換できることを確認します。</remarks>
    [Fact]
    public void ClosedSessionsAreGroupedByVehicleWithAttentionSummary()
    {
        var vehicleID = new VehicleID(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var vehicle = new ConnectionSessionVehicle(vehicleID, "BRZ", "ZD8");
        var completed = MakeClosedSession(100, 600, ConnectionSessionEndReason.UserDisconnected, vehicle);
        var completedWithDistance = completed with
        {
            StartingOdometerKilometers = 1_000,
            EndingOdometerKilometers = 1_001.2,
        };
        var interrupted = MakeClosedSession(200, 120, ConnectionSessionEndReason.VehicleNoResponse, vehicle) with
        {
            StartingOdometerKilometers = 1_001.2,
            EndingOdometerKilometers = 1_001.5,
        };
        var state = new ConnectionHistoryState
        {
            Phase = ConnectionHistoryStatePhase.Loaded,
            Sessions = [interrupted, completedWithDistance],
        };

        var group = state.VehicleGroups.FirstOrDefault();

        Assert.Single(state.VehicleGroups);
        Assert.Equal(2, group?.SessionCount);
        Assert.Equal(1, group?.InterruptedCount);
        Assert.Equal(720, group?.TotalDuration);
        Assert.Equal(1.5, group?.TotalDistanceKilometers ?? double.NegativeInfinity, 0.0001);
        Assert.Equal(1, state.InterruptedCount);
    }

    /// <summary>走行距離を確定できないセッションを含む車両集計を非対応として保持します。</summary>
    /// <remarks>責務: 部分的なA6対応履歴を不完全な総走行距離として合算しないことを確認します。</remarks>
    [Fact]
    public void VehicleDistanceIsUnsupportedWhenAnySessionLacksOdometerBounds()
    {
        var vehicle = new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8");
        var supported = MakeClosedSession(100, 60, ConnectionSessionEndReason.UserDisconnected, vehicle) with
        {
            StartingOdometerKilometers = 500,
            EndingOdometerKilometers = 500.4,
        };
        var unsupported = MakeClosedSession(200, 60, ConnectionSessionEndReason.UserDisconnected, vehicle);
        var state = new ConnectionHistoryState
        {
            Phase = ConnectionHistoryStatePhase.Loaded,
            Sessions = [unsupported, supported],
        };

        Assert.Null(state.VehicleGroups.FirstOrDefault()?.TotalDistanceKilometers);
    }

    /// <summary>日付範囲と終了理由を同時適用して該当セッションだけを残すことを検証します。</summary>
    /// <remarks>責務: 複数の絞り込み条件が論理積として適用されることを確認します。</remarks>
    [Fact]
    public void DateAndEndReasonFiltersCompose()
    {
        var vehicle = new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8");
        var normal = MakeClosedSession(100_000, 300, ConnectionSessionEndReason.UserDisconnected, vehicle);
        var matching = MakeClosedSession(200_000, 900, ConnectionSessionEndReason.VehicleNoResponse, vehicle);
        var tooLate = MakeClosedSession(400_000, 600, ConnectionSessionEndReason.VehicleNoResponse, vehicle);
        var state = new ConnectionHistoryState
        {
            Phase = ConnectionHistoryStatePhase.Loaded,
            Sessions = [tooLate, matching, normal],
        };
        state = state with
        {
            FilterStartDate = Unix(150_000),
            FilterEndDate = Unix(250_000),
            EndReasonFilter = ConnectionHistoryEndReasonFilter.VehicleNoResponse,
        };

        var result = state.FilteredSessions(new ConnectionHistoryVehicleGroupID.Registered(vehicle.Id));

        Assert.Equal([matching.Id], result.Select(session => session.Id));
    }

    /// <summary>走行時間による並び順が日時順と独立して適用されることを検証します。</summary>
    /// <remarks>責務: 車両別履歴を記録時間の降順へ並べ替えられることを確認します。</remarks>
    [Fact]
    public void DurationSortOrdersLongestFirst()
    {
        var vehicle = new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8");
        var shortSession = MakeClosedSession(300, 60, ConnectionSessionEndReason.UserDisconnected, vehicle);
        var longSession = MakeClosedSession(100, 900, ConnectionSessionEndReason.UserDisconnected, vehicle);
        var state = new ConnectionHistoryState
        {
            Phase = ConnectionHistoryStatePhase.Loaded,
            Sessions = [shortSession, longSession],
        };
        state = state with { SortOrder = ConnectionHistorySortOrder.LongestDuration };

        Assert.Equal(
            [longSession.Id, shortSession.Id],
            state.FilteredSessions(new ConnectionHistoryVehicleGroupID.Registered(vehicle.Id)).Select(session => session.Id));
    }

    /// <summary>リセット操作が日付と終了理由だけを初期化し、選択中の並び順を維持することを検証します。</summary>
    /// <remarks>責務: 1件の絞り込みリセット操作を期待する履歴状態へ反映できることを確認します。</remarks>
    [Fact]
    public void FilterResetPreservesSortOrder()
    {
        var state = new ConnectionHistoryState
        {
            FilterStartDate = Unix(100),
            FilterEndDate = Unix(200),
            EndReasonFilter = ConnectionHistoryEndReasonFilter.ConnectionLost,
            SortOrder = ConnectionHistorySortOrder.Oldest,
        };
        var model = new ConnectionHistoryModel(state, new FixedConnectionSessionRepository([]));

        model.Send(new ConnectionHistoryAction.FiltersReset());

        Assert.Null(model.State.FilterStartDate);
        Assert.Null(model.State.FilterEndDate);
        Assert.Equal(ConnectionHistoryEndReasonFilter.All, model.State.EndReasonFilter);
        Assert.Equal(ConnectionHistorySortOrder.Oldest, model.State.SortOrder);
    }

    /// <summary>ユーザーが意図した停止の確認後は警告件数から外し、観測理由を保持します。</summary>
    /// <remarks>責務: 接続喪失セッションの確認操作を正式データ状態と元終了理由の保持へ変換できることを確認します。</remarks>
    [Fact]
    public void StopReviewAcceptsUserInitiatedStopWithoutReplacingObservedReason()
    {
        var vehicle = new ConnectionSessionVehicle(VehicleID.New(), "BRZ", "ZD8");
        var session = MakeClosedSession(100, 60, ConnectionSessionEndReason.ConnectionLost, vehicle);
        var repository = new MutableConnectionSessionRepository([session]);
        var model = new ConnectionHistoryModel(
            repository,
            reviewInterruptedSession: new ReviewInterruptedConnectionSessionUseCase(repository));
        model.Send(new ConnectionHistoryAction.AccountIdentifierChanged("account"));

        model.Send(new ConnectionHistoryAction.StopReviewRequested(session.Id));
        Assert.Equal(ConnectionSessionEndReason.ConnectionLost, model.State.StopReviewPrompt?.ObservedReason);
        model.Send(new ConnectionHistoryAction.StopReviewConfirmed());

        var reviewed = model.State.Sessions.FirstOrDefault();
        Assert.Equal(ConnectionSessionEndReason.ConnectionLost, reviewed?.EndReason);
        Assert.Equal(ConnectionSessionStopReviewDecision.UserInitiated, reviewed?.StopReviewDecision);
        Assert.Equal(ConnectionSessionStatus.Completed, reviewed?.Status);
        Assert.Equal(0, model.State.InterruptedCount);
    }

    /// <summary>UNIX秒を日時へ変換します。</summary>
    /// <remarks>責務: Swift の <c>Date(timeIntervalSince1970:)</c> と同じ日時を作ります。</remarks>
    /// <param name="seconds">UNIX秒。</param>
    /// <returns>対応する日時。</returns>
    private static DateTimeOffset Unix(double seconds) => DateTimeOffset.UnixEpoch.AddSeconds(seconds);

    /// <summary>指定条件を持つ終了済みセッションを生成します。</summary>
    /// <remarks>責務: テスト入力を終了日時と終了理由が確定した1件のセッションへ変換します。</remarks>
    /// <param name="startedAt">Unix秒で表す開始日時。</param>
    /// <param name="duration">終了までの秒数。</param>
    /// <param name="reason">終了理由。</param>
    /// <param name="vehicle">関連付ける車両情報。</param>
    /// <returns>指定条件を保持する終了済みセッション。</returns>
    private static ConnectionSession MakeClosedSession(
        double startedAt,
        double duration,
        ConnectionSessionEndReason reason,
        ConnectionSessionVehicle vehicle)
    {
        var start = Unix(startedAt);
        return new ConnectionSession("account", startedAt: start, vehicle: vehicle) with
        {
            EndedAt = start.AddSeconds(duration),
            EndReason = reason,
        };
    }
}

/// <summary>削除マーカー取得を明示再開まで停止する同期テスト実装です。</summary>
file sealed class PausingConnectionSessionTransferRepository : IConnectionSessionTransferRepository
{
    /// <summary>停止中の削除マーカー取得を再開する継続です。</summary>
    private TaskCompletionSource<IReadOnlySet<ConnectionSessionID>>? deletionContinuation;
    /// <summary>停止中のアップロードを再開する継続です。</summary>
    private TaskCompletionSource<string>? uploadContinuation;
    /// <summary>中間アップロード進捗を通知済みかを示します。</summary>
    private bool didPublishUploadProgress;

    /// <summary>削除マーカー取得の呼出回数です。</summary>
    public int DeletionRequestCount { get; private set; }

    /// <summary>このテストではアップロードを固定Digestで受理します。</summary>
    /// <remarks>責務: テスト対象外の転送保存要求を副作用なしで満たします。</remarks>
    /// <param name="package">使用しない転送Payload。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>固定Digest。</returns>
    public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) =>
        Task.FromResult("digest");

    /// <summary>中間進捗を通知してテストから再開されるまでアップロードを停止します。</summary>
    /// <remarks>責務: 1件の進捗付き保存要求を観測可能な中間進捗で停止します。</remarks>
    /// <param name="package">使用しない転送Payload。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <param name="progress">中間進捗の通知先。</param>
    /// <returns>再開後の固定Digest。</returns>
    public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier, Action<double> progress)
    {
        progress(0.42);
        didPublishUploadProgress = true;
        uploadContinuation = new TaskCompletionSource<string>();
        return uploadContinuation.Task;
    }

    /// <summary>このテストでは転送Payloadを返しません。</summary>
    /// <remarks>責務: テスト対象外のCloudKit取得要求へ空配列を返します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空の転送配列。</returns>
    public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>([]);

    /// <summary>このテストではMac受領証公開を行いません。</summary>
    /// <remarks>責務: テスト対象外の受領証保存要求を副作用なしで満たします。</remarks>
    /// <param name="receipt">使用しないMac受領証。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
        Task.CompletedTask;

    /// <summary>このテストではMac受領証を返しません。</summary>
    /// <remarks>責務: テスト対象外の受領証取得要求へ空配列を返します。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空の受領証配列。</returns>
    public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
        string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([]);

    /// <summary>削除マーカー取得をテストから再開されるまで停止します。</summary>
    /// <remarks>責務: 1件の削除マーカー取得要求を進行中同期として観測可能にします。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>再開後の空セッションID集合。</returns>
    public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier)
    {
        DeletionRequestCount += 1;
        deletionContinuation = new TaskCompletionSource<IReadOnlySet<ConnectionSessionID>>();
        return deletionContinuation.Task;
    }

    /// <summary>このテストでは単一セッション削除を行いません。</summary>
    /// <remarks>責務: テスト対象外のCloudKit削除要求を副作用なしで満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) => Task.CompletedTask;

    /// <summary>このテストではアカウント全削除を行いません。</summary>
    /// <remarks>責務: テスト対象外のCloudKit全削除要求を副作用なしで満たします。</remarks>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>完了済みタスク。</returns>
    public Task DeleteAll(string accountIdentifier) => Task.CompletedTask;

    /// <summary>最初の削除マーカー取得開始まで待機します。</summary>
    /// <remarks>責務: テスト実行を同期Taskが停止点へ到達した時点まで進めます。</remarks>
    /// <returns>待機の完了。</returns>
    public async Task WaitForDeletionRequest()
    {
        while (DeletionRequestCount == 0)
        {
            await Task.Yield();
        }
    }

    /// <summary>停止中の削除マーカー取得へ空集合を返して再開します。</summary>
    /// <remarks>責務: 保持中の1件の同期継続を空の削除結果で完了します。</remarks>
    public void ResumeDeletionRequest()
    {
        deletionContinuation?.TrySetResult(new HashSet<ConnectionSessionID>());
        deletionContinuation = null;
    }

    /// <summary>中間アップロード進捗が通知されるまで待機します。</summary>
    /// <remarks>責務: テスト実行をアップロード進捗が履歴状態へ届く時点まで進めます。</remarks>
    /// <returns>待機の完了。</returns>
    public async Task WaitForUploadProgress()
    {
        while (!didPublishUploadProgress)
        {
            await Task.Yield();
        }
        await Task.Yield();
    }

    /// <summary>停止中のアップロードへ固定Digestを返して再開します。</summary>
    /// <remarks>責務: 保持中の1件のアップロード継続を成功として完了します。</remarks>
    public void ResumeUpload()
    {
        uploadContinuation?.TrySetResult("digest");
        uploadContinuation = null;
    }
}

/// <summary>Rawログを保持しない同期モデルテスト実装です。</summary>
file sealed class EmptyConnectionSessionRawLogRepository : IConnectionSessionRawLogRepository
{
    /// <summary>Raw応答追記を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外のRaw追記要求を満たします。</remarks>
    /// <param name="observation">使用しないRaw応答。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID)
    {
    }

    /// <summary>空のセッションRawログを返します。</summary>
    /// <remarks>責務: 任意セッションIDを空のRawログ配列へ変換します。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <returns>空配列。</returns>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) => [];

    /// <summary>空の車両別Rawログを返します。</summary>
    /// <remarks>責務: 任意車両スコープを空のRawログ配列へ変換します。</remarks>
    /// <param name="vehicleID">使用しない車両ID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    /// <returns>空配列。</returns>
    public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier) => [];

    /// <summary>CloudKit保存済み更新を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外のManifest保存要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="manifestDigest">使用しないDigest。</param>
    public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest)
    {
    }

    /// <summary>CloudKit失敗更新を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外の失敗保存要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void MarkCloudUploadFailed(ConnectionSessionID sessionID)
    {
    }

    /// <summary>Mac受領証更新を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外の受領証保存要求を満たします。</remarks>
    /// <param name="receipt">使用しない受領証。</param>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID)
    {
    }

    /// <summary>検証済み転送取込を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外の転送取込要求を満たします。</remarks>
    /// <param name="transfer">使用しない転送Payload。</param>
    public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer)
    {
    }

    /// <summary>ローカルRaw除去を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外のRaw除去要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    public void RemoveLocalEntries(ConnectionSessionID sessionID)
    {
    }
}

/// <summary>セッション削除を行わない同期モデルテスト実装です。</summary>
file sealed class NoOpConnectionSessionErasureRepository : IConnectionSessionErasureRepository
{
    /// <summary>ローカルセッション削除を副作用なしで受理します。</summary>
    /// <remarks>責務: テスト対象外の物理削除要求を満たします。</remarks>
    /// <param name="sessionID">使用しないセッションID。</param>
    /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
    public void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
    {
    }
}

/// <summary>保存内容を更新可能な接続履歴テスト実装です。</summary>
file sealed class MutableConnectionSessionRepository : IConnectionSessionRepository
{
    /// <summary>現在保持するセッション一覧です。</summary>
    private readonly List<ConnectionSession> storedSessions;

    /// <summary>初期セッション一覧を固定して生成します。</summary>
    /// <remarks>責務: 1件の固定セッション配列を更新可能なテスト保存先へ変換します。</remarks>
    /// <param name="sessions">初期状態として保持するセッション一覧。</param>
    public MutableConnectionSessionRepository(IReadOnlyList<ConnectionSession> sessions)
    {
        storedSessions = [.. sessions];
    }

    /// <summary>同じIDのセッションを現在内容で置き換えます。</summary>
    /// <remarks>責務: 1件のセッション保存要求をID単位のテスト状態更新へ変換します。</remarks>
    /// <param name="session">保存する接続セッション。</param>
    public void Save(ConnectionSession session)
    {
        storedSessions.RemoveAll(stored => stored.Id == session.Id);
        storedSessions.Add(session);
    }

    /// <summary>指定アカウントのセッションを新しい順で返します。</summary>
    /// <remarks>責務: 現在のテスト履歴を1件のアカウント識別子で絞り込みます。</remarks>
    /// <param name="accountIdentifier">取得対象のアカウント識別子。</param>
    /// <returns>開始日時が新しい順の該当セッション。</returns>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) =>
        storedSessions
            .Where(session => string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
            .OrderByDescending(session => session.StartedAt)
            .ToList();
}

/// <summary>テスト用の固定接続履歴を返します。</summary>
file sealed class FixedConnectionSessionRepository : IConnectionSessionRepository
{
    /// <summary>取得要求へ返す固定セッション一覧です。</summary>
    private readonly IReadOnlyList<ConnectionSession> sessions;

    /// <summary>固定セッション一覧を保持して生成します。</summary>
    /// <remarks>責務: 読込専用テスト実装へ返す履歴を固定します。</remarks>
    /// <param name="sessions">取得要求へ返す固定セッション一覧。</param>
    public FixedConnectionSessionRepository(IReadOnlyList<ConnectionSession> sessions)
    {
        this.sessions = sessions;
    }

    /// <summary>この読込専用テスト実装では保存を行いません。</summary>
    /// <remarks>責務: 保存要求を副作用なしで受理します。</remarks>
    /// <param name="session">使用しない接続セッション。</param>
    public void Save(ConnectionSession session)
    {
    }

    /// <summary>固定セッションから指定アカウント分を返します。</summary>
    /// <remarks>責務: 固定履歴を1件のアカウント識別子で絞り込みます。</remarks>
    /// <param name="accountIdentifier">取得対象のアカウント識別子。</param>
    /// <returns>指定アカウントに属する固定履歴。</returns>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) =>
        sessions.Where(session => string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal)).ToList();
}
