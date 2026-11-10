using System.Security.Cryptography;
using System.Text.Json;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// Apple の ID トークンの署名を確かめる公開鍵（JWKS、<c>https://appleid.apple.com/auth/keys</c>）を取得して覚えておきます。
/// </summary>
/// <remarks>
/// 鍵は既定で 24 時間覚えます。知らない鍵 ID（kid）が来たら取り直します（Apple が鍵を入れ替えたとき）。
/// ただし同じ失敗で何度も取りに行かないよう、取り直しは 1 分に 1 回までです。
/// </remarks>
public sealed class AppleJsonWebKeySetCache
{
    /// <summary>Apple の JWKS の URL です。</summary>
    public static readonly Uri AppleKeysUri = new("https://appleid.apple.com/auth/keys");

    /// <summary>知らない鍵のときに取り直す最小の間隔です。</summary>
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromMinutes(1);

    /// <summary>通信に使う HTTP クライアントです。</summary>
    private readonly HttpClient _httpClient;

    /// <summary>時計です。</summary>
    private readonly TimeProvider _time;

    /// <summary>JWKS の URL です。</summary>
    private readonly Uri _keysUri;

    /// <summary>鍵を覚えておく時間です。</summary>
    private readonly TimeSpan _timeToLive;

    /// <summary>取り直しを 1 件ずつにする鍵です。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>覚えている鍵（kid ごと）です。</summary>
    private Dictionary<string, RSAParameters> _keys = new(StringComparer.Ordinal);

    /// <summary>最後に取得した日時です。</summary>
    private DateTimeOffset? _fetchedAt;

    /// <summary>
    /// HTTP クライアントと時計を注入して作ります。
    /// </summary>
    /// <remarks>責務: 鍵の取得先と覚えておく時間を固定します。</remarks>
    /// <param name="httpClient">HTTP クライアント。</param>
    /// <param name="time">時計。<c>null</c> なら <see cref="TimeProvider.System"/>。</param>
    /// <param name="keysUri">JWKS の URL。<c>null</c> なら Apple の URL。</param>
    /// <param name="timeToLive">覚えておく時間。<c>null</c> なら 24 時間。</param>
    public AppleJsonWebKeySetCache(
        HttpClient httpClient,
        TimeProvider? time = null,
        Uri? keysUri = null,
        TimeSpan? timeToLive = null)
    {
        _httpClient = httpClient;
        _time = time ?? TimeProvider.System;
        _keysUri = keysUri ?? AppleKeysUri;
        _timeToLive = timeToLive ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// 鍵 ID に対応する RSA の公開鍵を返します。
    /// </summary>
    /// <remarks>責務: 覚えている鍵を優先し、古いか知らない鍵なら Apple から取り直して 1 件の公開鍵を返します。</remarks>
    /// <param name="keyId">ID トークンのヘッダーの kid。</param>
    /// <returns>公開鍵。見つからなければ <c>null</c>。</returns>
    /// <exception cref="HttpRequestException">取得に失敗した場合。</exception>
    public async Task<RSAParameters?> FindKey(string keyId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            var fresh = _fetchedAt is { } fetched && now - fetched < _timeToLive;
            if (fresh && _keys.TryGetValue(keyId, out var cached))
            {
                return cached;
            }
            var mayRefresh = _fetchedAt is not { } last || now - last >= MinimumRefreshInterval || !fresh;
            if (mayRefresh)
            {
                _keys = await Fetch().ConfigureAwait(false);
                _fetchedAt = now;
            }
            return _keys.TryGetValue(keyId, out var key) ? key : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// JWKS を取得して RSA の鍵へ変換します。
    /// </summary>
    /// <remarks>責務: 1 回の取得で、kty が RSA の鍵だけを kid ごとの公開鍵へ変換します。</remarks>
    /// <returns>kid ごとの公開鍵。</returns>
    private async Task<Dictionary<string, RSAParameters>> Fetch()
    {
        using var response = await _httpClient.GetAsync(_keysUri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var result = new Dictionary<string, RSAParameters>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            {
                return result;
            }
            foreach (var key in keys.EnumerateArray())
            {
                if (Text(key, "kty") != "RSA" || Text(key, "kid") is not { } kid
                    || Text(key, "n") is not { } n || Text(key, "e") is not { } e)
                {
                    continue;
                }
                result[kid] = new RSAParameters
                {
                    Modulus = System.Buffers.Text.Base64Url.DecodeFromChars(n),
                    Exponent = System.Buffers.Text.Base64Url.DecodeFromChars(e),
                };
            }
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new HttpRequestException("Apple の公開鍵の形式が正しくありません。", exception);
        }
        return result;
    }

    /// <summary>
    /// 文字列のプロパティを読みます。
    /// </summary>
    /// <remarks>責務: 1 件のキーの文字列を、なければ <c>null</c> として読みます。</remarks>
    /// <param name="element">オブジェクト。</param>
    /// <param name="name">キー。</param>
    /// <returns>文字列。</returns>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
