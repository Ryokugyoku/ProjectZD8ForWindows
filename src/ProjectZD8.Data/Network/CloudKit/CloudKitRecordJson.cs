using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit Web Services のレコードの JSON（<c>recordName</c>、<c>recordType</c>、<c>recordChangeTag</c>、<c>fields</c>）を相互に変換します。
/// </summary>
public static class CloudKitRecordJson
{
    /// <summary>
    /// 応答の 1 件のレコードを読みます。
    /// </summary>
    /// <remarks>責務: 1 件のレコードの JSON をレコードへ変換します。</remarks>
    /// <param name="element">レコードの JSON。</param>
    /// <returns>レコード。</returns>
    /// <exception cref="JsonException">recordName がない場合。</exception>
    public static CloudKitRecord Parse(JsonElement element)
    {
        var name = element.TryGetProperty("recordName", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()!
            : throw new JsonException("recordName がありません。");
        var type = element.TryGetProperty("recordType", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!
            : "";
        var tag = element.TryGetProperty("recordChangeTag", out var g) && g.ValueKind == JsonValueKind.String
            ? g.GetString()
            : null;
        var fields = new Dictionary<string, CloudKitFieldValue>(StringComparer.Ordinal);
        if (element.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in f.EnumerateObject())
            {
                fields[property.Name] = ParseField(property.Value);
            }
        }
        return new CloudKitRecord(name, type, fields, tag);
    }

    /// <summary>
    /// 保存するレコードを JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件のレコードを <c>records/modify</c> の <c>record</c> の JSON へ変換します。</remarks>
    /// <param name="record">レコード。</param>
    /// <param name="includeChangeTag">変更タグを含めるか。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject ToJson(CloudKitRecord record, bool includeChangeTag)
    {
        var fields = new JsonObject();
        foreach (var (key, value) in record.Fields)
        {
            fields[key] = FieldToJson(value);
        }
        var obj = new JsonObject
        {
            ["recordName"] = record.RecordName,
            ["recordType"] = record.RecordType,
        };
        if (includeChangeTag && record.RecordChangeTag is { } tag)
        {
            obj["recordChangeTag"] = tag;
        }
        obj["fields"] = fields;
        return obj;
    }

    /// <summary>
    /// フィールドの値を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の <c>{"value", "type"}</c> を値の種類へ変換します。型がなければ JSON の形から推測します。</remarks>
    /// <param name="element">フィールドの JSON。</param>
    /// <returns>フィールドの値。</returns>
    public static CloudKitFieldValue ParseField(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("value", out var value))
        {
            return new CloudKitFieldValue.UnknownValue(null, element);
        }
        var type = element.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        switch (type)
        {
            case "STRING" when value.ValueKind == JsonValueKind.String:
                return new CloudKitFieldValue.StringValue(value.GetString()!);
            case "INT64" when value.TryGetInt64Safe(out var n):
                return new CloudKitFieldValue.Int64Value(n);
            case "DOUBLE" when value.ValueKind == JsonValueKind.Number:
                return new CloudKitFieldValue.DoubleValue(value.GetDouble());
            case "TIMESTAMP" when value.ValueKind == JsonValueKind.Number:
                return new CloudKitFieldValue.TimestampValue(
                    DateTimeOffset.UnixEpoch.AddTicks((long)Math.Round(value.GetDouble() * TimeSpan.TicksPerMillisecond)));
            case "BYTES" when value.ValueKind == JsonValueKind.String:
                return new CloudKitFieldValue.BytesValue(Convert.FromBase64String(value.GetString()!));
            case "ASSETID" or "ASSET" when value.ValueKind == JsonValueKind.Object:
                return ParseAsset(value);
            case null when value.ValueKind == JsonValueKind.String:
                return new CloudKitFieldValue.StringValue(value.GetString()!);
            case null when value.TryGetInt64Safe(out var inferred):
                return new CloudKitFieldValue.Int64Value(inferred);
            case null when value.ValueKind == JsonValueKind.Object && value.TryGetProperty("downloadURL", out _):
                return ParseAsset(value);
            default:
                return new CloudKitFieldValue.UnknownValue(type, value);
        }
    }

    /// <summary>
    /// フィールドの値を JSON にします。
    /// </summary>
    /// <remarks>
    /// 責務: 1 件の値を <c>{"value", "type"}</c> へ変換します。TIMESTAMP は型を付けないと INT64 と推測されるので、必ず型を付けます。
    /// アップロード済みの資産は型を付けずに辞書をそのまま入れます。
    /// </remarks>
    /// <param name="value">フィールドの値。</param>
    /// <returns>JSON のオブジェクト。</returns>
    /// <exception cref="InvalidOperationException">読み込んだ資産や不明な値を保存しようとした場合。</exception>
    public static JsonObject FieldToJson(CloudKitFieldValue value) => value switch
    {
        CloudKitFieldValue.StringValue s => new JsonObject { ["value"] = s.Value, ["type"] = "STRING" },
        CloudKitFieldValue.Int64Value n => new JsonObject { ["value"] = n.Value, ["type"] = "INT64" },
        CloudKitFieldValue.DoubleValue d => new JsonObject { ["value"] = d.Value, ["type"] = "DOUBLE" },
        CloudKitFieldValue.TimestampValue t => new JsonObject
        {
            ["value"] = (t.Value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMillisecond,
            ["type"] = "TIMESTAMP",
        },
        CloudKitFieldValue.BytesValue b => new JsonObject { ["value"] = Convert.ToBase64String(b.Value), ["type"] = "BYTES" },
        CloudKitFieldValue.AssetUploadValue a => new JsonObject { ["value"] = JsonNode.Parse(a.Receipt.GetRawText()) },
        _ => throw new InvalidOperationException("この値は保存できません。"),
    };

    /// <summary>
    /// 資産の値を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の資産の辞書から、ダウンロード URL・チェックサム・サイズを取り出します。</remarks>
    /// <param name="value">資産の辞書。</param>
    /// <returns>資産。</returns>
    private static CloudKitFieldValue.AssetValue ParseAsset(JsonElement value) => new(
        value.TryGetProperty("downloadURL", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null,
        value.TryGetProperty("fileChecksum", out var sum) && sum.ValueKind == JsonValueKind.String ? sum.GetString() : null,
        value.TryGetProperty("size", out var size) && size.TryGetInt64Safe(out var bytes) ? bytes : null);

    /// <summary>
    /// 数値なら 64 ビット整数として読みます。
    /// </summary>
    /// <remarks>責務: 数値以外の要素で例外を出さずに整数を読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <param name="value">読んだ整数。</param>
    /// <returns>読めたら <c>true</c>。</returns>
    private static bool TryGetInt64Safe(this JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }
}
