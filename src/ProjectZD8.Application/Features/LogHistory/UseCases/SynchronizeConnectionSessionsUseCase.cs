using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LogHistory.UseCases;

/// <summary>接続セッション同期で現在端末が担う役割です。</summary>
public enum ConnectionSessionSyncDeviceRole
{
    /// <summary>セッション概要を送受信し既存Mac受領証を表示するiPhoneです。</summary>
    IPhone,
    /// <summary>セッション概要をiPhoneと同じ規則で送受信するMacです。</summary>
    MacOS,
}

/// <summary>終了済みセッション概要を双方向同期し、Raw PayloadをCloudKitへ保管します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class SynchronizeConnectionSessionsUseCase
{
    /// <summary>接続履歴のローカル保存先です。</summary>
    private readonly IConnectionSessionRepository sessionRepository;
    /// <summary>Rawログと端末別保管状態のローカル保存先です。</summary>
    private readonly IConnectionSessionRawLogRepository rawLogRepository;
    /// <summary>削除マーカーを現在端末へ反映するセッション物理削除境界です。</summary>
    private readonly IConnectionSessionErasureRepository sessionErasureRepository;
    /// <summary>CloudKitのセッション転送境界です。</summary>
    private readonly IConnectionSessionTransferRepository transferRepository;
    /// <summary>現在端末が担う同期役割です。</summary>
    private readonly ConnectionSessionSyncDeviceRole role;

    /// <summary>ローカル保存先、CloudKit境界、端末役割を固定して生成します。</summary>
    /// <remarks>責務: セッションのローカル正本を現在端末のCloudKit同期役割へ結び付けます。</remarks>
    /// <param name="sessionRepository">接続履歴のローカル保存先。</param>
    /// <param name="rawLogRepository">Rawログと保管状態のローカル保存先。</param>
    /// <param name="sessionErasureRepository">削除マーカーを反映するローカル物理削除境界。</param>
    /// <param name="transferRepository">CloudKitセッション転送境界。</param>
    /// <param name="role">既存受領証を反映するiPhoneまたは対等同期するMacの役割。</param>
    public SynchronizeConnectionSessionsUseCase(
        IConnectionSessionRepository sessionRepository,
        IConnectionSessionRawLogRepository rawLogRepository,
        IConnectionSessionErasureRepository sessionErasureRepository,
        IConnectionSessionTransferRepository transferRepository,
        ConnectionSessionSyncDeviceRole role)
    {
        this.sessionRepository = sessionRepository;
        this.rawLogRepository = rawLogRepository;
        this.sessionErasureRepository = sessionErasureRepository;
        this.transferRepository = transferRepository;
        this.role = role;
    }

    /// <summary>現在アカウントの受信処理と、明示された場合だけ送信待ち処理を実行します。</summary>
    /// <remarks>責務: 1件のアカウントスコープを指定された送信方針に従う端末間同期結果へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <param name="uploadsPendingSessions">送信待ちセッションも同期する場合は <see langword="true"/>。通常の履歴更新では <see langword="false"/>。</param>
    /// <exception cref="Exception">ローカル保存、CloudKit概要・Raw転送、または整合性検証に失敗した場合のエラー。</exception>
    public async Task Execute(string accountIdentifier, bool uploadsPendingSessions = false)
    {
        await ApplySessionDeletions(accountIdentifier);
        var remoteMetadata = await transferRepository.DownloadMetadata(accountIdentifier);
        var remoteManifestDigests = await transferRepository.DownloadTransferManifests(accountIdentifier);
        IReadOnlyList<VerifiedConnectionSessionTransfer> uploadedTransfers = uploadsPendingSessions
            ? await UploadPendingSessions(accountIdentifier, remoteManifestDigests)
            : [];
        var currentManifestDigests = new Dictionary<ConnectionSessionID, string>(remoteManifestDigests);
        foreach (var transfer in uploadedTransfers)
        {
            currentManifestDigests[transfer.Package.SessionID] = transfer.ManifestDigest;
        }
        var existingRemoteSessionIDs = new HashSet<ConnectionSessionID>(remoteMetadata.Select(item => item.Session.Id));
        existingRemoteSessionIDs.UnionWith(uploadedTransfers.Select(transfer => transfer.Package.SessionID));
        await PublishLocalMetadata(accountIdentifier, currentManifestDigests, existingRemoteSessionIDs);
        var localSessionsByID = sessionRepository.Sessions(accountIdentifier).ToDictionary(session => session.Id);
        var uploadedMetadata = new List<ConnectionSessionCloudMetadata>();
        foreach (var transfer in uploadedTransfers)
        {
            if (localSessionsByID.TryGetValue(transfer.Package.SessionID, out var local))
            {
                uploadedMetadata.Add(new ConnectionSessionCloudMetadata(
                    MetadataSession(local, transfer.ManifestDigest),
                    transfer.ManifestDigest));
            }
        }
        ImportMetadata(MetadataByReplacingRemoteWithUploads(remoteMetadata, uploadedMetadata));
        if (role == ConnectionSessionSyncDeviceRole.IPhone)
        {
            await ApplyMacReceipts(accountIdentifier);
        }
    }

    /// <summary>指定された終了済みセッションだけをiCloudへ送信します。</summary>
    /// <remarks>責務: 1件のローカル終了済みセッションを検証可能なCloudKit転送と同期済み概要へ変換します。</remarks>
    /// <param name="sessionID">送信対象の接続セッションID。</param>
    /// <param name="accountIdentifier">セッションを所有するAppleアカウント識別子。</param>
    /// <param name="progress"><c>0.0...1.0</c> の範囲で通知するAsset保存進捗。<see langword="null"/> なら通知しません。</param>
    /// <exception cref="Exception">対象不在、未終了、Rawログ読込、CloudKit保存、またはローカル状態更新に失敗した場合のエラー。</exception>
    public async Task Upload(
        ConnectionSessionID sessionID,
        string accountIdentifier,
        Action<double>? progress = null)
    {
        var session = sessionRepository.Sessions(accountIdentifier).FirstOrDefault(candidate => candidate.Id == sessionID);
        if (session is null
            || !string.Equals(session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal)
            || session.EndedAt is null
            || session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        if (session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded)
        {
            return;
        }
        _ = await UploadSession(session, accountIdentifier, progress ?? (_ => { }));
    }

    /// <summary>CloudKit上にRaw Payloadが実在するローカルセッション概要を公開します。</summary>
    /// <remarks>責務: 全ローカル終了済みセッションをRaw Asset非依存のCloudKit概要へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <param name="remoteManifestDigests">CloudKitに実在するセッション別Raw Manifest。</param>
    /// <param name="existingRemoteSessionIDs">同期開始時点ですでに概要が存在するセッションID。</param>
    /// <exception cref="Exception">履歴取得、Manifest整合性検証、または概要保存に失敗した場合のエラー。</exception>
    private async Task PublishLocalMetadata(
        string accountIdentifier,
        IReadOnlyDictionary<ConnectionSessionID, string> remoteManifestDigests,
        IReadOnlySet<ConnectionSessionID> existingRemoteSessionIDs)
    {
        foreach (var session in sessionRepository.Sessions(accountIdentifier))
        {
            if (session.EndedAt is null || existingRemoteSessionIDs.Contains(session.Id))
            {
                continue;
            }
            if (!remoteManifestDigests.TryGetValue(session.Id, out var remoteDigest))
            {
                continue;
            }
            if (session.RawLogSummary.ManifestDigest is { } localDigest
                && session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded
                && !string.Equals(localDigest, remoteDigest, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            await transferRepository.PublishMetadata(
                new ConnectionSessionCloudMetadata(MetadataSession(session, remoteDigest), remoteDigest),
                accountIdentifier);
        }
    }

    /// <summary>端末固有状態を除いたセッション概要を生成します。</summary>
    /// <remarks>責務: 1件のローカルセッションを一覧同期用の安定した概要表現へ正規化します。</remarks>
    /// <param name="session">正規化する終了済みセッション。</param>
    /// <param name="manifestDigest">CloudKitに実在するRaw Payload Manifest。</param>
    /// <returns>件数と容量を保持し端末固有状態を除いたセッション。</returns>
    private static ConnectionSession MetadataSession(ConnectionSession session, string manifestDigest) =>
        session with
        {
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: session.RawLogSummary.RecordCount,
                byteCount: session.RawLogSummary.ByteCount,
                localState: session.RawLogSummary.RecordCount == 0
                    ? ConnectionSessionLocalRawState.Empty
                    : ConnectionSessionLocalRawState.Removed,
                cloudState: ConnectionSessionCloudSyncState.Uploaded,
                manifestDigest: manifestDigest,
                macImportReceipt: null,
                lastAccessedAt: null),
        };

    /// <summary>今回公開した概要で同期開始時の古い概要を置き換えます。</summary>
    /// <remarks>責務: リモート概要と今回のアップロード概要をセッションID別の最新概要へ統合します。</remarks>
    /// <param name="remoteMetadata">同期開始時にCloudKitから取得した概要群。</param>
    /// <param name="uploads">今回Raw Payloadを公開したセッション概要群。</param>
    /// <returns>同一セッションでは今回の概要だけを保持する取込対象群。</returns>
    private static IReadOnlyList<ConnectionSessionCloudMetadata> MetadataByReplacingRemoteWithUploads(
        IReadOnlyList<ConnectionSessionCloudMetadata> remoteMetadata,
        IReadOnlyList<ConnectionSessionCloudMetadata> uploads)
    {
        var uploadedSessionIDs = new HashSet<ConnectionSessionID>(uploads.Select(item => item.Session.Id));
        return remoteMetadata.Where(item => !uploadedSessionIDs.Contains(item.Session.Id)).Concat(uploads).ToList();
    }

    /// <summary>CloudKit概要群をローカル一覧へ取り込みます。</summary>
    /// <remarks>責務: 全セッション概要をRaw Payload非取得のローカル履歴へ変換します。</remarks>
    /// <param name="metadata">CloudKitから取得したRaw非包含セッション概要群。</param>
    /// <exception cref="Exception">重複Manifest競合またはローカル概要取込に失敗した場合のエラー。</exception>
    private void ImportMetadata(IReadOnlyList<ConnectionSessionCloudMetadata> metadata)
    {
        var manifests = new Dictionary<ConnectionSessionID, string>();
        foreach (var item in metadata)
        {
            var sessionID = item.Session.Id;
            if (manifests.TryGetValue(sessionID, out var existing)
                && !string.Equals(existing, item.ManifestDigest, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            manifests[sessionID] = item.ManifestDigest;
            rawLogRepository.ImportCloudMetadata(item);
        }
    }

    /// <summary>CloudKit削除マーカーを現在端末の物理削除へ反映します。</summary>
    /// <remarks>責務: 1件のアカウントに属する全削除マーカーを現在端末のセッション物理削除へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <exception cref="Exception">CloudKit削除マーカー取得またはローカル物理削除に失敗した場合のエラー。</exception>
    private async Task ApplySessionDeletions(string accountIdentifier)
    {
        var localSessionIDs = new HashSet<ConnectionSessionID>(
            sessionRepository.Sessions(accountIdentifier).Select(session => session.Id));
        var deletedSessionIDs = await transferRepository.DeletedSessionIDs(accountIdentifier);
        foreach (var sessionID in deletedSessionIDs)
        {
            if (localSessionIDs.Contains(sessionID))
            {
                sessionErasureRepository.DeleteSession(sessionID, accountIdentifier);
            }
        }
    }

    /// <summary>ローカルの終了済み送信待ちセッションをCloudKitへ保存します。</summary>
    /// <remarks>責務: 送信可能な全ローカルセッションをCloudKit Assetと保存済みManifestへ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <param name="remoteManifestDigests">CloudKitで実在確認できたセッション別Manifest。</param>
    /// <returns>今回CloudKitへ保存した検証可能な転送Payload。</returns>
    /// <exception cref="Exception">履歴読込、Rawログ読込、またはCloudKit保存に失敗した場合のエラー。</exception>
    private async Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> UploadPendingSessions(
        string accountIdentifier,
        IReadOnlyDictionary<ConnectionSessionID, string> remoteManifestDigests)
    {
        var sessions = sessionRepository.Sessions(accountIdentifier);
        var uploadedTransfers = new List<VerifiedConnectionSessionTransfer>();
        foreach (var session in sessions)
        {
            var remoteManifestDigest = remoteManifestDigests.TryGetValue(session.Id, out var digest) ? digest : null;
            if (ShouldUpload(session, remoteManifestDigest))
            {
                uploadedTransfers.Add(await UploadSession(session, accountIdentifier, _ => { }));
            }
        }
        return uploadedTransfers;
    }

    /// <summary>1件のセッションPayloadと概要をCloudKitへ保存してローカル状態を更新します。</summary>
    /// <remarks>責務: 1件の送信可能なセッションをCloudKit保存済み転送へ変換します。</remarks>
    /// <param name="session">Rawログを現在端末に保持する終了済みセッション。</param>
    /// <param name="accountIdentifier">セッションを所有するAppleアカウント識別子。</param>
    /// <param name="progress"><c>0.0...1.0</c> の範囲で通知するAsset保存進捗。</param>
    /// <returns>CloudKitが返したManifestで検証できる転送Payload。</returns>
    /// <exception cref="Exception">Rawログ読込、CloudKit保存、またはローカル同期状態更新に失敗した場合のエラー。</exception>
    private async Task<VerifiedConnectionSessionTransfer> UploadSession(
        ConnectionSession session,
        string accountIdentifier,
        Action<double> progress)
    {
        try
        {
            var entries = rawLogRepository.Entries(session.Id);
            var package = new ConnectionSessionTransferPackage(SessionForTransfer(session, entries), entries);
            var digest = await transferRepository.Upload(package, accountIdentifier, progress);
            await transferRepository.PublishMetadata(
                new ConnectionSessionCloudMetadata(MetadataSession(session, digest), digest),
                accountIdentifier);
            rawLogRepository.MarkCloudUploaded(session.Id, digest);
            return new VerifiedConnectionSessionTransfer(package, digest);
        }
        catch
        {
            try
            {
                rawLogRepository.MarkCloudUploadFailed(session.Id);
            }
            catch
            {
                // Swift の try? と同じく、失敗状態の保存失敗は元のエラーを優先して無視します。
            }
            throw;
        }
    }

    /// <summary>端末固有の同期証跡を除いたセッション転送表現を生成します。</summary>
    /// <remarks>責務: 1件のローカルセッションとRawログを再送しても変化しない転送用セッションへ正規化します。</remarks>
    /// <param name="session">転送する終了済みセッション。</param>
    /// <param name="entries">転送する未デコードRawログ。</param>
    /// <returns>Raw件数を保持し、端末別CloudKit状態と受領証を除いたセッション。</returns>
    private static ConnectionSession SessionForTransfer(
        ConnectionSession session,
        IReadOnlyList<ConnectionSessionRawLogEntry> entries) =>
        session with
        {
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: entries.Count,
                byteCount: entries.Aggregate(0L, (total, entry) => total + entry.Payload.Count),
                localState: entries.Count == 0 ? ConnectionSessionLocalRawState.Empty : ConnectionSessionLocalRawState.Available,
                cloudState: ConnectionSessionCloudSyncState.NotUploaded,
                manifestDigest: null,
                macImportReceipt: null),
        };

    /// <summary>セッション履歴が現在CloudKit送信対象かを返します。</summary>
    /// <remarks>責務: 1件の接続セッションを終了、ローカル除去、CloudKit実在状態で送信対象判定します。</remarks>
    /// <param name="session">判定する接続セッション。</param>
    /// <param name="remoteManifestDigest">CloudKitで実在確認できたManifest。</param>
    /// <returns>CloudKitへ新規送信または再送する場合は <see langword="true"/>。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">同じセッションIDでローカルとCloudKitのManifestが競合する場合のエラー。</exception>
    private static bool ShouldUpload(ConnectionSession session, string? remoteManifestDigest)
    {
        if (session.EndedAt is null || session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed)
        {
            return false;
        }
        if (remoteManifestDigest is not null)
        {
            if (session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Pending
                || session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Failed)
            {
                return true;
            }
            if (session.RawLogSummary.ManifestDigest is { } localManifestDigest
                && !string.Equals(localManifestDigest, remoteManifestDigest, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            return false;
        }
        return true;
    }

    /// <summary>CloudKit上のMac受領証をiPhone側セッションへ反映します。</summary>
    /// <remarks>責務: 受信したMac受領証群を同じManifestのローカルセッションへ関連付けます。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <exception cref="Exception">CloudKit受領証取得に失敗した場合のエラー。</exception>
    private async Task ApplyMacReceipts(string accountIdentifier)
    {
        var receipts = await transferRepository.DownloadMacReceipts(accountIdentifier);
        var localSessionIDs = new HashSet<ConnectionSessionID>(
            sessionRepository.Sessions(accountIdentifier).Select(session => session.Id));
        var appliedSessionIDs = new HashSet<ConnectionSessionID>();
        foreach (var (sessionID, receipt) in receipts)
        {
            if (!localSessionIDs.Contains(sessionID))
            {
                continue;
            }
            if (!appliedSessionIDs.Add(sessionID))
            {
                continue;
            }
            rawLogRepository.MarkMacImported(receipt, sessionID);
        }
    }
}
