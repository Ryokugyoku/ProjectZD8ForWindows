using System;
using System.Collections.Generic;
using ProjectZD8.Application.Settings.Ports;
using ProjectZD8.Application.Settings.State;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】アカウント設定（言語・外観・自動アップロード）をメモリだけに持つ保存先です。
/// </summary>
/// <remarks>
/// iOS 版の <c>UbiquitousKeyValueStoreAccountSettingsStore</c> の代わり。ネットワーク担当の設定ストア
/// （端末内保存＋CloudKit）ができたら差し替えます。ほかの端末からの変更通知はありません。
/// </remarks>
public sealed class InterimAccountSettingsStore : IAccountSettingsStorePort
{
    /// <summary>アカウントごとの設定です。</summary>
    private readonly Dictionary<string, AccountSettings> _settings = new(StringComparer.Ordinal);

    /// <summary>アカウントの設定を返します。</summary>
    /// <remarks>責務: メモリ上の設定を返します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>設定。なければ null。</returns>
    public AccountSettings? Load(string accountIdentifier) =>
        _settings.TryGetValue(accountIdentifier, out var settings) ? settings : null;

    /// <summary>アカウントの設定を保存します。</summary>
    /// <remarks>責務: 設定をメモリへ置きます。</remarks>
    /// <param name="settings">設定。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    public void Save(AccountSettings settings, string accountIdentifier) => _settings[accountIdentifier] = settings;

    /// <summary>ほかの端末からの変更の監視を始めます（暫定版では通知しません）。</summary>
    /// <remarks>責務: 同期先がないため何もしません。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <param name="receive">変更を受け取る処理。</param>
    public void StartObserving(string accountIdentifier, Action<AccountSettings> receive)
    {
    }

    /// <summary>監視をやめます。</summary>
    /// <remarks>責務: 同期先がないため何もしません。</remarks>
    public void StopObserving()
    {
    }

    /// <summary>アカウントの設定を消します。</summary>
    /// <remarks>責務: アカウント削除時に設定を消します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    public void Remove(string accountIdentifier) => _settings.Remove(accountIdentifier);
}
