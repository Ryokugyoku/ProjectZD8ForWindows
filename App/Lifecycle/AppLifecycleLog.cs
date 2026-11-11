using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8ForWindows.AppRoot.Lifecycle;

/// <summary>
/// アプリの入口（<c>App.xaml.cs</c>）が書く診断ログです（分類 <c>app</c>）。
/// </summary>
/// <remarks>書き方は <c>CompositionLog</c> を参照。ID は 1100 番台。</remarks>
internal static partial class AppLifecycleLog
{
    /// <summary>起動・終了のロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.App);

    /// <summary>
    /// 2 つ目の起動から起動要求が転送されてきたことを書きます。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="kind">起動の種類。</param>
    [LoggerMessage(
        EventId = 1101,
        EventName = "app.activation.redirected",
        Level = LogLevel.Information,
        Message = "別の起動から起動要求を受け取りました（{Kind}）")]
    public static partial void ActivationRedirected(ILogger logger, string kind);

    /// <summary>
    /// プロトコル起動の URI を受け取ったことを書きます。
    /// </summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="uri">URI（<see cref="Redact.Url(System.Uri)"/> でクエリを落としたもの。認可コードが入るため）。</param>
    [LoggerMessage(
        EventId = 1102,
        EventName = "app.activation.protocol",
        Level = LogLevel.Debug,
        Message = "プロトコル起動を受け取りました: {Uri}")]
    public static partial void ProtocolActivation(ILogger logger, string uri);
}
