using ProjectZD8.Data.Mapping;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Domain.Entities;

/// <summary>
/// 車両プロフィールの保存形式の互換性を検証します（iOS の VehicleProfileCodableTests）。
/// </summary>
public sealed class VehicleProfileCodableTests
{
    /// <summary>
    /// OBD 由来の識別子を追加する前の保存データを復元できることを確かめます。
    /// </summary>
    /// <remarks>責務: obdIdentifier を持たない既存の JSON が、引き続き車両プロフィールへ復号できることを確かめます。</remarks>
    [Fact]
    public void TestDecodesLegacyProfileWithoutOBDIdentifier()
    {
        var profile = MakeProfile(obdIdentifier: null);
        var obj = VehicleProfileJsonMapper.ToJson(profile, SwiftJsonDateEncoding.SecondsSinceReferenceDate);
        obj.Remove("obdIdentifier");

        var decoded = SwiftJsonReader.Parse(
            SwiftJsonWriter.Write(obj, SwiftJsonEncodingOptions.Default),
            root => VehicleProfileJsonMapper.FromJson(root, SwiftJsonDateEncoding.SecondsSinceReferenceDate));

        Assert.Equal(profile, decoded);
        Assert.Null(decoded.ObdIdentifier);
    }

    /// <summary>
    /// VIN でない OBD 由来の識別子を保存して復元できることを確かめます。
    /// </summary>
    /// <remarks>責務: VIN を持たない登録車両の OBD 由来の識別子が、往復で保たれることを確かめます。</remarks>
    [Fact]
    public void TestRoundTripsProfileWithOBDIdentifier()
    {
        var profile = MakeProfile(obdIdentifier: "ZD81234567");

        var decoded = CloudKitPayloadJsonCodec.DecodeVehicleCatalog(CloudKitPayloadJsonCodec.EncodeVehicleCatalog([profile])).Single();

        Assert.Equal(profile, decoded);
        Assert.Equal("ZD81234567", decoded.DisplayIdentifier);
    }

    /// <summary>
    /// 固定値の車両プロフィールを作ります。
    /// </summary>
    /// <remarks>責務: 保存形式のテストで比べる 1 台分の再現できるプロフィールを作ります。</remarks>
    /// <param name="obdIdentifier">VIN とは別に保存する OBD 由来の識別子。</param>
    /// <returns>固定の日時と ID を持つテスト用のプロフィール。</returns>
    private static VehicleProfile MakeProfile(string? obdIdentifier) => new(
        vin: obdIdentifier is null ? "1D4GP00R55B123456" : "",
        name: "テスト車両",
        id: new VehicleID(Guid.Parse("00000000-0000-0000-0000-000000000001")),
        obdIdentifier: obdIdentifier,
        updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));
}
