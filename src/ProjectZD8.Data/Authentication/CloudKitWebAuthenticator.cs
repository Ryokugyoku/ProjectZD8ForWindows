using System.Net;
using ProjectZD8.Data.Network.CloudKit;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// CloudKit Web Services の Web 認証トークン（ckWebAuthToken）を、ブラウザーでの iCloud のサインインで手に入れます。
/// </summary>
/// <remarks>
/// <para>流れ：<c>users/caller</c> を呼ぶ → 421 <c>AUTHENTICATION_REQUIRED</c> の <c>redirectURL</c> をブラウザーで開く →
/// 利用者が iPhone と同じ Apple ID で iCloud にサインインする → CloudKit Console の API トークンの「Sign In Callback」
/// （URL Redirect）に登録した URL へ <c>?ckWebAuthToken=…</c> 付きで戻る → その URL（https のページなら、そこから
/// <c>revtorque-insight://auth/cloudkit?ckWebAuthToken=…</c> へ転送）でアプリへ戻る → トークンを保存し、もう一度
/// <c>users/caller</c> で確かめます。</para>
/// <para>トークンは既定で 30 分、サインイン画面で「サインインしたままにする」を選ぶと 2 週間有効です（Apple の資料）。</para>
/// </remarks>
public sealed class CloudKitWebAuthenticator
{
    /// <summary>既定の待ち時間です。</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>CloudKit のクライアントです。</summary>
    private readonly CloudKitWebServicesClient _client;

    /// <summary>ブラウザーからの戻りの受け口です。</summary>
    private readonly AuthorizationCallbackBroker _broker;

    /// <summary>ブラウザーを開く境界です。</summary>
    private readonly IExternalBrowserLauncher _launcher;

    /// <summary>戻りを待つ時間です。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>
    /// クライアント、戻りの受け口、ブラウザーを注入して作ります。
    /// </summary>
    /// <remarks>責務: CloudKit のサインインに使う境界を固定します。</remarks>
    /// <param name="client">CloudKit のクライアント。</param>
    /// <param name="broker">ブラウザーからの戻りの受け口。</param>
    /// <param name="launcher">ブラウザーを開く境界。</param>
    /// <param name="timeout">戻りを待つ時間。<c>null</c> なら 10 分。</param>
    public CloudKitWebAuthenticator(
        CloudKitWebServicesClient client,
        AuthorizationCallbackBroker broker,
        IExternalBrowserLauncher launcher,
        TimeSpan? timeout = null)
    {
        _client = client;
        _broker = broker;
        _launcher = launcher;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// CloudKit にサインインしていなければ、ブラウザーでサインインしてトークンを保存します。
    /// </summary>
    /// <remarks>責務: 1 回の CloudKit のサインインをやり切り、利用者のレコード名を返します。</remarks>
    /// <returns>CloudKit の利用者レコード名。</returns>
    /// <exception cref="CloudKitUnavailableException">設定がない場合。</exception>
    /// <exception cref="OperationCanceledException">利用者がやめたか、時間内に戻らなかった場合。</exception>
    /// <exception cref="CloudKitException">サインイン用の URL やトークンが得られない場合。</exception>
    public async Task<string> SignIn()
    {
        Uri redirect;
        try
        {
            return await _client.FetchCallerRecordName().ConfigureAwait(false);
        }
        catch (CloudKitAuthenticationRequiredException exception)
        {
            redirect = exception.RedirectUrl
                ?? throw new CloudKitException((HttpStatusCode)421, exception.ServerErrorCode, "redirectURL がありません。");
        }

        var callback = _broker.WaitForCallback(AuthorizationCallbackBroker.CloudKitRoute, _timeout);
        if (!await _launcher.Launch(redirect).ConfigureAwait(false))
        {
            _broker.Cancel(AuthorizationCallbackBroker.CloudKitRoute);
            try
            {
                await callback.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            throw new CloudKitUnavailableException(CloudKitAvailability.SignedOut);
        }
        Uri uri;
        try
        {
            uri = await callback.ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new OperationCanceledException("CloudKit のサインインが時間内に終わりませんでした。", exception);
        }

        var parameters = AuthorizationCallbackBroker.Parameters(uri);
        var token = parameters.TryGetValue("ckWebAuthToken", out var webAuthToken) && webAuthToken.Length > 0
            ? webAuthToken
            : parameters.TryGetValue("ckSession", out var session) && session.Length > 0
                ? session
                : throw new CloudKitException(HttpStatusCode.OK, null, "戻りの URL に ckWebAuthToken がありません。");
        _client.StoreWebAuthToken(token);
        return await _client.FetchCallerRecordName().ConfigureAwait(false);
    }

    /// <summary>
    /// 実行中のサインインをやめます。
    /// </summary>
    /// <remarks>責務: 戻りの待ちを取り消します。</remarks>
    public void Cancel() => _broker.Cancel(AuthorizationCallbackBroker.CloudKitRoute);

    /// <summary>
    /// CloudKit からサインアウトします。
    /// </summary>
    /// <remarks>責務: 保存済みのトークンを消します。</remarks>
    public void SignOut() => _client.SignOut();
}
