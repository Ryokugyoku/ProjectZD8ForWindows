namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit の private database（default zone）への最小の操作です。リポジトリはこの境界だけに依存します。
/// </summary>
public interface ICloudKitDatabase
{
    /// <summary>今 CloudKit を使えるかどうか。</summary>
    CloudKitAvailability Availability { get; }

    /// <summary>
    /// レコード名でレコードを 1 件読みます（<c>records/lookup</c>）。
    /// </summary>
    /// <remarks>責務: 1 件のレコード名を、レコードまたは「ない」に変換します。</remarks>
    /// <param name="recordName">レコード名。</param>
    /// <param name="desiredKeys">読むフィールド（<c>null</c> ならすべて）。</param>
    /// <returns>レコード。ないときは <c>null</c>。</returns>
    /// <exception cref="CloudKitException">CloudKit がエラーを返した場合。</exception>
    /// <exception cref="CloudKitUnavailableException">使えない場合。</exception>
    Task<CloudKitRecord?> LookupRecord(string recordName, IReadOnlyList<string>? desiredKeys = null);

    /// <summary>
    /// 文字列のフィールドが一致するレコードを、すべてのページを読んで返します（<c>records/query</c>）。
    /// </summary>
    /// <remarks>責務: 1 件のレコード種類と等値条件を、全ページのレコード一覧へ変換します。</remarks>
    /// <param name="recordType">レコードの種類。</param>
    /// <param name="fieldName">条件のフィールド名（スキーマで Queryable にしたもの）。</param>
    /// <param name="value">一致させる文字列。</param>
    /// <param name="desiredKeys">読むフィールド（<c>null</c> ならすべて）。</param>
    /// <returns>レコードの一覧。</returns>
    Task<IReadOnlyList<CloudKitRecord>> QueryRecords(
        string recordType,
        string fieldName,
        string value,
        IReadOnlyList<string>? desiredKeys = null);

    /// <summary>
    /// レコードを保存します（<c>records/modify</c>）。
    /// </summary>
    /// <remarks>責務: 1 件のレコードを指定の種類の操作で保存し、保存後のレコードを返します。</remarks>
    /// <param name="record">保存するレコード（<see cref="CloudKitSaveOperation.Update"/> なら変更タグが必要）。</param>
    /// <param name="operation">保存の種類。</param>
    /// <returns>保存後のレコード。</returns>
    Task<CloudKitRecord> SaveRecord(CloudKitRecord record, CloudKitSaveOperation operation);

    /// <summary>
    /// レコードがあれば消します（<c>forceDelete</c>。ないときは何もしません）。
    /// </summary>
    /// <remarks>責務: 1 件のレコード名を冪等に削除します。</remarks>
    /// <param name="recordName">レコード名。</param>
    Task DeleteRecordIfPresent(string recordName);

    /// <summary>
    /// 資産をアップロードし、レコードに入れる値を返します（<c>assets/upload</c> と、返った URL への送信）。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列を、保存するレコードの資産フィールドの値へ変換します。</remarks>
    /// <param name="recordType">資産を入れるレコードの種類。</param>
    /// <param name="recordName">資産を入れるレコード名。</param>
    /// <param name="fieldName">資産のフィールド名。</param>
    /// <param name="data">資産のバイト列。</param>
    /// <param name="progress">送信の進み具合（0〜1）。</param>
    /// <returns>レコードに入れる値。</returns>
    Task<CloudKitFieldValue.AssetUploadValue> UploadAsset(
        string recordType,
        string recordName,
        string fieldName,
        byte[] data,
        Action<double>? progress = null);

    /// <summary>
    /// 資産をダウンロードします（<c>downloadURL</c> への GET）。
    /// </summary>
    /// <remarks>責務: 1 件の資産をバイト列へ変換します。</remarks>
    /// <param name="asset">レコードから読んだ資産。</param>
    /// <param name="progress">受信の進み具合（0〜1）。</param>
    /// <returns>資産のバイト列。</returns>
    Task<byte[]> DownloadAsset(CloudKitFieldValue.AssetValue asset, Action<double>? progress = null);
}
