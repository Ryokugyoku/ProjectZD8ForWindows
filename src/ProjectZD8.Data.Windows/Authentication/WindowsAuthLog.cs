using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Windows.Authentication;

/// <summary>
/// Windows の資格情報の保存先の選択が書く診断ログです（分類 <c>auth</c>、ID は 8050 番台）。
/// </summary>
/// <remarks>秘密の値は書きません。保存先の種類だけです。</remarks>
internal static partial class WindowsAuthLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.Auth);

    /// <summary>資格情報マネージャー（PasswordVault）を使えず、DPAPI のファイルに切り替えたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">使えなかった理由。例外なしで読み戻せなかった場合は null。</param>
    [LoggerMessage(
        EventId = 8051,
        EventName = "auth.credential-store.fallback",
        Level = LogLevel.Warning,
        Message = "資格情報マネージャーを使えないため、DPAPI のファイルに切り替えます")]
    public static partial void FellBackToDpapi(ILogger logger, Exception? exception);

    /// <summary>使う資格情報の保存先を決めたことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="kind">保存先の種類（<c>password-vault</c>・<c>dpapi-file</c>）。</param>
    [LoggerMessage(
        EventId = 8052,
        EventName = "auth.credential-store.selected",
        Level = LogLevel.Debug,
        Message = "資格情報の保存先: {Kind}")]
    public static partial void Selected(ILogger logger, string kind);
}
