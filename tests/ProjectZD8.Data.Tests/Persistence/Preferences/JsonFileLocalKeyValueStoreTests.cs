using ProjectZD8.Data.Persistence.Preferences;

namespace ProjectZD8.Data.Tests.Persistence.Preferences;

/// <summary>
/// 端末内の小さな値の保存と、インストール識別子の保存を確かめます。
/// </summary>
public sealed class JsonFileLocalKeyValueStoreTests : IDisposable
{
    /// <summary>テスト専用の一時フォルダーです。</summary>
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
    /// 書いた値を、新しいインスタンスで読み戻せ、消せることを確かめます。
    /// </summary>
    /// <remarks>責務: ファイルへの保存と再読み込みを確かめます。</remarks>
    [Fact]
    public void ValuesSurviveReloadAndCanBeRemoved()
    {
        var path = Path.Combine(_directory, "nested", "preferences.json");
        var store = new JsonFileLocalKeyValueStore(path);
        store.SetData("data", [1, 2, 3]);
        store.SetString("text", "日本語");

        var reloaded = new JsonFileLocalKeyValueStore(path);
        Assert.Equal([1, 2, 3], reloaded.GetData("data"));
        Assert.Equal("日本語", reloaded.GetString("text"));

        reloaded.Remove("data");
        Assert.Null(new JsonFileLocalKeyValueStore(path).GetData("data"));
    }

    /// <summary>
    /// 壊れたファイルは空として扱い、落ちないことを確かめます。
    /// </summary>
    /// <remarks>責務: 壊れた保存データへの耐性を確かめます。</remarks>
    [Fact]
    public void CorruptFileIsTreatedAsEmpty()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "preferences.json");
        File.WriteAllText(path, "{not json");

        var store = new JsonFileLocalKeyValueStore(path);

        Assert.Null(store.GetString("x"));
        store.SetString("x", "y");
        Assert.Equal("y", new JsonFileLocalKeyValueStore(path).GetString("x"));
    }

    /// <summary>
    /// インストール識別子が、最初に小文字の UUID で作られ、以後は同じものを返すことを確かめます。
    /// </summary>
    /// <remarks>責務: UserDefaultsInstallationIdentityStore と同じ振る舞いを確かめます。</remarks>
    [Fact]
    public void InstallationIdentityIsStable()
    {
        var path = Path.Combine(_directory, "preferences.json");
        var id = Guid.Parse("ABCDEF00-0000-0000-0000-000000000001");
        var first = new LocalKeyValueInstallationIdentityStore(new JsonFileLocalKeyValueStore(path), () => id).Identity("PC 1");
        var second = new LocalKeyValueInstallationIdentityStore(new JsonFileLocalKeyValueStore(path), Guid.NewGuid).Identity("PC 2");

        Assert.Equal("abcdef00-0000-0000-0000-000000000001", first.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("PC 2", second.DisplayName);
    }
}
