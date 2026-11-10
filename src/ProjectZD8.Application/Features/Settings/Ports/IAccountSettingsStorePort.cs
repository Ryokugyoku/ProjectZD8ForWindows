using ProjectZD8.Application.Settings.State;

namespace ProjectZD8.Application.Settings.Ports;

/// <summary>Connectionを除くアカウント設定の保存と端末間変更通知を抽象化します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public interface IAccountSettingsStorePort
{
    /// <summary>指定アカウントの保存済み設定を読み込みます。</summary>
    /// <remarks>責務: 1件のアカウント識別子に対応する最新設定を復元します。</remarks>
    /// <param name="accountIdentifier">Appleがアプリへ割り当てた空でないユーザー識別子。</param>
    /// <returns>復元できた設定。未保存または不完全な場合は <c>null</c>。</returns>
    AccountSettings? Load(string accountIdentifier);

    /// <summary>指定アカウントの設定を次回起動および端末間同期用に保存します。</summary>
    /// <remarks>責務: 1件のアカウント設定をローカル保持と同期保存へ渡します。</remarks>
    /// <param name="settings">表示設定とセッション自動送信設定を含む保存対象。</param>
    /// <param name="accountIdentifier">Appleがアプリへ割り当てた空でないユーザー識別子。</param>
    void Save(AccountSettings settings, string accountIdentifier);

    /// <summary>指定アカウントへ外部から届く設定変更の監視を開始します。</summary>
    /// <remarks>責務: 1件のアカウントスコープで端末間設定変更だけを購読します。</remarks>
    /// <param name="accountIdentifier">Appleがアプリへ割り当てた空でないユーザー識別子。</param>
    /// <param name="receive">同期先から有効な設定が届いたときに呼び出す処理。</param>
    void StartObserving(string accountIdentifier, Action<AccountSettings> receive);

    /// <summary>現在のアカウント設定監視を終了します。</summary>
    /// <remarks>責務: 以前のアカウントスコープから届く変更通知を停止します。</remarks>
    void StopObserving();
}
