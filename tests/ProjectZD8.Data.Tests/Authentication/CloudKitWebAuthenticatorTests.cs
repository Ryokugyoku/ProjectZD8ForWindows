using System.Net;
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Configuration;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// CloudKit の Web 認証トークンを、421 の redirectURL からブラウザーで手に入れる流れを確かめます。
/// </summary>
public sealed class CloudKitWebAuthenticatorTests
{
    /// <summary>テストの設定です。</summary>
    private static readonly CloudServiceConfiguration Configuration = new()
    {
        AppleServicesId = "com.example.web",
        AppleRedirectUri = new Uri("https://example.com/apple-callback.html"),
        CloudKitApiToken = "api",
    };

    /// <summary>
    /// 421 の redirectURL を開き、戻りの ckWebAuthToken を保存して、users/caller で確かめることを確かめます。
    /// </summary>
    /// <remarks>責務: サインインの流れ全体を確かめます。</remarks>
    [Fact]
    public async Task SignInFollowsRedirectAndStoresToken()
    {
        var handler = new FakeHttpMessageHandler((request, _) => request.RequestUri!.Query.Contains("ckWebAuthToken=", StringComparison.Ordinal)
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, """{"userRecordName":"_abc"}""")
            : FakeHttpMessageHandler.Json((HttpStatusCode)421, """{"serverErrorCode":"AUTHENTICATION_REQUIRED","redirectURL":"https://idmsa.apple.com/signin?x=1"}"""));
        var secrets = new InMemorySecretStore();
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new SecretStoreCloudKitWebAuthTokenStore(secrets));
        var broker = new AuthorizationCallbackBroker();
        var launcher = new RedirectingLauncher(broker, new Uri("revtorque-insight://auth/cloudkit?ckWebAuthToken=t%2B1"));
        var authenticator = new CloudKitWebAuthenticator(client, broker, launcher);

        var userRecordName = await authenticator.SignIn();

        Assert.Equal("_abc", userRecordName);
        Assert.Equal(new Uri("https://idmsa.apple.com/signin?x=1"), launcher.Opened);
        Assert.Equal("t+1", secrets.Values[SecretStoreCloudKitWebAuthTokenStore.DefaultItemName]);
        Assert.Equal(CloudKitAvailability.Available, client.Availability);
        Assert.Contains("ckWebAuthToken=t%2B1", handler.Requests[^1].Uri.Query);

        authenticator.SignOut();
        Assert.Equal(CloudKitAvailability.SignedOut, client.Availability);
    }

    /// <summary>
    /// ブラウザーで開いたページが戻らなければ、時間切れで取り消しになることを確かめます。
    /// </summary>
    /// <remarks>責務: 時間切れの扱いを確かめます。</remarks>
    [Fact]
    public async Task TimeoutCancelsSignIn()
    {
        var handler = new FakeHttpMessageHandler((_, _) => FakeHttpMessageHandler.Json(
            (HttpStatusCode)421, """{"serverErrorCode":"AUTHENTICATION_REQUIRED","redirectURL":"https://idmsa.apple.com/signin"}"""));
        var client = new CloudKitWebServicesClient(new HttpClient(handler), Configuration, new SecretStoreCloudKitWebAuthTokenStore(new InMemorySecretStore()));
        var broker = new AuthorizationCallbackBroker();
        var authenticator = new CloudKitWebAuthenticator(client, broker, new RedirectingLauncher(broker, null), TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(authenticator.SignIn);
    }

    /// <summary>
    /// ブラウザーの代わりに、決めた戻りの URL を受け口へ渡します。
    /// </summary>
    private sealed class RedirectingLauncher : IExternalBrowserLauncher
    {
        /// <summary>戻りの受け口です。</summary>
        private readonly AuthorizationCallbackBroker _broker;

        /// <summary>戻りの URL です。<c>null</c> なら戻りません。</summary>
        private readonly Uri? _callback;

        /// <summary>
        /// 受け口と戻りの URL を指定して作ります。
        /// </summary>
        /// <remarks>責務: テストの筋書きを固定します。</remarks>
        /// <param name="broker">戻りの受け口。</param>
        /// <param name="callback">戻りの URL。</param>
        public RedirectingLauncher(AuthorizationCallbackBroker broker, Uri? callback)
        {
            _broker = broker;
            _callback = callback;
        }

        /// <summary>開いた URL です。</summary>
        public Uri? Opened { get; private set; }

        /// <inheritdoc />
        public Task<bool> Launch(Uri uri)
        {
            Opened = uri;
            if (_callback is { } callback)
            {
                _ = Task.Run(() => _broker.TryDeliver(callback));
            }
            return Task.FromResult(true);
        }
    }
}
