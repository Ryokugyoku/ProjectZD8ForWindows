using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 端末内の小さな値を 1 つの JSON ファイルに保存します（iOS の <c>UserDefaults</c> の代わり）。
/// </summary>
/// <remarks>
/// 形式は <c>{"data": {キー: base64}, "strings": {キー: 文字列}}</c>。書き込みは一時ファイルに書いてから置き換えます。
/// ファイルが壊れていたら空として扱います（落ちません）。複数のスレッドから呼んでも安全です。
/// </remarks>
public sealed class JsonFileLocalKeyValueStore : ILocalKeyValueStore
{
    /// <summary>保存先のファイルのパスです。</summary>
    private readonly string _filePath;

    /// <summary>読み書きを 1 つずつにする鍵です。</summary>
    private readonly Lock _lock = new();

    /// <summary>メモリ上のバイト列の値です。</summary>
    private Dictionary<string, byte[]>? _data;

    /// <summary>メモリ上の文字列の値です。</summary>
    private Dictionary<string, string>? _strings;

    /// <summary>
    /// 保存先のファイルのパスを指定して作ります。
    /// </summary>
    /// <remarks>責務: 1 件の JSON ファイルを保存先に固定します。フォルダーは組み立てが渡します。</remarks>
    /// <param name="filePath">JSON ファイルのパス（例：LocalFolder の <c>preferences.json</c>）。</param>
    public JsonFileLocalKeyValueStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <inheritdoc />
    public byte[]? GetData(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _data!.TryGetValue(key, out var value) ? value.ToArray() : null;
        }
    }

    /// <inheritdoc />
    public void SetData(string key, byte[] value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _strings!.Remove(key);
            _data![key] = value.ToArray();
            Persist();
        }
    }

    /// <inheritdoc />
    public string? GetString(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _strings!.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <inheritdoc />
    public void SetString(string key, string value)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _data!.Remove(key);
            _strings![key] = value;
            Persist();
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _data!.Remove(key) | _strings!.Remove(key);
            if (removed)
            {
                Persist();
            }
        }
    }

    /// <summary>
    /// まだ読んでいなければファイルを読みます。
    /// </summary>
    /// <remarks>責務: 1 回だけファイルの内容をメモリへ読み込みます。</remarks>
    private void EnsureLoaded()
    {
        if (_data is not null)
        {
            return;
        }
        _data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }
            using var document = JsonDocument.Parse(File.ReadAllBytes(_filePath));
            var root = document.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in data.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetBytesFromBase64(out var bytes))
                    {
                        _data[property.Name] = bytes;
                    }
                }
            }
            if (root.TryGetProperty("strings", out var strings) && strings.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in strings.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        _strings[property.Name] = property.Value.GetString()!;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _data.Clear();
            _strings.Clear();
        }
    }

    /// <summary>
    /// メモリ上の値をファイルへ書きます。
    /// </summary>
    /// <remarks>責務: 一時ファイルに書いてから置き換え、途中で止まっても前の内容を残します。</remarks>
    private void Persist()
    {
        var data = new JsonObject();
        foreach (var (key, value) in _data!.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            data[key] = Convert.ToBase64String(value);
        }
        var strings = new JsonObject();
        foreach (var (key, value) in _strings!.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            strings[key] = value;
        }
        var root = new JsonObject { ["data"] = data, ["strings"] = strings };
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _filePath, overwrite: true);
    }
}
