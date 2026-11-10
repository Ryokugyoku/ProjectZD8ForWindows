using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Domain.Entities;

/// <summary>
/// 接続セッションの転送互換性を検証します（iOS の ConnectionSessionCodableTests）。
/// </summary>
public sealed class ConnectionSessionCodableTests
{
    /// <summary>
    /// 生ログの転送パッケージが表示用の情報を含まず、安定したセッション ID だけを持つことを確かめます。
    /// </summary>
    /// <remarks>責務: 接続セッションから作った転送 JSON が、識別情報と生ログだけの形であることを確かめます。</remarks>
    [Fact]
    public void TestRawTransferPackageEncodesOnlyStableOwnershipAndEntries()
    {
        var session = new ConnectionSession("account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(200),
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };
        var package = new ConnectionSessionTransferPackage(session, []);

        var encoded = SwiftJsonWriter.Write(
            ConnectionSessionTransferJsonMapper.PackageToJson(package, SwiftJsonDateEncoding.SecondsSinceReferenceDate),
            SwiftJsonEncodingOptions.Default);
        var obj = JsonNode.Parse(encoded)!.AsObject();
        var decoded = SwiftJsonReader.Parse(
            encoded,
            root => ConnectionSessionTransferJsonMapper.PackageFromJson(root, SwiftJsonDateEncoding.SecondsSinceReferenceDate));

        Assert.Equal(
            new HashSet<string> { "accountIdentifier", "entries", "sessionID" },
            obj.Select(pair => pair.Key).ToHashSet());
        Assert.Equal(session.Id, decoded.SessionID);
        Assert.Equal(session.AccountIdentifier, decoded.AccountIdentifier);
        Assert.Empty(decoded.Entries);
    }

    /// <summary>
    /// 停止確認のキーがない古いデータを、未確認の状態として復元することを確かめます。
    /// </summary>
    /// <remarks>責務: 停止確認を追加する前の JSON を、後方互換の Domain の状態へ復元できることを確かめます。</remarks>
    [Fact]
    public void TestDecodeLegacyPayloadWithoutStopReviewDecision()
    {
        var session = new ConnectionSession("account", startedAt: DateTimeOffset.FromUnixTimeSeconds(100)) with
        {
            EndedAt = DateTimeOffset.FromUnixTimeSeconds(200),
            EndReason = ConnectionSessionEndReason.ConnectionLost,
        };
        var obj = ConnectionSessionJsonMapper.ToJson(session, SwiftJsonDateEncoding.SecondsSinceReferenceDate);
        obj.Remove("stopReviewDecision");
        obj.Remove("acquisitionDevice");
        var legacyData = SwiftJsonWriter.Write(obj, SwiftJsonEncodingOptions.Default);

        var decoded = SwiftJsonReader.Parse(
            legacyData,
            root => ConnectionSessionJsonMapper.FromJson(root, SwiftJsonDateEncoding.SecondsSinceReferenceDate));

        Assert.Null(decoded.StopReviewDecision);
        Assert.Null(decoded.AcquisitionDevice);
        Assert.True(decoded.NeedsStopReview);
        Assert.Equal(ConnectionSessionStatus.Interrupted, decoded.Status);
    }

    /// <summary>
    /// Swift が書く古い形式の JSON（停止確認と取得端末なし）をそのまま読めることを確かめます。
    /// </summary>
    /// <remarks>責務: リテラルの JSON から、iOS の古いデータを復元できることを確かめます。</remarks>
    [Fact]
    public void TestDecodesLiteralLegacySwiftSession()
    {
        const string json = """
            {"id":{"rawValue":"9F0E6B3A-1C2D-4E5F-8A9B-0C1D2E3F4A5B"},"accountIdentifier":"001234.abcd","startedAt":-978307100,
             "endedAt":-978307000,"endReason":"vehicleNoResponse",
             "vehicle":{"id":{"rawValue":"00000000-0000-0000-0000-000000000001"},"name":"ZD8","displayIdentifier":"JF1ZD8"},
             "startingOdometerKilometers":12000.5,"endingOdometerKilometers":12010.25,"distanceSourceModelCode":"ZD8",
             "rawLogSummary":{"recordCount":3,"byteCount":42,"localState":"available","cloudState":"uploaded","manifestDigest":"ab"}}
            """;

        var decoded = SwiftJsonReader.Parse(
            System.Text.Encoding.UTF8.GetBytes(json),
            root => ConnectionSessionJsonMapper.FromJson(root, SwiftJsonDateEncoding.SecondsSinceReferenceDate));

        Assert.Equal(Guid.Parse("9F0E6B3A-1C2D-4E5F-8A9B-0C1D2E3F4A5B"), decoded.Id.RawValue);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(100), decoded.StartedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), decoded.EndedAt);
        Assert.Equal(ConnectionSessionEndReason.VehicleNoResponse, decoded.EndReason);
        Assert.Equal("JF1ZD8", decoded.Vehicle!.DisplayIdentifier);
        Assert.Equal(9.75, decoded.RecordedDistanceKilometers);
        Assert.Equal(ConnectionSessionLocalRawState.Available, decoded.RawLogSummary.LocalState);
        Assert.Null(decoded.RawLogSummary.MacImportReceipt);
        Assert.True(decoded.NeedsStopReview);
    }

    /// <summary>
    /// 生ログの概要がないなど、必須のキーが欠けた JSON は読めないことを確かめます（Swift の DecodingError と同じ）。
    /// </summary>
    /// <remarks>責務: 必須のキーの検査を確かめます。</remarks>
    [Fact]
    public void TestMissingRequiredKeyFails()
    {
        const string json = """{"id":{"rawValue":"9F0E6B3A-1C2D-4E5F-8A9B-0C1D2E3F4A5B"},"accountIdentifier":"a","startedAt":0}""";

        Assert.Throws<JsonException>(() => SwiftJsonReader.Parse(
            System.Text.Encoding.UTF8.GetBytes(json),
            root => ConnectionSessionJsonMapper.FromJson(root, SwiftJsonDateEncoding.SecondsSinceReferenceDate)));
    }
}
