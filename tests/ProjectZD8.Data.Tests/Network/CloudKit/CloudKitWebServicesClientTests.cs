using System.Net;
using System.Text;
using System.Text.Json;
using ProjectZD8.Data.Configuration;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Network.CloudKit;

/// <summary>
/// CloudKit Web Services のクライアントが、Apple の資料どおりの要求を作り、応答を読むことを偽のハンドラーで確かめます。
/// </summary>
/// <remarks>実際の CloudKit には接続していません。応答の JSON は Apple の資料の例に合わせて書いたものです。</remarks>
public sealed class CloudKitWebServicesClientTests
{
    /// <summary>テスト用の設定です。</summary>
    private static readonly CloudServiceConfiguration Configuration = new()
    {
        AppleServicesId = "com.example.web",
        AppleRedirectUri = new Uri("https://example.com/apple-callback.html"),
        CloudKitContainerId = "iCloud.Ryokugyoku.ProjectZD8",
        CloudKitEnvironment = "development",
        CloudKitApiToken = "api-token",
    };

    /// <summary>
    /// メモリ上のトークンの保存先です。
    /// </summary>
    private sealed class MemoryTokenStore : ICloudKitWebAuthTokenStore
    {
        /// <summary>保存中のトークンです。</summary>
        public string? Token { get; set; }

        /// <inheritdoc />
        public string? Load() => Token;

        /// <inheritdoc />
        public void Save(string token) => Token = token;

        /// <inheritdoc />
        public void Remove() => Token = null;
    }

    /// <summary>
    /// 問い合わせが、URL エンコードしたトークン付きの URL と、等値条件の JSON で送られ、次のページも読むことを確かめます。
    /// </summary>
    /// <remarks>責務: records/query の要求の形とページ送りを確かめます。</remarks>
    [Fact]
    public async Task QueryBuildsAuthenticatedRequestAndFollowsContinuationMarker()
    {
        var page = 0;
        var handler = new FakeHttpMessageHandler((request, body) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            page++ == 0
                ? """{"records":[{"recordName":"r1","recordType":"ConnectionSessionDeletion","recordChangeTag":"t1","fields":{"sessionID":{"value":"abc","type":"STRING"}}}],"continuationMarker":"next"}"""
                : """{"records":[{"recordName":"r2","recordType":"ConnectionSessionDeletion","fields":{"payloadFormatVersion":{"value":2,"type":"INT64"},"endedAt":{"value":1731234567890,"type":"TIMESTAMP"}}}]}"""));
        var tokens = new MemoryTokenStore { Token = "a+b/c=" };
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, tokens);

        var records = await client.QueryRecords("ConnectionSessionDeletion", "accountFingerprint", "fp");

        Assert.Equal(["r1", "r2"], records.Select(r => r.RecordName));
        Assert.Equal("abc", records[0].GetString("sessionID"));
        Assert.Equal(2, records[1].GetInt64("payloadFormatVersion"));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1731234567890), records[1].GetTimestamp("endedAt"));
        var first = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, first.Method);
        Assert.Equal(
            "https://api.apple-cloudkit.com/database/1/iCloud.Ryokugyoku.ProjectZD8/development/private/records/query?ckAPIToken=api-token&ckWebAuthToken=a%2Bb%2Fc%3D",
            first.Uri.AbsoluteUri);
        using var sent = JsonDocument.Parse(first.Body!);
        var filter = sent.RootElement.GetProperty("query").GetProperty("filterBy")[0];
        Assert.Equal("_defaultZone", sent.RootElement.GetProperty("zoneID").GetProperty("zoneName").GetString());
        Assert.Equal("EQUALS", filter.GetProperty("comparator").GetString());
        Assert.Equal("accountFingerprint", filter.GetProperty("fieldName").GetString());
        Assert.Equal("fp", filter.GetProperty("fieldValue").GetProperty("value").GetString());
        using var second = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("next", second.RootElement.GetProperty("continuationMarker").GetString());
    }

    /// <summary>
    /// レコードがないとき（NOT_FOUND）の lookup が <c>null</c> を返すことを確かめます。
    /// </summary>
    /// <remarks>責務: レコード単位のエラーの扱いを確かめます。</remarks>
    [Fact]
    public async Task LookupReturnsNullForNotFound()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"records":[{"recordName":"vehicle-catalog-x","reason":"Record not found","serverErrorCode":"NOT_FOUND"}]}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore { Token = "t" });

        Assert.Null(await client.LookupRecord("vehicle-catalog-x"));
    }

    /// <summary>
    /// 421 のとき、トークンを消し、サインイン用の URL を返すことを確かめます。
    /// </summary>
    /// <remarks>責務: AUTHENTICATION_REQUIRED の扱いと状態の変化を確かめます。</remarks>
    [Fact]
    public async Task AuthenticationRequiredSurfacesRedirectUrlAndClearsToken()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            (HttpStatusCode)421,
            """{"uuid":"4f02f7aa","serverErrorCode":"AUTHENTICATION_REQUIRED","reason":"request needs authorization","redirectURL":"https://idmsa.apple.com/IDMSWebAuth/auth?x=1"}"""));
        var tokens = new MemoryTokenStore { Token = "expired" };
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, tokens);
        var changed = 0;
        client.AvailabilityChanged += (_, _) => changed++;

        var error = await Assert.ThrowsAsync<CloudKitAuthenticationRequiredException>(() => client.LookupRecord("x"));

        Assert.Equal(new Uri("https://idmsa.apple.com/IDMSWebAuth/auth?x=1"), error.RedirectUrl);
        Assert.Null(tokens.Token);
        Assert.Equal(CloudKitAvailability.SignedOut, client.Availability);
        Assert.Equal(1, changed);
    }

    /// <summary>
    /// users/caller はトークンなしでも呼べ、public database のパスを使うことを確かめます。
    /// </summary>
    /// <remarks>責務: サインインの開始に使う要求の形を確かめます。</remarks>
    [Fact]
    public async Task FetchCallerWithoutTokenUsesPublicDatabase()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            (HttpStatusCode)421,
            """{"serverErrorCode":"AUTHENTICATION_REQUIRED","redirectURL":"https://idmsa.apple.com/signin"}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore());

        await Assert.ThrowsAsync<CloudKitAuthenticationRequiredException>(() => client.FetchCallerRecordName());

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(
            "https://api.apple-cloudkit.com/database/1/iCloud.Ryokugyoku.ProjectZD8/development/public/users/caller?ckAPIToken=api-token",
            handler.Requests[0].Uri.AbsoluteUri);
    }

    /// <summary>
    /// 応答に新しいトークンが入っていれば保存し直し、次の要求で使うことを確かめます。
    /// </summary>
    /// <remarks>責務: ckWebAuthToken の入れ替えを確かめます（入る場所は Apple の資料に書かれていないため、JSON とヘッダーの両方を見ます）。</remarks>
    [Fact]
    public async Task RotatedTokenFromResponseIsPersistedAndUsedNext()
    {
        var count = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                count++ == 0 ? """{"userRecordName":"_u1","ckWebAuthToken":"second"}""" : """{"userRecordName":"_u1"}""");
            if (count == 2)
            {
                response.Headers.Add("ckSession", "third");
            }
            return response;
        });
        var tokens = new MemoryTokenStore { Token = "first" };
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, tokens);

        Assert.Equal("_u1", await client.FetchCallerRecordName());
        Assert.Equal("second", tokens.Token);
        await client.FetchCallerRecordName();

        Assert.Contains("ckWebAuthToken=second", handler.Requests[1].Uri.Query);
        Assert.Equal("third", tokens.Token);
    }

    /// <summary>
    /// 資産を、トークンの取得→返った URL への送信→レコードの値、の順で扱えることを確かめます。
    /// </summary>
    /// <remarks>責務: assets/upload と、アップロード先への送信と、singleFile の受け取りを確かめます。</remarks>
    [Fact]
    public async Task UploadAssetRequestsTokenThenPostsBytes()
    {
        var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Host == "api.apple-cloudkit.com"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"tokens":[{"recordName":"r","fieldName":"payloadAsset","url":"https://p01-ckdatabasews.icloud.com/upload/abc"}]}""")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"singleFile":{"wrappingKey":"wk","fileChecksum":"fc","receipt":"rc","referenceChecksum":"ref","size":3}}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore { Token = "t" });
        var progress = new List<double>();

        var asset = await client.UploadAsset("ConnectionSessionRawLog", "r", "payloadAsset", [1, 2, 3], progress.Add);

        Assert.Equal("rc", asset.Receipt.GetProperty("receipt").GetString());
        Assert.Contains("/private/assets/upload?", handler.Requests[0].Uri.AbsoluteUri);
        using (var sent = JsonDocument.Parse(handler.Requests[0].Body!))
        {
            var token = sent.RootElement.GetProperty("tokens")[0];
            Assert.Equal("ConnectionSessionRawLog", token.GetProperty("recordType").GetString());
            Assert.Equal("payloadAsset", token.GetProperty("fieldName").GetString());
        }
        Assert.Equal("https://p01-ckdatabasews.icloud.com/upload/abc", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal([1, 2, 3], handler.Requests[1].Bytes!);
        Assert.Equal(1, progress[^1]);

        var json = CloudKitRecordJson.FieldToJson(asset).ToJsonString();
        Assert.Equal("""{"value":{"wrappingKey":"wk","fileChecksum":"fc","receipt":"rc","referenceChecksum":"ref","size":3}}""", json);
    }

    /// <summary>
    /// 資産のダウンロード URL の <c>${f}</c> をファイル名に置き換えて取得することを確かめます。
    /// </summary>
    /// <remarks>責務: downloadURL の扱いを確かめます。</remarks>
    [Fact]
    public async Task DownloadAssetReplacesFileNamePlaceholder()
    {
        var handler = new FakeHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("[]")),
        });
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore { Token = "t" });
        var asset = (CloudKitFieldValue.AssetValue)CloudKitRecordJson.ParseField(JsonDocument.Parse(
            """{"value":{"fileChecksum":"x","size":2,"downloadURL":"https://cvws.icloud-content.com/B/abc/${f}?o=1"},"type":"ASSETID"}""").RootElement);

        var data = await client.DownloadAsset(asset);

        Assert.Equal("[]", Encoding.UTF8.GetString(data));
        Assert.Equal("https://cvws.icloud-content.com/B/abc/asset.bin?o=1", handler.Requests[0].Uri.AbsoluteUri);
    }

    /// <summary>
    /// 保存が records/modify の形で送られ、TIMESTAMP と BYTES に型が付くことを確かめます。
    /// </summary>
    /// <remarks>責務: 保存の要求の形と、競合のエラーを確かめます。</remarks>
    [Fact]
    public async Task SaveRecordSendsTypedFieldsAndSurfacesConflict()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"records":[{"recordName":"account-settings-fp","reason":"changed","serverErrorCode":"CONFLICT"}]}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore { Token = "t" });
        var record = new CloudKitRecord(
            "account-settings-fp",
            "AccountSettings",
            new Dictionary<string, CloudKitFieldValue>
            {
                ["settingsData"] = new CloudKitFieldValue.BytesValue([0x7B, 0x7D]),
                ["updatedAt"] = new CloudKitFieldValue.TimestampValue(DateTimeOffset.FromUnixTimeMilliseconds(1000)),
            },
            "tag1");

        var error = await Assert.ThrowsAsync<CloudKitException>(() => client.SaveRecord(record, CloudKitSaveOperation.Update));

        Assert.Equal("CONFLICT", error.ServerErrorCode);
        using var sent = JsonDocument.Parse(handler.Requests[0].Body!);
        var operation = sent.RootElement.GetProperty("operations")[0];
        Assert.Equal("update", operation.GetProperty("operationType").GetString());
        var sentRecord = operation.GetProperty("record");
        Assert.Equal("tag1", sentRecord.GetProperty("recordChangeTag").GetString());
        Assert.Equal("BYTES", sentRecord.GetProperty("fields").GetProperty("settingsData").GetProperty("type").GetString());
        Assert.Equal("e30=", sentRecord.GetProperty("fields").GetProperty("settingsData").GetProperty("value").GetString());
        Assert.Equal(1000, sentRecord.GetProperty("fields").GetProperty("updatedAt").GetProperty("value").GetInt64());
        Assert.Equal("TIMESTAMP", sentRecord.GetProperty("fields").GetProperty("updatedAt").GetProperty("type").GetString());
    }

    /// <summary>
    /// 設定がないとき・サインインしていないときは、通信せずに使えないことを知らせることを確かめます。
    /// </summary>
    /// <remarks>責務: ローカルモードとサインアウトの状態を確かめます。</remarks>
    [Fact]
    public async Task UnavailableStatesDoNotSendRequests()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("送ってはいけません"));
        var notConfigured = new CloudKitWebServicesClient(new HttpClient(handler), CloudServiceConfiguration.NotConfigured, new MemoryTokenStore { Token = "t" });
        var signedOut = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore());

        Assert.Equal(CloudKitAvailability.NotConfigured, notConfigured.Availability);
        Assert.Equal(CloudKitAvailability.SignedOut, signedOut.Availability);
        await Assert.ThrowsAsync<CloudKitUnavailableException>(() => notConfigured.LookupRecord("x"));
        await Assert.ThrowsAsync<CloudKitUnavailableException>(() => signedOut.QueryRecords("T", "f", "v"));
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// 削除でレコードがなくても失敗しないことを確かめます。
    /// </summary>
    /// <remarks>責務: forceDelete と NOT_FOUND の扱いを確かめます。</remarks>
    [Fact]
    public async Task DeleteIgnoresNotFound()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"records":[{"recordName":"x","serverErrorCode":"NOT_FOUND"}]}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new MemoryTokenStore { Token = "t" });

        await client.DeleteRecordIfPresent("x");

        using var sent = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("forceDelete", sent.RootElement.GetProperty("operations")[0].GetProperty("operationType").GetString());
    }
}
