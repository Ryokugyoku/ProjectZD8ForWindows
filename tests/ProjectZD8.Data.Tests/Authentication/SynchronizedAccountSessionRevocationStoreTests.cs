using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>
/// セッション失効の世代の比較と通知の分離を検証します（iOS の UbiquitousKeyValueStoreAccountSessionRevocationStoreTests）。
/// </summary>
public sealed class SynchronizedAccountSessionRevocationStoreTests
{
    /// <summary>
    /// 新しいログインが今の失効世代を受理し、すぐにはログアウトしないことを確かめます。
    /// </summary>
    /// <remarks>責務: 登録済みの世代と同じ同期値が失効の通知にならないことを確かめます。</remarks>
    [Fact]
    public void TestRegisteredCurrentSessionDoesNotReceiveExistingRevocation()
    {
        var (store, _, _) = Make();
        store.PublishRevocation("account-a");
        store.RegisterCurrentSession("account-a");
        var deliveryCount = 0;

        store.StartObserving("account-a", () => deliveryCount++);

        Assert.Equal(0, deliveryCount);
        store.StopObserving();
    }

    /// <summary>
    /// 監視を始めた後に届いた新しい世代を、同じ世代につき 1 回だけ通知することを確かめます。
    /// </summary>
    /// <remarks>責務: 1 件の外部の失効世代が、重複しないコールバックになることを確かめます。</remarks>
    [Fact]
    public void TestExternalRevocationIsDeliveredOnlyOncePerMarker()
    {
        var (store, cloud, _) = Make();
        store.RegisterCurrentSession("account-a");
        var deliveryCount = 0;
        store.StartObserving("account-a", () => deliveryCount++);
        store.PublishRevocation("account-a");
        var key = cloud.SavedData.Keys.Single();

        cloud.PostExternalChange(key);
        cloud.PostExternalChange(key);

        Assert.Equal(1, deliveryCount);
        store.StopObserving();
    }

    /// <summary>
    /// オフラインの間に更新された失効世代を、次に監視を始めたときに見つけることを確かめます。
    /// </summary>
    /// <remarks>責務: 通知を受けなかった端末でも、保存済みの基準との差から失効を見つけることを確かめます。</remarks>
    [Fact]
    public void TestObservationStartDetectsRevocationMissedWhileOffline()
    {
        var (store, cloud, defaults) = Make();
        store.RegisterCurrentSession("account-a");
        store.PublishRevocation("account-a");
        var relaunchedStore = new SynchronizedAccountSessionRevocationStore(defaults, cloud);
        var deliveryCount = 0;

        relaunchedStore.StartObserving("account-a", () => deliveryCount++);

        Assert.Equal(1, deliveryCount);
        relaunchedStore.StopObserving();
    }

    /// <summary>
    /// 失効の同期キーが Apple のユーザー識別子を直接含まず、アカウントごとに分かれることを確かめます。
    /// </summary>
    /// <remarks>責務: 2 件のユーザー識別子を、互いに違う不可逆な失効キーへ変換することを確かめます。</remarks>
    [Fact]
    public void TestRevocationKeysAreAccountScopedWithoutRawIdentifier()
    {
        var (store, cloud, _) = Make();

        store.PublishRevocation("private-user-a");
        store.PublishRevocation("private-user-b");

        Assert.Equal(2, cloud.SavedData.Count);
        Assert.DoesNotContain(cloud.SavedData.Keys, key => key.Contains("private-user", StringComparison.Ordinal));
    }

    /// <summary>
    /// 失効の印が UUID の大文字の文字列（UTF-8）であることを確かめます（iOS の <c>UUID().uuidString</c>）。
    /// </summary>
    /// <remarks>責務: CloudKit の marker に入れる値の形を確かめます。</remarks>
    [Fact]
    public void MarkerIsUppercaseUuidString()
    {
        var cloud = new FakeSynchronizedKeyValueStore();
        var store = new SynchronizedAccountSessionRevocationStore(
            new InMemoryLocalKeyValueStore(),
            cloud,
            () => Guid.Parse("2f1a8e2c-0000-4000-8000-000000000001"));

        store.PublishRevocation("account");

        Assert.Equal("2F1A8E2C-0000-4000-8000-000000000001", System.Text.Encoding.UTF8.GetString(cloud.SavedData.Values.Single()));
        Assert.Equal(SynchronizedAccountSessionRevocationStore.RevocationKey("account"), cloud.SavedData.Keys.Single());
    }

    /// <summary>
    /// テストの対象と依存を作ります。
    /// </summary>
    /// <remarks>責務: 1 件のテストへ専用の保存先と対象を同じ構成で渡します。</remarks>
    /// <returns>対象、同期先、端末内の保存先。</returns>
    private static (SynchronizedAccountSessionRevocationStore Store, FakeSynchronizedKeyValueStore Cloud, InMemoryLocalKeyValueStore Defaults) Make()
    {
        var defaults = new InMemoryLocalKeyValueStore();
        var cloud = new FakeSynchronizedKeyValueStore();
        return (new SynchronizedAccountSessionRevocationStore(defaults, cloud), cloud, defaults);
    }
}
