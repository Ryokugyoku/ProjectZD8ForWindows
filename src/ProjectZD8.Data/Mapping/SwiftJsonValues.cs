using System.Text.Json.Nodes;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// Swift の合成 Codable と同じ形の JSON 値を作ります。
/// </summary>
public static class SwiftJsonValues
{
    /// <summary>
    /// UUID を Swift の <c>uuidString</c>（大文字、ハイフン付き）にします。
    /// </summary>
    /// <remarks>責務: 1 件の UUID を Swift と同じ文字列へ変換します。</remarks>
    /// <param name="value">UUID。</param>
    /// <returns>大文字の UUID 文字列。</returns>
    public static string UuidString(Guid value) => value.ToString("D").ToUpperInvariant();

    /// <summary>
    /// UUID の JSON 値を作ります。
    /// </summary>
    /// <remarks>責務: 1 件の UUID を大文字の文字列の JSON 値へ変換します。</remarks>
    /// <param name="value">UUID。</param>
    /// <returns>JSON の文字列。</returns>
    public static JsonNode Uuid(Guid value) => JsonValue.Create(UuidString(value));

    /// <summary>
    /// <c>RawRepresentable</c> でない ID 型（<c>VehicleID</c> など）の JSON 値 <c>{"rawValue": "..."}</c> を作ります。
    /// </summary>
    /// <remarks>責務: 1 件の UUID を Swift の ID 型の入れ子オブジェクトへ変換します。</remarks>
    /// <param name="rawValue">ID の UUID。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonNode Identifier(Guid rawValue) => new JsonObject { ["rawValue"] = Uuid(rawValue) };

    /// <summary>
    /// 日時の JSON 値を作ります。
    /// </summary>
    /// <remarks>責務: 1 件の日時を指定方式の JSON 数値へ変換します。</remarks>
    /// <param name="value">日時。</param>
    /// <param name="encoding">日付の方式。</param>
    /// <returns>JSON の数値。</returns>
    public static JsonNode Date(DateTimeOffset value, SwiftJsonDateEncoding encoding) =>
        JsonValue.Create(SwiftJsonDate.ToNumber(value, encoding));

    /// <summary>
    /// Swift の <c>Data</c> と同じ base64 文字列の JSON 値を作ります。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列を、パディング付きの標準 base64 へ変換します。</remarks>
    /// <param name="value">バイト列。</param>
    /// <returns>JSON の文字列。</returns>
    public static JsonNode Data(byte[] value) => JsonValue.Create(Convert.ToBase64String(value));

    /// <summary>
    /// Swift の <c>[UInt8]</c> と同じ数値の配列の JSON 値を作ります。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列を 0〜255 の数値の配列へ変換します（<c>Data</c> と違い base64 にはなりません）。</remarks>
    /// <param name="value">バイト列。</param>
    /// <returns>JSON の配列。</returns>
    public static JsonNode ByteArray(IReadOnlyList<byte> value)
    {
        var array = new JsonArray();
        foreach (var b in value)
        {
            array.Add(JsonValue.Create((int)b));
        }
        return array;
    }

    /// <summary>
    /// 値があるときだけキーを追加します（Swift の <c>encodeIfPresent</c>）。
    /// </summary>
    /// <remarks>責務: <c>nil</c> のプロパティを出力から省きます。</remarks>
    /// <param name="obj">追加先。</param>
    /// <param name="key">キー。</param>
    /// <param name="value">値。<c>null</c> なら何もしません。</param>
    public static void AddIfPresent(JsonObject obj, string key, JsonNode? value)
    {
        if (value is not null)
        {
            obj[key] = value;
        }
    }
}
