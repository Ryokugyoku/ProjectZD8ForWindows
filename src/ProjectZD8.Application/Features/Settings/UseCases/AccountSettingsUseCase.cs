using ProjectZD8.Application.Settings.Ports;
using ProjectZD8.Application.Settings.State;

namespace ProjectZD8.Application.Settings.UseCases;

/// <summary>Connectionを除くアカウント設定の保存、復元、同期監視を調整します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class AccountSettingsUseCase
{
    /// <summary>アカウント設定を保持する保存境界です。</summary>
    private readonly IAccountSettingsStorePort store;

    /// <summary>保存境界を注入してユースケースを生成します。</summary>
    /// <remarks>責務: アカウント設定のライフサイクルを1件の保存境界へ結び付けます。</remarks>
    /// <param name="store">ローカル保持と端末間同期を提供する保存境界。</param>
    public AccountSettingsUseCase(IAccountSettingsStorePort store)
    {
        this.store = store;
    }

    /// <summary>指定アカウントの保存済み設定または既定値を返します。</summary>
    /// <remarks>責務: 1件のアカウント設定を保存境界から復元可能な表示初期値へ変換します。</remarks>
    /// <param name="accountIdentifier">Appleがアプリへ割り当てた空でないユーザー識別子。</param>
    /// <returns>保存済み設定。未保存の場合は既定設定。</returns>
    public AccountSettings Load(string accountIdentifier) =>
        store.Load(accountIdentifier) ?? new AccountSettings();

    /// <summary>指定アカウントの設定を保存します。</summary>
    /// <remarks>責務: Connectionを含まない1件の設定をアカウント保存境界へ渡します。</remarks>
    /// <param name="settings">保存する表示設定とセッション自動送信設定。</param>
    /// <param name="accountIdentifier">Appleがアプリへ割り当てた空でないユーザー識別子。</param>
    public void Save(AccountSettings settings, string accountIdentifier)
    {
        store.Save(settings, accountIdentifier);
    }

    /// <summary>指定アカウントの端末間設定変更を購読します。</summary>
    /// <remarks>責務: 1件のアカウントスコープの同期変更を呼出元へ中継します。</remarks>
    /// <param name="accountIdentifier">Appleがアプリへ割り当てた空でないユーザー識別子。</param>
    /// <param name="receive">有効な同期設定を受け取る処理。</param>
    public void StartObserving(string accountIdentifier, Action<AccountSettings> receive)
    {
        store.StartObserving(accountIdentifier, receive);
    }

    /// <summary>現在の端末間設定変更購読を終了します。</summary>
    /// <remarks>責務: 保存境界が保持する以前のアカウント監視を停止します。</remarks>
    public void StopObserving()
    {
        store.StopObserving();
    }
}
