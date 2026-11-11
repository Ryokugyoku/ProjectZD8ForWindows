using System;
using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// 組み立て（<see cref="WindowsApplicationComposition"/>）が書く診断ログです。後続の計装の手本です。
/// </summary>
/// <remarks>
/// <para>
/// 書き方：<c>[LoggerMessage]</c> のソース生成で、イベント名（<c>EventName</c>、<c>分類.対象.結果</c>）と ID を固定します。
/// 無効なレベルでは生成コードが <see cref="ILogger.IsEnabled"/> を見て何も割り当てません。
/// 値はメッセージの <c>{Name}</c> の名前で <c>props</c> に入ります（数値・短い文字列だけ。個人を特定する値は <see cref="Redact"/> を通す）。
/// Error には必ず例外を付けます。
/// </para>
/// <para>
/// ID は分類ごとに 1000 番台で振ります（app は 1000〜、store.sqlite は 6000〜。docs/LOGGING_STRATEGY.md の 3 の表の順）。
/// </para>
/// </remarks>
internal static partial class CompositionLog
{
    /// <summary>起動・設定のロガーです。</summary>
    public static readonly ILogger App = DiagnosticLog.For(DiagnosticCategories.App);

    /// <summary>SQLite の保存先のロガーです。</summary>
    public static readonly ILogger StoreSqlite = DiagnosticLog.For(DiagnosticCategories.StoreSqlite);

    /// <summary>
    /// Sign in with Apple の機能フラグは有効だが、設定が足りないため Coming Soon のままにすることを書きます。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="hasServicesId">Services ID があるか。</param>
    /// <param name="hasRedirectUri">リダイレクト先があるか。</param>
    /// <param name="hasApiToken">CloudKit の API トークンがあるか（値は書かない）。</param>
    /// <param name="cloudKitEnvironment">CloudKit の環境。</param>
    [LoggerMessage(
        EventId = 1001,
        EventName = "app.config.apple-sign-in-incomplete",
        Level = LogLevel.Warning,
        Message = "AppleSignIn:Enabled は true ですが、設定が足りないため Coming Soon のままにします（ServicesId={HasServicesId}, RedirectUri={HasRedirectUri}, ApiToken={HasApiToken}, Environment={CloudKitEnvironment}）")]
    public static partial void AppleSignInSettingsIncomplete(
        ILogger logger,
        bool hasServicesId,
        bool hasRedirectUri,
        bool hasApiToken,
        string cloudKitEnvironment);

    /// <summary>
    /// 設定画面の「詳細ログ」を入れてから期間（7 日）が過ぎたので、起動時に自動で切ったことを書きます。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="days">自動で切れるまでの日数。</param>
    [LoggerMessage(
        EventId = 1103,
        EventName = "app.verbose.expired",
        Level = LogLevel.Information,
        Message = "詳細ログを入れてから {Days} 日たったので、自動で切りました")]
    public static partial void VerboseLoggingExpired(ILogger logger, int days);

    /// <summary>
    /// SQLite の保存先を開けず、使えない保存先へ切り替えたことを書きます（利用者には保存できないと見える）。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">開けなかった理由。</param>
    /// <param name="store">保存先の名前（<c>connection-sessions</c> など）。</param>
    [LoggerMessage(
        EventId = 6001,
        EventName = "store.sqlite.open.failed",
        Level = LogLevel.Error,
        Message = "保存先 {Store} を開けませんでした。使えない保存先に切り替えます")]
    public static partial void StoreOpenFailed(ILogger logger, Exception exception, string store);
}
