using ProjectZD8.Data.Authentication;
using ProjectZD8.Data.Persistence.Preferences;

namespace ProjectZD8.Data.Tests.TestSupport;

/// <summary>
/// 端末内の小さな値をメモリ上に保存します（UserDefaults の suite の代わり）。
/// </summary>
internal sealed class InMemoryLocalKeyValueStore : ILocalKeyValueStore
{
    /// <summary>バイト列の値です。</summary>
    public Dictionary<string, byte[]> Data { get; } = new(StringComparer.Ordinal);

    /// <summary>文字列の値です。</summary>
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public byte[]? GetData(string key) => Data.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void SetData(string key, byte[] value) => Data[key] = value;

    /// <inheritdoc />
    public string? GetString(string key) => Strings.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void SetString(string key, string value) => Strings[key] = value;

    /// <inheritdoc />
    public void Remove(string key)
    {
        Data.Remove(key);
        Strings.Remove(key);
    }
}

/// <summary>
/// 同期値をメモリ上に保存し、テストから外部変更を発生させられる偽物です（iOS テストの UbiquitousKeyValueStorePortFake）。
/// </summary>
internal sealed class FakeSynchronizedKeyValueStore : ISynchronizedKeyValueStore
{
    /// <summary>キーごとの同期値です。</summary>
    public Dictionary<string, byte[]> SavedData { get; } = new(StringComparer.Ordinal);

    /// <summary>同期要求の回数です。</summary>
    public int SynchronizationCount { get; private set; }

    /// <inheritdoc />
    public event Action<IReadOnlyList<string>>? ChangedExternally;

    /// <inheritdoc />
    public byte[]? Data(string key) => SavedData.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc />
    public void SetData(string key, byte[] value) => SavedData[key] = value;

    /// <inheritdoc />
    public void RemoveObject(string key) => SavedData.Remove(key);

    /// <inheritdoc />
    public bool Synchronize()
    {
        SynchronizationCount++;
        return true;
    }

    /// <summary>
    /// 外部変更を発生させます。
    /// </summary>
    /// <remarks>責務: iOS の didChangeExternallyNotification の投稿を再現します。</remarks>
    /// <param name="keys">変わったキー。</param>
    public void PostExternalChange(params string[] keys) => ChangedExternally?.Invoke(keys);
}

/// <summary>
/// 秘密をメモリ上に保存する偽物です（Keychain・PasswordVault の代わり）。
/// </summary>
internal sealed class InMemorySecretStore : ISecretStore
{
    /// <summary>名前ごとの秘密です。</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>次の操作で失敗させるかどうかです。</summary>
    public bool Fails { get; set; }

    /// <inheritdoc />
    public string? Read(string name) =>
        Fails ? throw new SecretStoreException("fail") : Values.TryGetValue(name, out var value) ? value : null;

    /// <inheritdoc />
    public void Write(string name, string value)
    {
        if (Fails)
        {
            throw new SecretStoreException("fail");
        }
        Values[name] = value;
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        if (Fails)
        {
            throw new SecretStoreException("fail");
        }
        Values.Remove(name);
    }
}
