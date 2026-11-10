using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Configuration;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// Sign in with Apple の Web の流れ（URL、ブラウザー、カスタム URL スキームでの戻り、ID トークンの検証）を確かめます。
/// </summary>
public sealed class WebAppleAccountAuthorizationClientTests : IDisposable
{
    /// <summary>テストの現在の日時です。</summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_762_000_000);

    /// <summary>テストの設定です。</summary>
    private static readonly CloudServiceConfiguration Configuration = new()
    {
        AppleServicesId = AppleSignInTestSupport.ServicesId,
        AppleRedirectUri = new Uri("https://example.com/revtorque/apple-callback.html"),
        CloudKitApiToken = "api",
    };

    /// <summary>鍵とトークンを作る補助です。</summary>
    private readonly AppleSignInTestSupport _support = new();

    /// <summary>
    /// 後始末をします。
    /// </summary>
    /// <remarks>責務: 鍵を破棄します。</remarks>
    public void Dispose() => _support.Dispose();

    /// <summary>
    /// 認可の URL を開き、戻りの id_token から sub をユーザー識別子として返すことを確かめます。
    /// </summary>
    /// <remarks>責務: 正常な流れと、認可の URL のパラメーターを確かめます。</remarks>
    [Fact]
    public async Task AuthorizeOpensAppleAndReturnsSubject()
    {
        var broker = new AuthorizationCallbackBroker();
        var launcher = new CallbackLauncher(uri => new Uri(
            $"revtorque-insight://auth/apple?state={Uri.EscapeDataString(Query(uri)["state"])}&code=c1&id_token={_support.Token(Query(uri)["nonce"], Now)}"),
            broker);
        var client = MakeClient(broker, launcher);

        var session = await client.Authorize();

        Assert.Equal(new AppleAccountSession("001234.abcdef0123456789.0123"), session);
        var opened = Query(launcher.Opened!);
        Assert.Equal("https://appleid.apple.com/auth/authorize", launcher.Opened!.GetLeftPart(UriPartial.Path));
        Assert.Equal(AppleSignInTestSupport.ServicesId, opened["client_id"]);
        Assert.Equal("https://example.com/revtorque/apple-callback.html", opened["redirect_uri"]);
        Assert.Equal("code id_token", opened["response_type"]);
        Assert.Equal("fragment", opened["response_mode"]);
        Assert.False(opened.ContainsKey("scope"));
        Assert.Equal(AppleAccountCredentialState.Authorized, await client.CredentialState(session.UserIdentifier));
    }

    /// <summary>
    /// 利用者がキャンセルした・state が違う・設定がない、をそれぞれの分類で返すことを確かめます。
    /// </summary>
    /// <remarks>責務: Cancelled、Failed、Unavailable の分類を確かめます。</remarks>
    [Fact]
    public async Task FailuresAreClassifiedLikeIOS()
    {
        var broker = new AuthorizationCallbackBroker();
        var cancelled = MakeClient(broker, new CallbackLauncher(_ => new Uri("revtorque-insight://auth/apple?error=user_cancelled_authorize"), broker));
        var wrongState = MakeClient(broker, new CallbackLauncher(uri => new Uri(
            $"revtorque-insight://auth/apple?state=forged&id_token={_support.Token(Query(uri)["nonce"], Now)}"), broker));
        var notConfigured = new WebAppleAccountAuthorizationClient(
            CloudServiceConfiguration.NotConfigured,
            broker,
            new CallbackLauncher(_ => throw new InvalidOperationException(), broker),
            Validator());

        Assert.Equal(AppleAccountAuthorizationError.Cancelled, (await Assert.ThrowsAsync<AppleAccountAuthorizationException>(cancelled.Authorize)).Reason);
        Assert.Equal(AppleAccountAuthorizationError.Failed, (await Assert.ThrowsAsync<AppleAccountAuthorizationException>(wrongState.Authorize)).Reason);
        Assert.Equal(AppleAccountAuthorizationError.Unavailable, (await Assert.ThrowsAsync<AppleAccountAuthorizationException>(notConfigured.Authorize)).Reason);
    }

    /// <summary>
    /// 戻りが時間内に来ないときは Cancelled になることを確かめます。
    /// </summary>
    /// <remarks>責務: 待ちの時間切れを確かめます。</remarks>
    [Fact]
    public async Task TimeoutIsCancelled()
    {
        var broker = new AuthorizationCallbackBroker();
        var client = new WebAppleAccountAuthorizationClient(
            Configuration,
            broker,
            new CallbackLauncher(_ => null, broker),
            Validator(),
            timeout: TimeSpan.FromMilliseconds(50));

        var error = await Assert.ThrowsAsync<AppleAccountAuthorizationException>(client.Authorize);

        Assert.Equal(AppleAccountAuthorizationError.Cancelled, error.Reason);
    }

    /// <summary>
    /// ローカルモードのアカウントが、同じ識別子を返し続けることを確かめます。
    /// </summary>
    /// <remarks>責務: 設定がないときの代わりのアカウントを確かめます。</remarks>
    [Fact]
    public async Task LocalAccountIsStable()
    {
        var defaults = new InMemoryLocalKeyValueStore();
        var adapter = new LocalAccountAuthorizationAdapter(defaults, () => Guid.Parse("11111111-2222-3333-4444-555555555555"));

        var first = await adapter.Authorize();
        var second = await new LocalAccountAuthorizationAdapter(defaults, Guid.NewGuid).Authorize();

        Assert.Equal("local-11111111-2222-3333-4444-555555555555", first.UserIdentifier);
        Assert.Equal(first, second);
        Assert.Equal(AppleAccountCredentialState.Authorized, await adapter.CredentialState(first.UserIdentifier));
        Assert.Equal(AppleAccountCredentialState.NotFound, await adapter.CredentialState("someone-else"));
    }

    /// <summary>
    /// テストの対象を作ります。
    /// </summary>
    /// <remarks>責務: 決めた時計と JWKS で対象を組み立てます。</remarks>
    /// <param name="broker">戻りの受け口。</param>
    /// <param name="launcher">ブラウザーの代わり。</param>
    /// <returns>対象。</returns>
    private WebAppleAccountAuthorizationClient MakeClient(AuthorizationCallbackBroker broker, IExternalBrowserLauncher launcher) =>
        new(Configuration, broker, launcher, Validator());

    /// <summary>
    /// ID トークンの検証を作ります。
    /// </summary>
    /// <remarks>責務: テストの鍵で検証する対象を作ります。</remarks>
    /// <returns>検証。</returns>
    private AppleIdentityTokenValidator Validator()
    {
        var time = new FixedTimeProvider(Now);
        return new AppleIdentityTokenValidator(
            new AppleJsonWebKeySetCache(new HttpClient(_support.KeysHandler()), time),
            AppleSignInTestSupport.ServicesId,
            time);
    }

    /// <summary>
    /// URL のクエリーを辞書にします。
    /// </summary>
    /// <remarks>責務: テストで URL のパラメーターを読みます。</remarks>
    /// <param name="uri">URL。</param>
    /// <returns>パラメーター。</returns>
    private static IReadOnlyDictionary<string, string> Query(Uri uri) => AuthorizationCallbackBroker.Parameters(uri);

    /// <summary>
    /// ブラウザーの代わりに、開いた URL から戻りの URL を作って受け口へ渡します（開発者の https のページの転送の代わり）。
    /// </summary>
    private sealed class CallbackLauncher : IExternalBrowserLauncher
    {
        /// <summary>開いた URL から戻りの URL を作る処理です。<c>null</c> を返すと戻りません。</summary>
        private readonly Func<Uri, Uri?> _callback;

        /// <summary>戻りの受け口です。</summary>
        private readonly AuthorizationCallbackBroker _broker;

        /// <summary>
        /// 戻りの作り方と受け口を指定して作ります。
        /// </summary>
        /// <remarks>責務: テストの筋書きを固定します。</remarks>
        /// <param name="callback">戻りの URL を作る処理。</param>
        /// <param name="broker">戻りの受け口。</param>
        public CallbackLauncher(Func<Uri, Uri?> callback, AuthorizationCallbackBroker broker)
        {
            _callback = callback;
            _broker = broker;
        }

        /// <summary>開いた URL です。</summary>
        public Uri? Opened { get; private set; }

        /// <inheritdoc />
        public Task<bool> Launch(Uri uri)
        {
            Opened = uri;
            if (_callback(uri) is { } callback)
            {
                _ = Task.Run(() => Assert.True(_broker.TryDeliver(callback)));
            }
            return Task.FromResult(true);
        }
    }
}
