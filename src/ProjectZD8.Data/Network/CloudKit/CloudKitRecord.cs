namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit Web Services のレコード 1 件です（default zone）。
/// </summary>
public sealed record CloudKitRecord
{
    /// <summary>
    /// レコード名、種類、フィールドから作ります。
    /// </summary>
    /// <remarks>責務: 1 件のレコードの識別情報とフィールドを固定します。</remarks>
    /// <param name="recordName">レコード名。</param>
    /// <param name="recordType">レコードの種類。</param>
    /// <param name="fields">フィールド。</param>
    /// <param name="recordChangeTag">サーバーの変更タグ（新しく作るときは <c>null</c>）。</param>
    public CloudKitRecord(
        string recordName,
        string recordType,
        IReadOnlyDictionary<string, CloudKitFieldValue> fields,
        string? recordChangeTag = null)
    {
        RecordName = recordName;
        RecordType = recordType;
        Fields = new Dictionary<string, CloudKitFieldValue>(fields, StringComparer.Ordinal);
        RecordChangeTag = recordChangeTag;
    }

    /// <summary>レコード名。</summary>
    public string RecordName { get; }

    /// <summary>レコードの種類。</summary>
    public string RecordType { get; }

    /// <summary>サーバーの変更タグ。</summary>
    public string? RecordChangeTag { get; }

    /// <summary>フィールド。</summary>
    public IReadOnlyDictionary<string, CloudKitFieldValue> Fields { get; }

    /// <summary>
    /// 文字列のフィールドを返します。
    /// </summary>
    /// <remarks>責務: 1 件のフィールドを、STRING のときだけ文字列として返します（iOS の <c>as? String</c>）。</remarks>
    /// <param name="key">フィールド名。</param>
    /// <returns>文字列。ないか型が違えば <c>null</c>。</returns>
    public string? GetString(string key) =>
        Fields.TryGetValue(key, out var value) && value is CloudKitFieldValue.StringValue s ? s.Value : null;

    /// <summary>
    /// 整数のフィールドを返します。
    /// </summary>
    /// <remarks>責務: 1 件のフィールドを、INT64 のときだけ整数として返します（iOS の <c>as? NSNumber</c>）。</remarks>
    /// <param name="key">フィールド名。</param>
    /// <returns>整数。ないか型が違えば <c>null</c>。</returns>
    public long? GetInt64(string key) =>
        Fields.TryGetValue(key, out var value) && value is CloudKitFieldValue.Int64Value n ? n.Value : null;

    /// <summary>
    /// バイト列のフィールドを返します。
    /// </summary>
    /// <remarks>責務: 1 件のフィールドを、BYTES のときだけバイト列として返します（iOS の <c>as? Data</c>）。</remarks>
    /// <param name="key">フィールド名。</param>
    /// <returns>バイト列。ないか型が違えば <c>null</c>。</returns>
    public byte[]? GetBytes(string key) =>
        Fields.TryGetValue(key, out var value) && value is CloudKitFieldValue.BytesValue b ? b.Value : null;

    /// <summary>
    /// 日時のフィールドを返します。
    /// </summary>
    /// <remarks>責務: 1 件のフィールドを、TIMESTAMP のときだけ日時として返します（iOS の <c>as? Date</c>）。</remarks>
    /// <param name="key">フィールド名。</param>
    /// <returns>日時。ないか型が違えば <c>null</c>。</returns>
    public DateTimeOffset? GetTimestamp(string key) =>
        Fields.TryGetValue(key, out var value) && value is CloudKitFieldValue.TimestampValue t ? t.Value : null;

    /// <summary>
    /// 資産のフィールドを返します。
    /// </summary>
    /// <remarks>責務: 1 件のフィールドを、ASSETID のときだけ資産として返します（iOS の <c>as? CKAsset</c>）。</remarks>
    /// <param name="key">フィールド名。</param>
    /// <returns>資産。ないか型が違えば <c>null</c>。</returns>
    public CloudKitFieldValue.AssetValue? GetAsset(string key) =>
        Fields.TryGetValue(key, out var value) && value is CloudKitFieldValue.AssetValue a ? a : null;
}
