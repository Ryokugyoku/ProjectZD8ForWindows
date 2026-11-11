using System.Reflection;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics.Tests.Support;

[assembly: AssemblyMetadata("DiagnosticsChannel", "beta")]

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>チャンネル・レベル・分類の絞り込みを確かめます。</summary>
public sealed class DiagnosticLevelTests
{
    /// <summary>Dev は Trace 以上を全部書き、リングバッファを持たない。</summary>
    [Fact]
    public void Dev_WritesEverything()
    {
        var policy = DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Dev);

        Assert.True(policy.ShouldWrite(Events.Make(LogLevel.Trace, "acq")));
        Assert.False(policy.HasBuffer);
    }

    /// <summary>Beta は Debug 以上を書き、obd.wire だけ Trace も書く。</summary>
    [Fact]
    public void Beta_WritesDebugAndWireTrace()
    {
        var policy = DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Beta);

        Assert.True(policy.ShouldWrite(Events.Make(LogLevel.Debug, "acq")));
        Assert.False(policy.ShouldWrite(Events.Make(LogLevel.Trace, "acq")));
        Assert.False(policy.IsEnabled("acq", LogLevel.Trace));
        Assert.True(policy.ShouldWrite(Events.Make(LogLevel.Trace, "obd.wire")));
        Assert.True(policy.IsEnabled("obd.wire", LogLevel.Trace));
        Assert.False(policy.HasBuffer);
    }

    /// <summary>Stable は Warning 以上だけ書き、Debug 以上（obd.wire は Trace）をリングバッファに持つ。</summary>
    [Fact]
    public void Stable_WritesWarningAndBuffersDebug()
    {
        var policy = DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable);

        Assert.False(policy.ShouldWrite(Events.Make(LogLevel.Information, "acq")));
        Assert.True(policy.ShouldWrite(Events.Make(LogLevel.Warning, "acq")));
        Assert.True(policy.ShouldBuffer(Events.Make(LogLevel.Debug, "acq")));
        Assert.False(policy.ShouldBuffer(Events.Make(LogLevel.Trace, "acq")));
        Assert.True(policy.ShouldBuffer(Events.Make(LogLevel.Trace, "obd.wire")));
        Assert.False(policy.ShouldWrite(Events.Make(LogLevel.Trace, "obd.wire")));
        Assert.True(policy.IsEnabled("acq", LogLevel.Debug));
        Assert.False(policy.IsEnabled("acq", LogLevel.Trace));
        Assert.Equal(2000, policy.BufferCapacity);
    }

    /// <summary>起動・終了などはレベルに関係なく書く。</summary>
    [Fact]
    public void Stable_AlwaysWritesLifecycleEvents()
    {
        var policy = DiagnosticLevelPolicy.ForChannel(DiagnosticsChannel.Stable);

        Assert.True(policy.ShouldWrite(Events.Make(LogLevel.Information, "app", DiagnosticEventNames.AppStart)));
        Assert.True(policy.ShouldWrite(Events.Make(LogLevel.Information, "app", DiagnosticEventNames.AppExit)));
        Assert.False(policy.ShouldWrite(Events.Make(LogLevel.Information, "app", "app.other.done")));
    }

    /// <summary>分類の指定は点で区切った先頭部分にも効き、長い方が優先される。</summary>
    [Fact]
    public void CategoryLevels_MatchByPrefixWithLongestFirst()
    {
        var policy = new DiagnosticLevelPolicy(
            LogLevel.Warning,
            new Dictionary<string, LogLevel> { ["obd"] = LogLevel.Information, ["obd.wire"] = LogLevel.Trace });

        Assert.True(policy.IsEnabled("obd.connect", LogLevel.Information));
        Assert.False(policy.IsEnabled("obd.connect", LogLevel.Debug));
        Assert.True(policy.IsEnabled("obd.wire", LogLevel.Trace));
        Assert.True(policy.IsEnabled("obd.wire.elm", LogLevel.Trace));
        Assert.False(policy.IsEnabled("obdx", LogLevel.Information));
        Assert.False(policy.IsEnabled("acq", LogLevel.Information));
    }

    /// <summary>上書きは 環境変数 → appsettings → 詳細ログ → チャンネル の順に強い。</summary>
    [Fact]
    public void Resolver_AppliesOverridesInOrder()
    {
        var environment = DiagnosticLevelResolver.Resolve(DiagnosticsChannel.Stable, "trace", "error", verboseLogging: true);
        Assert.Equal(DiagnosticLevelSource.Environment, environment.Source);
        Assert.Equal(LogLevel.Trace, environment.Policy.FileLevel);

        var file = DiagnosticLevelResolver.Resolve(DiagnosticsChannel.Stable, null, "error", verboseLogging: true);
        Assert.Equal(DiagnosticLevelSource.SettingsFile, file.Source);
        Assert.Equal(LogLevel.Error, file.Policy.FileLevel);
        Assert.True(file.Policy.HasBuffer);

        var verbose = DiagnosticLevelResolver.Resolve(DiagnosticsChannel.Stable, null, null, verboseLogging: true);
        Assert.Equal(DiagnosticLevelSource.VerboseSwitch, verbose.Source);
        Assert.Equal(LogLevel.Debug, verbose.Policy.FileLevel);
        Assert.True(verbose.Policy.IsEnabled("obd.wire", LogLevel.Trace));
        Assert.False(verbose.Policy.HasBuffer);

        var channel = DiagnosticLevelResolver.Resolve(DiagnosticsChannel.Stable, null, null, verboseLogging: false);
        Assert.Equal(DiagnosticLevelSource.Channel, channel.Source);
        Assert.Equal(LogLevel.Warning, channel.Policy.FileLevel);
    }

    /// <summary>読めない値は無視して次へ進む。</summary>
    [Fact]
    public void Resolver_SkipsUnreadableValues()
    {
        var resolution = DiagnosticLevelResolver.Resolve(DiagnosticsChannel.Beta, "loud", " Warn ", verboseLogging: false);

        Assert.Equal(DiagnosticLevelSource.SettingsFile, resolution.Source);
        Assert.Equal(LogLevel.Warning, resolution.Policy.FileLevel);
    }

    /// <summary>Debug 以下を明示したら obd.wire は Trace、リングバッファは持たない。</summary>
    [Fact]
    public void ForLevel_DebugTurnsOnWireTrace()
    {
        var policy = DiagnosticLevelPolicy.ForLevel(DiagnosticsChannel.Stable, LogLevel.Debug);

        Assert.True(policy.IsEnabled("obd.wire", LogLevel.Trace));
        Assert.False(policy.IsEnabled("acq", LogLevel.Trace));
        Assert.False(policy.HasBuffer);
    }

    /// <summary>appsettings.Local.json の Diagnostics:Level を読む。なければ・壊れていれば null。</summary>
    [Fact]
    public void ReadSettingsFileLevel_ReadsDiagnosticsLevel()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "appsettings.Local.json");

        File.WriteAllText(path, """{ "Apple": { "ServicesId": "x" }, "Diagnostics": { "Level": "debug" } }""");
        Assert.Equal("debug", DiagnosticLevelResolver.ReadSettingsFileLevel(path));

        File.WriteAllText(path, """{ "Apple": { } }""");
        Assert.Null(DiagnosticLevelResolver.ReadSettingsFileLevel(path));

        File.WriteAllText(path, "{ 壊れている");
        Assert.Null(DiagnosticLevelResolver.ReadSettingsFileLevel(path));

        Assert.Null(DiagnosticLevelResolver.ReadSettingsFileLevel(Path.Combine(directory.Path, "none.json")));
        Assert.Null(DiagnosticLevelResolver.ReadSettingsFileLevel(null));
    }

    /// <summary>チャンネルはアセンブリのメタデータから読む（大文字小文字を問わない）。</summary>
    [Fact]
    public void Channel_ReadsAssemblyMetadata()
    {
        Assert.Equal(DiagnosticsChannel.Beta, DiagnosticsChannelRawValue.FromAssembly(typeof(DiagnosticLevelTests).Assembly));
        Assert.Equal(DiagnosticsChannel.Stable, DiagnosticsChannelRawValue.FromAssembly(typeof(object).Assembly));
        Assert.Equal(DiagnosticsChannel.Dev, DiagnosticsChannelRawValue.Parse("Dev"));
        Assert.Equal(DiagnosticsChannel.Stable, DiagnosticsChannelRawValue.Parse("unknown"));
        Assert.Equal("beta", DiagnosticsChannel.Beta.ToRawValue());
    }

    /// <summary>保存の上限はチャンネルごとに文書の表のとおり。</summary>
    [Fact]
    public void Retention_MatchesDocumentedLimits()
    {
        Assert.Equal(new DiagnosticRetention(500L << 20, TimeSpan.FromDays(14)), DiagnosticRetention.ForChannel(DiagnosticsChannel.Dev));
        Assert.Equal(new DiagnosticRetention(200L << 20, TimeSpan.FromDays(14)), DiagnosticRetention.ForChannel(DiagnosticsChannel.Beta));
        Assert.Equal(new DiagnosticRetention(20L << 20, TimeSpan.FromDays(30)), DiagnosticRetention.ForChannel(DiagnosticsChannel.Stable));
    }
}
