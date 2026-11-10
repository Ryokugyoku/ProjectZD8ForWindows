using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Settings.Ports;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Authentication;

/// <summary>Apple でサインインしたときに、ローカルモードのデータを 1 回だけ引き継ぐ処理を検証します。</summary>
public sealed class LocalAccountDataAdoptingAuthorizationPortTests
{
    /// <summary>最初の Apple サインインで、データと設定を引き継いで印を付けることを確かめます。</summary>
    [Fact]
    public async Task FirstAppleSignInAdoptsLocalDataAndSettings()
    {
        var harness = new Harness("apple-sub");
        harness.Defaults.SetString(LocalAccountAuthorizationAdapter.IdentifierKey, "local-1");
        var localSettings = new AccountSettings() with { AutomaticSessionUploadEnabled = true };
        harness.Settings.Values["local-1"] = localSettings;

        var session = await harness.Port.Authorize();

        Assert.Equal("apple-sub", session.UserIdentifier);
        Assert.Equal([("local-1", "apple-sub")], harness.Reassignments);
        Assert.Equal(localSettings, harness.Settings.Values["apple-sub"]);
        Assert.Equal("apple-sub", harness.Defaults.GetString(LocalAccountDataAdoptingAuthorizationPort.AdoptedByKey));
    }

    /// <summary>引き継ぎ先にすでに設定があれば、上書きしないことを確かめます。</summary>
    [Fact]
    public async Task ExistingAppleSettingsAreKept()
    {
        var harness = new Harness("apple-sub");
        harness.Defaults.SetString(LocalAccountAuthorizationAdapter.IdentifierKey, "local-1");
        harness.Settings.Values["local-1"] = new AccountSettings() with { AutomaticSessionUploadEnabled = true };
        var appleSettings = new AccountSettings() with { AutomaticSessionUploadEnabled = false };
        harness.Settings.Values["apple-sub"] = appleSettings;

        await harness.Port.Authorize();

        Assert.Equal(appleSettings, harness.Settings.Values["apple-sub"]);
    }

    /// <summary>2 回目以降のサインインや、別の Apple アカウントには引き継がないことを確かめます。</summary>
    [Fact]
    public async Task AdoptionHappensOnlyOnce()
    {
        var harness = new Harness("apple-sub");
        harness.Defaults.SetString(LocalAccountAuthorizationAdapter.IdentifierKey, "local-1");
        await harness.Port.Authorize();

        harness.Inner.NextIdentifier = "another-apple-sub";
        await harness.Port.Authorize();

        Assert.Single(harness.Reassignments);
    }

    /// <summary>ローカルモードを使っていなければ、何もしないことを確かめます。</summary>
    [Fact]
    public async Task NoLocalAccountMeansNothingToAdopt()
    {
        var harness = new Harness("apple-sub");

        await harness.Port.Authorize();

        Assert.Empty(harness.Reassignments);
        Assert.Null(harness.Defaults.GetString(LocalAccountDataAdoptingAuthorizationPort.AdoptedByKey));
    }

    /// <summary>付け替えに失敗してもサインインは成功し、印を付けずに次のサインインでやり直すことを確かめます。</summary>
    [Fact]
    public async Task FailedAdoptionIsRetriedOnNextSignIn()
    {
        var harness = new Harness("apple-sub") { FailNextReassignment = true };
        harness.Defaults.SetString(LocalAccountAuthorizationAdapter.IdentifierKey, "local-1");

        var session = await harness.Port.Authorize();

        Assert.Equal("apple-sub", session.UserIdentifier);
        Assert.Null(harness.Defaults.GetString(LocalAccountDataAdoptingAuthorizationPort.AdoptedByKey));

        await harness.Port.Authorize();

        Assert.Equal([("local-1", "apple-sub")], harness.Reassignments);
        Assert.Equal("apple-sub", harness.Defaults.GetString(LocalAccountDataAdoptingAuthorizationPort.AdoptedByKey));
    }

    /// <summary>保存済みセッションの確認（復元）では引き継がないことを確かめます。</summary>
    [Fact]
    public async Task CredentialStateDoesNotAdopt()
    {
        var harness = new Harness("apple-sub");
        harness.Defaults.SetString(LocalAccountAuthorizationAdapter.IdentifierKey, "local-1");

        var state = await harness.Port.CredentialState("apple-sub");

        Assert.Equal(AppleAccountCredentialState.Authorized, state);
        Assert.Empty(harness.Reassignments);
    }

    /// <summary>テストの依存一式です。</summary>
    private sealed class Harness
    {
        /// <summary>依存一式を作ります。</summary>
        /// <param name="appleIdentifier">Apple の認可が返す sub。</param>
        public Harness(string appleIdentifier)
        {
            Inner = new FakeAppleAuthorization { NextIdentifier = appleIdentifier };
            Port = new LocalAccountDataAdoptingAuthorizationPort(Inner, Defaults, Reassign, Settings);
        }

        /// <summary>Apple の認可の代用です。</summary>
        public FakeAppleAuthorization Inner { get; }

        /// <summary>端末内の値です。</summary>
        public InMemoryLocalKeyValueStore Defaults { get; } = new();

        /// <summary>設定の保存先の代用です。</summary>
        public FakeSettingsStore Settings { get; } = new();

        /// <summary>行われた付け替えです。</summary>
        public List<(string From, string To)> Reassignments { get; } = [];

        /// <summary>次の付け替えを失敗させるなら true。</summary>
        public bool FailNextReassignment { get; set; }

        /// <summary>試験対象です。</summary>
        public LocalAccountDataAdoptingAuthorizationPort Port { get; }

        /// <summary>付け替えを記録する代用です。</summary>
        /// <param name="from">元。</param>
        /// <param name="to">先。</param>
        /// <returns>付け替えた件数（1 件ずつとする）。</returns>
        private AccountDataReassignmentResult Reassign(string from, string to)
        {
            if (FailNextReassignment)
            {
                FailNextReassignment = false;
                throw new InvalidOperationException("disk full");
            }

            Reassignments.Add((from, to));
            return new AccountDataReassignmentResult(1, 0, 1, 1);
        }
    }

    /// <summary>決まった sub を返す Apple の認可の代用です。</summary>
    private sealed class FakeAppleAuthorization : IAppleAccountAuthorizationPort
    {
        /// <summary>次に返す sub です。</summary>
        public string NextIdentifier { get; set; } = "apple-sub";

        /// <inheritdoc />
        public Task<AppleAccountSession> Authorize() => Task.FromResult(new AppleAccountSession(NextIdentifier));

        /// <inheritdoc />
        public Task<AppleAccountCredentialState> CredentialState(string userIdentifier) =>
            Task.FromResult(AppleAccountCredentialState.Authorized);
    }

    /// <summary>メモリ上の設定の保存先です。</summary>
    private sealed class FakeSettingsStore : IAccountSettingsStorePort
    {
        /// <summary>アカウントごとの設定です。</summary>
        public Dictionary<string, AccountSettings> Values { get; } = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public AccountSettings? Load(string accountIdentifier) => Values.GetValueOrDefault(accountIdentifier);

        /// <inheritdoc />
        public void Save(AccountSettings settings, string accountIdentifier) => Values[accountIdentifier] = settings;

        /// <inheritdoc />
        public void StartObserving(string accountIdentifier, Action<AccountSettings> receive)
        {
        }

        /// <inheritdoc />
        public void StopObserving()
        {
        }
    }
}
