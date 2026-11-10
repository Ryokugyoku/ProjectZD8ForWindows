using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using static ProjectZD8.Data.Mapping.SwiftJsonReader;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// 整備記録（作業項目・写真・締結部品を含む）と Swift の <c>MaintenanceRecord</c>（合成 Codable）の JSON を相互に変換します。
/// </summary>
/// <remarks>
/// 作業項目・写真・締結の ID は ID 型ではなく素の <c>UUID</c> なので、大文字の文字列になります。
/// 整備記録の ID と車両 ID は <c>{"rawValue": "..."}</c> です。
/// </remarks>
public static class MaintenanceRecordJsonMapper
{
    /// <summary>
    /// 整備記録を JSON の木にします。
    /// </summary>
    /// <remarks>責務: 1 件の整備記録を Swift と同じキーと値の JSON オブジェクトへ変換します。</remarks>
    /// <param name="record">整備記録。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject ToJson(MaintenanceRecord record, SwiftJsonDateEncoding dates)
    {
        var obj = new JsonObject
        {
            ["id"] = SwiftJsonValues.Identifier(record.Id.RawValue),
            ["vehicleID"] = SwiftJsonValues.Identifier(record.VehicleID.RawValue),
            ["kind"] = record.Kind.ToRawValue(),
            ["title"] = record.Title,
            ["performedAt"] = SwiftJsonValues.Date(record.PerformedAt, dates),
        };
        SwiftJsonValues.AddIfPresent(obj, "odometerKilometers", record.OdometerKilometers is { } km ? JsonValue.Create(km) : null);
        obj["notes"] = record.Notes;
        obj["workItems"] = ToArray(record.WorkItems, WorkItemToJson);
        obj["photos"] = ToArray(record.Photos, photo => PhotoToJson(photo, dates));
        obj["fastenerGroups"] = ToArray(record.FastenerGroups, group => GroupToJson(group, dates));
        obj["createdAt"] = SwiftJsonValues.Date(record.CreatedAt, dates);
        obj["updatedAt"] = SwiftJsonValues.Date(record.UpdatedAt, dates);
        SwiftJsonValues.AddIfPresent(obj, "deletedAt", record.DeletedAt is { } deleted ? SwiftJsonValues.Date(deleted, dates) : null);
        return obj;
    }

    /// <summary>
    /// JSON から整備記録を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを、Swift の合成 Decodable と同じ規則で整備記録へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>整備記録。</returns>
    /// <exception cref="JsonException">必須のキーがないか、型が違う場合。</exception>
    public static MaintenanceRecord FromJson(JsonElement element, SwiftJsonDateEncoding dates)
    {
        Object(element);
        return new MaintenanceRecord(
            vehicleID: new VehicleID(Identifier(Required(element, "vehicleID"))),
            kind: Enum<MaintenanceKind>(Required(element, "kind"), MaintenanceKindRawValue.TryParse),
            title: String(Required(element, "title")),
            id: new MaintenanceRecordID(Identifier(Required(element, "id"))),
            performedAt: Date(Required(element, "performedAt"), dates),
            odometerKilometers: Optional(element, "odometerKilometers") is { } km ? Double(km) : null,
            notes: String(Required(element, "notes")),
            workItems: Array(Required(element, "workItems"), WorkItemFromJson),
            photos: Array(Required(element, "photos"), item => PhotoFromJson(item, dates)),
            fastenerGroups: Array(Required(element, "fastenerGroups"), item => GroupFromJson(item, dates)),
            createdAt: Date(Required(element, "createdAt"), dates),
            updatedAt: Date(Required(element, "updatedAt"), dates),
            deletedAt: Optional(element, "deletedAt") is { } deleted ? Date(deleted, dates) : null);
    }

    /// <summary>
    /// 一覧を JSON の配列にします。
    /// </summary>
    /// <remarks>責務: 各要素を変換関数で JSON にして配列へ並べます。</remarks>
    /// <typeparam name="T">要素の型。</typeparam>
    /// <param name="items">一覧。</param>
    /// <param name="convert">変換関数。</param>
    /// <returns>JSON の配列。</returns>
    private static JsonArray ToArray<T>(IEnumerable<T> items, Func<T, JsonNode> convert)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(convert(item));
        }
        return array;
    }

    /// <summary>
    /// UUID の一覧を JSON の配列にします。
    /// </summary>
    /// <remarks>責務: UUID の一覧を大文字の文字列の配列へ変換します。</remarks>
    /// <param name="ids">UUID の一覧。</param>
    /// <returns>JSON の配列。</returns>
    private static JsonArray UuidArray(IEnumerable<Guid> ids) => ToArray(ids, SwiftJsonValues.Uuid);

    /// <summary>
    /// 作業項目を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の作業項目を宣言順のキーで JSON へ変換します。</remarks>
    /// <param name="item">作業項目。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode WorkItemToJson(MaintenanceWorkItem item)
    {
        var obj = new JsonObject
        {
            ["id"] = SwiftJsonValues.Uuid(item.Id),
            ["component"] = item.Component.ToRawValue(),
            ["operation"] = item.Operation.ToRawValue(),
            ["partName"] = item.PartName,
            ["productName"] = item.ProductName,
            ["manufacturer"] = item.Manufacturer,
            ["specification"] = item.Specification,
        };
        SwiftJsonValues.AddIfPresent(obj, "quantity", item.Quantity is { } quantity ? JsonValue.Create(quantity) : null);
        obj["unit"] = item.Unit;
        obj["notes"] = item.Notes;
        return obj;
    }

    /// <summary>
    /// JSON から作業項目を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを作業項目へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <returns>作業項目。</returns>
    private static MaintenanceWorkItem WorkItemFromJson(JsonElement element) => new(
        component: Enum<MaintenanceComponent>(Required(element, "component"), MaintenanceComponentRawValue.TryParse),
        operation: Enum<MaintenanceOperation>(Required(element, "operation"), MaintenanceOperationRawValue.TryParse),
        id: Uuid(Required(element, "id")),
        partName: String(Required(element, "partName")),
        productName: String(Required(element, "productName")),
        manufacturer: String(Required(element, "manufacturer")),
        specification: String(Required(element, "specification")),
        quantity: Optional(element, "quantity") is { } quantity ? Double(quantity) : null,
        unit: String(Required(element, "unit")),
        notes: String(Required(element, "notes")));

    /// <summary>
    /// 整備写真を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の写真を base64 のデータ付き JSON へ変換します。</remarks>
    /// <param name="photo">写真。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode PhotoToJson(MaintenancePhoto photo, SwiftJsonDateEncoding dates) => new JsonObject
    {
        ["id"] = SwiftJsonValues.Uuid(photo.Id),
        ["data"] = SwiftJsonValues.Data(photo.Data),
        ["caption"] = photo.Caption,
        ["capturedAt"] = SwiftJsonValues.Date(photo.CapturedAt, dates),
    };

    /// <summary>
    /// JSON から整備写真を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを写真へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>写真。</returns>
    private static MaintenancePhoto PhotoFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        data: Base64Data(Required(element, "data")),
        id: Uuid(Required(element, "id")),
        caption: String(Required(element, "caption")),
        capturedAt: Date(Required(element, "capturedAt"), dates));

    /// <summary>
    /// 締結部品グループを JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の締結部品グループを宣言順のキーで JSON へ変換します。</remarks>
    /// <param name="group">締結部品グループ。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode GroupToJson(MaintenanceFastenerGroup group, SwiftJsonDateEncoding dates)
    {
        var obj = new JsonObject
        {
            ["id"] = SwiftJsonValues.Uuid(group.Id),
            ["name"] = group.Name,
            ["location"] = group.Location,
        };
        SwiftJsonValues.AddIfPresent(obj, "expectedCount", group.ExpectedCount is { } expected ? JsonValue.Create(expected) : null);
        obj["removedCount"] = group.RemovedCount;
        obj["removalNotes"] = group.RemovalNotes;
        obj["removalPhotoIDs"] = UuidArray(group.RemovalPhotoIDs);
        obj["installations"] = ToArray(group.Installations, installation => InstallationToJson(installation, dates));
        return obj;
    }

    /// <summary>
    /// JSON から締結部品グループを復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを締結部品グループへ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>締結部品グループ。</returns>
    private static MaintenanceFastenerGroup GroupFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        name: String(Required(element, "name")),
        id: Uuid(Required(element, "id")),
        location: String(Required(element, "location")),
        expectedCount: Optional(element, "expectedCount") is { } expected ? Int32(expected) : null,
        removedCount: Int32(Required(element, "removedCount")),
        removalNotes: String(Required(element, "removalNotes")),
        removalPhotoIDs: Array(Required(element, "removalPhotoIDs"), Uuid),
        installations: Array(Required(element, "installations"), item => InstallationFromJson(item, dates)));

    /// <summary>
    /// 締結の証跡を JSON にします。
    /// </summary>
    /// <remarks>責務: 1 件の締結の証跡を宣言順のキーで JSON へ変換します。</remarks>
    /// <param name="installation">締結の証跡。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonNode InstallationToJson(FastenerInstallationEvidence installation, SwiftJsonDateEncoding dates)
    {
        var obj = new JsonObject
        {
            ["id"] = SwiftJsonValues.Uuid(installation.Id),
            ["position"] = installation.Position,
        };
        SwiftJsonValues.AddIfPresent(obj, "torqueNewtonMeters", installation.TorqueNewtonMeters is { } torque ? JsonValue.Create(torque) : null);
        obj["tool"] = installation.Tool;
        obj["tightenedBy"] = installation.TightenedBy;
        SwiftJsonValues.AddIfPresent(obj, "tightenedAt", installation.TightenedAt is { } at ? SwiftJsonValues.Date(at, dates) : null);
        obj["photoIDs"] = UuidArray(installation.PhotoIDs);
        obj["notes"] = installation.Notes;
        return obj;
    }

    /// <summary>
    /// JSON から締結の証跡を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを締結の証跡へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>締結の証跡。</returns>
    private static FastenerInstallationEvidence InstallationFromJson(JsonElement element, SwiftJsonDateEncoding dates) => new(
        position: String(Required(element, "position")),
        id: Uuid(Required(element, "id")),
        torqueNewtonMeters: Optional(element, "torqueNewtonMeters") is { } torque ? Double(torque) : null,
        tool: String(Required(element, "tool")),
        tightenedBy: String(Required(element, "tightenedBy")),
        tightenedAt: Optional(element, "tightenedAt") is { } at ? Date(at, dates) : null,
        photoIDs: Array(Required(element, "photoIDs"), Uuid),
        notes: String(Required(element, "notes")));
}
