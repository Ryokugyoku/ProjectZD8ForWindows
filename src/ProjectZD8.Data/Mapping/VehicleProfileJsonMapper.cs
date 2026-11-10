using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;
using static ProjectZD8.Data.Mapping.SwiftJsonReader;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// 車両プロフィールと Swift の <c>VehicleProfile</c>（合成 Codable）の JSON を相互に変換します。
/// </summary>
/// <remarks>
/// キーは宣言順：id、vin、obdIdentifier、name、manufacturer、engineModel、powertrain、energySources、
/// tankCapacityLiters、modelYear、note、photoData、isDefault、updatedAt。<c>nil</c> のキーは省きます。
/// <c>obdIdentifier</c> がない古いデータも読めます。
/// </remarks>
public static class VehicleProfileJsonMapper
{
    /// <summary>
    /// 車両プロフィールを JSON の木にします。
    /// </summary>
    /// <remarks>責務: 1 件の車両プロフィールを Swift と同じキーと値の JSON オブジェクトへ変換します。</remarks>
    /// <param name="vehicle">車両プロフィール。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject ToJson(VehicleProfile vehicle, SwiftJsonDateEncoding dates)
    {
        var obj = new JsonObject
        {
            ["id"] = SwiftJsonValues.Identifier(vehicle.Id.RawValue),
            ["vin"] = vehicle.Vin,
        };
        SwiftJsonValues.AddIfPresent(obj, "obdIdentifier", vehicle.ObdIdentifier is { } obd ? JsonValue.Create(obd) : null);
        obj["name"] = vehicle.Name;
        obj["manufacturer"] = vehicle.Manufacturer;
        obj["engineModel"] = vehicle.EngineModel;
        obj["powertrain"] = vehicle.Powertrain.ToRawValue();
        var sources = new JsonArray();
        foreach (var source in vehicle.EnergySources)
        {
            sources.Add(JsonValue.Create(source.ToRawValue()));
        }
        obj["energySources"] = sources;
        SwiftJsonValues.AddIfPresent(obj, "tankCapacityLiters", vehicle.TankCapacityLiters is { } tank ? JsonValue.Create(tank) : null);
        SwiftJsonValues.AddIfPresent(obj, "modelYear", vehicle.ModelYear is { } year ? JsonValue.Create(year) : null);
        obj["note"] = vehicle.Note;
        SwiftJsonValues.AddIfPresent(obj, "photoData", vehicle.PhotoData is { } photo ? SwiftJsonValues.Data(photo) : null);
        obj["isDefault"] = vehicle.IsDefault;
        obj["updatedAt"] = SwiftJsonValues.Date(vehicle.UpdatedAt, dates);
        return obj;
    }

    /// <summary>
    /// JSON から車両プロフィールを復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを、Swift の合成 Decodable と同じ規則で車両プロフィールへ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <param name="dates">日付の方式。</param>
    /// <returns>車両プロフィール。</returns>
    /// <exception cref="JsonException">必須のキーがないか、型が違う場合。</exception>
    public static VehicleProfile FromJson(JsonElement element, SwiftJsonDateEncoding dates)
    {
        Object(element);
        return new VehicleProfile(
            vin: String(Required(element, "vin")),
            name: String(Required(element, "name")),
            id: new VehicleID(Identifier(Required(element, "id"))),
            obdIdentifier: Optional(element, "obdIdentifier") is { } obd ? String(obd) : null,
            manufacturer: String(Required(element, "manufacturer")),
            engineModel: String(Required(element, "engineModel")),
            powertrain: Enum<VehiclePowertrainKind>(Required(element, "powertrain"), VehiclePowertrainKindRawValue.TryParse),
            energySources: Array(
                Required(element, "energySources"),
                item => Enum<VehicleEnergySource>(item, VehicleEnergySourceRawValue.TryParse)),
            tankCapacityLiters: Optional(element, "tankCapacityLiters") is { } tank ? Double(tank) : null,
            modelYear: Optional(element, "modelYear") is { } year ? Int32(year) : null,
            note: String(Required(element, "note")),
            photoData: Optional(element, "photoData") is { } photo ? Base64Data(photo) : null,
            isDefault: Bool(Required(element, "isDefault")),
            updatedAt: Date(Required(element, "updatedAt"), dates));
    }
}
