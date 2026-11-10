using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using static ProjectZD8.Data.Mapping.SwiftJsonReader;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// 生ログの転送パッケージ（<c>ConnectionSessionTransferPackage</c>）とセッション概要（<c>ConnectionSessionCloudMetadata</c>）の JSON を相互に変換します。
/// </summary>
/// <remarks>
/// 転送パッケージのキーは accountIdentifier、entries、sessionID の 3 つだけです。
/// 生ログの payload は Swift の <c>[UInt8]</c> なので、base64 ではなく数値の配列です。
/// </remarks>
public static class ConnectionSessionTransferJsonMapper
{
    /// <summary>
    /// 転送パッケージを JSON の木にします。
    /// </summary>
    /// <remarks>責務: 1 件の転送パッケージを Swift と同じキーと値の JSON オブジェクトへ変換します。</remarks>
    /// <param name="package">転送パッケージ。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject PackageToJson(ConnectionSessionTransferPackage package, SwiftJsonDateEncoding dates)
    {
        var entries = new JsonArray();
        foreach (var entry in package.Entries)
        {
            entries.Add(EntryToJson(entry, dates));
        }
        return new JsonObject
        {
            ["sessionID"] = SwiftJsonValues.Identifier(package.SessionID.RawValue),
            ["accountIdentifier"] = package.AccountIdentifier,
            ["entries"] = entries,
        };
    }

    /// <summary>
    /// JSON から転送パッケージを復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを転送パッケージへ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>転送パッケージ。</returns>
    /// <exception cref="JsonException">必須のキーがないか、型が違う場合。</exception>
    public static ConnectionSessionTransferPackage PackageFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        new ConnectionSessionID(Identifier(Required(element, "sessionID"))),
        String(Required(element, "accountIdentifier")),
        Array(Required(element, "entries"), item => EntryFromJson(item, dates)));

    /// <summary>
    /// セッション概要を JSON の木にします。
    /// </summary>
    /// <remarks>責務: 1 件のセッション概要を session と manifestDigest の JSON オブジェクトへ変換します。</remarks>
    /// <param name="metadata">セッション概要。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject MetadataToJson(ConnectionSessionCloudMetadata metadata, SwiftJsonDateEncoding dates) => new()
    {
        ["session"] = ConnectionSessionJsonMapper.ToJson(metadata.Session, dates),
        ["manifestDigest"] = metadata.ManifestDigest,
    };

    /// <summary>
    /// JSON からセッション概要を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトをセッション概要へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>セッション概要。</returns>
    /// <exception cref="JsonException">必須のキーがないか、型が違う場合。</exception>
    public static ConnectionSessionCloudMetadata MetadataFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        ConnectionSessionJsonMapper.FromJson(Required(element, "session"), dates),
        String(Required(element, "manifestDigest")));

    /// <summary>
    /// 生ログの 1 行を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の生ログを宣言順のキーで JSON へ変換します。</remarks>
    /// <param name="entry">生ログ。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode EntryToJson(ConnectionSessionRawLogEntry entry, SwiftJsonDateEncoding dates) => new JsonObject
    {
        ["sequence"] = entry.Sequence,
        ["observedAt"] = SwiftJsonValues.Date(entry.ObservedAt, dates),
        ["batchElapsedNanoseconds"] = entry.BatchElapsedNanoseconds,
        ["service"] = (int)entry.Service,
        ["pid"] = (int)entry.Pid,
        ["payload"] = SwiftJsonValues.ByteArray(entry.Payload),
    };

    /// <summary>
    /// JSON から生ログの 1 行を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを生ログへ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>生ログ。</returns>
    private static ConnectionSessionRawLogEntry EntryFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        Int64(Required(element, "sequence")),
        Date(Required(element, "observedAt"), dates),
        UInt64(Required(element, "batchElapsedNanoseconds")),
        Byte(Required(element, "service")),
        Byte(Required(element, "pid")),
        ByteArray(Required(element, "payload")));
}
