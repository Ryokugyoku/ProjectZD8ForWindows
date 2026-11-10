using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// 認証セッションの保存を検証します（iOS の KeychainAuthenticationSessionStoreTests。保存先はメモリ上の偽物）。
/// </summary>
public sealed class SecretStoreAuthenticationSessionStoreTests
{
    /// <summary>
    /// Apple のユーザー識別子を保存・読み込み・削除できることを確かめます。
    /// </summary>
    /// <remarks>責務: 一意な項目で、認証セッションの識別子の往復と削除を確かめます。</remarks>
    [Fact]
    public void TestSaveLoadAndRemoveUserIdentifier()
    {
        var store = MakeStore(new InMemorySecretStore());

        store.SaveUserIdentifier("keychain-user");
        Assert.Equal("keychain-user", store.LoadUserIdentifier());

        store.RemoveUserIdentifier();
        Assert.Null(store.LoadUserIdentifier());
    }

    /// <summary>
    /// 空の Apple のユーザー識別子を保存しないことを確かめます。
    /// </summary>
    /// <remarks>責務: 不完全な識別子が、書き込みの前に拒否されることを確かめます。</remarks>
    [Fact]
    public void TestEmptyUserIdentifierIsRejected()
    {
        var store = MakeStore(new InMemorySecretStore());

        var error = Assert.Throws<AuthenticationSessionStoreException>(() => store.SaveUserIdentifier(""));

        Assert.Equal(AuthenticationSessionStoreError.Unavailable, error.Reason);
    }

    /// <summary>
    /// 保存先を使えないときは Unavailable になることを確かめます。
    /// </summary>
    /// <remarks>責務: OS の保存先のエラーの変換を確かめます。</remarks>
    [Fact]
    public void StoreFailureIsUnavailable()
    {
        var store = MakeStore(new InMemorySecretStore { Fails = true });

        Assert.Equal(
            AuthenticationSessionStoreError.Unavailable,
            Assert.Throws<AuthenticationSessionStoreException>(() => store.LoadUserIdentifier()).Reason);
        Assert.Equal(
            AuthenticationSessionStoreError.Unavailable,
            Assert.Throws<AuthenticationSessionStoreException>(() => store.SaveUserIdentifier("x")).Reason);
    }

    /// <summary>
    /// CloudKit のトークンの保存先が、読めないときにサインアウトとして扱い、保存と削除ができることを確かめます。
    /// </summary>
    /// <remarks>責務: ckWebAuthToken の保存を確かめます。</remarks>
    [Fact]
    public void WebAuthTokenStoreRoundTrips()
    {
        var secrets = new InMemorySecretStore();
        var tokens = new SecretStoreCloudKitWebAuthTokenStore(secrets);

        Assert.Null(tokens.Load());
        tokens.Save("token");
        Assert.Equal("token", new SecretStoreCloudKitWebAuthTokenStore(secrets).Load());
        tokens.Remove();
        Assert.Null(new SecretStoreCloudKitWebAuthTokenStore(secrets).Load());
        Assert.Null(new SecretStoreCloudKitWebAuthTokenStore(new InMemorySecretStore { Fails = true }).Load());
    }

    /// <summary>
    /// テストごとの保存先を作ります。
    /// </summary>
    /// <remarks>責務: テストの間でぶつからない一意な項目名を割り当てます。</remarks>
    /// <param name="secrets">秘密の保存先。</param>
    /// <returns>対象。</returns>
    private static SecretStoreAuthenticationSessionStore MakeStore(InMemorySecretStore secrets) =>
        new(secrets, $"Ryokugyoku.ProjectZD8Tests.{Guid.NewGuid()}");
}
