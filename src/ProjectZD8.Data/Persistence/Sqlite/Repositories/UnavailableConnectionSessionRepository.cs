using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>製品用セッションDBを準備できない場合に明示的失敗を返します。</summary>
/// <remarks>iOS版 <c>UnavailableConnectionSessionRepository</c> の移植です。</remarks>
public sealed class UnavailableConnectionSessionRepository :
    IConnectionSessionRepository,
    IConnectionSessionRawLogRepository,
    IConnectionSessionErasureRepository,
    IAccountConnectionSessionErasureRepository,
    IConnectionSessionAcquisitionRepository,
    IConnectionSessionAcquisitionBatchRepository,
    IConnectionSessionAcquisitionTerminationRepository
{
    /// <summary>manifest開始保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 取得開始要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="manifest">保存できないmanifest。</param>
    /// <param name="startedAt">保存できない開始日時。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID) =>
        throw AcquisitionUnavailable();

    /// <summary>取得終了保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 取得終了要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="endedAt">保存できない終了日時。</param>
    /// <param name="reason">保存できない終了理由。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID) =>
        throw AcquisitionUnavailable();

    /// <summary>manifest読取を利用不能として失敗させます。</summary>
    /// <remarks>責務: manifest照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">読み込めないsession。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>取得境界読取を利用不能として失敗させます。</summary>
    /// <remarks>責務: 取得境界照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">読み込めないsession。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>batch開始保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: batch開始要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="evidence">保存できないbatch。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>dispatch開始保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: dispatch開始要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="requestOrdinal">保存できない要求順。</param>
    /// <param name="batchIdentity">保存できない親batch。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID) =>
        throw AcquisitionUnavailable();

    /// <summary>正応答保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 正応答の原子保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="observation">保存できない正応答。</param>
    /// <param name="valueOutcome">保存できない値評価結果。</param>
    /// <param name="elapsedNanoseconds">保存できない経過時間。</param>
    /// <param name="reasonCode">保存できない理由code。</param>
    /// <param name="requestOrdinal">保存できない要求順。</param>
    /// <param name="batchIdentity">保存できない親batch。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public PIDRequestEvidence SaveRespondedRequest(
        OBDRawResponseObservation observation,
        PIDRequestValueOutcome valueOutcome,
        ulong elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>非応答保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 非応答の保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="outcome">保存できない結果。</param>
    /// <param name="elapsedNanoseconds">保存できない経過時間。</param>
    /// <param name="reasonCode">保存できない理由code。</param>
    /// <param name="requestOrdinal">保存できない要求順。</param>
    /// <param name="batchIdentity">保存できない親batch。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public PIDRequestEvidence SaveNonRespondedRequest(
        PIDRequestTransportOutcome outcome,
        ulong? elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>batch終端保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: batch終端要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="evidence">保存できないbatch。</param>
    /// <param name="sessionID">保存できない親session。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>batch読取を利用不能として失敗させます。</summary>
    /// <remarks>責務: batch照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">読み込めないsession。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID) => throw AcquisitionUnavailable();

    /// <summary>session取得終了を利用不能として失敗させます。</summary>
    /// <remarks>責務: 原子的なsession終了要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="session">終了できないsession。</param>
    /// <param name="endedAt">保存できない終了日時。</param>
    /// <param name="reason">保存できない終了理由。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public ConnectionSession FinishSessionAcquisition(ConnectionSession session, DateTimeOffset endedAt, ConnectionSessionEndReason reason) =>
        throw AcquisitionUnavailable();

    /// <summary>中断session回復を利用不能として失敗させます。</summary>
    /// <remarks>責務: process終了回復要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="accountIdentifier">回復できないアカウント。</param>
    /// <param name="recoveredAt">保存できない回復日時。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">常に <c>Unavailable</c>。</exception>
    public IReadOnlyList<ConnectionSession> RecoverInterruptedSessionAcquisitions(string accountIdentifier, DateTimeOffset recoveredAt) =>
        throw AcquisitionUnavailable();

    /// <summary>セッション保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="session">保存できないセッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void Save(ConnectionSession session) => throw Unavailable();

    /// <summary>セッション読込を利用不能として失敗させます。</summary>
    /// <remarks>責務: 履歴照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="accountIdentifier">読み込めないアカウント。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) => throw Unavailable();

    /// <summary>アカウント履歴削除を利用不能として失敗させます。</summary>
    /// <remarks>責務: アカウント削除時の履歴削除を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="accountIdentifier">削除できないアカウント。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void DeleteSessions(string accountIdentifier) => throw Unavailable();

    /// <summary>セッション削除を利用不能として失敗させます。</summary>
    /// <remarks>責務: 単一セッション削除を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">削除できないセッション。</param>
    /// <param name="accountIdentifier">削除できないアカウント。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) => throw Unavailable();

    /// <summary>Raw追記を利用不能として失敗させます。</summary>
    /// <remarks>責務: Raw保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="observation">保存できない応答。</param>
    /// <param name="sessionID">保存できないセッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID) => throw Unavailable();

    /// <summary>Raw読込を利用不能として失敗させます。</summary>
    /// <remarks>責務: Raw照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">読み込めないセッション。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID) => throw Unavailable();

    /// <summary>車両Raw抽出を利用不能として失敗させます。</summary>
    /// <remarks>責務: 車両単位Raw照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="vehicleID">抽出できない車両。</param>
    /// <param name="accountIdentifier">抽出できないアカウント。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier) => throw Unavailable();

    /// <summary>CloudKit保存済み記録を利用不能として失敗させます。</summary>
    /// <remarks>責務: 転送状態更新を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">更新できないセッション。</param>
    /// <param name="manifestDigest">保存できないDigest。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest) => throw Unavailable();

    /// <summary>CloudKit失敗記録を利用不能として失敗させます。</summary>
    /// <remarks>責務: 転送失敗状態更新を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">更新できないセッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void MarkCloudUploadFailed(ConnectionSessionID sessionID) => throw Unavailable();

    /// <summary>Mac受領証保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 受領証保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="receipt">保存できない受領証。</param>
    /// <param name="sessionID">更新できないセッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID) => throw Unavailable();

    /// <summary>転送取込を利用不能として失敗させます。</summary>
    /// <remarks>責務: Raw取込要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="transfer">取り込めない転送。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) => throw Unavailable();

    /// <summary>概要取込を利用不能として失敗させます。</summary>
    /// <remarks>責務: 概要取込要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="metadata">取り込めない概要。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void ImportCloudMetadata(ConnectionSessionCloudMetadata metadata) => throw Unavailable();

    /// <summary>Raw復元を利用不能として失敗させます。</summary>
    /// <remarks>責務: オンデマンド復元要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="transfer">復元できない転送。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void RestoreVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) => throw Unavailable();

    /// <summary>閲覧日時保存を利用不能として失敗させます。</summary>
    /// <remarks>責務: 閲覧日時更新を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="date">保存できない日時。</param>
    /// <param name="sessionID">更新できないセッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void MarkRawLogAccessed(DateTimeOffset date, ConnectionSessionID sessionID) => throw Unavailable();

    /// <summary>ローカルRaw除去を利用不能として失敗させます。</summary>
    /// <remarks>責務: 除去要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="sessionID">除去できないセッション。</param>
    /// <exception cref="ConnectionSessionRepositoryException">常に <c>Unavailable</c>。</exception>
    public void RemoveLocalEntries(ConnectionSessionID sessionID) => throw Unavailable();

    /// <summary>接続セッション保存先の利用不能エラーを生成します。</summary>
    /// <remarks>責務: 共通のエラー値を作ります。</remarks>
    /// <returns><c>Unavailable</c> の例外。</returns>
    private static ConnectionSessionRepositoryException Unavailable() => new(ConnectionSessionRepositoryError.Unavailable);

    /// <summary>取得証拠保存先の利用不能エラーを生成します。</summary>
    /// <remarks>責務: 共通のエラー値を作ります。</remarks>
    /// <returns><c>Unavailable</c> の例外。</returns>
    private static ConnectionSessionAcquisitionRepositoryException AcquisitionUnavailable() =>
        new(ConnectionSessionAcquisitionRepositoryError.Unavailable);
}
