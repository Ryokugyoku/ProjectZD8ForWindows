using ProjectZD8.Application.Settings.Ports;
using ProjectZD8.Application.Settings.State;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Data.Network.CloudKit;

namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 端末間の同期と端末内の保持を組み合わせてアカウント設定を保存します（iOS の <c>UbiquitousKeyValueStoreAccountSettingsStore</c>）。
/// </summary>
/// <remarks>
/// <para>キーは iOS と同じ <c>settings.account.v1.&lt;SHA-256&gt;</c>。同期先は Windows では CloudKit の AccountSettings レコード
/// （<c>CloudKitSynchronizedKeyValueStore</c> が対応づけます）、ローカルモードでは <see cref="InMemorySynchronizedKeyValueStore"/>。</para>
/// <para>iOS との違い：監視を始めたときにも同期を要求します（CloudKit にはプッシュ通知の代わりがないため、PROGRESS.md の設計案どおり
/// 「監視開始時とアプリが前面に来たときに読む」）。JSON はキーを並べ替えて書きます（読むときは順番を問いません）。</para>
/// <para>UI スレッドから呼ぶ前提です。</para>
/// </remarks>
public sealed class SynchronizedAccountSettingsStore : IAccountSettingsStorePort
{
    /// <summary>同期できなくても次回の起動へ値を残す端末内の保存先です。</summary>
    private readonly ILocalKeyValueStore _defaults;

    /// <summary>端末間で設定を同期する保存先です。</summary>
    private readonly ISynchronizedKeyValueStore _ubiquitousStore;

    /// <summary>現在監視中のアカウント設定のキーです。</summary>
    private string? _observedKey;

    /// <summary>有効な外部変更を Application へ返す処理です。</summary>
    private Action<AccountSettings>? _receiveExternalSettings;

    /// <summary>
    /// 端末内の保存先と同期先を注入して作ります。
    /// </summary>
    /// <remarks>責務: アカウント設定の端末内保持と同期に使う 2 つの境界を固定します。</remarks>
    /// <param name="defaults">オフラインでの再起動用の端末内保存先。</param>
    /// <param name="ubiquitousStore">端末間同期の保存先。</param>
    public SynchronizedAccountSettingsStore(ILocalKeyValueStore defaults, ISynchronizedKeyValueStore ubiquitousStore)
    {
        _defaults = defaults;
        _ubiquitousStore = ubiquitousStore;
        _ubiquitousStore.ChangedExternally += ReceiveExternalChange;
    }

    /// <summary>
    /// 指定アカウントの同期値または端末内の値を読みます。
    /// </summary>
    /// <remarks>責務: 1 件のアカウントキーについて同期値を優先し、端末内の値へフォールバックします。</remarks>
    /// <param name="accountIdentifier">Apple がアプリへ割り当てた空でないユーザー識別子。</param>
    /// <returns>復元できた設定。未保存または不完全な場合は <c>null</c>。</returns>
    public AccountSettings? Load(string accountIdentifier)
    {
        var key = SettingsKey(accountIdentifier);
        _ubiquitousStore.Synchronize();

        if (AccountSettingsJsonMapper.TryDecode(_ubiquitousStore.Data(key)) is { } synchronized)
        {
            PersistLocally(synchronized, key);
            return synchronized;
        }
        return AccountSettingsJsonMapper.TryDecode(_defaults.GetData(key));
    }

    /// <summary>
    /// 指定アカウントの設定を端末内と同期先へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント設定を同じ符号化値で端末内保持と同期へ反映します。</remarks>
    /// <param name="settings">接続を含まない保存対象の設定。</param>
    /// <param name="accountIdentifier">Apple がアプリへ割り当てた空でないユーザー識別子。</param>
    public void Save(AccountSettings settings, string accountIdentifier)
    {
        var data = AccountSettingsJsonMapper.Encode(settings);
        var key = SettingsKey(accountIdentifier);
        _defaults.SetData(key, data);
        _ubiquitousStore.SetData(key, data);
        _ubiquitousStore.Synchronize();
    }

    /// <summary>
    /// 指定アカウントの端末内の設定と同期先の設定を消します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント設定キーを両方の保存先から除いて同期を要求します。</remarks>
    /// <param name="accountIdentifier">Apple がアプリへ割り当てた空でないユーザー識別子。</param>
    public void Remove(string accountIdentifier)
    {
        var key = SettingsKey(accountIdentifier);
        if (string.Equals(_observedKey, key, StringComparison.Ordinal))
        {
            StopObserving();
        }
        _defaults.Remove(key);
        _ubiquitousStore.RemoveObject(key);
        _ubiquitousStore.Synchronize();
    }

    /// <summary>
    /// 指定アカウントの外部変更を購読し、同期を要求します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント設定キーだけを外部変更から選んで購読します。</remarks>
    /// <param name="accountIdentifier">Apple がアプリへ割り当てた空でないユーザー識別子。</param>
    /// <param name="receive">有効な同期設定を受け取る処理。</param>
    public void StartObserving(string accountIdentifier, Action<AccountSettings> receive)
    {
        StopObserving();
        _observedKey = SettingsKey(accountIdentifier);
        _receiveExternalSettings = receive;
        _ubiquitousStore.Synchronize();
    }

    /// <summary>
    /// 外部変更の購読を終えます。
    /// </summary>
    /// <remarks>責務: 登録済みのアカウント固有のコールバックを破棄します。</remarks>
    public void StopObserving()
    {
        _observedKey = null;
        _receiveExternalSettings = null;
    }

    /// <summary>
    /// 外部変更から現在のアカウントの設定だけを反映します。
    /// </summary>
    /// <remarks>責務: 変わったキーを現在のアカウントと照合し、有効な設定 1 件を通知します。</remarks>
    /// <param name="changedKeys">変わったキー。</param>
    private void ReceiveExternalChange(IReadOnlyList<string> changedKeys)
    {
        if (_observedKey is not { } observedKey
            || !changedKeys.Contains(observedKey, StringComparer.Ordinal)
            || AccountSettingsJsonMapper.TryDecode(_ubiquitousStore.Data(observedKey)) is not { } settings)
        {
            return;
        }
        PersistLocally(settings, observedKey);
        _receiveExternalSettings?.Invoke(settings);
    }

    /// <summary>
    /// Apple のユーザー識別子を直接含まないアカウント固有のキーへ変換します。
    /// </summary>
    /// <remarks>責務: 1 件のユーザー識別子を SHA-256 の名前空間付き設定キーへ変換します（iOS と同じキー）。</remarks>
    /// <param name="accountIdentifier">Apple のユーザー識別子。</param>
    /// <returns>スキーマ世代と不可逆フィンガープリントを含む保存キー。</returns>
    public static string SettingsKey(string accountIdentifier) =>
        $"settings.account.v1.{CloudKitAccountFingerprint.Of(accountIdentifier)}";

    /// <summary>
    /// 同期から得た有効な設定を端末内へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件の有効な設定をオフライン再起動用のデータとして保存します。</remarks>
    /// <param name="settings">同期から復元した設定。</param>
    /// <param name="key">アカウント固有の保存キー。</param>
    private void PersistLocally(AccountSettings settings, string key) =>
        _defaults.SetData(key, AccountSettingsJsonMapper.Encode(settings));
}
