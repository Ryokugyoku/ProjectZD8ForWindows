namespace ProjectZD8.Data.Tests.TestSupport;

/// <summary>テストごとに作り、破棄で消す一時ディレクトリーです。</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    /// <summary>一意な一時ディレクトリーを作ります。</summary>
    /// <remarks>責務: システムの一時フォルダーの下に空のディレクトリーを用意します。</remarks>
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProjectZD8.Data.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>ディレクトリーのパスです。</summary>
    public string Path { get; }

    /// <summary>ディレクトリーを消します。</summary>
    /// <remarks>責務: 中身ごと削除します。失敗しても無視します。</remarks>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
