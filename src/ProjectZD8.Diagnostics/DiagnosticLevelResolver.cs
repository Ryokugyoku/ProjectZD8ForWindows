using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// どこで決めた最低レベルを使うかです。
/// </summary>
public enum DiagnosticLevelSource
{
    /// <summary>環境変数 <c>PROJECTZD8_DIAGNOSTICS_LEVEL</c>。</summary>
    Environment,

    /// <summary><c>appsettings.Local.json</c> の <c>Diagnostics:Level</c>。</summary>
    SettingsFile,

    /// <summary>設定画面の「詳細ログ」スイッチ。</summary>
    VerboseSwitch,

    /// <summary>チャンネルの既定。</summary>
    Channel,
}

/// <summary>
/// 決めた最低レベルの決まりと、その出どころです。
/// </summary>
/// <param name="Policy">決まり。</param>
/// <param name="Source">出どころ。</param>
public sealed record DiagnosticLevelResolution(DiagnosticLevelPolicy Policy, DiagnosticLevelSource Source);

/// <summary>
/// 実行時の上書きを強い順に当てて、最低レベルの決まりを決めます（docs/LOGGING_STRATEGY.md の 5）。
/// </summary>
/// <remarks>
/// 強い順：環境変数 <c>PROJECTZD8_DIAGNOSTICS_LEVEL</c> → <c>appsettings.Local.json</c> の <c>Diagnostics:Level</c>
/// → 設定画面の「詳細ログ」スイッチ → チャンネルの既定。読めない値は無視して次へ進みます。
/// </remarks>
public static class DiagnosticLevelResolver
{
    /// <summary>最低レベルを上書きする環境変数の名前です。</summary>
    public const string EnvironmentVariable = "PROJECTZD8_DIAGNOSTICS_LEVEL";

    /// <summary>
    /// 最低レベルの決まりを決めます。
    /// </summary>
    /// <param name="channel">チャンネル。</param>
    /// <param name="environmentLevel">環境変数の値。</param>
    /// <param name="settingsFileLevel"><c>appsettings.Local.json</c> の <c>Diagnostics:Level</c> の値。</param>
    /// <param name="verboseLogging">「詳細ログ」スイッチが入っているか。</param>
    /// <returns>決まりと出どころ。</returns>
    public static DiagnosticLevelResolution Resolve(
        DiagnosticsChannel channel,
        string? environmentLevel,
        string? settingsFileLevel,
        bool verboseLogging)
    {
        if (TryParseLevel(environmentLevel, out var level))
        {
            return new(DiagnosticLevelPolicy.ForLevel(channel, level), DiagnosticLevelSource.Environment);
        }

        if (TryParseLevel(settingsFileLevel, out level))
        {
            return new(DiagnosticLevelPolicy.ForLevel(channel, level), DiagnosticLevelSource.SettingsFile);
        }

        // 「詳細ログ」は Beta と同じ量にする。Dev はもともと多いので、そのまま。
        if (verboseLogging && channel == DiagnosticsChannel.Stable)
        {
            return new(DiagnosticLevelPolicy.Verbose(), DiagnosticLevelSource.VerboseSwitch);
        }

        return new(DiagnosticLevelPolicy.ForChannel(channel), DiagnosticLevelSource.Channel);
    }

    /// <summary>
    /// レベルの名前（大文字小文字を問わない）を読みます。
    /// </summary>
    /// <param name="text"><c>trace</c> / <c>debug</c> / <c>info</c>(<c>information</c>) / <c>warn</c>(<c>warning</c>) / <c>error</c> / <c>fatal</c>(<c>critical</c>)。</param>
    /// <param name="level">読んだレベル。</param>
    /// <returns>読めたら true。</returns>
    public static bool TryParseLevel(string? text, out LogLevel level)
    {
        LogLevel? parsed = text?.Trim().ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "info" or "information" => LogLevel.Information,
            "warn" or "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "fatal" or "critical" => LogLevel.Critical,
            _ => null,
        };
        level = parsed ?? LogLevel.None;
        return parsed is not null;
    }

    /// <summary>
    /// <c>appsettings.Local.json</c> の <c>Diagnostics:Level</c> を読みます。
    /// </summary>
    /// <remarks>責務: ファイルがない・壊れている・値がないときは null を返し、落ちません。</remarks>
    /// <param name="path">ファイルのパス。null なら null。</param>
    /// <returns>値。なければ null。</returns>
    public static string? ReadSettingsFileLevel(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Diagnostics", out var section)
                && section.ValueKind == JsonValueKind.Object
                && section.TryGetProperty("Level", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
