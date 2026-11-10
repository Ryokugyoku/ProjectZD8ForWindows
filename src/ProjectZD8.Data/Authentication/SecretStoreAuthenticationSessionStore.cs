using ProjectZD8.Application.Authentication.Ports;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// 秘密の保存先を使って、Apple のユーザー識別子を端末内に安全に保持します（iOS の <c>KeychainAuthenticationSessionStore</c>）。
/// </summary>
public sealed class SecretStoreAuthenticationSessionStore : IAuthenticationSessionStorePort
{
    /// <summary>既定の項目名です（iOS の Keychain のサービス名とアカウント名を合わせたもの）。</summary>
    public const string DefaultItemName = "Ryokugyoku.ProjectZD8.authentication/apple-account-user-identifier";

    /// <summary>秘密の保存先です。</summary>
    private readonly ISecretStore _store;

    /// <summary>Apple のユーザー識別子を入れる項目名です。</summary>
    private readonly string _itemName;

    /// <summary>
    /// 保存先と項目名を注入して作ります。
    /// </summary>
    /// <remarks>責務: 認証セッションの識別子の保存先を 1 件の項目に固定します。</remarks>
    /// <param name="store">秘密の保存先。</param>
    /// <param name="itemName">項目名（テストでは一意な名前を渡します）。</param>
    public SecretStoreAuthenticationSessionStore(ISecretStore store, string itemName = DefaultItemName)
    {
        _store = store;
        _itemName = itemName;
    }

    /// <summary>
    /// 保存済みの Apple のユーザー識別子を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の保存値を認証セッションの識別子へ復元します。</remarks>
    /// <returns>保存済みの識別子。未保存なら <c>null</c>。</returns>
    /// <exception cref="AuthenticationSessionStoreException">読めないか、保存値が空の場合。</exception>
    public string? LoadUserIdentifier()
    {
        string? value;
        try
        {
            value = _store.Read(_itemName);
        }
        catch (SecretStoreException)
        {
            throw new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);
        }
        if (value is null)
        {
            return null;
        }
        return value.Length > 0 ? value : throw new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);
    }

    /// <summary>
    /// Apple のユーザー識別子を置き換えて保存します。
    /// </summary>
    /// <remarks>責務: 1 件の空でない識別子を端末内の安全な保存先へ保存します。</remarks>
    /// <param name="userIdentifier">Apple がこのアプリへ割り当てた空でないユーザー識別子。</param>
    /// <exception cref="AuthenticationSessionStoreException">空の識別子か、書けない場合。</exception>
    public void SaveUserIdentifier(string userIdentifier)
    {
        if (userIdentifier.Length == 0)
        {
            throw new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);
        }
        try
        {
            _store.Write(_itemName, userIdentifier);
        }
        catch (SecretStoreException)
        {
            throw new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);
        }
    }

    /// <summary>
    /// 保存済みの Apple のユーザー識別子を消します。
    /// </summary>
    /// <remarks>責務: 1 件の認証セッションの項目をない状態にします。</remarks>
    /// <exception cref="AuthenticationSessionStoreException">消せない場合。</exception>
    public void RemoveUserIdentifier()
    {
        try
        {
            _store.Delete(_itemName);
        }
        catch (SecretStoreException)
        {
            throw new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);
        }
    }
}
