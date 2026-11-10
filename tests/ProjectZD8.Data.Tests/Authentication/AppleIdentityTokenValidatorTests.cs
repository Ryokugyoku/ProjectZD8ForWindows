using ProjectZD8.Data.Authentication;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// ID トークンの検証（署名、iss、aud、exp、nonce）と、公開鍵の取得の覚え方を確かめます。
/// </summary>
public sealed class AppleIdentityTokenValidatorTests : IDisposable
{
    /// <summary>テストの現在の日時です。</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_762_000_000);

    /// <summary>鍵とトークンを作る補助です。</summary>
    private readonly AppleSignInTestSupport _support = new();

    /// <summary>
    /// 後始末をします。
    /// </summary>
    /// <remarks>責務: 鍵を破棄します。</remarks>
    public void Dispose() => _support.Dispose();

    /// <summary>
    /// 正しいトークンから sub を取り出し、2 回目は公開鍵を取り直さないことを確かめます。
    /// </summary>
    /// <remarks>責務: 正常な検証と JWKS の覚え方を確かめます。</remarks>
    [Fact]
    public async Task ValidTokenYieldsSubjectAndKeysAreCached()
    {
        var handler = _support.KeysHandler();
        var time = new FixedTimeProvider(Now);
        var validator = new AppleIdentityTokenValidator(
            new AppleJsonWebKeySetCache(new HttpClient(handler), time),
            AppleSignInTestSupport.ServicesId,
            time);

        var claims = await validator.Validate(_support.Token("n1", Now), "n1");
        await validator.Validate(_support.Token("n2", Now), "n2");

        Assert.Equal("001234.abcdef0123456789.0123", claims.Subject);
        Assert.Single(handler.Requests);
        Assert.Equal("https://appleid.apple.com/auth/keys", handler.Requests[0].Uri.AbsoluteUri);
    }

    /// <summary>
    /// 期待と違うトークンを、それぞれの理由で拒むことを確かめます。
    /// </summary>
    /// <remarks>責務: nonce、aud、iss、exp、kid、署名の検査を確かめます。</remarks>
    [Fact]
    public async Task InvalidTokensAreRejectedWithReason()
    {
        var time = new FixedTimeProvider(Now);
        var validator = new AppleIdentityTokenValidator(
            new AppleJsonWebKeySetCache(new HttpClient(_support.KeysHandler()), time),
            AppleSignInTestSupport.ServicesId,
            time);

        async Task<AppleIdentityTokenError> Reason(string token, string nonce = "n") =>
            (await Assert.ThrowsAsync<AppleIdentityTokenException>(() => validator.Validate(token, nonce))).Reason;

        Assert.Equal(AppleIdentityTokenError.NonceMismatch, await Reason(_support.Token("other", Now)));
        Assert.Equal(AppleIdentityTokenError.InvalidAudience, await Reason(_support.Token("n", Now, audience: "com.example.other")));
        Assert.Equal(AppleIdentityTokenError.InvalidIssuer, await Reason(_support.Token("n", Now, issuer: "https://evil.example")));
        Assert.Equal(AppleIdentityTokenError.Expired, await Reason(_support.Token("n", Now.AddHours(-1))));
        Assert.Equal(AppleIdentityTokenError.UnknownKey, await Reason(_support.Token("n", Now, keyId: "OTHER")));
        Assert.Equal(AppleIdentityTokenError.Malformed, await Reason("not-a-jwt"));
        var token = _support.Token("n", Now);
        var otherSignature = _support.Token("different", Now).Split('.')[2];
        var tampered = token[..(token.LastIndexOf('.') + 1)] + otherSignature;
        Assert.Equal(AppleIdentityTokenError.InvalidSignature, await Reason(tampered));
    }
}
