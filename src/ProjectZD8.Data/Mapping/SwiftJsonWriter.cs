using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// JSON の木を Swift の <c>JSONEncoder</c> と同じ形のバイト列（UTF-8、空白なし）に書き出します。
/// </summary>
/// <remarks>
/// 日本語などの非 ASCII 文字はエスケープしません（Swift と同じ）。キーの順番は、並べ替えなしなら追加した順
/// （Swift の合成 Codable の宣言順）、並べ替えありなら <see cref="SwiftJsonKeyComparer"/> の順です。
/// </remarks>
public static class SwiftJsonWriter
{
    /// <summary>非 ASCII をそのまま書く書き込み設定です。</summary>
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false,
    };

    /// <summary>
    /// JSON の木をバイト列に書き出します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON の木を、指定した Swift の設定と同じ形の UTF-8 バイト列へ変換します。</remarks>
    /// <param name="node">書き出す JSON の木。</param>
    /// <param name="options">Swift の <c>JSONEncoder</c> の設定。</param>
    /// <returns>UTF-8 の JSON バイト列。</returns>
    public static byte[] Write(JsonNode node, SwiftJsonEncodingOptions options)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteNode(writer, node, options.SortedKeys);
        }
        var bytes = stream.ToArray();
        return options.EscapeSlashes ? EscapeSlashes(bytes) : bytes;
    }

    /// <summary>
    /// 1 つの節を書き出します。
    /// </summary>
    /// <remarks>責務: 1 件の節を、キーの順番を守って書き出します。</remarks>
    /// <param name="writer">書き込み先。</param>
    /// <param name="node">書き出す節。<c>null</c> は JSON の null。</param>
    /// <param name="sortedKeys">キーを並べ替えるか。</param>
    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node, bool sortedKeys)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                IEnumerable<KeyValuePair<string, JsonNode?>> properties = obj;
                if (sortedKeys)
                {
                    properties = obj.OrderBy(pair => pair.Key, SwiftJsonKeyComparer.Instance);
                }
                foreach (var (key, value) in properties)
                {
                    writer.WritePropertyName(key);
                    WriteNode(writer, value, sortedKeys);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteNode(writer, item, sortedKeys);
                }
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// <c>/</c> を <c>\/</c> に置き換えます。
    /// </summary>
    /// <remarks>
    /// 責務: Swift の既定のスラッシュのエスケープを再現します。JSON の構造文字に <c>/</c> はなく、
    /// UTF-8 の多バイト文字にも 0x2F は現れないので、バイト単位の置き換えで文字列の中だけが変わります。
    /// </remarks>
    /// <param name="bytes">書き出した JSON。</param>
    /// <returns>エスケープ後の JSON。</returns>
    private static byte[] EscapeSlashes(byte[] bytes)
    {
        var count = bytes.Count(b => b == (byte)'/');
        if (count == 0)
        {
            return bytes;
        }
        var result = new byte[bytes.Length + count];
        var index = 0;
        foreach (var b in bytes)
        {
            if (b == (byte)'/')
            {
                result[index++] = (byte)'\\';
            }
            result[index++] = b;
        }
        return result;
    }
}
