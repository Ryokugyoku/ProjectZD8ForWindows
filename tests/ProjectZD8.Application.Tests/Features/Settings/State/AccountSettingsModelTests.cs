using ProjectZD8.Application.Settings.Actions;
using ProjectZD8.Application.Settings.Ports;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Application.Settings.UseCases;

namespace ProjectZD8.Application.Tests.Settings.State;

/// <summary>アカウント設定モデルのスコープ分離、保存、同期反映を検証します。</summary>
public sealed class AccountSettingsModelTests
{
    /// <summary>認証済みアカウントの保存設定を初期表示へ復元することを検証します。</summary>
    /// <remarks>責務: アカウント識別子の通知が対応する言語と外観だけをモデルへ読み込むことを確認します。</remarks>
    [Fact]
    public void AccountActivationRestoresSavedSettings()
    {
        var store = new AccountSettingsStorePortFake();
        store.SavedSettings["user-a"] = new AccountSettings(language: SettingsLanguage.English, appearance: SettingsAppearance.Dark);
        var model = MakeModel(store);

        model.Send(new AccountSettingsAction.AccountIdentifierChanged("user-a"));

        Assert.Equal(new AccountSettings(language: SettingsLanguage.English, appearance: SettingsAppearance.Dark), model.Settings);
        Assert.Equal("user-a", store.ObservedAccountIdentifier);
    }

    /// <summary>表示設定と自動アップロードの変更を現在のアカウントだけへ保存することを検証します。</summary>
    /// <remarks>責務: 3件の設定操作を1件のアカウント設定として保存します。</remarks>
    [Fact]
    public void SelectionChangesPersistForActiveAccount()
    {
        var store = new AccountSettingsStorePortFake();
        var model = MakeModel(store);
        model.Send(new AccountSettingsAction.AccountIdentifierChanged("user-a"));

        model.Send(new AccountSettingsAction.LanguageSelected(SettingsLanguage.Spanish));
        model.Send(new AccountSettingsAction.AppearanceSelected(SettingsAppearance.Light));
        model.Send(new AccountSettingsAction.AutomaticSessionUploadChanged(true));

        Assert.Equal(
            new AccountSettings(
                language: SettingsLanguage.Spanish,
                appearance: SettingsAppearance.Light,
                automaticSessionUploadEnabled: true),
            store.SavedSettings.GetValueOrDefault("user-a"));
        Assert.Equal(3, store.SaveCount);
    }

    /// <summary>別アカウントへ切り替えた際に以前の設定を引き継がないことを検証します。</summary>
    /// <remarks>責務: アカウントスコープ変更が新しい識別子固有の設定だけを表示状態へ反映します。</remarks>
    [Fact]
    public void AccountSwitchKeepsSettingsIsolated()
    {
        var store = new AccountSettingsStorePortFake();
        store.SavedSettings["user-a"] = new AccountSettings(language: SettingsLanguage.English, appearance: SettingsAppearance.Dark);
        store.SavedSettings["user-b"] = new AccountSettings(language: SettingsLanguage.Spanish, appearance: SettingsAppearance.Light);
        var model = MakeModel(store);

        model.Send(new AccountSettingsAction.AccountIdentifierChanged("user-a"));
        model.Send(new AccountSettingsAction.AccountIdentifierChanged("user-b"));

        Assert.Equal(new AccountSettings(language: SettingsLanguage.Spanish, appearance: SettingsAppearance.Light), model.Settings);
        Assert.Equal(2, store.StopObservationCount);
    }

    /// <summary>別端末から届いた設定を起動中の表示へ反映することを検証します。</summary>
    /// <remarks>責務: 現在のアカウントに対する同期変更をモデルの言語と外観へ反映します。</remarks>
    [Fact]
    public void ExternalSettingsUpdateCurrentState()
    {
        var store = new AccountSettingsStorePortFake();
        var model = MakeModel(store);
        model.Send(new AccountSettingsAction.AccountIdentifierChanged("user-a"));

        store.SendExternal(new AccountSettings(language: SettingsLanguage.English, appearance: SettingsAppearance.Dark));

        Assert.Equal(new AccountSettings(language: SettingsLanguage.English, appearance: SettingsAppearance.Dark), model.Settings);
    }

    /// <summary>テスト対象モデルを指定保存境界で生成します。</summary>
    /// <remarks>責務: 1件の設定モデルテスト用依存関係を構築します。</remarks>
    /// <param name="store">保存、復元、同期通知を記録するFake。</param>
    /// <returns>Fakeを注入したアカウント設定モデル。</returns>
    private static AccountSettingsModel MakeModel(AccountSettingsStorePortFake store) =>
        new(new AccountSettings(), new AccountSettingsUseCase(store));

    /// <summary>アカウント設定モデルテストで保存と同期通知を記録します。</summary>
    private sealed class AccountSettingsStorePortFake : IAccountSettingsStorePort
    {
        /// <summary>外部変更として設定を返す処理です。</summary>
        private Action<AccountSettings>? receive;

        /// <summary>アカウント識別子ごとに保持するテスト設定です。</summary>
        public Dictionary<string, AccountSettings> SavedSettings { get; } = new(StringComparer.Ordinal);

        /// <summary>保存要求を受け取った回数です。</summary>
        public int SaveCount { get; private set; }

        /// <summary>現在監視しているアカウント識別子です。</summary>
        public string? ObservedAccountIdentifier { get; private set; }

        /// <summary>監視終了要求を受け取った回数です。</summary>
        public int StopObservationCount { get; private set; }

        /// <summary>指定アカウントのテスト設定を返します。</summary>
        /// <remarks>責務: 1件のアカウント識別子に対応するFake設定を復元します。</remarks>
        /// <param name="accountIdentifier">読込対象のアカウント識別子。</param>
        /// <returns>Fakeに保存されている設定。</returns>
        public AccountSettings? Load(string accountIdentifier) => SavedSettings.GetValueOrDefault(accountIdentifier);

        /// <summary>指定アカウントへテスト設定を保存します。</summary>
        /// <remarks>責務: 1件の保存要求をアカウント別Fake領域へ記録します。</remarks>
        /// <param name="settings">保存要求された設定。</param>
        /// <param name="accountIdentifier">保存対象のアカウント識別子。</param>
        public void Save(AccountSettings settings, string accountIdentifier)
        {
            SaveCount += 1;
            SavedSettings[accountIdentifier] = settings;
        }

        /// <summary>指定アカウントの外部変更受信処理を保持します。</summary>
        /// <remarks>責務: 1件のアカウント監視要求をテストから呼出可能な状態へ記録します。</remarks>
        /// <param name="accountIdentifier">監視対象のアカウント識別子。</param>
        /// <param name="receive">外部設定を返す処理。</param>
        public void StartObserving(string accountIdentifier, Action<AccountSettings> receive)
        {
            ObservedAccountIdentifier = accountIdentifier;
            this.receive = receive;
        }

        /// <summary>現在のテスト監視を終了します。</summary>
        /// <remarks>責務: 監視終了を記録して外部変更受信処理を破棄します。</remarks>
        public void StopObserving()
        {
            StopObservationCount += 1;
            ObservedAccountIdentifier = null;
            receive = null;
        }

        /// <summary>指定設定を別端末からの変更として通知します。</summary>
        /// <remarks>責務: 1件の同期設定を現在のテスト受信処理へ渡します。</remarks>
        /// <param name="settings">外部変更として通知する設定。</param>
        public void SendExternal(AccountSettings settings)
        {
            receive?.Invoke(settings);
        }
    }
}
