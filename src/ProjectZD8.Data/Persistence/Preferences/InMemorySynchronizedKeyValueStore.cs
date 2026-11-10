namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 同期しない（メモリだけの）同期値の保存先です。CloudKit の設定がないローカルモードで使います。
/// </summary>
/// <remarks>iCloud にサインインしていない iPhone の iCloud Key-Value Store と同じく、値はこの実行の間だけ残ります（端末内のコピーは別に保存されます）。</remarks>
public sealed class InMemorySynchronizedKeyValueStore : ISynchronizedKeyValueStore
{
    /// <summary>キーごとの値です。</summary>
    private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event Action<IReadOnlyList<string>>? ChangedExternally
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public byte[]? Data(string key) => _values.TryGetValue(key, out var value) ? value.ToArray() : null;

    /// <inheritdoc />
    public void SetData(string key, byte[] value) => _values[key] = value.ToArray();

    /// <inheritdoc />
    public void RemoveObject(string key) => _values.Remove(key);

    /// <inheritdoc />
    public bool Synchronize() => true;
}
