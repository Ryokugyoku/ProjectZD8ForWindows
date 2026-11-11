using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ProjectZD8.Data.Configuration;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit Web Services（REST）の private database（default zone）を、API トークンと Web 認証トークンで操作します。
/// </summary>
/// <remarks>
/// <para>URL は <c>https://api.apple-cloudkit.com/database/1/{コンテナー}/{環境}/{private|public}/{操作}?ckAPIToken=…&amp;ckWebAuthToken=…</c>。</para>
/// <para>Apple の資料では ckWebAuthToken は 1 往復ごとに新しいものが応答で返り、古いものは使えなくなります。
/// 新しいトークンが応答のどこに入るかは資料に書かれていないため、応答の JSON の <c>ckWebAuthToken</c>・<c>ckSession</c> と、
/// 同じ名前の HTTP ヘッダーを見て、見つかれば保存し直します（実機では未確認）。トークンの順番を守るため、要求は 1 件ずつ送ります。</para>
/// <para>421 <c>AUTHENTICATION_REQUIRED</c> のときは保存済みのトークンを消し、サインイン用の <c>redirectURL</c> を
/// <see cref="CloudKitAuthenticationRequiredException"/> で返します。</para>
/// </remarks>
public sealed class CloudKitWebServicesClient : ICloudKitDatabase
{
    /// <summary>default zone の JSON です。</summary>
    private static readonly JsonObject DefaultZone = new() { ["zoneName"] = "_defaultZone" };

    /// <summary>新しいトークンが入っているかもしれない名前です（JSON のキーと HTTP ヘッダー）。</summary>
    private static readonly string[] RotatedTokenNames = ["ckWebAuthToken", "ckSession"];

    /// <summary>通信に使う HTTP クライアントです（テストでは偽のハンドラーを入れます）。</summary>
    private readonly HttpClient _httpClient;

    /// <summary>接続先と API トークンの設定です。</summary>
    private readonly CloudServiceConfiguration _configuration;

    /// <summary>Web 認証トークンの保存先です。</summary>
    private readonly ICloudKitWebAuthTokenStore _tokenStore;

    /// <summary>トークンの順番を守るため、要求を 1 件ずつにする鍵です。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// HTTP クライアント、設定、トークンの保存先を注入して作ります。
    /// </summary>
    /// <remarks>責務: CloudKit Web Services の接続先と認証情報の出どころを固定します。</remarks>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="configuration">CloudKit の設定。</param>
    /// <param name="tokenStore">ckWebAuthToken の保存先。</param>
    public CloudKitWebServicesClient(
        HttpClient httpClient,
        CloudServiceConfiguration configuration,
        ICloudKitWebAuthTokenStore tokenStore)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _tokenStore = tokenStore;
    }

    /// <summary>
    /// 使えるかどうかが変わったとき（トークンの保存・削除）に発生します。
    /// </summary>
    public event EventHandler? AvailabilityChanged;

    /// <inheritdoc />
    public CloudKitAvailability Availability =>
        !_configuration.IsConfigured
            ? CloudKitAvailability.NotConfigured
            : string.IsNullOrEmpty(_tokenStore.Load())
                ? CloudKitAvailability.SignedOut
                : CloudKitAvailability.Available;

    /// <summary>
    /// 新しく手に入れた Web 認証トークンを保存します（サインインのコールバックから）。
    /// </summary>
    /// <remarks>責務: 1 件のトークンを保存し、状態の変化を知らせます。</remarks>
    /// <param name="token">空でないトークン。</param>
    /// <exception cref="ArgumentException">空の場合。</exception>
    public void StoreWebAuthToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("トークンが空です。", nameof(token));
        }
        _tokenStore.Save(token);
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Web 認証トークンを消して CloudKit からサインアウトします。
    /// </summary>
    /// <remarks>責務: 保存済みのトークンを消し、状態の変化を知らせます。</remarks>
    public void SignOut()
    {
        _tokenStore.Remove();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// サインイン中の利用者を確かめます（<c>users/caller</c>）。サインインしていなければ 421 になります。
    /// </summary>
    /// <remarks>責務: 現在のトークンが有効かを確かめ、利用者のレコード名を返します。</remarks>
    /// <returns>CloudKit の利用者レコード名（<c>userRecordName</c>）。</returns>
    /// <exception cref="CloudKitAuthenticationRequiredException">サインインが必要な場合（<c>redirectURL</c> 付き）。</exception>
    /// <exception cref="CloudKitUnavailableException">設定がない場合。</exception>
    public async Task<string> FetchCallerRecordName()
    {
        if (!_configuration.IsConfigured)
        {
            throw new CloudKitUnavailableException(CloudKitAvailability.NotConfigured);
        }
        var root = await Send(HttpMethod.Get, "public", "users/caller", body: null).ConfigureAwait(false);
        return root.TryGetProperty("userRecordName", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()!
            : throw new CloudKitException(HttpStatusCode.OK, null, "userRecordName がありません。");
    }

    /// <inheritdoc />
    public async Task<CloudKitRecord?> LookupRecord(string recordName, IReadOnlyList<string>? desiredKeys = null)
    {
        EnsureAvailable();
        var body = new JsonObject
        {
            ["zoneID"] = DefaultZone.DeepClone(),
            ["records"] = new JsonArray(new JsonObject { ["recordName"] = recordName }),
        };
        AddDesiredKeys(body, desiredKeys);
        var root = await Send(HttpMethod.Post, "private", "records/lookup", body).ConfigureAwait(false);
        foreach (var item in Records(root))
        {
            if (RecordError(item) is { } error)
            {
                if (error.IsNotFound)
                {
                    return null;
                }
                throw error;
            }
            return CloudKitRecordJson.Parse(item);
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudKitRecord>> QueryRecords(
        string recordType,
        string fieldName,
        string value,
        IReadOnlyList<string>? desiredKeys = null)
    {
        EnsureAvailable();
        var output = new List<CloudKitRecord>();
        string? marker = null;
        do
        {
            var body = new JsonObject
            {
                ["zoneID"] = DefaultZone.DeepClone(),
                ["resultsLimit"] = 200,
                ["query"] = new JsonObject
                {
                    ["recordType"] = recordType,
                    ["filterBy"] = new JsonArray(new JsonObject
                    {
                        ["comparator"] = "EQUALS",
                        ["fieldName"] = fieldName,
                        ["fieldValue"] = new JsonObject { ["value"] = value, ["type"] = "STRING" },
                    }),
                },
            };
            AddDesiredKeys(body, desiredKeys);
            if (marker is not null)
            {
                body["continuationMarker"] = marker;
            }
            var root = await Send(HttpMethod.Post, "private", "records/query", body, recordType).ConfigureAwait(false);
            foreach (var item in Records(root))
            {
                if (RecordError(item) is { } error)
                {
                    throw error;
                }
                output.Add(CloudKitRecordJson.Parse(item));
            }
            marker = root.TryGetProperty("continuationMarker", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        while (marker is not null);
        return output;
    }

    /// <inheritdoc />
    public async Task<CloudKitRecord> SaveRecord(CloudKitRecord record, CloudKitSaveOperation operation)
    {
        EnsureAvailable();
        var operationType = operation switch
        {
            CloudKitSaveOperation.Create => "create",
            CloudKitSaveOperation.Update => "update",
            CloudKitSaveOperation.ForceReplace => "forceReplace",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        var body = new JsonObject
        {
            ["zoneID"] = DefaultZone.DeepClone(),
            ["atomic"] = true,
            ["operations"] = new JsonArray(new JsonObject
            {
                ["operationType"] = operationType,
                ["record"] = CloudKitRecordJson.ToJson(record, includeChangeTag: operation == CloudKitSaveOperation.Update),
            }),
        };
        var root = await Send(HttpMethod.Post, "private", "records/modify", body, record.RecordType).ConfigureAwait(false);
        foreach (var item in Records(root))
        {
            if (RecordError(item) is { } error)
            {
                throw error;
            }
            return CloudKitRecordJson.Parse(item);
        }
        throw new CloudKitException(HttpStatusCode.OK, null, "保存の結果がありません。");
    }

    /// <inheritdoc />
    public async Task DeleteRecordIfPresent(string recordName)
    {
        EnsureAvailable();
        var body = new JsonObject
        {
            ["zoneID"] = DefaultZone.DeepClone(),
            ["operations"] = new JsonArray(new JsonObject
            {
                ["operationType"] = "forceDelete",
                ["record"] = new JsonObject { ["recordName"] = recordName },
            }),
        };
        var root = await Send(HttpMethod.Post, "private", "records/modify", body).ConfigureAwait(false);
        foreach (var item in Records(root))
        {
            if (RecordError(item) is { IsNotFound: false } error)
            {
                throw error;
            }
        }
    }

    /// <inheritdoc />
    public async Task<CloudKitFieldValue.AssetUploadValue> UploadAsset(
        string recordType,
        string recordName,
        string fieldName,
        byte[] data,
        Action<double>? progress = null)
    {
        EnsureAvailable();
        var body = new JsonObject
        {
            ["zoneID"] = DefaultZone.DeepClone(),
            ["tokens"] = new JsonArray(new JsonObject
            {
                ["recordType"] = recordType,
                ["recordName"] = recordName,
                ["fieldName"] = fieldName,
            }),
        };
        var root = await Send(HttpMethod.Post, "private", "assets/upload", body, recordType).ConfigureAwait(false);
        string? url = null;
        if (root.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var token in tokens.EnumerateArray())
            {
                if (token.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                {
                    url = u.GetString();
                    break;
                }
            }
        }
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uploadUri))
        {
            throw new CloudKitException(HttpStatusCode.OK, null, "資産のアップロード先がありません。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri)
        {
            Content = new CloudKitProgressContent(data, progress),
        };
        var uploadStarted = Stopwatch.GetTimestamp();
        var uploadStatus = 0;
        try
        {
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            uploadStatus = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var uploaded = ParseJson(text);
            if (!response.IsSuccessStatusCode)
            {
                throw ErrorFrom(response.StatusCode, uploaded);
            }
            if (uploaded is { } json && json.TryGetProperty("singleFile", out var singleFile)
                && singleFile.ValueKind == JsonValueKind.Object)
            {
                SyncLog.AssetTransferred(SyncLog.Logger, "asset/upload", data.Length, uploadStatus, SyncLog.ElapsedMs(uploadStarted));
                return new CloudKitFieldValue.AssetUploadValue(singleFile);
            }
            throw new CloudKitException(response.StatusCode, null, "アップロードの応答に singleFile がありません。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SyncLog.AssetFailed(SyncLog.Logger, exception, "asset/upload", uploadStatus, SyncLog.ElapsedMs(uploadStarted));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadAsset(CloudKitFieldValue.AssetValue asset, Action<double>? progress = null)
    {
        EnsureAvailable();
        var started = Stopwatch.GetTimestamp();
        var status = new StrongBox<int>();
        try
        {
            var data = await DownloadAssetCore(asset, progress, status).ConfigureAwait(false);
            SyncLog.AssetTransferred(SyncLog.Logger, "asset/download", data.Length, status.Value, SyncLog.ElapsedMs(started));
            return data;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SyncLog.AssetFailed(SyncLog.Logger, exception, "asset/download", status.Value, SyncLog.ElapsedMs(started));
            throw;
        }
    }

    /// <summary>
    /// 資産を 1 件ダウンロードします。
    /// </summary>
    /// <remarks>責務: ダウンロード URL から本体を読み、HTTP の状態を診断ログ用に <paramref name="status"/> へ残します。</remarks>
    /// <param name="asset">ダウンロードする資産。</param>
    /// <param name="progress">進み具合（0〜1）の通知先。</param>
    /// <param name="status">HTTP の状態の受け渡し先。</param>
    /// <returns>資産の本体。</returns>
    private async Task<byte[]> DownloadAssetCore(CloudKitFieldValue.AssetValue asset, Action<double>? progress, StrongBox<int> status)
    {
        if (asset.DownloadUrl is null
            || !Uri.TryCreate(asset.DownloadUrl.Replace("${f}", "asset.bin", StringComparison.Ordinal), UriKind.Absolute, out var uri))
        {
            throw new CloudKitException(HttpStatusCode.OK, null, "資産のダウンロード URL がありません。");
        }
        using var response = await _httpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        status.Value = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            throw new CloudKitException(response.StatusCode, null, "資産をダウンロードできません。");
        }
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream(total is > 0 and < int.MaxValue ? (int)total : 0);
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (total is > 0)
            {
                progress?.Invoke(Math.Min(1, (double)buffer.Length / total.Value));
            }
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// 使えない状態なら例外を投げます。
    /// </summary>
    /// <remarks>責務: 設定とトークンがそろっていることを保証します。</remarks>
    /// <exception cref="CloudKitUnavailableException">使えない場合。</exception>
    private void EnsureAvailable()
    {
        var availability = Availability;
        if (availability != CloudKitAvailability.Available)
        {
            throw new CloudKitUnavailableException(availability);
        }
    }

    /// <summary>
    /// 要求を 1 件送り、応答の JSON を返します。
    /// </summary>
    /// <remarks>責務: 認証パラメーターを付けて 1 件送り、トークンの更新・421・エラーを処理します。</remarks>
    /// <param name="method">HTTP メソッド。</param>
    /// <param name="database"><c>private</c> または <c>public</c>。</param>
    /// <param name="subpath">操作のパス（<c>records/query</c> など）。</param>
    /// <param name="body">送る JSON。<c>null</c> なら本文なし。</param>
    /// <param name="recordType">診断ログに書くレコード種別。ない操作は <c>null</c>。</param>
    /// <returns>応答の JSON のルート。</returns>
    private async Task<JsonElement> Send(HttpMethod method, string database, string subpath, JsonNode? body, string? recordType = null)
    {
        var started = Stopwatch.GetTimestamp();
        var operation = database + "/" + subpath;
        var status = new StrongBox<int>();
        try
        {
            var root = await SendCore(method, database, subpath, body, status).ConfigureAwait(false);
            if (SyncLog.Logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
            {
                var count = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var records)
                    && records.ValueKind == JsonValueKind.Array
                        ? records.GetArrayLength()
                        : 0;
                SyncLog.RequestCompleted(SyncLog.Logger, operation, recordType ?? string.Empty, count, status.Value, SyncLog.ElapsedMs(started));
            }
            return root;
        }
        catch (CloudKitAuthenticationRequiredException exception)
        {
            SyncLog.AuthenticationRequired(SyncLog.Logger, operation, exception.RedirectUrl is not null, SyncLog.ElapsedMs(started));
            throw;
        }
        catch (OperationCanceledException)
        {
            SyncLog.RequestCanceled(SyncLog.Logger, operation, SyncLog.ElapsedMs(started));
            throw;
        }
        catch (Exception exception)
        {
            var cloudKit = exception as CloudKitException;
            SyncLog.RequestFailed(
                SyncLog.Logger,
                exception,
                operation,
                recordType ?? string.Empty,
                status.Value != 0 ? status.Value : (int?)cloudKit?.StatusCode ?? 0,
                cloudKit?.ServerErrorCode ?? string.Empty,
                SyncLog.ElapsedMs(started));
            throw;
        }
    }

    /// <summary>
    /// 要求を 1 件送ります（診断ログを書かない本体）。
    /// </summary>
    /// <remarks>責務: <see cref="Send"/> の本体です。HTTP の状態を診断ログ用に <paramref name="status"/> へ残します。</remarks>
    /// <param name="method">HTTP メソッド。</param>
    /// <param name="database"><c>private</c> または <c>public</c>。</param>
    /// <param name="subpath">操作のパス。</param>
    /// <param name="body">送る JSON。</param>
    /// <param name="status">HTTP の状態の受け渡し先。</param>
    /// <returns>応答の JSON のルート。</returns>
    private async Task<JsonElement> SendCore(HttpMethod method, string database, string subpath, JsonNode? body, StrongBox<int> status)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var token = _tokenStore.Load();
            using var request = new HttpRequestMessage(method, BuildUri(database, subpath, token));
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            status.Value = (int)response.StatusCode;
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = ParseJson(text);
            RotateToken(token, response, json);

            if ((int)response.StatusCode == 421)
            {
                if (!string.IsNullOrEmpty(_tokenStore.Load()))
                {
                    _tokenStore.Remove();
                    AvailabilityChanged?.Invoke(this, EventArgs.Empty);
                }
                var redirect = json is { } j && j.TryGetProperty("redirectURL", out var r) && r.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(r.GetString(), UriKind.Absolute, out var redirectUri)
                        ? redirectUri
                        : null;
                throw new CloudKitAuthenticationRequiredException(redirect, ReasonOf(json));
            }
            if (!response.IsSuccessStatusCode)
            {
                throw ErrorFrom(response.StatusCode, json);
            }
            return json ?? throw new CloudKitException(response.StatusCode, null, "応答が JSON ではありません。");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 要求の URL を作ります。
    /// </summary>
    /// <remarks>責務: コンテナー・環境・データベース・操作と、URL エンコードしたトークンから 1 件の URL を作ります。</remarks>
    /// <param name="database">データベース。</param>
    /// <param name="subpath">操作のパス。</param>
    /// <param name="token">Web 認証トークン。</param>
    /// <returns>要求の URL。</returns>
    private Uri BuildUri(string database, string subpath, string? token)
    {
        var builder = new StringBuilder();
        builder.Append(_configuration.CloudKitBaseUri.AbsoluteUri.TrimEnd('/'));
        builder.Append("/database/1/");
        builder.Append(Uri.EscapeDataString(_configuration.CloudKitContainerId));
        builder.Append('/').Append(_configuration.CloudKitEnvironment);
        builder.Append('/').Append(database);
        builder.Append('/').Append(subpath);
        builder.Append("?ckAPIToken=").Append(Uri.EscapeDataString(_configuration.CloudKitApiToken ?? ""));
        if (!string.IsNullOrEmpty(token))
        {
            // Apple の資料どおり + / = を %2B %2F %3D にします（EscapeDataString はこれを満たします）。
            builder.Append("&ckWebAuthToken=").Append(Uri.EscapeDataString(token));
        }
        return new Uri(builder.ToString());
    }

    /// <summary>
    /// 応答に新しいトークンがあれば保存し直します。
    /// </summary>
    /// <remarks>責務: 1 往復ごとに変わるトークンを、次の要求で使えるように保存します。</remarks>
    /// <param name="sentToken">送ったトークン。</param>
    /// <param name="response">応答。</param>
    /// <param name="json">応答の JSON。</param>
    private void RotateToken(string? sentToken, HttpResponseMessage response, JsonElement? json)
    {
        if (string.IsNullOrEmpty(sentToken) || (int)response.StatusCode == 421)
        {
            return;
        }
        string? rotated = null;
        foreach (var name in RotatedTokenNames)
        {
            if (json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
            {
                rotated = value.GetString();
                break;
            }
            if (response.Headers.TryGetValues(name, out var values) && values.FirstOrDefault() is { Length: > 0 } header)
            {
                rotated = header;
                break;
            }
        }
        if (rotated is not null && !string.Equals(rotated, sentToken, StringComparison.Ordinal))
        {
            _tokenStore.Save(rotated);
        }
    }

    /// <summary>
    /// 文字列を JSON として読みます。
    /// </summary>
    /// <remarks>責務: 応答の本文を、読めれば JSON のルートへ変換します。</remarks>
    /// <param name="text">本文。</param>
    /// <returns>JSON のルート。読めなければ <c>null</c>。</returns>
    private static JsonElement? ParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 応答のレコードの配列を返します。
    /// </summary>
    /// <remarks>責務: 応答の <c>records</c> を列挙できる形にします。</remarks>
    /// <param name="root">応答のルート。</param>
    /// <returns>レコードの JSON。</returns>
    private static IEnumerable<JsonElement> Records(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var records)
        && records.ValueKind == JsonValueKind.Array
            ? records.EnumerateArray()
            : [];

    /// <summary>
    /// レコード単位のエラーを読みます。
    /// </summary>
    /// <remarks>責務: <c>serverErrorCode</c> を持つ要素を例外に変換します。</remarks>
    /// <param name="item">レコードの JSON。</param>
    /// <returns>エラー。エラーでなければ <c>null</c>。</returns>
    private static CloudKitException? RecordError(JsonElement item) =>
        item.TryGetProperty("serverErrorCode", out var code) && code.ValueKind == JsonValueKind.String
            ? new CloudKitException(HttpStatusCode.OK, code.GetString(), ReasonOf(item))
            : null;

    /// <summary>
    /// 失敗した応答から例外を作ります。
    /// </summary>
    /// <remarks>責務: HTTP の状態と応答の JSON を 1 件の例外へ変換します。</remarks>
    /// <param name="status">HTTP の状態。</param>
    /// <param name="json">応答の JSON。</param>
    /// <returns>例外。</returns>
    private static CloudKitException ErrorFrom(HttpStatusCode status, JsonElement? json) =>
        new(
            status,
            json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("serverErrorCode", out var code)
                && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null,
            ReasonOf(json));

    /// <summary>
    /// 応答の <c>reason</c> を読みます。
    /// </summary>
    /// <remarks>責務: エラーの理由の文字列を取り出します。</remarks>
    /// <param name="json">応答の JSON。</param>
    /// <returns>理由。</returns>
    private static string? ReasonOf(JsonElement? json) =>
        json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("reason", out var reason)
        && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;

    /// <summary>
    /// 読むフィールドを指定します。
    /// </summary>
    /// <remarks>責務: <c>desiredKeys</c> を要求の JSON に加えます。</remarks>
    /// <param name="body">要求の JSON。</param>
    /// <param name="desiredKeys">読むフィールド。</param>
    private static void AddDesiredKeys(JsonObject body, IReadOnlyList<string>? desiredKeys)
    {
        if (desiredKeys is null)
        {
            return;
        }
        var keys = new JsonArray();
        foreach (var key in desiredKeys)
        {
            keys.Add(JsonValue.Create(key));
        }
        body["desiredKeys"] = keys;
    }
}
