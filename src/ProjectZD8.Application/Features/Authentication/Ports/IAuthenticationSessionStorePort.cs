namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>Appleアカウントセッション識別子の安全な保存を抽象化します。</summary>
public interface IAuthenticationSessionStorePort
{
    /// <summary>保存済みのAppleユーザー識別子を読み込みます。</summary>
    /// <remarks>責務: 現在保存されている認証セッション識別子を1件復元します。</remarks>
    /// <returns>保存済み識別子。未保存の場合は <see langword="null"/>。</returns>
    /// <exception cref="AuthenticationSessionStoreException">安全な保存領域を読み込めない場合。</exception>
    string? LoadUserIdentifier();

    /// <summary>Appleユーザー識別子を安全な保存領域へ置き換え保存します。</summary>
    /// <remarks>責務: 1件の認証セッション識別子を次回起動用に保存します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てた空でないユーザー識別子。</param>
    /// <exception cref="AuthenticationSessionStoreException">安全な保存領域へ書き込めない場合。</exception>
    void SaveUserIdentifier(string userIdentifier);

    /// <summary>保存済みAppleユーザー識別子を削除します。</summary>
    /// <remarks>責務: 無効になった認証セッション識別子を安全な保存領域から除去します。</remarks>
    /// <exception cref="AuthenticationSessionStoreException">安全な保存領域から削除できない場合。</exception>
    void RemoveUserIdentifier();
}
