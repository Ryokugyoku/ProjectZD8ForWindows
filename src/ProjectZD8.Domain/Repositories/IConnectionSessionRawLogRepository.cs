using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>未デコードOBD応答を接続セッション単位で永続化します。</summary>
/// <remarks>
/// Swift のプロトコル拡張で既定実装を持つメソッド（概要取込、オンデマンド復元、閲覧日時記録、件数、ページ読取）は
/// 既定のインターフェイスメソッドとして同じ互換動作を提供します。
/// </remarks>
public interface IConnectionSessionRawLogRepository
{
    /// <summary>Raw応答を現在セッションの次の順序へ追記します。</summary>
    /// <remarks>責務: 1件の未デコードOBD応答を指定セッションへ追加します。</remarks>
    /// <param name="observation">取得境界が返した未デコード応答。</param>
    /// <param name="sessionID">追記対象の接続セッションID。</param>
    /// <exception cref="Exception">セッション不在、制約違反、または永続化失敗。</exception>
    void Append(OBDRawResponseObservation observation, ConnectionSessionID sessionID);

    /// <summary>セッション内のRaw応答を記録順で返します。</summary>
    /// <remarks>責務: 1件のセッションIDに属する未デコード応答を順序付きで復元します。</remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <returns><c>Sequence</c>昇順のRawログ。</returns>
    /// <exception cref="Exception">永続化済みログを読み込めない場合の保存先エラー。</exception>
    IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID);

    /// <summary>セッション内のRaw応答件数を返します。</summary>
    /// <remarks>
    /// 責務: 1件のセッションIDに属するRawログ総件数を復元します。
    /// 既定実装は全件読取結果からRawログ件数を返します（既存の全件読取能力をRawログ件数へ変換します）。
    /// </remarks>
    /// <param name="sessionID">件数を取得する接続セッションID。</param>
    /// <returns>保存済みRawログ件数。</returns>
    /// <exception cref="Exception">永続化済みログ件数を読み込めない場合（既定実装では全件読取に失敗した場合）の保存先エラー。</exception>
    long EntryCount(ConnectionSessionID sessionID) => Entries(sessionID).Count;

    /// <summary>セッション内のRaw応答を指定順序より後から上限件数まで返します。</summary>
    /// <remarks>
    /// 責務: 1件のセッションRawログを記録順カーソルで分割して復元します。
    /// 既定実装は全件読取結果へ記録順カーソルと上限を適用します（既存の全件読取能力を互換的な1ページのRawログへ変換します）。
    /// </remarks>
    /// <param name="sessionID">読み込む接続セッションID。</param>
    /// <param name="sequence">直前に読み込んだ記録順序。先頭ページでは <see langword="null"/>。</param>
    /// <param name="limit">1回で返す最大件数。</param>
    /// <returns><c>Sequence</c>昇順で指定上限以下のRawログ。</returns>
    /// <exception cref="Exception">永続化済みログを読み込めない場合（既定実装では全件読取に失敗した場合）の保存先エラー。</exception>
    IReadOnlyList<ConnectionSessionRawLogEntry> Entries(ConnectionSessionID sessionID, long? sequence, int limit)
    {
        var lowerBound = sequence ?? -1;
        return Entries(sessionID)
            .OrderBy(entry => entry.Sequence)
            .Where(entry => entry.Sequence > lowerBound)
            .Take(Math.Max(0, limit))
            .ToList();
    }

    /// <summary>車両に属する全セッションのRaw応答を時系列で返します。</summary>
    /// <remarks>責務: 1件のアカウントと車両IDに属する未デコード応答をセッション境界付きで復元します。</remarks>
    /// <param name="vehicleID">学習抽出対象の登録車両ID。</param>
    /// <param name="accountIdentifier">車両とセッションを所有するAppleアカウント識別子。</param>
    /// <returns>セッション開始日時、セッションID、<c>Sequence</c> の安定順で並ぶRawログ。</returns>
    /// <exception cref="Exception">永続化済みログを読み込めない場合の保存先エラー。</exception>
    IReadOnlyList<VehicleConnectionSessionRawLogEntry> Entries(VehicleID vehicleID, string accountIdentifier);

    /// <summary>CloudKitへの保存結果をセッションへ記録します。</summary>
    /// <remarks>責務: 1件のセッションを指定ManifestでCloudKit保存済みへ遷移させます。</remarks>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <param name="manifestDigest">保存済みAssetバイトのSHA-256。</param>
    /// <exception cref="Exception">セッション更新を完了できない場合の保存先エラー。</exception>
    void MarkCloudUploaded(ConnectionSessionID sessionID, string manifestDigest);

    /// <summary>CloudKit転送失敗を再試行可能状態として記録します。</summary>
    /// <remarks>責務: 1件のセッションをCloudKit転送失敗状態へ遷移させます。</remarks>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <exception cref="Exception">セッション更新を完了できない場合の保存先エラー。</exception>
    void MarkCloudUploadFailed(ConnectionSessionID sessionID);

    /// <summary>Mac取込受領証をセッションへ保存します。</summary>
    /// <remarks>責務: 1件のMac受領証を対応するセッションManifestへ関連付けます。</remarks>
    /// <param name="receipt">Macが発行した永続取込受領証。</param>
    /// <param name="sessionID">更新する接続セッションID。</param>
    /// <exception cref="Exception">セッション更新を完了できない場合の保存先エラー。</exception>
    void MarkMacImported(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID);

    /// <summary>検証済みRaw転送Payloadを既存セッションへ冪等に取り込みます。</summary>
    /// <remarks>責務: 1件の検証済みRaw Payloadを同じ安定セッションIDのRawログへ復元します。</remarks>
    /// <param name="transfer">CloudKitから取得した検証済み転送Payload。</param>
    /// <exception cref="Exception">セッション不在、所有者不一致、制約違反、または永続化失敗。</exception>
    void ImportVerifiedTransfer(VerifiedConnectionSessionTransfer transfer);

    /// <summary>検証済みセッション概要をRaw Payloadなしで取り込みます。</summary>
    /// <remarks>
    /// 責務: 1件のCloudKit概要をローカル一覧表示用セッションへ反映します。
    /// 既定実装（互換実装）は概要取込を変更なしで受け付け、既存テスト実装の概要取込要求を副作用なしで満たします。
    /// </remarks>
    /// <param name="metadata">Raw Payloadを含まない検証対象セッション概要（既定実装では使用しない）。</param>
    /// <exception cref="Exception">既存内容との不一致または永続化失敗。</exception>
    void ImportCloudMetadata(ConnectionSessionCloudMetadata metadata)
    {
    }

    /// <summary>明示要求された検証済みRaw Payloadを既存セッションへ復元します。</summary>
    /// <remarks>
    /// 責務: 1件のオンデマンド転送を同じ安定セッションIDのローカルRawキャッシュへ復元します。
    /// 既定実装（互換実装）は従来の検証済み転送取込 <see cref="ImportVerifiedTransfer"/> へ委譲します。
    /// </remarks>
    /// <param name="transfer">CloudKitから取得した検証済み転送Payload。</param>
    /// <exception cref="Exception">セッション不在、所有者不一致、制約違反、または永続化失敗（既定実装では既存転送取込に失敗した場合のエラー）。</exception>
    void RestoreVerifiedTransfer(VerifiedConnectionSessionTransfer transfer) =>
        ImportVerifiedTransfer(transfer);

    /// <summary>Rawログの最終閲覧日時を現在端末だけへ記録します。</summary>
    /// <remarks>
    /// 責務: 1件のローカルRawキャッシュへ最終閲覧日時を関連付けます。
    /// 既定実装（互換実装）は閲覧日時記録を変更なしで受け付け、既存テスト実装の閲覧日時更新要求を副作用なしで満たします。
    /// </remarks>
    /// <param name="date">閲覧可能状態へした日時（既定実装では使用しない）。</param>
    /// <param name="sessionID">更新する接続セッションID（既定実装では使用しない）。</param>
    /// <exception cref="Exception">セッション不在、Raw不在、または永続化失敗。</exception>
    void MarkRawLogAccessed(DateTimeOffset date, ConnectionSessionID sessionID)
    {
    }

    /// <summary>セッション概要を残して現在端末のRaw応答だけを除去します。</summary>
    /// <remarks>責務: 1件の終了済みセッションから端末内RawログPayloadだけを削除します。</remarks>
    /// <param name="sessionID">ローカルPayloadを除去するセッションID。</param>
    /// <exception cref="Exception">セッション状態不正または永続化失敗。</exception>
    void RemoveLocalEntries(ConnectionSessionID sessionID);
}
