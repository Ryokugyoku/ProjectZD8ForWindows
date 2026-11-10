using System.Text.Json.Nodes;
using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// iOS 版が CloudKit に置く JSON（車両カタログ、整備カタログ、生ログ転送、セッション概要）を、同じ形で作り・読みます。
/// </summary>
/// <remarks>
/// 車両カタログと整備カタログは Swift の既定の <c>JSONEncoder()</c>（2001 年基準の秒、宣言順、<c>\/</c>）。
/// 生ログ転送とセッション概要は <c>[.sortedKeys, .withoutEscapingSlashes]</c> と <c>.millisecondsSince1970</c>。
/// </remarks>
public static class CloudKitPayloadJsonCodec
{
    /// <summary>
    /// 車両カタログ（VehicleCatalog の catalogAsset）を作ります。
    /// </summary>
    /// <remarks>責務: 車両プロフィールの一覧を、iOS 版の <c>JSONEncoder().encode([VehicleProfile])</c> と同じ JSON へ変換します。</remarks>
    /// <param name="vehicles">車両プロフィールの一覧（並び順はそのまま）。</param>
    /// <returns>UTF-8 の JSON。</returns>
    public static byte[] EncodeVehicleCatalog(IEnumerable<VehicleProfile> vehicles)
    {
        var array = new JsonArray();
        foreach (var vehicle in vehicles)
        {
            array.Add(VehicleProfileJsonMapper.ToJson(vehicle, SwiftJsonDateEncoding.SecondsSinceReferenceDate));
        }
        return SwiftJsonWriter.Write(array, SwiftJsonEncodingOptions.Default);
    }

    /// <summary>
    /// 車両カタログを読みます。
    /// </summary>
    /// <remarks>責務: catalogAsset の JSON を車両プロフィールの一覧へ変換します。</remarks>
    /// <param name="data">UTF-8 の JSON。</param>
    /// <returns>車両プロフィールの一覧。</returns>
    /// <exception cref="System.Text.Json.JsonException">読めない場合。</exception>
    public static IReadOnlyList<VehicleProfile> DecodeVehicleCatalog(ReadOnlySpan<byte> data) =>
        SwiftJsonReader.Parse(
            data,
            root => SwiftJsonReader.Array(
                root,
                item => VehicleProfileJsonMapper.FromJson(item, SwiftJsonDateEncoding.SecondsSinceReferenceDate)));

    /// <summary>
    /// 整備カタログ（MaintenanceCatalog の catalogAsset）を作ります。
    /// </summary>
    /// <remarks>責務: 整備記録の一覧を、iOS 版の <c>JSONEncoder().encode([MaintenanceRecord])</c> と同じ JSON へ変換します。</remarks>
    /// <param name="records">整備記録の一覧。</param>
    /// <returns>UTF-8 の JSON。</returns>
    public static byte[] EncodeMaintenanceCatalog(IEnumerable<MaintenanceRecord> records)
    {
        var array = new JsonArray();
        foreach (var record in records)
        {
            array.Add(MaintenanceRecordJsonMapper.ToJson(record, SwiftJsonDateEncoding.SecondsSinceReferenceDate));
        }
        return SwiftJsonWriter.Write(array, SwiftJsonEncodingOptions.Default);
    }

    /// <summary>
    /// 整備カタログを読みます。
    /// </summary>
    /// <remarks>責務: catalogAsset の JSON を整備記録の一覧へ変換します。</remarks>
    /// <param name="data">UTF-8 の JSON。</param>
    /// <returns>整備記録の一覧。</returns>
    /// <exception cref="System.Text.Json.JsonException">読めない場合。</exception>
    public static IReadOnlyList<MaintenanceRecord> DecodeMaintenanceCatalog(ReadOnlySpan<byte> data) =>
        SwiftJsonReader.Parse(
            data,
            root => SwiftJsonReader.Array(
                root,
                item => MaintenanceRecordJsonMapper.FromJson(item, SwiftJsonDateEncoding.SecondsSinceReferenceDate)));

    /// <summary>
    /// 生ログの転送パッケージ（ConnectionSessionRawLog の payloadAsset）を作ります。
    /// </summary>
    /// <remarks>責務: 1 件の転送パッケージを、キーを並べ替えミリ秒の日付にした JSON へ変換します。</remarks>
    /// <param name="package">転送パッケージ。</param>
    /// <returns>UTF-8 の JSON。</returns>
    public static byte[] EncodeTransferPackage(ConnectionSessionTransferPackage package) =>
        SwiftJsonWriter.Write(
            ConnectionSessionTransferJsonMapper.PackageToJson(package, SwiftJsonDateEncoding.MillisecondsSince1970),
            SwiftJsonEncodingOptions.SessionTransfer);

    /// <summary>
    /// 生ログの転送パッケージを読みます。
    /// </summary>
    /// <remarks>責務: payloadAsset の JSON を転送パッケージへ変換します。</remarks>
    /// <param name="data">UTF-8 の JSON。</param>
    /// <returns>転送パッケージ。</returns>
    /// <exception cref="System.Text.Json.JsonException">読めない場合。</exception>
    public static ConnectionSessionTransferPackage DecodeTransferPackage(ReadOnlySpan<byte> data) =>
        SwiftJsonReader.Parse(
            data,
            root => ConnectionSessionTransferJsonMapper.PackageFromJson(root, SwiftJsonDateEncoding.MillisecondsSince1970));

    /// <summary>
    /// セッション概要（ConnectionSessionMetadata の metadataData）を作ります。
    /// </summary>
    /// <remarks>責務: 1 件のセッション概要を、キーを並べ替えミリ秒の日付にした JSON へ変換します。</remarks>
    /// <param name="metadata">セッション概要。</param>
    /// <returns>UTF-8 の JSON。</returns>
    public static byte[] EncodeMetadata(ConnectionSessionCloudMetadata metadata) =>
        SwiftJsonWriter.Write(
            ConnectionSessionTransferJsonMapper.MetadataToJson(metadata, SwiftJsonDateEncoding.MillisecondsSince1970),
            SwiftJsonEncodingOptions.SessionTransfer);

    /// <summary>
    /// セッション概要を読みます。
    /// </summary>
    /// <remarks>責務: metadataData の JSON をセッション概要へ変換します。</remarks>
    /// <param name="data">UTF-8 の JSON。</param>
    /// <returns>セッション概要。</returns>
    /// <exception cref="System.Text.Json.JsonException">読めない場合。</exception>
    public static ConnectionSessionCloudMetadata DecodeMetadata(ReadOnlySpan<byte> data) =>
        SwiftJsonReader.Parse(
            data,
            root => ConnectionSessionTransferJsonMapper.MetadataFromJson(root, SwiftJsonDateEncoding.MillisecondsSince1970));
}
