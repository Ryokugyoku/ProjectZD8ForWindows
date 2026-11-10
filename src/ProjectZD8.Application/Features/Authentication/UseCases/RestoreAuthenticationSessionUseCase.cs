using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;

namespace ProjectZD8.Application.Authentication.UseCases;

/// <summary>保存済みAppleセッションを現在の資格状態と照合します。</summary>
public sealed class RestoreAuthenticationSessionUseCase
{
    /// <summary>Apple資格状態を取得する認証境界です。</summary>
    private readonly IAppleAccountAuthorizationPort _authorizationPort;

    /// <summary>Appleユーザー識別子を保持する安全な保存境界です。</summary>
    private readonly IAuthenticationSessionStorePort _sessionStore;

    /// <summary>認証境界とセッション保存境界を注入します。</summary>
    /// <remarks>責務: 保存済みセッション復元を1件のApple資格確認境界と安全な保存境界へ結び付けます。</remarks>
    /// <param name="authorizationPort">Apple資格状態を取得する認証境界。</param>
    /// <param name="sessionStore">Appleユーザー識別子を保持する安全な保存境界。</param>
    public RestoreAuthenticationSessionUseCase(
        IAppleAccountAuthorizationPort authorizationPort,
        IAuthenticationSessionStorePort sessionStore)
    {
        _authorizationPort = authorizationPort;
        _sessionStore = sessionStore;
    }

    /// <summary>保存済み識別子を照合し、現在も有効なAppleセッションを返します。</summary>
    /// <remarks>責務: 保存済みApple識別子を現在有効なアプリ内セッションへ復元します。</remarks>
    /// <returns>Appleが現在も許可しているセッション。未保存または無効な場合は <see langword="null"/>。</returns>
    /// <exception cref="Exception">保存領域またはApple資格確認を利用できない場合の境界エラー。</exception>
    public async Task<AppleAccountSession?> Execute()
    {
        if (_sessionStore.LoadUserIdentifier() is not { } userIdentifier)
        {
            return null;
        }

        switch (await _authorizationPort.CredentialState(userIdentifier))
        {
            case AppleAccountCredentialState.Authorized:
                return new AppleAccountSession(userIdentifier);
            case AppleAccountCredentialState.Revoked:
            case AppleAccountCredentialState.NotFound:
                _sessionStore.RemoveUserIdentifier();
                return null;
            case AppleAccountCredentialState.Transferred:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(AppleAccountCredentialState));
        }
    }
}
