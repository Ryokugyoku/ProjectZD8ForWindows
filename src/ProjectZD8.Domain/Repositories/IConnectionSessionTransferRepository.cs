using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Repositories;

/// <summary>接続セッションPayloadとMac受領証をCloudKit経由で交換します。</summary>
/// <remarks>
/// Swift のプロトコル拡張で既定実装を持つメソッド（概要公開・取得、Manifest取得、単一セッション取得、進捗付き保存・取得）は
/// 既定のインターフェイスメソッドとして同じ互換動作を提供します。
/// </remarks>
public interface IConnectionSessionTransferRepository
{
    /// <summary>Raw Payloadを含まないセッション概要をCloudKitへ保存します。</summary>
    /// <remarks>
    /// 責務: 1件のセッション概要をRaw Assetから独立したCloudKitレコードへ変換します。
    /// 既定実装は概要専用実装を持たない転送先では追加保存を省略し、テスト境界の公開要求を副作用なしで受け付けます。
    /// </remarks>
    /// <param name="metadata">一覧共有に必要なセッション概要とRaw Manifest（既定実装では使用しない）。</param>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子（既定実装では使用しない）。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    /// <exception cref="Exception">概要の符号化またはCloudKit保存に失敗した場合のエラー。</exception>
    Task PublishMetadata(ConnectionSessionCloudMetadata metadata, string accountIdentifier) => Task.CompletedTask;

    /// <summary>指定アカウントのセッション概要だけを取得します。</summary>
    /// <remarks>
    /// 責務: CloudKit上の軽量概要レコードをRaw Asset非取得のセッション一覧へ変換します。
    /// 既定実装は概要専用実装を持たない転送先から、Raw専用Payloadと概要同期を混在させない空の概要一覧を返します。
    /// </remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>Raw Payloadを含まないセッション概要（既定実装では空）。</returns>
    /// <exception cref="Exception">CloudKit取得、復号、または整合性検証に失敗した場合のエラー。</exception>
    Task<IReadOnlyList<ConnectionSessionCloudMetadata>> DownloadMetadata(string accountIdentifier) =>
        Task.FromResult<IReadOnlyList<ConnectionSessionCloudMetadata>>(Array.Empty<ConnectionSessionCloudMetadata>());

    /// <summary>CloudKitに実在するRaw PayloadのManifestを取得します。</summary>
    /// <remarks>
    /// 責務: Raw Assetを読み込まずにセッション別Manifestを復元します。
    /// 既定実装は既存の全転送取得結果（<see cref="DownloadTransfers"/>）からManifest辞書を復元します。
    /// </remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>セッションIDをキーとするRaw Payload Manifest。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">既定実装で同じセッションIDに異なるManifestがある場合（<c>IntegrityConflict</c>）。</exception>
    /// <exception cref="Exception">CloudKit取得または必須フィールド復元に失敗した場合のエラー。</exception>
    async Task<IReadOnlyDictionary<ConnectionSessionID, string>> DownloadTransferManifests(string accountIdentifier)
    {
        var manifests = new Dictionary<ConnectionSessionID, string>();
        foreach (var transfer in await DownloadTransfers(accountIdentifier))
        {
            var id = transfer.Package.SessionID;
            if (manifests.TryGetValue(id, out var existing)
                && !string.Equals(existing, transfer.ManifestDigest, StringComparison.Ordinal))
            {
                throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.IntegrityConflict);
            }
            manifests[id] = transfer.ManifestDigest;
        }
        return manifests;
    }

    /// <summary>指定セッションのRaw Payloadだけをオンデマンド取得します。</summary>
    /// <remarks>
    /// 責務: 1件のセッションIDをDigest検証済みRaw転送Payloadへ変換します。
    /// 既定実装は既存の全転送取得結果（<see cref="DownloadTransfers"/>）から指定セッションを選択します。
    /// </remarks>
    /// <param name="sessionID">Raw Payloadを取得するセッションID。</param>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>Asset整合性を検証したセッション転送Payload。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">既定実装で転送が存在しない場合（<c>InvalidState</c>）。</exception>
    /// <exception cref="Exception">CloudKit取得、Asset読込、Digest検証、または復号に失敗した場合のエラー。</exception>
    async Task<VerifiedConnectionSessionTransfer> DownloadTransfer(ConnectionSessionID sessionID, string accountIdentifier)
    {
        var transfers = await DownloadTransfers(accountIdentifier);
        foreach (var transfer in transfers)
        {
            if (transfer.Package.SessionID == sessionID)
            {
                return transfer;
            }
        }
        throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
    }

    /// <summary>指定セッションのRaw Payloadを進捗通知付きでオンデマンド取得します。</summary>
    /// <remarks>
    /// 責務: 1件のセッションIDを取得進捗とDigest検証済みRaw転送Payloadへ変換します。
    /// 既定実装は進捗専用実装を持たない転送先を開始（0）・完了（1）通知付きの既存取得へ委譲します。
    /// </remarks>
    /// <param name="sessionID">Raw Payloadを取得するセッションID。</param>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <param name="progress"><c>0.0...1.0</c> の範囲で通知するAsset取得進捗。</param>
    /// <returns>Asset整合性を検証したセッション転送Payload。</returns>
    /// <exception cref="Exception">CloudKit取得、Asset読込、Digest検証、または復号に失敗した場合のエラー。</exception>
    async Task<VerifiedConnectionSessionTransfer> DownloadTransfer(
        ConnectionSessionID sessionID,
        string accountIdentifier,
        Action<double> progress)
    {
        progress(0);
        var transfer = await DownloadTransfer(sessionID, accountIdentifier);
        progress(1);
        return transfer;
    }

    /// <summary>セッションIDに紐づくRaw PayloadをCloudKitへ保存します。</summary>
    /// <remarks>責務: 1件のセッションID付きRaw Payloadを検証可能なCloudKit Assetへ変換します。</remarks>
    /// <param name="package">保存するセッション識別情報と未デコードRawログ。</param>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>保存したAssetバイトのSHA-256。</returns>
    /// <exception cref="Exception">符号化、一時ファイル、またはCloudKit保存に失敗した場合のエラー。</exception>
    Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier);

    /// <summary>セッションIDに紐づくRaw Payloadを進捗通知付きでCloudKitへ保存します。</summary>
    /// <remarks>
    /// 責務: 1件のセッションID付きRaw Payloadを保存進捗と検証可能なCloudKit Assetへ変換します。
    /// 既定実装は進捗専用実装を持たない転送先を開始（0）・完了（1）通知付きの既存保存へ委譲します。
    /// </remarks>
    /// <param name="package">保存するセッション識別情報と未デコードRawログ。</param>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <param name="progress"><c>0.0...1.0</c> の範囲で通知するAsset保存進捗。</param>
    /// <returns>保存したAssetバイトのSHA-256。</returns>
    /// <exception cref="Exception">符号化、一時ファイル、またはCloudKit保存に失敗した場合のエラー。</exception>
    async Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier, Action<double> progress)
    {
        progress(0);
        var digest = await Upload(package, accountIdentifier);
        progress(1);
        return digest;
    }

    /// <summary>指定アカウントの検証済みセッションPayloadを取得します。</summary>
    /// <remarks>責務: CloudKit上のセッションAsset群をDigest検証済みPayloadへ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>AssetのSHA-256検証に成功したセッションPayload。</returns>
    /// <exception cref="Exception">CloudKit取得、Digest検証、または復号に失敗した場合のエラー。</exception>
    Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier);

    /// <summary>Macの永続取込受領証をCloudKitへ保存します。</summary>
    /// <remarks>責務: 1件のMac取込結果をセッション別CloudKit受領証へ変換します。</remarks>
    /// <param name="receipt">Macが読み戻し検証したManifest受領証。</param>
    /// <param name="sessionID">取り込んだ接続セッションID。</param>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>保存の完了を表すタスク。</returns>
    /// <exception cref="Exception">CloudKit保存を完了できない場合のエラー。</exception>
    Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier);

    /// <summary>iPhoneが参照するMac取込受領証を取得します。</summary>
    /// <remarks>責務: 指定アカウントのCloudKit受領証をセッションID別の取込結果へ復元します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>セッションIDとMac取込受領証の組。</returns>
    /// <exception cref="Exception">CloudKit取得またはレコード復元に失敗した場合のエラー。</exception>
    Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
        string accountIdentifier);

    /// <summary>全端末で物理削除すべきセッションIDを取得します。</summary>
    /// <remarks>責務: 1件のアカウントに属するCloudKit削除マーカーをセッションID集合へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象のAppleアカウント識別子。</param>
    /// <returns>全端末から削除すべき接続セッションID集合。</returns>
    /// <exception cref="Exception">CloudKit取得またはレコード復元に失敗した場合のエラー。</exception>
    Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier);

    /// <summary>セッション削除マーカーを公開し、対応するCloudKit Payloadと受領証を物理削除します。</summary>
    /// <remarks>責務: 1件の接続セッションを全端末削除対象として記録しCloudKit運転データを物理削除します。</remarks>
    /// <param name="sessionID">削除対象の接続セッションID。</param>
    /// <param name="accountIdentifier">削除対象を所有するAppleアカウント識別子。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    /// <exception cref="Exception">削除マーカー保存、CloudKit検索、またはレコード削除に失敗した場合のエラー。</exception>
    Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier);

    /// <summary>指定アカウントの全セッション転送とMac受領証を削除します。</summary>
    /// <remarks>責務: 1件のアカウント識別子に属するCloudKit運転データを削除済み状態へします。</remarks>
    /// <param name="accountIdentifier">削除対象のAppleアカウント識別子。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    /// <exception cref="Exception">CloudKit上のセッションAssetまたはMac受領証を削除できない場合のエラー。</exception>
    Task DeleteAll(string accountIdentifier);
}
