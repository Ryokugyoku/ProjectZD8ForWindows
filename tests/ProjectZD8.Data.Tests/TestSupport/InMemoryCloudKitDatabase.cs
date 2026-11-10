using ProjectZD8.Data.Network.CloudKit;

namespace ProjectZD8.Data.Tests.TestSupport;

/// <summary>
/// CloudKit の private database をメモリ上で再現します（リポジトリのテスト用）。
/// </summary>
/// <remarks>資産はアップロードしたバイト列を保存し、読み込みのレコードではダウンロード URL に置き換えます。</remarks>
internal sealed class InMemoryCloudKitDatabase : ICloudKitDatabase
{
    /// <summary>レコード名ごとのレコードです。</summary>
    public Dictionary<string, CloudKitRecord> Records { get; } = new(StringComparer.Ordinal);

    /// <summary>資産の URL ごとのバイト列です。</summary>
    public Dictionary<string, byte[]> Assets { get; } = new(StringComparer.Ordinal);

    /// <summary>今の状態です。</summary>
    public CloudKitAvailability Availability { get; set; } = CloudKitAvailability.Available;

    /// <summary>次の通信で投げる例外です。</summary>
    public Exception? NextFailure { get; set; }

    /// <summary>保存の回数です。</summary>
    public int SaveCount { get; private set; }

    /// <summary>変更タグの番号です。</summary>
    private int _tag;

    /// <summary>
    /// 使えない状態や失敗の筋書きを確かめます。
    /// </summary>
    /// <remarks>責務: 状態に応じて例外を投げます。</remarks>
    private void Check()
    {
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
        if (Availability != CloudKitAvailability.Available)
        {
            throw new CloudKitUnavailableException(Availability);
        }
    }

    /// <inheritdoc />
    public Task<CloudKitRecord?> LookupRecord(string recordName, IReadOnlyList<string>? desiredKeys = null)
    {
        Check();
        return Task.FromResult(Records.TryGetValue(recordName, out var record) ? Project(record, desiredKeys) : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CloudKitRecord>> QueryRecords(
        string recordType,
        string fieldName,
        string value,
        IReadOnlyList<string>? desiredKeys = null)
    {
        Check();
        IReadOnlyList<CloudKitRecord> result = Records.Values
            .Where(r => r.RecordType == recordType && r.GetString(fieldName) == value)
            .Select(r => Project(r, desiredKeys))
            .ToArray();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<CloudKitRecord> SaveRecord(CloudKitRecord record, CloudKitSaveOperation operation)
    {
        Check();
        SaveCount++;
        var exists = Records.TryGetValue(record.RecordName, out var existing);
        if (operation == CloudKitSaveOperation.Create && exists)
        {
            throw new CloudKitException(System.Net.HttpStatusCode.OK, "CONFLICT", "exists");
        }
        if (operation == CloudKitSaveOperation.Update && (!exists || existing!.RecordChangeTag != record.RecordChangeTag))
        {
            throw new CloudKitException(System.Net.HttpStatusCode.OK, "CONFLICT", "changed");
        }
        var fields = operation == CloudKitSaveOperation.Update
            ? new Dictionary<string, CloudKitFieldValue>(existing!.Fields)
            : new Dictionary<string, CloudKitFieldValue>();
        foreach (var (key, value) in record.Fields)
        {
            fields[key] = value is CloudKitFieldValue.AssetUploadValue upload
                ? new CloudKitFieldValue.AssetValue(upload.Receipt.GetProperty("url").GetString(), null, null)
                : value;
        }
        var saved = new CloudKitRecord(record.RecordName, record.RecordType, fields, $"tag{++_tag}");
        Records[record.RecordName] = saved;
        return Task.FromResult(saved);
    }

    /// <inheritdoc />
    public Task DeleteRecordIfPresent(string recordName)
    {
        Check();
        Records.Remove(recordName);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CloudKitFieldValue.AssetUploadValue> UploadAsset(
        string recordType,
        string recordName,
        string fieldName,
        byte[] data,
        Action<double>? progress = null)
    {
        Check();
        var url = $"https://assets.test/{recordName}/{fieldName}/{Guid.NewGuid():N}/${{f}}";
        Assets[url] = data.ToArray();
        progress?.Invoke(1);
        using var document = System.Text.Json.JsonDocument.Parse($"{{\"url\":\"{url}\"}}");
        return Task.FromResult(new CloudKitFieldValue.AssetUploadValue(document.RootElement));
    }

    /// <inheritdoc />
    public Task<byte[]> DownloadAsset(CloudKitFieldValue.AssetValue asset, Action<double>? progress = null)
    {
        Check();
        progress?.Invoke(1);
        return Task.FromResult(Assets[asset.DownloadUrl!].ToArray());
    }

    /// <summary>
    /// 指定のフィールドだけにしたレコードを返します。
    /// </summary>
    /// <remarks>責務: desiredKeys の動きを再現します。</remarks>
    /// <param name="record">レコード。</param>
    /// <param name="desiredKeys">読むフィールド。</param>
    /// <returns>レコード。</returns>
    private static CloudKitRecord Project(CloudKitRecord record, IReadOnlyList<string>? desiredKeys) =>
        desiredKeys is null
            ? record
            : new CloudKitRecord(
                record.RecordName,
                record.RecordType,
                record.Fields.Where(pair => desiredKeys.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value),
                record.RecordChangeTag);
}
