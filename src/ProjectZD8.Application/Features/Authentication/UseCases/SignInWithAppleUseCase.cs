using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;

namespace ProjectZD8.Application.Authentication.UseCases;

/// <summary>Apple認証を実行して有効なセッション識別子を安全に保存します。</summary>
public sealed class SignInWithAppleUseCase
{
    /// <summary>Appleシステム認証を実行する境界です。</summary>
    private readonly IAppleAccountAuthorizationPort _authorizationPort;

    /// <summary>Appleユーザー識別子を保持する安全な保存境界です。</summary>
    private readonly IAuthenticationSessionStorePort _sessionStore;

    /// <summary>認証境界とセッション保存境界を注入します。</summary>
    /// <remarks>責務: Appleログインを1件のシステム認証境界と安全な保存境界へ結び付けます。</remarks>
    /// <param name="authorizationPort">Appleシステム認証を実行する境界。</param>
    /// <param name="sessionStore">Appleユーザー識別子を保持する安全な保存境界。</param>
    public SignInWithAppleUseCase(
        IAppleAccountAuthorizationPort authorizationPort,
        IAuthenticationSessionStorePort sessionStore)
    {
        _authorizationPort = authorizationPort;
        _sessionStore = sessionStore;
    }

    /// <summary>Apple認証を完了し、返されたユーザー識別子を次回起動用に保存します。</summary>
    /// <remarks>責務: 1回のAppleログイン結果を認証済みセッションとして確定します。</remarks>
    /// <returns>Appleが発行したアプリ固有セッション。</returns>
    /// <exception cref="AppleAccountAuthorizationException">Apple認証を完了できない場合、または空のユーザー識別子が返された場合（<c>Failed</c>）。</exception>
    /// <exception cref="Exception">安全なセッション保存を完了できない場合の境界エラー。</exception>
    public async Task<AppleAccountSession> Execute()
    {
        var session = await _authorizationPort.Authorize();
        if (session.UserIdentifier.Length == 0)
        {
            throw new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Failed);
        }
        _sessionStore.SaveUserIdentifier(session.UserIdentifier);
        return session;
    }
}
