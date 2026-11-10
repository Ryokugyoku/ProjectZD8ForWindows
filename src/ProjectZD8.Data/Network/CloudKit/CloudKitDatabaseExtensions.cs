namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// iOS 版の「読んでから保存する」手順を CloudKit Web Services で行う拡張です。
/// </summary>
public static class CloudKitDatabaseExtensions
{
    /// <summary>
    /// 同じ名前のレコードがあれば指定したフィールドだけを更新し、なければ作ります。
    /// </summary>
    /// <remarks>
    /// 責務: iOS の <c>record(for:)</c> → なければ <c>CKRecord(recordType:recordID:)</c> → <c>save</c> と同じ結果にします。
    /// 読んだ後にほかの端末が書き換えていたら、iOS と同じく競合（<c>CONFLICT</c>）で失敗します。
    /// </remarks>
    /// <param name="database">CloudKit のデータベース。</param>
    /// <param name="recordType">レコードの種類。</param>
    /// <param name="recordName">レコード名。</param>
    /// <param name="fields">書くフィールド。</param>
    /// <returns>保存後のレコード。</returns>
    public static async Task<CloudKitRecord> UpsertRecord(
        this ICloudKitDatabase database,
        string recordType,
        string recordName,
        IReadOnlyDictionary<string, CloudKitFieldValue> fields)
    {
        var existing = await database.LookupRecord(recordName, desiredKeys: []).ConfigureAwait(false);
        return existing is null
            ? await database.SaveRecord(new CloudKitRecord(recordName, recordType, fields), CloudKitSaveOperation.Create)
                .ConfigureAwait(false)
            : await database.SaveRecord(
                    new CloudKitRecord(recordName, existing.RecordType.Length > 0 ? existing.RecordType : recordType, fields, existing.RecordChangeTag),
                    CloudKitSaveOperation.Update)
                .ConfigureAwait(false);
    }
}
