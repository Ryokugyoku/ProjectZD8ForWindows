using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 端末内の設定ファイル（JSON）の読込が書く診断ログです（分類 <c>app</c>、ID は 1500 番台）。
/// </summary>
/// <remarks>ファイルのパスは <see cref="Redact.Path(string?, string?)"/> を通します。設定の中身は書きません。</remarks>
internal static partial class PreferencesLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.App);

    /// <summary>既定のアダプターの設定ファイルを読めず、未設定として扱うことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    /// <param name="file">ファイル（伏せたパス）。</param>
    [LoggerMessage(
        EventId = 1511,
        EventName = "app.preferences.default-adapter.unreadable",
        Level = LogLevel.Warning,
        Message = "既定のアダプターの設定を読めないため、未設定として扱います: {File}")]
    public static partial void DefaultAdapterUnreadable(ILogger logger, Exception exception, string file);

    /// <summary>端末内の設定ファイルを読めず、空として扱うことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    /// <param name="file">ファイル（伏せたパス）。</param>
    [LoggerMessage(
        EventId = 1512,
        EventName = "app.preferences.local-store.unreadable",
        Level = LogLevel.Warning,
        Message = "端末内の設定ファイルを読めないため、空として扱います: {File}")]
    public static partial void LocalStoreUnreadable(ILogger logger, Exception exception, string file);

    /// <summary>アカウントごとの設定を読めず、設定なしとして扱うことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">読めなかった理由。</param>
    [LoggerMessage(
        EventId = 1513,
        EventName = "app.settings.decode.failed",
        Level = LogLevel.Warning,
        Message = "アカウントの設定を読めないため、設定なしとして扱います")]
    public static partial void AccountSettingsUndecodable(ILogger logger, Exception exception);
}
