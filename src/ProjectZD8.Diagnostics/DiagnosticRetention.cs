namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログのファイルを残す上限（合計サイズ・日数）です。
/// </summary>
/// <param name="MaxTotalBytes">合計サイズの上限。</param>
/// <param name="MaxAge">残す日数の上限。</param>
public sealed record DiagnosticRetention(long MaxTotalBytes, TimeSpan MaxAge)
{
    /// <summary>
    /// チャンネルの上限を返します（docs/LOGGING_STRATEGY.md の 2 の表）。
    /// </summary>
    /// <param name="channel">チャンネル。</param>
    /// <returns>Dev は 500 MB・14 日、Beta は 200 MB・14 日、Stable は 20 MB・30 日。</returns>
    public static DiagnosticRetention ForChannel(DiagnosticsChannel channel) => channel switch
    {
        DiagnosticsChannel.Dev => new(500L * 1024 * 1024, TimeSpan.FromDays(14)),
        DiagnosticsChannel.Beta => new(200L * 1024 * 1024, TimeSpan.FromDays(14)),
        _ => new(20L * 1024 * 1024, TimeSpan.FromDays(30)),
    };
}
