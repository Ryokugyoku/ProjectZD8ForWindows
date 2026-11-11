using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics.Tests.Support;

/// <summary>テストごとの一時フォルダーです（Dispose で消す）。</summary>
internal sealed class TempDirectory : IDisposable
{
    /// <summary>作ります。</summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProjectZD8.Diagnostics.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>フォルダーのパスです。</summary>
    public string Path { get; }

    /// <summary>消します。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>手で進める時計です。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>今の時刻です。</summary>
    public DateTimeOffset Now { get; set; } = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>受け取ったイベントを覚えておく出力先です。</summary>
internal sealed class RecordingSink(DiagnosticLevelPolicy policy) : IDiagnosticSink
{
    /// <summary>受け取ったイベントです。</summary>
    public List<DiagnosticEvent> Events { get; } = [];

    /// <summary>クラッシュとして受け取ったイベントです。</summary>
    public List<DiagnosticEvent> Crashes { get; } = [];

    /// <inheritdoc />
    public bool IsEnabled(string category, LogLevel level) => policy.IsEnabled(category, level);

    /// <inheritdoc />
    public void Emit(DiagnosticEvent diagnosticEvent)
    {
        lock (Events)
        {
            Events.Add(diagnosticEvent);
        }
    }

    /// <inheritdoc />
    public bool Flush(TimeSpan timeout) => true;

    /// <inheritdoc />
    public void WriteCrash(DiagnosticEvent crash) => Crashes.Add(crash);
}

/// <summary>JSONL のファイルを読むための道具です。</summary>
internal static class Jsonl
{
    /// <summary>書込み中でも読めるように開いて、行ごとに返します。</summary>
    /// <param name="path">ファイル。</param>
    /// <returns>行。</returns>
    public static List<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>行を JSON として読みます。</summary>
    /// <param name="path">ファイル。</param>
    /// <returns>各行の JSON。</returns>
    public static List<JsonElement> Read(string path) =>
        ReadLines(path).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

    /// <summary>フォルダーの <c>app-*.jsonl</c> を全部読みます（ファイル名の順）。</summary>
    /// <param name="directory">フォルダー。</param>
    /// <returns>各行の JSON。</returns>
    public static List<JsonElement> ReadApp(string directory) =>
        Directory.GetFiles(directory, "app-*.jsonl")
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.Ordinal)
            .SelectMany(Read)
            .ToList();

    /// <summary>イベント名です。</summary>
    /// <param name="line">行。</param>
    /// <returns><c>evt</c>。</returns>
    public static string Evt(this JsonElement line) => line.GetProperty("evt").GetString()!;

    /// <summary>通し番号です。</summary>
    /// <param name="line">行。</param>
    /// <returns><c>seq</c>。</returns>
    public static long Seq(this JsonElement line) => line.GetProperty("seq").GetInt64();

    /// <summary>リングバッファから吐き出した行か。</summary>
    /// <param name="line">行。</param>
    /// <returns>そうなら true。</returns>
    public static bool IsRing(this JsonElement line) => line.TryGetProperty("ring", out var ring) && ring.GetBoolean();
}

/// <summary>テストで書くイベントを作ります。</summary>
internal static class Events
{
    /// <summary>イベントを作ります。</summary>
    /// <param name="level">レベル。</param>
    /// <param name="category">分類。</param>
    /// <param name="name">イベント名。</param>
    /// <returns>イベント。</returns>
    public static DiagnosticEvent Make(LogLevel level, string category = "acq", string? name = null) =>
        new(level, category, name ?? $"{category}.test.{level.ToString().ToLowerInvariant()}", "テスト");
}

/// <summary>出力先を差し替えるテストをまとめて、並行に走らせないための集まりです。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalDiagnosticLogCollection
{
    /// <summary>集まりの名前です。</summary>
    public const string Name = "DiagnosticLog の出力先を差し替えるテスト";
}
