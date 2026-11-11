using System.Reflection;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 配布の版（チャンネル）です。書く量と保存の上限を決めます（docs/LOGGING_STRATEGY.md の 2・5）。
/// </summary>
public enum DiagnosticsChannel
{
    /// <summary>開発（Debug ビルド）。Trace 以上を全部書く。</summary>
    Dev,

    /// <summary>テストリリース。Debug 以上（<c>obd.wire</c> は Trace）を全部書く。</summary>
    Beta,

    /// <summary>リリース。Warning 以上だけ書き、Debug 以上の直近をリングバッファに持つ。</summary>
    Stable,
}

/// <summary>
/// ビルド時に埋め込んだチャンネルを読みます。
/// </summary>
/// <remarks>
/// MSBuild のプロパティ <c>DiagnosticsChannel</c> を、アプリのアセンブリの
/// <c>[AssemblyMetadata("DiagnosticsChannel", "...")]</c> として埋め込みます（ProjectZD8ForWindows.csproj）。
/// </remarks>
public static class DiagnosticsChannelRawValue
{
    /// <summary>アセンブリのメタデータのキーです。</summary>
    public const string MetadataKey = "DiagnosticsChannel";

    /// <summary>
    /// アセンブリに埋め込んだチャンネルを返します。
    /// </summary>
    /// <param name="assembly">アプリのアセンブリ。</param>
    /// <returns>チャンネル。埋め込みがない・読めないなら <see cref="DiagnosticsChannel.Stable"/>（いちばん控えめ）。</returns>
    public static DiagnosticsChannel FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var value = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal))
            ?.Value;
        return Parse(value);
    }

    /// <summary>
    /// 文字列（大文字小文字を問わない）からチャンネルを返します。
    /// </summary>
    /// <param name="value"><c>dev</c> / <c>beta</c> / <c>stable</c>。</param>
    /// <returns>チャンネル。分からなければ <see cref="DiagnosticsChannel.Stable"/>。</returns>
    public static DiagnosticsChannel Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dev" => DiagnosticsChannel.Dev,
        "beta" => DiagnosticsChannel.Beta,
        _ => DiagnosticsChannel.Stable,
    };

    /// <summary>
    /// ログに書く文字列を返します。
    /// </summary>
    /// <param name="channel">チャンネル。</param>
    /// <returns><c>dev</c> / <c>beta</c> / <c>stable</c>。</returns>
    public static string ToRawValue(this DiagnosticsChannel channel) => channel switch
    {
        DiagnosticsChannel.Dev => "dev",
        DiagnosticsChannel.Beta => "beta",
        _ => "stable",
    };
}
