using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Data.Configuration;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// Sign in with Apple の Web の流れで Apple アカウントを認証します（iOS の <c>AuthenticationServicesAppleAccountAuthorizationClient</c> の Windows 版）。
/// </summary>
/// <remarks>
/// <para>流れ：既定のブラウザーで Apple の認可ページを開く → Apple が開発者の https のページ（Services ID の Return URL）へ
/// <c>#code=…&amp;id_token=…&amp;state=…</c> 付きで戻す → そのページが <c>revtorque-insight://auth/apple?…</c> へ転送する →
/// アプリが <see cref="AuthorizationCallbackBroker.TryDeliver"/> でここへ渡す → state を照合し、ID トークンを Apple の公開鍵で検証して
/// <c>sub</c> をユーザー識別子にします。同じ Team の Services ID なら、<c>sub</c> は iOS の <c>credential.user</c> と同じです。</para>
/// <para>利用者がキャンセルした・10 分（既定）以内に戻らなかったときは Cancelled、検証に失敗したら Failed、設定がない・
/// すでに実行中・ブラウザーを開けないときは Unavailable を投げます（iOS と同じ分類）。</para>
/// <para><b>iOS との違い</b>：Web には資格状態を問い合わせる手段がない（client secret を持つサーバーが必要）ため、
/// <see cref="CredentialState"/> は常に Authorized を返します。ほかの端末でのログアウトは、セッション失効の同期で検出します。</para>
/// </remarks>
public sealed class WebAppleAccountAuthorizationClient : IAppleAccountAuthorizationPort
{
    /// <summary>既定の待ち時間です。</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Services ID とリダイレクト先の設定です。</summary>
    private readonly CloudServiceConfiguration _configuration;

    /// <summary>ブラウザーからの戻りの受け口です。</summary>
    private readonly AuthorizationCallbackBroker _broker;

    /// <summary>ブラウザーを開く境界です。</summary>
    private readonly IExternalBrowserLauncher _launcher;

    /// <summary>ID トークンの検証です。</summary>
    private readonly AppleIdentityTokenValidator _validator;

    /// <summary>state と nonce を作る処理です。</summary>
    private readonly Func<string>? _randomToken;

    /// <summary>戻りを待つ時間です。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>実行中かどうかです。</summary>
    private int _inProgress;

    /// <summary>
    /// 設定、戻りの受け口、ブラウザー、ID トークンの検証を注入して作ります。
    /// </summary>
    /// <remarks>責務: Web のサインインに使う境界を固定します。</remarks>
    /// <param name="configuration">Services ID とリダイレクト先を含む設定。</param>
    /// <param name="broker">ブラウザーからの戻りの受け口。</param>
    /// <param name="launcher">ブラウザーを開く境界。</param>
    /// <param name="validator">ID トークンの検証。</param>
    /// <param name="randomToken">state と nonce を作る処理（テスト用）。</param>
    /// <param name="timeout">戻りを待つ時間。<c>null</c> なら 10 分。</param>
    public WebAppleAccountAuthorizationClient(
        CloudServiceConfiguration configuration,
        AuthorizationCallbackBroker broker,
        IExternalBrowserLauncher launcher,
        AppleIdentityTokenValidator validator,
        Func<string>? randomToken = null,
        TimeSpan? timeout = null)
    {
        _configuration = configuration;
        _broker = broker;
        _launcher = launcher;
        _validator = validator;
        _randomToken = randomToken;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// ブラウザーで Apple の認可ページを開き、戻りを待ってアプリ固有のセッションを返します。
    /// </summary>
    /// <remarks>責務: 1 回の Apple ID の認可要求を Web の流れで実行します。</remarks>
    /// <returns>Apple がこのアプリへ割り当てたユーザー識別子を持つセッション。</returns>
    /// <exception cref="AppleAccountAuthorizationException">設定がない・キャンセル・失敗の場合。</exception>
    public async Task<AppleAccountSession> Authorize()
    {
        if (!_configuration.IsConfigured || _configuration.AppleServicesId is not { } servicesId
            || _configuration.AppleRedirectUri is not { } redirectUri)
        {
            throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Unavailable);
        }
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
        {
            throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Unavailable);
        }
        try
        {
            var request = AppleAuthorizationRequest.Create(servicesId, redirectUri, _randomToken);
            var callback = _broker.WaitForCallback(AuthorizationCallbackBroker.AppleRoute, _timeout);
            if (!await _launcher.Launch(request.Uri).ConfigureAwait(false))
            {
                _broker.Cancel(AuthorizationCallbackBroker.AppleRoute);
                await IgnoreCancellation(callback).ConfigureAwait(false);
                AuthLog.AppleBrowserLaunchFailed(AuthLog.Logger);
                throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Unavailable);
            }

            Uri uri;
            try
            {
                uri = await callback.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                AuthLog.AppleSignInCancelled(AuthLog.Logger, exception is TimeoutException);
                throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Cancelled);
            }
            return await SessionFrom(uri, request).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _inProgress, 0);
        }
    }

    /// <summary>
    /// 保存済みのユーザー識別子の資格状態を返します。
    /// </summary>
    /// <remarks>責務: Web では問い合わせられないので、保存済みのセッションを有効として扱います。</remarks>
    /// <param name="userIdentifier">以前の認証で保存したユーザー識別子。</param>
    /// <returns>常に <see cref="AppleAccountCredentialState.Authorized"/>。</returns>
    public Task<AppleAccountCredentialState> CredentialState(string userIdentifier) =>
        Task.FromResult(AppleAccountCredentialState.Authorized);

    /// <summary>
    /// 実行中のサインインをやめます。
    /// </summary>
    /// <remarks>責務: 戻りの待ちを取り消し、<see cref="Authorize"/> を Cancelled で終わらせます。</remarks>
    public void CancelAuthorization() => _broker.Cancel(AuthorizationCallbackBroker.AppleRoute);

    /// <summary>
    /// 戻りの URL からセッションを作ります。
    /// </summary>
    /// <remarks>責務: エラー・state・ID トークンを確かめ、ユーザー識別子を取り出します。</remarks>
    /// <param name="uri">戻りの URL。</param>
    /// <param name="request">送った要求。</param>
    /// <returns>セッション。</returns>
    private async Task<AppleAccountSession> SessionFrom(Uri uri, AppleAuthorizationRequest request)
    {
        var parameters = AuthorizationCallbackBroker.Parameters(uri);
        if (parameters.TryGetValue("error", out var error))
        {
            AuthLog.AppleCallbackRejected(AuthLog.Logger, string.Equals(error, "user_cancelled_authorize", StringComparison.Ordinal) ? "user-cancelled" : "apple-error");
            throw new AppleAccountAuthorizationException(
                string.Equals(error, "user_cancelled_authorize", StringComparison.Ordinal)
                    ? AppleAccountAuthorizationError.Cancelled
                    : AppleAccountAuthorizationError.Failed);
        }
        if (!parameters.TryGetValue("state", out var state) || !string.Equals(state, request.State, StringComparison.Ordinal)
            || !parameters.TryGetValue("id_token", out var idToken) || idToken.Length == 0)
        {
            AuthLog.AppleCallbackRejected(AuthLog.Logger, "state-mismatch-or-missing-token");
            throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Failed);
        }
        try
        {
            var claims = await _validator.Validate(idToken, request.Nonce).ConfigureAwait(false);
            return new AppleAccountSession(claims.Subject);
        }
        catch (Exception exception) when (exception is AppleIdentityTokenException or HttpRequestException or TaskCanceledException)
        {
            AuthLog.AppleIdTokenFailed(AuthLog.Logger, exception);
            throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Failed);
        }
    }

    /// <summary>
    /// 取り消した待ちの終わりを待ちます。
    /// </summary>
    /// <remarks>責務: 取り消しの例外を観測済みにして、未観測の例外を残しません。</remarks>
    /// <param name="task">待ち。</param>
    /// <returns>処理。</returns>
    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
        }
    }
}
