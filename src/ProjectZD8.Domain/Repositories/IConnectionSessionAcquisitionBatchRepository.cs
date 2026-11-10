using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>session内の取得batchとPID request evidenceを永続化する能力です。</summary>
public interface IConnectionSessionAcquisitionBatchRepository
{
    /// <summary>batch開始とpolicy選択済み要求列を未確定状態で保存します。</summary>
    /// <remarks>責務: 物理要求前のbatch intentを同じidentityで再試行可能な未確定証拠として保存します。</remarks>
    /// <param name="evidence">terminal結果をまだ持たないbatch開始証拠。</param>
    /// <param name="sessionID">batchを所有する接続session。</param>
    /// <exception cref="Exception">重複、競合、親manifest欠落、または保存失敗。</exception>
    void BeginBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID);

    /// <summary>選択済み要求が物理送信処理へ進むことを記録します。</summary>
    /// <remarks>責務: 1件の未開始要求を結果未確定のdispatch開始状態へ単調遷移させます。</remarks>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">要求を所有するbatch identity。</param>
    /// <param name="sessionID">batchを所有する接続session。</param>
    /// <exception cref="Exception">batch/request欠落、重複、競合、または保存失敗。</exception>
    void MarkRequestDispatchBegun(int requestOrdinal, AcquisitionBatchIdentity batchIdentity, ConnectionSessionID sessionID);

    /// <summary>正応答Rawと対応するrequest terminal証拠を原子的に保存します。</summary>
    /// <remarks>責務: dispatch開始済み要求のRaw追記とresponded terminal化を単一永続化境界で確定します。</remarks>
    /// <param name="observation">既存Raw表へ追加する未デコード正応答。</param>
    /// <param name="valueOutcome">取得時定義による値評価結果。</param>
    /// <param name="elapsedNanoseconds">要求開始から正応答観測までの単調経過時間。</param>
    /// <param name="reasonCode">transcript原文を含まない承認済み分類理由code。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">要求を所有するbatch identity。</param>
    /// <param name="sessionID">batchとRawを所有する接続session。</param>
    /// <returns>採番済みRaw sequenceを参照するcanonical request evidence。</returns>
    /// <exception cref="Exception">dispatch前、sealed、重複、競合、所有関係不一致、または原子保存失敗。</exception>
    PIDRequestEvidence SaveRespondedRequest(
        OBDRawResponseObservation observation,
        PIDRequestValueOutcome valueOutcome,
        ulong elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID);

    /// <summary>Rawを持たない排他的なrequest terminal証拠を保存します。</summary>
    /// <remarks>責務: dispatch開始済み要求を非responded terminal結果へ単調遷移させます。</remarks>
    /// <param name="outcome">Rawを生成しない確認済みtransport結果。</param>
    /// <param name="elapsedNanoseconds">要求開始からterminal観測までの単調経過時間。回復時に不明なら <see langword="null"/>。</param>
    /// <param name="reasonCode">transcript原文を含まない承認済み分類理由code。</param>
    /// <param name="requestOrdinal">batch内要求順。</param>
    /// <param name="batchIdentity">要求を所有するbatch identity。</param>
    /// <param name="sessionID">batchを所有する接続session。</param>
    /// <returns>Raw sequenceを持たないcanonical request evidence。</returns>
    /// <exception cref="Exception">responded指定、dispatch前、sealed、重複、競合、または保存失敗。</exception>
    PIDRequestEvidence SaveNonRespondedRequest(
        PIDRequestTransportOutcome outcome,
        ulong? elapsedNanoseconds,
        string? reasonCode,
        int requestOrdinal,
        AcquisitionBatchIdentity batchIdentity,
        ConnectionSessionID sessionID);

    /// <summary>terminal request列とbatch終端を保存済み開始証拠へ反映します。</summary>
    /// <remarks>責務: partialを含むbatch terminal証拠を既存Raw参照の整合確認後に原子的に確定します。</remarks>
    /// <param name="evidence">terminalまたは回復済みbatch証拠。</param>
    /// <param name="sessionID">batchを所有する接続session。</param>
    /// <exception cref="Exception">batch/request欠落、Raw参照欠落、競合、または保存失敗。</exception>
    void FinishBatch(AcquisitionBatchEvidence evidence, ConnectionSessionID sessionID);

    /// <summary>sessionのbatch証拠をordinal順で返します。</summary>
    /// <remarks>責務: 1件のsessionに保存されたpartialを含む全batchを安定順で復元します。</remarks>
    /// <param name="sessionID">読取対象session。</param>
    /// <returns>batch ordinal昇順の取得証拠。</returns>
    /// <exception cref="Exception">保存値不正または読取失敗。</exception>
    IReadOnlyList<AcquisitionBatchEvidence> Batches(ConnectionSessionID sessionID);
}