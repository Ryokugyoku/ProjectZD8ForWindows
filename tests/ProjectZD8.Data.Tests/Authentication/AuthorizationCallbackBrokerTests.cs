using ProjectZD8.Data.Authentication;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// カスタム URL スキームでの戻りの受け渡しを確かめます。
/// </summary>
public sealed class AuthorizationCallbackBrokerTests
{
    /// <summary>
    /// 経路の合う戻りだけを、待っている処理へ渡すことを確かめます。
    /// </summary>
    /// <remarks>責務: スキーム・ホスト・経路の照合を確かめます。</remarks>
    [Fact]
    public async Task DeliversOnlyMatchingRoute()
    {
        var broker = new AuthorizationCallbackBroker();
        var wait = broker.WaitForCallback(AuthorizationCallbackBroker.CloudKitRoute, TimeSpan.FromSeconds(5));

        Assert.False(broker.TryDeliver(new Uri("https://example.com/auth/cloudkit")));
        Assert.False(broker.TryDeliver(new Uri("revtorque-insight://auth/apple?x=1")));
        Assert.False(broker.TryDeliver(new Uri("other-scheme://auth/cloudkit")));
        Assert.True(broker.TryDeliver(new Uri("revtorque-insight://auth/cloudkit?ckWebAuthToken=a%2Bb%2Fc%3D")));

        var delivered = await wait;
        Assert.Equal("a+b/c=", AuthorizationCallbackBroker.Parameters(delivered)["ckWebAuthToken"]);
        Assert.False(broker.TryDeliver(new Uri("revtorque-insight://auth/cloudkit?again=1")));
    }

    /// <summary>
    /// クエリーとフラグメントの両方のパラメーターを読み、<c>+</c> を空白にしないことを確かめます。
    /// </summary>
    /// <remarks>責務: 戻りの URL のパラメーターの読み方を確かめます。</remarks>
    [Fact]
    public void ParametersReadQueryAndFragment()
    {
        var parameters = AuthorizationCallbackBroker.Parameters(
            new Uri("revtorque-insight://auth/apple?state=s1#code=c%2B1&id_token=a.b.c&raw=x+y"));

        Assert.Equal("s1", parameters["state"]);
        Assert.Equal("c+1", parameters["code"]);
        Assert.Equal("a.b.c", parameters["id_token"]);
        Assert.Equal("x+y", parameters["raw"]);
    }

    /// <summary>
    /// 同じ経路で新しく待つと前の待ちが取り消され、取り消し・時間切れも知らせることを確かめます。
    /// </summary>
    /// <remarks>責務: 待ちの置き換え・取り消し・時間切れを確かめます。</remarks>
    [Fact]
    public async Task NewWaitCancelsPreviousAndTimeoutIsReported()
    {
        var broker = new AuthorizationCallbackBroker();
        var first = broker.WaitForCallback(AuthorizationCallbackBroker.AppleRoute, TimeSpan.FromSeconds(5));
        var second = broker.WaitForCallback(AuthorizationCallbackBroker.AppleRoute, TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        broker.Cancel(AuthorizationCallbackBroker.AppleRoute);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAsync<TimeoutException>(
            () => broker.WaitForCallback(AuthorizationCallbackBroker.AppleRoute, TimeSpan.FromMilliseconds(20)));
    }
}
