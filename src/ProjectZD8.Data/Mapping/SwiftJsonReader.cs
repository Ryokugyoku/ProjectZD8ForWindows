using System.Text.Json;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// Swift の合成 <c>Decodable</c> と同じ厳しさで JSON を読みます。
/// </summary>
/// <remarks>
/// 必須のキーがない・型が違う・列挙の値が不明なときは <see cref="JsonException"/> を投げます（Swift の
/// <c>DecodingError</c> と同じ）。Optional のキーは、ないか null なら <c>null</c> になります（<c>decodeIfPresent</c>）。
/// 知らないキーは無視します。
/// </remarks>
public static class SwiftJsonReader
{
    /// <summary>
    /// バイト列を JSON として読み、値へ変換します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON バイト列を解析し、変換関数で値へ変換します。</remarks>
    /// <typeparam name="T">変換後の型。</typeparam>
    /// <param name="utf8">UTF-8 の JSON。</param>
    /// <param name="convert">ルート要素の変換関数。</param>
    /// <returns>変換した値。</returns>
    /// <exception cref="JsonException">JSON が壊れているか、形が合わない場合。</exception>
    public static T Parse<T>(ReadOnlySpan<byte> utf8, Func<JsonElement, T> convert)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowTrailingCommas = false });
        using var document = JsonDocument.ParseValue(ref reader);
        return convert(document.RootElement);
    }

    /// <summary>
    /// オブジェクトであることを確かめます。
    /// </summary>
    /// <remarks>責務: 1 件の要素がキー付きコンテナーとして読めることを保証します。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>同じ要素。</returns>
    /// <exception cref="JsonException">オブジェクトでない場合。</exception>
    public static JsonElement Object(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"オブジェクトが必要ですが {element.ValueKind} でした。");
        }
        return element;
    }

    /// <summary>
    /// 必須のキーの値を返します。
    /// </summary>
    /// <remarks>責務: 1 件のキーが存在し null でないことを保証します。</remarks>
    /// <param name="obj">オブジェクト。</param>
    /// <param name="key">キー。</param>
    /// <returns>キーの値。</returns>
    /// <exception cref="JsonException">キーがないか null の場合。</exception>
    public static JsonElement Required(JsonElement obj, string key)
    {
        if (!Object(obj).TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"必須のキー {key} がありません。");
        }
        return value;
    }

    /// <summary>
    /// 省略できるキーの値を返します。
    /// </summary>
    /// <remarks>責務: 1 件のキーを、なければ <c>null</c> として読みます。</remarks>
    /// <param name="obj">オブジェクト。</param>
    /// <param name="key">キー。</param>
    /// <returns>キーの値。ないか null なら <c>null</c>。</returns>
    public static JsonElement? Optional(JsonElement obj, string key)
    {
        if (!Object(obj).TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return value;
    }

    /// <summary>
    /// 文字列を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を文字列として読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>文字列。</returns>
    /// <exception cref="JsonException">文字列でない場合。</exception>
    public static string String(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : throw new JsonException($"文字列が必要ですが {element.ValueKind} でした。");

    /// <summary>
    /// 真偽値を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を真偽値として読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>真偽値。</returns>
    /// <exception cref="JsonException">真偽値でない場合。</exception>
    public static bool Bool(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new JsonException($"真偽値が必要ですが {element.ValueKind} でした。"),
    };

    /// <summary>
    /// 倍精度の数値を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を倍精度の数値として読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>数値。</returns>
    /// <exception cref="JsonException">数値でない場合。</exception>
    public static double Double(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value)
            ? value
            : throw new JsonException($"数値が必要ですが {element.ValueKind} でした。");

    /// <summary>
    /// 64 ビット整数を読みます。
    /// </summary>
    /// <remarks>
    /// 責務: 1 件の要素を整数として読みます。Swift と同じく、<c>3.0</c> のように整数で表せる小数も受け付けます。
    /// </remarks>
    /// <param name="element">要素。</param>
    /// <returns>整数。</returns>
    /// <exception cref="JsonException">整数で表せない場合。</exception>
    public static long Int64(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var value))
            {
                return value;
            }
            if (element.TryGetDouble(out var number) && number == Math.Floor(number)
                && number >= long.MinValue && number < 9.2233720368547758E18)
            {
                return (long)number;
            }
        }
        throw new JsonException("64 ビット整数で表せる数値が必要です。");
    }

    /// <summary>
    /// 32 ビット整数（Swift の小さな <c>Int</c>）を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を 32 ビット整数として読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>整数。</returns>
    /// <exception cref="JsonException">範囲外の場合。</exception>
    public static int Int32(JsonElement element)
    {
        var value = Int64(element);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : throw new JsonException("32 ビット整数の範囲を超えています。");
    }

    /// <summary>
    /// 符号なし 64 ビット整数を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を <c>UInt64</c> として読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>整数。</returns>
    /// <exception cref="JsonException">範囲外の場合。</exception>
    public static ulong UInt64(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out var value)
            ? value
            : throw new JsonException("符号なし 64 ビット整数が必要です。");

    /// <summary>
    /// 0〜255 の整数（Swift の <c>UInt8</c>）を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を 1 バイトとして読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>バイト。</returns>
    /// <exception cref="JsonException">範囲外の場合。</exception>
    public static byte Byte(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetByte(out var value)
            ? value
            : throw new JsonException("0〜255 の整数が必要です。");

    /// <summary>
    /// UUID 文字列を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の要素を、ハイフン付き 36 文字の UUID として読みます（大文字小文字は問いません）。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>UUID。</returns>
    /// <exception cref="JsonException">UUID として読めない場合。</exception>
    public static Guid Uuid(JsonElement element) =>
        Guid.TryParseExact(String(element), "D", out var value)
            ? value
            : throw new JsonException("UUID の形式が正しくありません。");

    /// <summary>
    /// ID 型（<c>{"rawValue": "..."}</c>）を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の入れ子オブジェクトから UUID を取り出します。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>ID の UUID。</returns>
    public static Guid Identifier(JsonElement element) => Uuid(Required(element, "rawValue"));

    /// <summary>
    /// 日時を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の数値を指定方式の日時として読みます。</remarks>
    /// <param name="element">要素。</param>
    /// <param name="encoding">日付の方式。</param>
    /// <returns>日時。</returns>
    public static DateTimeOffset Date(JsonElement element, SwiftJsonDateEncoding encoding) =>
        SwiftJsonDate.FromNumber(Double(element), encoding);

    /// <summary>
    /// base64 の <c>Data</c> を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の base64 文字列をバイト列へ変換します。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>バイト列。</returns>
    /// <exception cref="JsonException">base64 として読めない場合。</exception>
    public static byte[] Base64Data(JsonElement element)
    {
        try
        {
            return Convert.FromBase64String(String(element));
        }
        catch (FormatException exception)
        {
            throw new JsonException("base64 の形式が正しくありません。", exception);
        }
    }

    /// <summary>
    /// 数値の配列（Swift の <c>[UInt8]</c>）を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の配列をバイト列へ変換します。</remarks>
    /// <param name="element">要素。</param>
    /// <returns>バイト列。</returns>
    public static byte[] ByteArray(JsonElement element) => Array(element, Byte).ToArray();

    /// <summary>
    /// 配列を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の配列の各要素を変換関数で読みます。</remarks>
    /// <typeparam name="T">要素の型。</typeparam>
    /// <param name="element">要素。</param>
    /// <param name="convert">各要素の変換関数。</param>
    /// <returns>変換した要素の一覧。</returns>
    /// <exception cref="JsonException">配列でない場合。</exception>
    public static IReadOnlyList<T> Array<T>(JsonElement element, Func<JsonElement, T> convert)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"配列が必要ですが {element.ValueKind} でした。");
        }
        var result = new List<T>(element.GetArrayLength());
        foreach (var item in element.EnumerateArray())
        {
            result.Add(convert(item));
        }
        return result;
    }

    /// <summary>
    /// 列挙の raw value を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の文字列を、raw value の解析関数で列挙へ変換します。</remarks>
    /// <typeparam name="T">列挙の型。</typeparam>
    /// <param name="element">要素。</param>
    /// <param name="tryParse">raw value の解析関数。</param>
    /// <returns>列挙の値。</returns>
    /// <exception cref="JsonException">不明な値の場合。</exception>
    public static T Enum<T>(JsonElement element, TryParseRawValue<T> tryParse)
    {
        var raw = String(element);
        return tryParse(raw, out var value)
            ? value
            : throw new JsonException($"{typeof(T).Name} に {raw} という値はありません。");
    }

    /// <summary>
    /// raw value から列挙を解析する関数の型です（<c>XxxRawValue.TryParse</c>）。
    /// </summary>
    /// <typeparam name="T">列挙の型。</typeparam>
    /// <param name="rawValue">raw value。</param>
    /// <param name="value">解析した値。</param>
    /// <returns>解析できたら <c>true</c>。</returns>
    public delegate bool TryParseRawValue<T>(string rawValue, out T value);
}
