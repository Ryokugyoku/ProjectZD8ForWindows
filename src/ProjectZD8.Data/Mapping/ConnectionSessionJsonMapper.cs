using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using static ProjectZD8.Data.Mapping.SwiftJsonReader;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// 接続セッション（車両・取得端末・生ログ概要・Mac 受領証を含む）と Swift の <c>ConnectionSession</c> の JSON を相互に変換します。
/// </summary>
/// <remarks>
/// 計算プロパティ（status、needsStopReview など）は出力しません。<c>stopReviewDecision</c> と
/// <c>acquisitionDevice</c> がない古いデータも読めます。
/// </remarks>
public static class ConnectionSessionJsonMapper
{
    /// <summary>
    /// 接続セッションを JSON の木にします。
    /// </summary>
    /// <remarks>責務: 1 件の接続セッションを Swift と同じキーと値の JSON オブジェクトへ変換します。</remarks>
    /// <param name="session">接続セッション。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject ToJson(ConnectionSession session, SwiftJsonDateEncoding dates)
    {
        var obj = new JsonObject
        {
            ["id"] = SwiftJsonValues.Identifier(session.Id.RawValue),
            ["accountIdentifier"] = session.AccountIdentifier,
            ["startedAt"] = SwiftJsonValues.Date(session.StartedAt, dates),
        };
        SwiftJsonValues.AddIfPresent(obj, "endedAt", session.EndedAt is { } ended ? SwiftJsonValues.Date(ended, dates) : null);
        SwiftJsonValues.AddIfPresent(obj, "endReason", session.EndReason is { } reason ? JsonValue.Create(reason.ToRawValue()) : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "stopReviewDecision",
            session.StopReviewDecision is { } decision ? JsonValue.Create(decision.ToRawValue()) : null);
        SwiftJsonValues.AddIfPresent(obj, "vehicle", session.Vehicle is { } vehicle ? VehicleToJson(vehicle) : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "acquisitionDevice",
            session.AcquisitionDevice is { } device
                ? new JsonObject { ["platform"] = device.Platform.ToRawValue(), ["name"] = device.Name }
                : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "startingOdometerKilometers",
            session.StartingOdometerKilometers is { } starting ? JsonValue.Create(starting) : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "endingOdometerKilometers",
            session.EndingOdometerKilometers is { } ending ? JsonValue.Create(ending) : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "distanceSourceModelCode",
            session.DistanceSourceModelCode is { } code ? JsonValue.Create(code) : null);
        obj["rawLogSummary"] = SummaryToJson(session.RawLogSummary, dates);
        return obj;
    }

    /// <summary>
    /// JSON から接続セッションを復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを、Swift の合成 Decodable と同じ規則で接続セッションへ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>接続セッション。</returns>
    /// <exception cref="JsonException">必須のキーがないか、型が違う場合。</exception>
    public static ConnectionSession FromJson(JsonElement element, SwiftJsonDateEncoding dates)
    {
        Object(element);
        var session = new ConnectionSession(
            accountIdentifier: String(Required(element, "accountIdentifier")),
            id: new ConnectionSessionID(Identifier(Required(element, "id"))),
            startedAt: Date(Required(element, "startedAt"), dates),
            vehicle: Optional(element, "vehicle") is { } vehicle ? VehicleFromJson(vehicle) : null,
            acquisitionDevice: Optional(element, "acquisitionDevice") is { } device
                ? new ConnectionSessionAcquisitionDevice(
                    Enum<ConnectionSessionAcquisitionPlatform>(
                        Required(device, "platform"),
                        ConnectionSessionAcquisitionPlatformRawValue.TryParse),
                    String(Required(device, "name")))
                : null);
        return session with
        {
            EndedAt = Optional(element, "endedAt") is { } ended ? Date(ended, dates) : null,
            EndReason = Optional(element, "endReason") is { } reason
                ? Enum<ConnectionSessionEndReason>(reason, ConnectionSessionEndReasonRawValue.TryParse)
                : null,
            StopReviewDecision = Optional(element, "stopReviewDecision") is { } decision
                ? Enum<ConnectionSessionStopReviewDecision>(decision, ConnectionSessionStopReviewDecisionRawValue.TryParse)
                : null,
            StartingOdometerKilometers = Optional(element, "startingOdometerKilometers") is { } starting ? Double(starting) : null,
            EndingOdometerKilometers = Optional(element, "endingOdometerKilometers") is { } ending ? Double(ending) : null,
            DistanceSourceModelCode = Optional(element, "distanceSourceModelCode") is { } code ? String(code) : null,
            RawLogSummary = SummaryFromJson(Required(element, "rawLogSummary"), dates),
        };
    }

    /// <summary>
    /// セッションの車両を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件のセッション車両を id・name・displayIdentifier の JSON へ変換します。</remarks>
    /// <param name="vehicle">セッションの車両。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode VehicleToJson(ConnectionSessionVehicle vehicle) => new JsonObject
    {
        ["id"] = SwiftJsonValues.Identifier(vehicle.Id.RawValue),
        ["name"] = vehicle.Name,
        ["displayIdentifier"] = vehicle.DisplayIdentifier,
    };

    /// <summary>
    /// JSON からセッションの車両を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトをセッション車両へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <returns>セッションの車両。</returns>
    private static ConnectionSessionVehicle VehicleFromJson(JsonElement element) => new(
        new VehicleID(Identifier(Required(element, "id"))),
        String(Required(element, "name")),
        String(Required(element, "displayIdentifier")));

    /// <summary>
    /// 生ログの概要を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の生ログ概要を宣言順のキーで JSON へ変換します。</remarks>
    /// <param name="summary">生ログの概要。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode SummaryToJson(ConnectionSessionRawLogSummary summary, SwiftJsonDateEncoding dates)
    {
        var obj = new JsonObject
        {
            ["recordCount"] = summary.RecordCount,
            ["byteCount"] = summary.ByteCount,
            ["localState"] = summary.LocalState.ToRawValue(),
            ["cloudState"] = summary.CloudState.ToRawValue(),
        };
        SwiftJsonValues.AddIfPresent(obj, "manifestDigest", summary.ManifestDigest is { } digest ? JsonValue.Create(digest) : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "macImportReceipt",
            summary.MacImportReceipt is { } receipt ? ReceiptToJson(receipt, dates) : null);
        SwiftJsonValues.AddIfPresent(
            obj,
            "lastAccessedAt",
            summary.LastAccessedAt is { } accessed ? SwiftJsonValues.Date(accessed, dates) : null);
        return obj;
    }

    /// <summary>
    /// JSON から生ログの概要を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを生ログ概要へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>生ログの概要。</returns>
    private static ConnectionSessionRawLogSummary SummaryFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        recordCount: Int64(Required(element, "recordCount")),
        byteCount: Int64(Required(element, "byteCount")),
        localState: Enum<ConnectionSessionLocalRawState>(Required(element, "localState"), ConnectionSessionLocalRawStateRawValue.TryParse),
        cloudState: Enum<ConnectionSessionCloudSyncState>(Required(element, "cloudState"), ConnectionSessionCloudSyncStateRawValue.TryParse),
        manifestDigest: Optional(element, "manifestDigest") is { } digest ? String(digest) : null,
        macImportReceipt: Optional(element, "macImportReceipt") is { } receipt ? ReceiptFromJson(receipt, dates) : null,
        lastAccessedAt: Optional(element, "lastAccessedAt") is { } accessed ? Date(accessed, dates) : null);

    /// <summary>
    /// Mac 取込受領証を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の受領証を宣言順のキーで JSON へ変換します。</remarks>
    /// <param name="receipt">受領証。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode ReceiptToJson(ConnectionSessionMacImportReceipt receipt, SwiftJsonDateEncoding dates) => new JsonObject
    {
        ["deviceID"] = receipt.DeviceID,
        ["deviceName"] = receipt.DeviceName,
        ["importedAt"] = SwiftJsonValues.Date(receipt.ImportedAt, dates),
        ["manifestDigest"] = receipt.ManifestDigest,
    };

    /// <summary>
    /// JSON から Mac 取込受領証を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを受領証へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>受領証。</returns>
    private static ConnectionSessionMacImportReceipt ReceiptFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        String(Required(element, "deviceID")),
        String(Required(element, "deviceName")),
        Date(Required(element, "importedAt"), dates),
        String(Required(element, "manifestDigest")));
}
