using ProjectZD8.Application.Settings.State;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Persistence.Preferences;

/// <summary>
/// アカウント設定の端末内の保持とアカウントごとの分離を検証します（iOS の UbiquitousKeyValueStoreAccountSettingsStoreTests）。
/// </summary>
public sealed class SynchronizedAccountSettingsStoreTests : IDisposable
{
    /// <summary>テスト専用の一時フォルダーです（UserDefaults の suite の代わり）。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ProjectZD8.Data.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 一時フォルダーを消します。
    /// </summary>
    /// <remarks>責務: テストの後始末をします。</remarks>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// 保存した値が、新しい保存先の組でも端末内から復元できることを確かめます。
    /// </summary>
    /// <remarks>責務: 同期値がない再起動に相当する状態でも、言語と外観が端末内に残ることを確かめます。</remarks>
    [Fact]
    public void TestSavedSettingsSurviveLocalRelaunchWithoutCloudValue()
    {
        var path = Path.Combine(_directory, "preferences.json");
        var firstStore = new SynchronizedAccountSettingsStore(new JsonFileLocalKeyValueStore(path), new FakeSynchronizedKeyValueStore());
        var expected = new AccountSettings(SettingsLanguage.English, SettingsAppearance.Dark);
        firstStore.Save(expected, "account-a");

        var relaunchedStore = new SynchronizedAccountSettingsStore(new JsonFileLocalKeyValueStore(path), new FakeSynchronizedKeyValueStore());

        Assert.Equal(expected, relaunchedStore.Load("account-a"));
    }

    /// <summary>
    /// 同じ端末でも、違う Apple ユーザーの設定が分かれることを確かめます。
    /// </summary>
    /// <remarks>責務: 2 件のユーザー識別子を、互いに違う不可逆なキーへ保存することを確かめます。</remarks>
    [Fact]
    public void TestDifferentAccountsUseIsolatedKeysWithoutRawIdentifier()
    {
        var cloud = new FakeSynchronizedKeyValueStore();
        var store = new SynchronizedAccountSettingsStore(new InMemoryLocalKeyValueStore(), cloud);
        var first = new AccountSettings(SettingsLanguage.English, SettingsAppearance.Dark);
        var second = new AccountSettings(SettingsLanguage.Spanish, SettingsAppearance.Light);

        store.Save(first, "private-apple-user-a");
        store.Save(second, "private-apple-user-b");

        Assert.Equal(first, store.Load("private-apple-user-a"));
        Assert.Equal(second, store.Load("private-apple-user-b"));
        Assert.Equal(2, cloud.SavedData.Count);
        Assert.DoesNotContain(cloud.SavedData.Keys, key => key.Contains("private-apple-user", StringComparison.Ordinal));
    }

    /// <summary>
    /// 外部変更が、監視しているアカウントだけに反映されることを確かめます。
    /// </summary>
    /// <remarks>責務: 変わったキーが一致する 1 件の同期設定を、コールバックと端末内へ渡すことを確かめます。</remarks>
    [Fact]
    public void TestExternalChangeUpdatesObservedAccount()
    {
        var cloud = new FakeSynchronizedKeyValueStore();
        var store = new SynchronizedAccountSettingsStore(new InMemoryLocalKeyValueStore(), cloud);
        store.Save(new AccountSettings(), "account-a");
        var key = cloud.SavedData.Keys.Single();
        AccountSettings? received = null;
        store.StartObserving("account-a", settings => received = settings);
        var external = new AccountSettings(SettingsLanguage.Spanish, SettingsAppearance.Dark);
        cloud.SavedData[key] = AccountSettingsJsonMapper.Encode(external);

        cloud.PostExternalChange(key);

        Assert.Equal(external, received);
        Assert.Equal(external, store.Load("account-a"));
        store.StopObserving();
    }

    /// <summary>
    /// 監視していないアカウントのキーの変更は届かないことを確かめます。
    /// </summary>
    /// <remarks>責務: キーの絞り込みを確かめます。</remarks>
    [Fact]
    public void ExternalChangeForOtherKeyIsIgnored()
    {
        var cloud = new FakeSynchronizedKeyValueStore();
        var store = new SynchronizedAccountSettingsStore(new InMemoryLocalKeyValueStore(), cloud);
        AccountSettings? received = null;
        store.StartObserving("account-a", settings => received = settings);
        var otherKey = SynchronizedAccountSettingsStore.SettingsKey("account-b");
        cloud.SavedData[otherKey] = AccountSettingsJsonMapper.Encode(new AccountSettings(SettingsLanguage.English));

        cloud.PostExternalChange(otherKey);

        Assert.Null(received);
        Assert.True(cloud.SynchronizationCount >= 1);
    }

    /// <summary>
    /// アカウントの削除で、端末内と同期先の両方の設定が消えることを確かめます。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント設定の削除が、両方の保存先に値を残さないことを確かめます。</remarks>
    [Fact]
    public void TestRemoveDeletesLocalAndSynchronizedSettings()
    {
        var cloud = new FakeSynchronizedKeyValueStore();
        var store = new SynchronizedAccountSettingsStore(new InMemoryLocalKeyValueStore(), cloud);
        store.Save(new AccountSettings(SettingsLanguage.English, SettingsAppearance.Dark), "delete-account");

        store.Remove("delete-account");

        Assert.Null(store.Load("delete-account"));
        Assert.Empty(cloud.SavedData);
    }
}
