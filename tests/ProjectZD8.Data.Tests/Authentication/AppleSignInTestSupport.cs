using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// テスト用の RSA 鍵で Apple の ID トークンと JWKS を作ります（実際の Apple には接続しません）。
/// </summary>
internal sealed class AppleSignInTestSupport : IDisposable
{
    /// <summary>テストの鍵 ID です。</summary>
    public const string KeyId = "TESTKID";

    /// <summary>テストの Services ID です。</summary>
    public const string ServicesId = "com.example.revtorque.web";

    /// <summary>署名の鍵です。</summary>
    private readonly RSA _rsa = RSA.Create(2048);

    /// <summary>
    /// JWKS を返す偽の HTTP ハンドラーを作ります。
    /// </summary>
    /// <remarks>責務: <c>https://appleid.apple.com/auth/keys</c> の応答を再現します。</remarks>
    /// <returns>ハンドラー。</returns>
    public FakeHttpMessageHandler KeysHandler()
    {
        var parameters = _rsa.ExportParameters(false);
        var jwks = new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "RSA",
                ["kid"] = KeyId,
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["n"] = Base64Url.EncodeToString(parameters.Modulus),
                ["e"] = Base64Url.EncodeToString(parameters.Exponent),
            }),
        }.ToJsonString();
        return new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK, jwks));
    }

    /// <summary>
    /// 署名した ID トークンを作ります。
    /// </summary>
    /// <remarks>責務: 指定したクレームの JWT（RS256）を作ります。</remarks>
    /// <param name="nonce">nonce。</param>
    /// <param name="now">現在の日時。</param>
    /// <param name="subject">sub。</param>
    /// <param name="audience">aud。</param>
    /// <param name="issuer">iss。</param>
    /// <param name="keyId">kid。</param>
    /// <returns>JWT。</returns>
    public string Token(
        string nonce,
        DateTimeOffset now,
        string subject = "001234.abcdef0123456789.0123",
        string audience = ServicesId,
        string issuer = "https://appleid.apple.com",
        string keyId = KeyId)
    {
        var header = new JsonObject { ["kid"] = keyId, ["alg"] = "RS256" }.ToJsonString();
        var payload = new JsonObject
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["sub"] = subject,
            ["nonce"] = nonce,
            ["nonce_supported"] = true,
        }.ToJsonString();
        var signingInput = $"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header))}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload))}";
        var signature = _rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>
    /// 鍵を破棄します。
    /// </summary>
    /// <remarks>責務: テストの後始末をします。</remarks>
    public void Dispose() => _rsa.Dispose();
}

/// <summary>
/// 決めた日時を返す時計です。
/// </summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    /// <summary>
    /// 日時を指定して作ります。
    /// </summary>
    /// <remarks>責務: テストの現在の日時を固定します。</remarks>
    /// <param name="now">現在の日時。</param>
    public FixedTimeProvider(DateTimeOffset now)
    {
        Now = now;
    }

    /// <summary>現在の日時です。</summary>
    public DateTimeOffset Now { get; set; }

    /// <summary>
    /// 現在の日時を返します。
    /// </summary>
    /// <remarks>責務: 固定の日時を返します。</remarks>
    /// <returns>現在の日時。</returns>
    public override DateTimeOffset GetUtcNow() => Now;
}
