using System.Text;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Mapping;

/// <summary>
/// Swift の JSONEncoder が書くはずの JSON（リテラル）と、C# の出力・読み込みが一致することを確かめます。
/// </summary>
/// <remarks>
/// リテラルは、Swift の合成 Codable の規則（宣言順、nil は省略、UUID は大文字、Data は base64、[UInt8] は数値の配列、
/// 既定の日付は 2001 年基準の秒、既定では <c>/</c> を <c>\/</c>）から手で書いたものです。実機の出力とは突き合わせていません。
/// </remarks>
public sealed class SwiftJsonFixtureTests
{
    /// <summary>テストのセッション ID です。</summary>
    private static readonly Guid SessionUuid = Guid.Parse("9f0e6b3a-1c2d-4e5f-8a9b-0c1d2e3f4a5b");

    /// <summary>
    /// 車両カタログが Swift の既定の JSONEncoder と同じバイト列になることを確かめます。
    /// </summary>
    /// <remarks>責務: 宣言順・省略・日付・ID の形を確かめます。</remarks>
    [Fact]
    public void VehicleCatalogMatchesSwiftDefaultEncoder()
    {
        var profile = new VehicleProfile(
            vin: "1D4GP00R55B123456",
            name: "テスト車両",
            id: new VehicleID(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            updatedAt: DateTimeOffset.FromUnixTimeSeconds(100));

        var json = Encoding.UTF8.GetString(CloudKitPayloadJsonCodec.EncodeVehicleCatalog([profile]));

        Assert.Equal(
            """[{"id":{"rawValue":"00000000-0000-0000-0000-000000000001"},"vin":"1D4GP00R55B123456","name":"テスト車両","manufacturer":"","engineModel":"","powertrain":"combustion","energySources":["gasolineRegular"],"note":"","isDefault":false,"updatedAt":-978307100}]""",
            json);
    }

    /// <summary>
    /// 写真などの base64 の <c>/</c> が、Swift の既定どおり <c>\/</c> になることを確かめます。
    /// </summary>
    /// <remarks>責務: スラッシュのエスケープと Optional の値の出力を確かめます。</remarks>
    [Fact]
    public void VehicleCatalogEscapesSlashesAndWritesOptionals()
    {
        var profile = new VehicleProfile(
            vin: "",
            name: "A/B",
            id: new VehicleID(Guid.Parse("00000000-0000-0000-0000-000000000002")),
            obdIdentifier: "ZD8",
            powertrain: VehiclePowertrainKind.PlugInHybrid,
            energySources: [VehicleEnergySource.GasolinePremium, VehicleEnergySource.Electricity],
            tankCapacityLiters: 50.5,
            modelYear: 2024,
            photoData: [0xFF, 0xFF],
            isDefault: true,
            updatedAt: DateTimeOffset.FromUnixTimeMilliseconds(978307200500));

        var json = Encoding.UTF8.GetString(CloudKitPayloadJsonCodec.EncodeVehicleCatalog([profile]));

        Assert.Equal(
            """[{"id":{"rawValue":"00000000-0000-0000-0000-000000000002"},"vin":"","obdIdentifier":"ZD8","name":"A\/B","manufacturer":"","engineModel":"","powertrain":"plugInHybrid","energySources":["gasolinePremium","electricity"],"tankCapacityLiters":50.5,"modelYear":2024,"note":"","photoData":"\/\/8=","isDefault":true,"updatedAt":0.5}]""",
            json);
        Assert.Equal(profile, CloudKitPayloadJsonCodec.DecodeVehicleCatalog(Encoding.UTF8.GetBytes(json)).Single());
    }

    /// <summary>
    /// iOS が書いた車両カタログ（キーの順番が違っても）を読めることを確かめます。
    /// </summary>
    /// <remarks>責務: 読み込みがキーの順番に依存しないこと、小文字の UUID も読めることを確かめます。</remarks>
    [Fact]
    public void VehicleCatalogDecodesSwiftOutputInAnyKeyOrder()
    {
        const string json = """[{"updatedAt":752112000.25,"isDefault":false,"note":"memo","energySources":["diesel"],"powertrain":"combustion","engineModel":"FA24","manufacturer":"SUBARU","name":"BRZ","vin":"","id":{"rawValue":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"},"unknownFutureKey":1}]""";

        var vehicle = CloudKitPayloadJsonCodec.DecodeVehicleCatalog(Encoding.UTF8.GetBytes(json)).Single();

        Assert.Equal(Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"), vehicle.Id.RawValue);
        Assert.Equal(SwiftJsonDate.ReferenceDate.AddSeconds(752112000.25), vehicle.UpdatedAt);
        Assert.Equal([VehicleEnergySource.Diesel], vehicle.EnergySources);
        Assert.Null(vehicle.ObdIdentifier);
        Assert.Null(vehicle.PhotoData);
    }

    /// <summary>
    /// 生ログの転送パッケージが、キーを並べ替えミリ秒の日付にした Swift の出力と同じになることを確かめます。
    /// </summary>
    /// <remarks>責務: sortedKeys・millisecondsSince1970・[UInt8] の数値の配列を確かめます。</remarks>
    [Fact]
    public void TransferPackageMatchesSwiftSortedMillisecondsEncoder()
    {
        var package = new ConnectionSessionTransferPackage(
            new ConnectionSessionID(SessionUuid),
            "account",
            [new ConnectionSessionRawLogEntry(1, DateTimeOffset.FromUnixTimeMilliseconds(100500), 1500, 0x01, 0x0C, [0x1A, 0xF8])]);

        var json = Encoding.UTF8.GetString(CloudKitPayloadJsonCodec.EncodeTransferPackage(package));

        Assert.Equal(
            """{"accountIdentifier":"account","entries":[{"batchElapsedNanoseconds":1500,"observedAt":100500,"payload":[26,248],"pid":12,"sequence":1,"service":1}],"sessionID":{"rawValue":"9F0E6B3A-1C2D-4E5F-8A9B-0C1D2E3F4A5B"}}""",
            json);
        Assert.Equal(package, CloudKitPayloadJsonCodec.DecodeTransferPackage(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// セッション概要が、キーを並べ替えた（大文字小文字を区別しない）Swift の出力と同じになることを確かめます。
    /// </summary>
    /// <remarks>責務: 入れ子のオブジェクトのキーも並べ替わること、endedAt が endReason より前に来ることを確かめます。</remarks>
    [Fact]
    public void MetadataMatchesSwiftSortedEncoder()
    {
        var session = new ConnectionSession("account", new ConnectionSessionID(SessionUuid), DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(200),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
            AcquisitionDevice = new ConnectionSessionAcquisitionDevice(ConnectionSessionAcquisitionPlatform.IPhone, "iPhone/15"),
        };
        var metadata = new ConnectionSessionCloudMetadata(session, "abc");

        var json = Encoding.UTF8.GetString(CloudKitPayloadJsonCodec.EncodeMetadata(metadata));

        Assert.Equal(
            """{"manifestDigest":"abc","session":{"accountIdentifier":"account","acquisitionDevice":{"name":"iPhone/15","platform":"iPhone"},"endedAt":200000,"endReason":"userDisconnected","id":{"rawValue":"9F0E6B3A-1C2D-4E5F-8A9B-0C1D2E3F4A5B"},"rawLogSummary":{"byteCount":0,"cloudState":"notUploaded","localState":"empty","recordCount":0},"startedAt":100000}}""",
            json);
        Assert.Equal(metadata, CloudKitPayloadJsonCodec.DecodeMetadata(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// 整備カタログが Swift の既定の JSONEncoder と同じになり、往復できることを確かめます。
    /// </summary>
    /// <remarks>責務: 素の UUID（作業項目・写真）と ID 型（整備記録・車両）の違い、入れ子の順番を確かめます。</remarks>
    [Fact]
    public void MaintenanceCatalogMatchesSwiftDefaultEncoder()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(978307300);
        var record = new MaintenanceRecord(
            vehicleID: new VehicleID(Guid.Parse("00000000-0000-0000-0000-000000000001")),
            kind: MaintenanceKind.Light,
            title: "オイル交換",
            id: new MaintenanceRecordID(Guid.Parse("00000000-0000-0000-0000-0000000000AA")),
            performedAt: at,
            workItems:
            [
                new MaintenanceWorkItem(
                    MaintenanceComponent.EngineOil,
                    MaintenanceOperation.Replacement,
                    id: Guid.Parse("00000000-0000-0000-0000-0000000000BB"),
                    quantity: 4.5,
                    unit: "L"),
            ],
            photos: [new MaintenancePhoto([1, 2, 3], Guid.Parse("00000000-0000-0000-0000-0000000000CC"), "前", at)],
            fastenerGroups:
            [
                new MaintenanceFastenerGroup(
                    "ドレンボルト",
                    Guid.Parse("00000000-0000-0000-0000-0000000000DD"),
                    expectedCount: 1,
                    removedCount: 1,
                    installations:
                    [
                        new FastenerInstallationEvidence(
                            "オイルパン",
                            Guid.Parse("00000000-0000-0000-0000-0000000000EE"),
                            torqueNewtonMeters: 39,
                            photoIDs: [Guid.Parse("00000000-0000-0000-0000-0000000000CC")]),
                    ]),
            ],
            createdAt: at,
            updatedAt: at);

        var json = Encoding.UTF8.GetString(CloudKitPayloadJsonCodec.EncodeMaintenanceCatalog([record]));

        Assert.Equal(
            """[{"id":{"rawValue":"00000000-0000-0000-0000-0000000000AA"},"vehicleID":{"rawValue":"00000000-0000-0000-0000-000000000001"},"kind":"light","title":"オイル交換","performedAt":100,"notes":"","workItems":[{"id":"00000000-0000-0000-0000-0000000000BB","component":"engineOil","operation":"replacement","partName":"","productName":"","manufacturer":"","specification":"","quantity":4.5,"unit":"L","notes":""}],"photos":[{"id":"00000000-0000-0000-0000-0000000000CC","data":"AQID","caption":"前","capturedAt":100}],"fastenerGroups":[{"id":"00000000-0000-0000-0000-0000000000DD","name":"ドレンボルト","location":"","expectedCount":1,"removedCount":1,"removalNotes":"","removalPhotoIDs":[],"installations":[{"id":"00000000-0000-0000-0000-0000000000EE","position":"オイルパン","torqueNewtonMeters":39,"tool":"","tightenedBy":"","photoIDs":["00000000-0000-0000-0000-0000000000CC"],"notes":""}]}],"createdAt":100,"updatedAt":100}]""",
            json);
        Assert.Equal(record, CloudKitPayloadJsonCodec.DecodeMaintenanceCatalog(Encoding.UTF8.GetBytes(json)).Single());
    }

    /// <summary>
    /// アカウント設定が、並べ替えたキーで書かれ、iOS の並べ替えなしの JSON も読めることを確かめます。
    /// </summary>
    /// <remarks>責務: settingsData の形と、不完全な JSON を <c>null</c> にすることを確かめます。</remarks>
    [Fact]
    public void AccountSettingsUseSortedKeysAndRejectIncompleteJson()
    {
        var settings = new AccountSettings(SettingsLanguage.English, SettingsAppearance.Dark, automaticSessionUploadEnabled: true);

        Assert.Equal(
            """{"appearance":"dark","automaticSessionUploadEnabled":true,"language":"english"}""",
            Encoding.UTF8.GetString(AccountSettingsJsonMapper.Encode(settings)));
        Assert.Equal(
            settings,
            AccountSettingsJsonMapper.TryDecode(Encoding.UTF8.GetBytes(
                """{"language":"english","appearance":"dark","automaticSessionUploadEnabled":true}""")));
        Assert.Null(AccountSettingsJsonMapper.TryDecode(Encoding.UTF8.GetBytes("""{"language":"english","appearance":"dark"}""")));
        Assert.Null(AccountSettingsJsonMapper.TryDecode(Encoding.UTF8.GetBytes("""{"language":"french","appearance":"dark","automaticSessionUploadEnabled":false}""")));
    }

    /// <summary>
    /// Swift の日付の数値と日時の変換が往復することを確かめます。
    /// </summary>
    /// <remarks>責務: 2 つの日付の方式の基準を確かめます。</remarks>
    [Fact]
    public void DateEncodingsUseSwiftReferencePoints()
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds(1_731_234_567_890);

        Assert.Equal(1_731_234_567_890d, SwiftJsonDate.ToNumber(date, SwiftJsonDateEncoding.MillisecondsSince1970));
        Assert.Equal(1_731_234_567.890d - 978_307_200d, SwiftJsonDate.ToNumber(date, SwiftJsonDateEncoding.SecondsSinceReferenceDate), 6);
        Assert.Equal(date, SwiftJsonDate.FromNumber(752_927_367.89, SwiftJsonDateEncoding.SecondsSinceReferenceDate));
    }
}
