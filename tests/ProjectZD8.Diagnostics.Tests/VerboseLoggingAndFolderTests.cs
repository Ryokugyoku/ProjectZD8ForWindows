using ProjectZD8.Diagnostics.Tests.Support;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>「詳細ログ」の 7 日での自動切り、診断ログのサイズと削除を確かめます。</summary>
public sealed class VerboseLoggingAndFolderTests : IDisposable
{
    /// <summary>テストで使う「今」です。</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>一時フォルダー（データのフォルダー）です。</summary>
    private readonly TempDirectory _data = new();

    /// <summary>手で進める時計です。</summary>
    private readonly ManualTimeProvider _clock = new(Start);

    /// <summary>診断ログのフォルダーです。</summary>
    private string DiagnosticsDirectory => Path.Combine(_data.Path, "diagnostics");

    /// <summary>一時フォルダーを消します。</summary>
    public void Dispose() => _data.Dispose();

    /// <summary>入れていなければ Off。</summary>
    [Fact]
    public void Lease_NotEnabled_IsOff()
    {
        var lease = new VerboseLoggingLease(_data.Path, _clock);
        Assert.Equal(VerboseLoggingStartup.Off, lease.ApplyAtStartup());
        Assert.Null(lease.EnabledAt);
        Assert.Null(lease.ExpiresAt);
    }

    /// <summary>入れた日時を保存し、再起動しても 7 日未満なら On のまま。</summary>
    [Fact]
    public void Lease_Enabled_StaysOnUntilSevenDays()
    {
        Assert.True(new VerboseLoggingLease(_data.Path, _clock).Enable());

        _clock.Now = Start.AddDays(6).AddHours(23);
        var restarted = new VerboseLoggingLease(_data.Path, _clock);
        Assert.Equal(VerboseLoggingStartup.On, restarted.ApplyAtStartup());
        Assert.Equal(Start, restarted.EnabledAt);
        Assert.Equal(Start.AddDays(7), restarted.ExpiresAt);
    }

    /// <summary>7 日たった次の起動で自動で切れ、保存も消える。</summary>
    [Fact]
    public void Lease_AfterSevenDays_ExpiresAtStartup()
    {
        new VerboseLoggingLease(_data.Path, _clock).Enable();

        _clock.Now = Start.AddDays(7);
        var restarted = new VerboseLoggingLease(_data.Path, _clock);
        Assert.Equal(VerboseLoggingStartup.Expired, restarted.ApplyAtStartup());
        Assert.Null(restarted.EnabledAt);
        Assert.Equal(VerboseLoggingStartup.Off, restarted.ApplyAtStartup());
        Assert.False(File.Exists(Path.Combine(DiagnosticsDirectory, VerboseLoggingLease.FileName)));
    }

    /// <summary>自分で切ると記録が消え、次の起動も Off。</summary>
    [Fact]
    public void Lease_Disable_ClearsRecord()
    {
        var lease = new VerboseLoggingLease(_data.Path, _clock);
        lease.Enable();
        lease.Disable();
        Assert.Equal(VerboseLoggingStartup.Off, new VerboseLoggingLease(_data.Path, _clock).ApplyAtStartup());
    }

    /// <summary>壊れた保存や、遠い未来の日時は入っていないものとして扱う。</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"enabledAt\":\"yesterday-ish\"}")]
    [InlineData("{\"enabledAt\":\"2030-01-01T00:00:00.0000000Z\"}")]
    public void Lease_InvalidRecord_IsNotActive(string content)
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        File.WriteAllText(Path.Combine(DiagnosticsDirectory, VerboseLoggingLease.FileName), content);

        var lease = new VerboseLoggingLease(_data.Path, _clock);
        Assert.NotEqual(VerboseLoggingStartup.On, lease.ApplyAtStartup());
        Assert.False(File.Exists(Path.Combine(DiagnosticsDirectory, VerboseLoggingLease.FileName)));
    }

    /// <summary>合計サイズは診断ログのファイルだけを数える。</summary>
    [Fact]
    public void TotalBytes_CountsDiagnosticLogsOnly()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        File.WriteAllBytes(Path.Combine(DiagnosticsDirectory, "app-20261111-aaaaaa.jsonl"), new byte[100]);
        File.WriteAllBytes(Path.Combine(DiagnosticsDirectory, "app-20261111-aaaaaa.1.jsonl"), new byte[50]);
        File.WriteAllBytes(Path.Combine(DiagnosticsDirectory, "crash-20261111-bbbbbb.jsonl"), new byte[7]);
        File.WriteAllBytes(Path.Combine(DiagnosticsDirectory, "other.jsonl"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(DiagnosticsDirectory, VerboseLoggingLease.FileName), new byte[1000]);

        Assert.Equal(157, DiagnosticLogFolder.TotalBytes(DiagnosticsDirectory));
        Assert.Equal(0, DiagnosticLogFolder.TotalBytes(Path.Combine(_data.Path, "missing")));
    }

    /// <summary>診断ログを消しても、今の実行のファイルと、診断ログではないファイルは残る。</summary>
    [Fact]
    public void Clear_KeepsCurrentRunAndOtherFiles()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        string[] old = ["app-20261101-bbbbbb.jsonl", "app-20261101-bbbbbb.1.jsonl", "crash-20261101-bbbbbb.jsonl", "app-20261105-cccccc.jsonl"];
        string[] current = ["app-20261111-aaaaaa.jsonl", "app-20261111-aaaaaa.1.jsonl", "crash-20261111-aaaaaa.jsonl"];
        string[] others = [VerboseLoggingLease.FileName, "notes.txt", "other.jsonl"];
        foreach (var name in old.Concat(current).Concat(others))
        {
            File.WriteAllText(Path.Combine(DiagnosticsDirectory, name), "x");
        }

        var deleted = DiagnosticLogFolder.Clear(DiagnosticsDirectory, "aaaaaa");

        Assert.Equal(old.Length, deleted);
        Assert.All(old, name => Assert.False(File.Exists(Path.Combine(DiagnosticsDirectory, name)), name));
        Assert.All(current.Concat(others), name => Assert.True(File.Exists(Path.Combine(DiagnosticsDirectory, name)), name));
    }

    /// <summary>書込み中で消せないファイルがあっても、ほかは消して例外にしない。</summary>
    [Fact]
    public void Clear_SkipsFilesThatCannotBeDeleted()
    {
        Directory.CreateDirectory(DiagnosticsDirectory);
        var locked = Path.Combine(DiagnosticsDirectory, "app-20261101-bbbbbb.jsonl");
        var free = Path.Combine(DiagnosticsDirectory, "app-20261102-cccccc.jsonl");
        File.WriteAllText(free, "x");
        using var handle = new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.Read);

        var deleted = DiagnosticLogFolder.Clear(DiagnosticsDirectory, "aaaaaa");

        Assert.Equal(1, deleted);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
    }
}
