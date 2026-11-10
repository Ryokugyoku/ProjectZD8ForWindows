using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// Sign in with Apple の ID トークン（JWT）を、Apple の公開鍵で検証します（<c>System.Security.Cryptography</c> だけを使います）。
/// </summary>
/// <remarks>
/// 確かめること：ヘッダーの alg が RS256、kid の鍵で署名が正しい、iss が <c>https://appleid.apple.com</c>、
/// aud が Services ID、exp が過ぎていない（時計のずれを 60 秒まで許す）、nonce が要求と一致、sub がある。
/// </remarks>
public sealed class AppleIdentityTokenValidator
{
    /// <summary>Apple の発行者です。</summary>
    public const string AppleIssuer = "https://appleid.apple.com";

    /// <summary>許す時計のずれです。</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(60);

    /// <summary>公開鍵の取得先です。</summary>
    private readonly AppleJsonWebKeySetCache _keys;

    /// <summary>このアプリの Services ID（aud）です。</summary>
    private readonly string _servicesId;

    /// <summary>時計です。</summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// 公開鍵の取得先、Services ID、時計を注入して作ります。
    /// </summary>
    /// <remarks>責務: ID トークンの検証に使う鍵と対象を固定します。</remarks>
    /// <param name="keys">Apple の公開鍵の取得先。</param>
    /// <param name="servicesId">Services ID。</param>
    /// <param name="time">時計。<c>null</c> なら <see cref="TimeProvider.System"/>。</param>
    public AppleIdentityTokenValidator(AppleJsonWebKeySetCache keys, string servicesId, TimeProvider? time = null)
    {
        _keys = keys;
        _servicesId = servicesId;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// ID トークンを検証して中身を返します。
    /// </summary>
    /// <remarks>責務: 1 件の JWT の署名と主な項目を確かめ、ユーザー識別子を取り出します。</remarks>
    /// <param name="idToken">Apple が返した id_token。</param>
    /// <param name="expectedNonce">要求に付けた nonce。</param>
    /// <returns>検証済みの中身。</returns>
    /// <exception cref="AppleIdentityTokenException">検証に失敗した場合。</exception>
    /// <exception cref="HttpRequestException">公開鍵を取得できない場合。</exception>
    public async Task<AppleIdentityTokenClaims> Validate(string idToken, string expectedNonce)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.Malformed);
        }
        JsonElement header;
        JsonElement payload;
        byte[] signature;
        try
        {
            header = ParseJson(parts[0]);
            payload = ParseJson(parts[1]);
            signature = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.Malformed);
        }

        if (Text(header, "alg") != "RS256")
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.UnsupportedAlgorithm);
        }
        if (Text(header, "kid") is not { } kid || await _keys.FindKey(kid).ConfigureAwait(false) is not { } key)
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.UnknownKey);
        }
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(key);
            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            if (!rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw new AppleIdentityTokenException(AppleIdentityTokenError.InvalidSignature);
            }
        }

        if (!string.Equals(Text(payload, "iss"), AppleIssuer, StringComparison.Ordinal))
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.InvalidIssuer);
        }
        if (!HasAudience(payload, _servicesId))
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.InvalidAudience);
        }
        var now = _time.GetUtcNow();
        if (Seconds(payload, "exp") is not { } exp || DateTimeOffset.FromUnixTimeSeconds(exp) + ClockSkew <= now)
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.Expired);
        }
        if (Seconds(payload, "iat") is { } iat && DateTimeOffset.FromUnixTimeSeconds(iat) - ClockSkew > now)
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.Expired);
        }
        if (!string.Equals(Text(payload, "nonce"), expectedNonce, StringComparison.Ordinal))
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.NonceMismatch);
        }
        if (Text(payload, "sub") is not { Length: > 0 } subject)
        {
            throw new AppleIdentityTokenException(AppleIdentityTokenError.MissingSubject);
        }
        return new AppleIdentityTokenClaims(subject, DateTimeOffset.FromUnixTimeSeconds(exp), Text(payload, "email"));
    }

    /// <summary>
    /// base64url の JSON を読みます。
    /// </summary>
    /// <remarks>責務: JWT の 1 区画を JSON のオブジェクトへ変換します。</remarks>
    /// <param name="segment">区画。</param>
    /// <returns>JSON のオブジェクト。</returns>
    private static JsonElement ParseJson(string segment)
    {
        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(segment));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("JWT の区画がオブジェクトではありません。");
        }
        return document.RootElement.Clone();
    }

    /// <summary>
    /// 文字列のクレームを読みます。
    /// </summary>
    /// <remarks>責務: 1 件のキーの文字列を、なければ <c>null</c> として読みます。</remarks>
    /// <param name="element">オブジェクト。</param>
    /// <param name="name">キー。</param>
    /// <returns>文字列。</returns>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// 秒のクレーム（exp、iat）を読みます。
    /// </summary>
    /// <remarks>責務: 数値または数字の文字列を 1970 年からの秒として読みます。</remarks>
    /// <param name="element">オブジェクト。</param>
    /// <param name="name">キー。</param>
    /// <returns>秒。なければ <c>null</c>。</returns>
    private static long? Seconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    /// <summary>
    /// aud が Services ID を含むかを返します。
    /// </summary>
    /// <remarks>責務: 文字列または配列の aud を Services ID と照合します。</remarks>
    /// <param name="payload">中身。</param>
    /// <param name="audience">期待する aud。</param>
    /// <returns>含めば <c>true</c>。</returns>
    private static bool HasAudience(JsonElement payload, string audience)
    {
        if (!payload.TryGetProperty("aud", out var aud))
        {
            return false;
        }
        return aud.ValueKind switch
        {
            JsonValueKind.String => string.Equals(aud.GetString(), audience, StringComparison.Ordinal),
            JsonValueKind.Array => aud.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), audience, StringComparison.Ordinal)),
            _ => false,
        };
    }
}
