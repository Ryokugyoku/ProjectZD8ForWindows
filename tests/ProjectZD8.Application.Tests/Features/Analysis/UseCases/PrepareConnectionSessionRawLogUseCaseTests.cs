using ProjectZD8.Application.Analysis.Actions;
using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Application.Analysis.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Analysis.UseCases;

/// <summary>解析開始時のRawログオンデマンド復元を検証します。</summary>
/// <remarks>
/// Swift 版は Data 層の GRDB リポジトリ（メモリ上の SQLite）を使います。C# では Data 層が未移植のため、
/// テスト内の <see cref="InMemoryConnectionSessionRepository"/> が GRDB 実装の該当動作（概要取込、オンデマンド復元、閲覧日時記録、ローカル除去）を再現します。
/// </remarks>
public sealed class PrepareConnectionSessionRawLogUseCaseTests
{
    /// <summary>端末から退避済みのRawログをCloudKitから復元して最終閲覧日時を記録します。</summary>
    /// <remarks>責務: 解析要求が単一セッションのRawダウンロード、復元、閲覧記録を完了することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task DownloadsRemovedRawAndMarksAccessTime()
    {
        var local = new InMemoryConnectionSessionRepository();
        var session = new ConnectionSession("account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 2,
                localState: ConnectionSessionLocalRawState.Removed,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: "manifest",
                macImportReceipt: null),
        };
        local.ImportCloudMetadata(new ConnectionSessionCloudMetadata(session, "manifest"));
        var entry = new ConnectionSessionRawLogEntry(
            sequence: 0,
            observedAt: DateTimeOffset.FromUnixTimeSeconds(105),
            batchElapsedNanoseconds: 1,
            service: 0x01,
            pid: 0x0C,
            payload: new byte[] { 0x1A, 0xF8 });
        var transfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(session, new[] { entry }),
            "manifest");
        var cloud = new RawDownloadTransferRepository(transfer);
        var accessedAt = DateTimeOffset.FromUnixTimeSeconds(120);
        var useCase = new PrepareConnectionSessionRawLogUseCase(
            sessionRepository: local,
            rawLogRepository: local,
            transferRepository: cloud,
            now: () => accessedAt);
        var reportedProgress = new List<double>();

        await useCase.Execute(session, reportedProgress.Add);

        Assert.Equal(new[] { session.Id }, cloud.DownloadedSessionIDs);
        Assert.Equal(new double[] { 0, 1 }, reportedProgress);
        Assert.Equal(new[] { entry }, local.Entries(session.Id));
        var restored = Assert.IsType<ConnectionSession>(local.Sessions("account").FirstOrDefault());
        Assert.Equal(ConnectionSessionLocalRawState.Available, restored.RawLogSummary.LocalState);
        Assert.Equal(accessedAt, restored.RawLogSummary.LastAccessedAt);
    }

    /// <summary>CloudKit転送でミリ秒未満が丸められた日時でも同じRawログを復元します。</summary>
    /// <remarks>責務: 実端末日時とAsset日時の小数ミリ秒差がオンデマンド復元を妨げないことを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task DownloadsRawWhenTransferDatesAreRoundedToMilliseconds()
    {
        var local = new InMemoryConnectionSessionRepository();
        var localSession = new ConnectionSession(
            "account",
            startedAt: DateTimeOffset.UnixEpoch.AddSeconds(100.123_456)) with
        {
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(110.987_654),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 2,
                localState: ConnectionSessionLocalRawState.Removed,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: "manifest",
                macImportReceipt: null),
        };
        local.ImportCloudMetadata(new ConnectionSessionCloudMetadata(localSession, "manifest"));
        var transferredSession = new ConnectionSession(
            localSession.AccountIdentifier,
            id: localSession.Id,
            startedAt: DateTimeOffset.UnixEpoch.AddSeconds(100.123)) with
        {
            EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(110.987),
            EndReason = localSession.EndReason,
            RawLogSummary = localSession.RawLogSummary,
        };
        var entry = new ConnectionSessionRawLogEntry(
            sequence: 0,
            observedAt: DateTimeOffset.UnixEpoch.AddSeconds(105.456),
            batchElapsedNanoseconds: 1,
            service: 0x01,
            pid: 0x0C,
            payload: new byte[] { 0x1A, 0xF8 });
        var transfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(transferredSession, new[] { entry }),
            "manifest");
        var useCase = new PrepareConnectionSessionRawLogUseCase(
            sessionRepository: local,
            rawLogRepository: local,
            transferRepository: new RawDownloadTransferRepository(transfer));
        var persistedBeforeRestore = Assert.IsType<ConnectionSession>(local.Sessions("account").FirstOrDefault());
        Assert.True(Math.Abs((persistedBeforeRestore.StartedAt - transferredSession.StartedAt).TotalSeconds) <= 0.001_001);
        Assert.True(
            Math.Abs((persistedBeforeRestore.EndedAt!.Value - transferredSession.EndedAt!.Value).TotalSeconds)
            <= 0.001_001);

        await useCase.Execute(localSession);

        Assert.Equal(new[] { entry }, local.Entries(localSession.Id));
        var restored = Assert.IsType<ConnectionSession>(local.Sessions("account").FirstOrDefault());
        Assert.Equal(ConnectionSessionLocalRawState.Available, restored.RawLogSummary.LocalState);
        Assert.Equal(persistedBeforeRestore.StartedAt, restored.StartedAt);
        Assert.Equal(persistedBeforeRestore.EndedAt, restored.EndedAt);
    }

    /// <summary>詳細画面と一覧概要が古くてもセッションIDに紐づく検証済みRawログを復元します。</summary>
    /// <remarks>責務: ローカル削除後の古い画面状態とManifestをセッションID基準のオンデマンド復元成功へ変換できることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task DownloadsRawBySessionIDWhenLocalSnapshotAndManifestAreStale()
    {
        var local = new InMemoryConnectionSessionRepository();
        var session = new ConnectionSession("account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        var entry = new ConnectionSessionRawLogEntry(
            sequence: 0,
            observedAt: DateTimeOffset.FromUnixTimeSeconds(105),
            batchElapsedNanoseconds: 1,
            service: 0x01,
            pid: 0x0C,
            payload: new byte[] { 0x1A, 0xF8 });
        var uploadedTransfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(session, new[] { entry }),
            "stale-local-manifest");
        local.Save(session);
        local.ImportVerifiedTransfer(uploadedTransfer);
        var staleSession = Assert.IsType<ConnectionSession>(local.Sessions("account").FirstOrDefault());
        local.RemoveLocalEntries(session.Id);
        var removedSession = Assert.IsType<ConnectionSession>(local.Sessions("account").FirstOrDefault());
        Assert.Equal(ConnectionSessionLocalRawState.Available, staleSession.RawLogSummary.LocalState);
        Assert.Equal(ConnectionSessionLocalRawState.Removed, removedSession.RawLogSummary.LocalState);
        var archivedSession = session with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(111),
            EndReason = ConnectionSessionEndReason.ConnectionLost,
        };
        var currentCloudTransfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(archivedSession, new[] { entry }),
            "current-cloud-manifest");
        var cloud = new RawDownloadTransferRepository(currentCloudTransfer);
        var useCase = new PrepareConnectionSessionRawLogUseCase(
            sessionRepository: local,
            rawLogRepository: local,
            transferRepository: cloud);

        await useCase.Execute(staleSession);

        Assert.Equal(new[] { session.Id }, cloud.DownloadedSessionIDs);
        Assert.Equal(new[] { entry }, local.Entries(session.Id));
        var restored = Assert.IsType<ConnectionSession>(local.Sessions("account").FirstOrDefault());
        Assert.Equal(ConnectionSessionLocalRawState.Available, restored.RawLogSummary.LocalState);
        Assert.Equal("current-cloud-manifest", restored.RawLogSummary.ManifestDigest);
        Assert.Equal(session.EndedAt, restored.EndedAt);
        Assert.Equal(session.EndReason, restored.EndReason);
    }

    /// <summary>未ダウンロードセッションの選択時は容量付き確認を公開しCloudKitへ接続しません。</summary>
    /// <remarks>責務: PID時系列操作を副作用前のRaw取得確認状態へ変換することを確認します。</remarks>
    [Fact]
    public void SelectionWaitsForDownloadConfirmationAndCancellationStaysLocal()
    {
        using var mainThread = HeldSynchronizationContext.Install();
        var local = new InMemoryConnectionSessionRepository();
        var session = new ConnectionSession("account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 10,
                byteCount: 2_048,
                localState: ConnectionSessionLocalRawState.Removed,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: "manifest",
                macImportReceipt: null),
        };
        var transfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(session, Array.Empty<ConnectionSessionRawLogEntry>()),
            "manifest");
        var cloud = new RawDownloadTransferRepository(transfer);
        var model = new SessionLogAnalysisModel(
            state: new SessionLogAnalysisState(),
            decodeTimeline: new DecodeSessionLogTimelineUseCase(
                rawLogRepository: local,
                definitionRepository: new EmptyDefinitionRepository()),
            prepareRawLog: new PrepareConnectionSessionRawLogUseCase(
                sessionRepository: local,
                rawLogRepository: local,
                transferRepository: cloud));

        model.Send(new SessionLogAnalysisAction.SessionSelected(session));

        Assert.Equal(SessionLogAnalysisStatePhase.AwaitingDownloadConfirmation, model.State.Phase);
        Assert.Equal(session.Id, model.State.DownloadPrompt?.SessionID);
        Assert.Equal(2_048L, model.State.DownloadPrompt?.ByteCount);
        Assert.Empty(cloud.DownloadedSessionIDs);

        model.Send(new SessionLogAnalysisAction.DownloadCancelled());

        Assert.Equal(new SessionLogAnalysisState(), model.State);
        Assert.Empty(cloud.DownloadedSessionIDs);
    }

    /// <summary>ユーザー確認後はiCloud取得進捗段階へ遷移します。</summary>
    /// <remarks>責務: Raw取得確認操作を0パーセントから始まるダウンロード状態へ変換することを確認します。</remarks>
    [Fact]
    public void ConfirmationStartsDownloadProgress()
    {
        using var mainThread = HeldSynchronizationContext.Install();
        var local = new InMemoryConnectionSessionRepository();
        var session = new ConnectionSession("account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(110),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: 1,
                byteCount: 512,
                localState: ConnectionSessionLocalRawState.Removed,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: "manifest",
                macImportReceipt: null),
        };
        var transfer = new VerifiedConnectionSessionTransfer(
            new ConnectionSessionTransferPackage(session, Array.Empty<ConnectionSessionRawLogEntry>()),
            "manifest");
        var cloud = new RawDownloadTransferRepository(transfer);
        var model = new SessionLogAnalysisModel(
            state: new SessionLogAnalysisState(),
            decodeTimeline: new DecodeSessionLogTimelineUseCase(
                rawLogRepository: local,
                definitionRepository: new EmptyDefinitionRepository()),
            prepareRawLog: new PrepareConnectionSessionRawLogUseCase(
                sessionRepository: local,
                rawLogRepository: local,
                transferRepository: cloud));
        model.Send(new SessionLogAnalysisAction.SessionSelected(session));

        model.Send(new SessionLogAnalysisAction.DownloadConfirmed());

        Assert.Equal(SessionLogAnalysisStatePhase.Downloading, model.State.Phase);
        Assert.Equal(0, model.State.DownloadProgress);
        Assert.Null(model.State.DownloadPrompt);
    }

    /// <summary>Swift の MainActor のように、テスト中は投入された続きを実行しない同期コンテキストです。</summary>
    /// <remarks>
    /// Swift のテストは MainActor 上で同期的に検証するため、モデルが開始した Task はテストが待機するまで進みません。
    /// C# でも同じ順序で検証できるよう、<see cref="Task.Yield"/> 後の続きを保留したままにします。
    /// </remarks>
    private sealed class HeldSynchronizationContext : SynchronizationContext, IDisposable
    {
        /// <summary>設定前の同期コンテキストです。</summary>
        private readonly SynchronizationContext? _previous;

        /// <summary>設定前の同期コンテキストを保持して生成します。</summary>
        /// <remarks>責務: 破棄時に戻す同期コンテキストを記録します。</remarks>
        /// <param name="previous">設定前の同期コンテキスト。</param>
        private HeldSynchronizationContext(SynchronizationContext? previous) => _previous = previous;

        /// <summary>現在のスレッドへ保留用の同期コンテキストを設定します。</summary>
        /// <remarks>責務: テスト中だけ非同期処理の続きを保留する環境を作ります。</remarks>
        /// <returns>破棄すると元の同期コンテキストへ戻すオブジェクト。</returns>
        public static HeldSynchronizationContext Install()
        {
            var context = new HeldSynchronizationContext(Current);
            SetSynchronizationContext(context);
            return context;
        }

        /// <summary>投入された続きを実行せずに保留します。</summary>
        /// <remarks>責務: テストの同期検証中にモデルの非同期処理が進まないようにします。</remarks>
        /// <param name="d">保留する処理。</param>
        /// <param name="state">処理へ渡す状態。</param>
        public override void Post(SendOrPostCallback d, object? state)
        {
        }

        /// <summary>元の同期コンテキストへ戻します。</summary>
        /// <remarks>責務: テスト終了時にスレッドの同期コンテキストを復元します。</remarks>
        public void Dispose() => SetSynchronizationContext(_previous);
    }

    /// <summary>GRDB版接続セッションリポジトリの該当動作をメモリ上で再現します。</summary>
    private sealed class InMemoryConnectionSessionRepository : IConnectionSessionRepository, IConnectionSessionRawLogRepository
    {
        /// <summary>セッションIDごとの保存済みセッションです。</summary>
        private readonly Dictionary<ConnectionSessionID, ConnectionSession> _sessions = [];
        /// <summary>セッションIDごとの保存済みRawログです。</summary>
        private readonly Dictionary<ConnectionSessionID, List<ConnectionSessionRawLogEntry>> _entries = [];

        /// <summary>セッションを保存し、既存のRaw保管状態は保持します。</summary>
        /// <remarks>責務: GRDB実装と同じく既存セッションのRaw概要を上書きせずに保存します。</remarks>
        /// <param name="session">保存するセッション。</param>
        public void Save(ConnectionSession session)
        {
            if (_sessions.TryGetValue(session.Id, out var existing))
            {
                session = session with { RawLogSummary = existing.RawLogSummary };
            }
            _sessions[session.Id] = session;
        }

        /// <summary>指定アカウントのセッションを開始日時の降順で返します。</summary>
        /// <remarks>責務: GRDB実装と同じくローカルRawがある場合は実レコードから件数と保管状態を補正します。</remarks>
        /// <param name="accountIdentifier">取得対象のアカウント識別子。</param>
        /// <returns>開始日時が新しい順のセッション。</returns>
        public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) =>
            _sessions.Values
                .Where(session => string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
                .OrderByDescending(session => session.StartedAt)
                .Select(WithActualLocalRawSummary)
                .ToList();

        /// <summary>セッション概要をRawなしで取り込みます。</summary>
        /// <remarks>責務: GRDB実装と同じく新規概要をローカルRaw除去済みの一覧項目として保存します。</remarks>
        /// <param name="metadata">取り込む概要とManifest。</param>
        /// <exception cref="ConnectionSessionRepositoryException">終了していない、またはManifestがない場合。</exception>
        public void ImportCloudMetadata(ConnectionSessionCloudMetadata metadata)
        {
            var transferred = metadata.Session;
            if (transferred.EndedAt is null || transferred.AccountIdentifier.Length == 0 || metadata.ManifestDigest.Length == 0)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            if (_sessions.TryGetValue(transferred.Id, out var existing))
            {
                _sessions[transferred.Id] = existing with
                {
                    RawLogSummary = existing.RawLogSummary with
                    {
                        RecordCount = transferred.RawLogSummary.RecordCount,
                        ByteCount = transferred.RawLogSummary.ByteCount,
                        CloudState = ConnectionSessionCloudSyncState.Uploaded,
                        ManifestDigest = metadata.ManifestDigest,
                    },
                };
                return;
            }
            _sessions[transferred.Id] = transferred with
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
        }

        /// <summary>検証済み転送を既存セッションへ復元します。</summary>
        /// <remarks>責務: GRDB実装と同じくRaw概要だけを転送内容へ置き換え、セッションの表示情報は保持します。</remarks>
        /// <param name="transfer">復元する検証済み転送。</param>
        /// <exception cref="ConnectionSessionRepositoryException">順序欠損、セッション不在、所有者不一致、または未終了の場合。</exception>
        public void RestoreVerifiedTransfer(VerifiedConnectionSessionTransfer transfer)
        {
            var package = transfer.Package;
            if (!package.Entries.Select((entry, offset) => entry.Sequence == offset).All(matches => matches)
                || !_sessions.TryGetValue(package.SessionID, out var restored)
                || !string.Equals(restored.AccountIdentifier, package.AccountIdentifier, StringComparison.Ordinal)
                || restored.EndedAt is null)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            var existingSummary = restored.RawLogSummary;
            _sessions[restored.Id] = restored with
            {
                RawLogSummary = new ConnectionSessionRawLogSummary(
                    recordCount: package.Entries.Count,
                    byteCount: package.Entries.Sum(entry => (long)entry.Payload.Count),
                    localState: package.Entries.Count == 0
                        ? ConnectionSessionLocalRawState.Empty
                        : ConnectionSessionLocalRawState.Available,
                    cloudState: ConnectionSessionCloudSyncState.Uploaded,
                    manifestDigest: transfer.ManifestDigest,
                    macImportReceipt: existingSummary.MacImportReceipt?.ManifestDigest == transfer.ManifestDigest
                        ? existingSummary.MacImportReceipt
                        : null,
                    lastAccessedAt: existingSummary.LastAccessedAt),
            };
            _entries[restored.Id] = package.Entries.ToList();
        }

        /// <summary>検証済み転送を取り込みます。</summary>
        /// <remarks>責務: GRDB実装と同じくオンデマンド復元へ委譲します。</remarks>
        /// <param name="transfer">取り込む検証済み転送。</param>
        public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) => RestoreVerifiedTransfer(transfer);

        /// <summary>Rawログの最終閲覧日時を記録します。</summary>
        /// <remarks>責務: GRDB実装と同じくローカルRawがあるセッションだけ閲覧日時を更新します。</remarks>
        /// <param name="date">閲覧日時。</param>
        /// <param name="sessionID">更新するセッションID。</param>
        /// <exception cref="ConnectionSessionRepositoryException">セッション不在またはRaw除去済みの場合。</exception>
        public void MarkRawLogAccessed(DateTimeOffset date, ConnectionSessionID sessionID)
        {
            if (!_sessions.TryGetValue(sessionID, out var session)
                || session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            _sessions[sessionID] = session with { RawLogSummary = session.RawLogSummary with { LastAccessedAt = date } };
        }

        /// <summary>セッション概要を残してRawログだけを除去します。</summary>
        /// <remarks>責務: GRDB実装と同じく終了済みでRawがあるセッションだけをローカル除去状態へ遷移させます。</remarks>
        /// <param name="sessionID">除去するセッションID。</param>
        /// <exception cref="ConnectionSessionRepositoryException">セッション不在、未終了、またはRawがない場合。</exception>
        public void RemoveLocalEntries(ConnectionSessionID sessionID)
        {
            if (!_sessions.TryGetValue(sessionID, out var session)
                || session.EndedAt is null
                || session.RawLogSummary.LocalState != ConnectionSessionLocalRawState.Available)
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            }
            _entries.Remove(sessionID);
            _sessions[sessionID] = session with
            {
                RawLogSummary = session.RawLogSummary with { LocalState = ConnectionSessionLocalRawState.Removed },
            };
        }

        /// <summary>保存済みRawログを記録順で返します。</summary>
        /// <remarks>責務: 1件のセッションIDに属するRawログを順序付きで返します。</remarks>
        /// <param name="sessionID">読み込むセッションID。</param>
        /// <returns>記録順のRawログ。</returns>
        public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) =>
            _entries.TryGetValue(sessionID, out var entries)
                ? entries.OrderBy(entry => entry.Sequence).ToList()
                : [];

        /// <summary>このテストでは使用しないRaw追記を拒否します。</summary>
        /// <remarks>責務: テスト対象外の操作を明示的に失敗させます。</remarks>
        /// <param name="observation">使用しない応答。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID) => throw new NotSupportedException();

        /// <summary>このテストでは使用しない車両別Rawログを拒否します。</summary>
        /// <remarks>責務: テスト対象外の操作を明示的に失敗させます。</remarks>
        /// <param name="vehicleID">使用しない車両ID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>返しません。</returns>
        public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier) => throw new NotSupportedException();

        /// <summary>このテストでは使用しないCloudKit保存結果を拒否します。</summary>
        /// <remarks>責務: テスト対象外の操作を明示的に失敗させます。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="manifestDigest">使用しないDigest。</param>
        public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest) => throw new NotSupportedException();

        /// <summary>このテストでは使用しないCloudKit失敗を拒否します。</summary>
        /// <remarks>責務: テスト対象外の操作を明示的に失敗させます。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void MarkCloudUploadFailed(ConnectionSessionID sessionID) => throw new NotSupportedException();

        /// <summary>このテストでは使用しないMac取込結果を拒否します。</summary>
        /// <remarks>責務: テスト対象外の操作を明示的に失敗させます。</remarks>
        /// <param name="receipt">使用しない受領証。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID) => throw new NotSupportedException();

        /// <summary>ローカルRawがあるセッションの概要を実レコードで補正します。</summary>
        /// <remarks>責務: GRDB実装と同じく現在のRaw件数とPayload合計を概要へ反映します。</remarks>
        /// <param name="session">補正対象のセッション。</param>
        /// <returns>補正したセッション。</returns>
        private ConnectionSession WithActualLocalRawSummary(ConnectionSession session)
        {
            if (session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
            {
                return session;
            }
            var entries = Entries(session.Id);
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
    }

    /// <summary>解析状態テストへ空のPID定義一覧を提供します。</summary>
    private sealed class EmptyDefinitionRepository : IOBDPIDDefinitionRepository
    {
        /// <summary>空のPID定義一覧を返します。</summary>
        /// <remarks>責務: PID定義読込要求を空配列へ変換します。</remarks>
        /// <returns>空のPID定義配列。</returns>
        public IReadOnlyList<OBDPIDDefinition> Definitions() => [];

        /// <summary>このテストではPID定義保存を変更なしで受け付けます。</summary>
        /// <remarks>責務: テスト対象外のPID定義保存要求を満たします。</remarks>
        /// <param name="definition">使用しないPID定義。</param>
        public void Upsert(OBDPIDDefinition definition) { }

        /// <summary>このテストでは一致するPID定義を返しません。</summary>
        /// <remarks>責務: 任意のService/PID検索を定義なしへ変換します。</remarks>
        /// <param name="service">使用しないOBD Service番号。</param>
        /// <param name="pid">使用しないPID番号。</param>
        /// <returns>常に <see langword="null"/>。</returns>
        public OBDPIDDefinition? Definition(byte service, byte pid) => null;
    }

    /// <summary>単一セッションのCloudKit Raw取得をメモリ上で再現します。</summary>
    private sealed class RawDownloadTransferRepository : IConnectionSessionTransferRepository
    {
        /// <summary>取得で返す検証済み転送です。</summary>
        private readonly VerifiedConnectionSessionTransfer _transfer;
        /// <summary>オンデマンド取得されたセッションIDです。</summary>
        public List<ConnectionSessionID> DownloadedSessionIDs { get; } = [];

        /// <summary>固定転送を保持して生成します。</summary>
        /// <remarks>責務: Raw復元テストへ返す単一転送Payloadを固定します。</remarks>
        /// <param name="transfer">オンデマンド取得で返す転送Payload。</param>
        public RawDownloadTransferRepository(VerifiedConnectionSessionTransfer transfer) => _transfer = transfer;

        /// <summary>このテストではアップロードを利用不能として失敗させます。</summary>
        /// <remarks>責務: テスト対象外のRawアップロード要求を拒否します。</remarks>
        /// <param name="package">使用しない転送Payload。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>この実装はDigestを返しません。</returns>
        /// <exception cref="ConnectionSessionRepositoryException">常に <c>InvalidState</c>。</exception>
        public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) =>
            Task.FromException<string>(new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState));

        /// <summary>固定転送を互換全件取得として返します。</summary>
        /// <remarks>責務: プロトコル互換の全件取得へ固定転送を供給します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>固定転送1件。</returns>
        public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>(new[] { _transfer });

        /// <summary>指定セッションの固定Raw転送を返します。</summary>
        /// <remarks>責務: 1件のオンデマンドRaw取得要求を検査履歴と固定転送へ変換します。</remarks>
        /// <param name="sessionID">取得対象のセッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>初期化時に保持した検証済み転送。</returns>
        /// <exception cref="ConnectionSessionRepositoryException">指定IDが固定転送と異なる場合の状態エラー。</exception>
        public Task<VerifiedConnectionSessionTransfer> DownloadTransfer(ConnectionSessionID sessionID, string accountIdentifier)
        {
            if (sessionID != _transfer.Package.SessionID)
            {
                return Task.FromException<VerifiedConnectionSessionTransfer>(
                    new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState));
            }
            DownloadedSessionIDs.Add(sessionID);
            return Task.FromResult(_transfer);
        }

        /// <summary>このテストではMac受領証公開を変更なしで受け付けます。</summary>
        /// <remarks>責務: テスト対象外の受領証公開要求を満たします。</remarks>
        /// <param name="receipt">使用しない受領証。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
            Task.CompletedTask;

        /// <summary>このテストでは空のMac受領証を返します。</summary>
        /// <remarks>責務: テスト対象外の受領証取得要求を空結果で満たします。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空配列。</returns>
        public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([]);

        /// <summary>このテストでは削除マーカーなしを返します。</summary>
        /// <remarks>責務: テスト対象外の削除マーカー取得要求を空集合で満たします。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空集合。</returns>
        public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
            Task.FromResult<IReadOnlySet<ConnectionSessionID>>(new HashSet<ConnectionSessionID>());

        /// <summary>このテストでは単一削除を変更なしで受け付けます。</summary>
        /// <remarks>責務: テスト対象外の単一削除要求を満たします。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) => Task.CompletedTask;

        /// <summary>このテストでは全削除を変更なしで受け付けます。</summary>
        /// <remarks>責務: テスト対象外の全削除要求を満たします。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteAll(string accountIdentifier) => Task.CompletedTask;
    }
}
