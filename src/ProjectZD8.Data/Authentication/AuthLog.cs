using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// サインイン・資格情報の保存先・ローカルモードの引き継ぎが書く診断ログです（分類 <c>auth</c>、ID は 8000 番台）。
/// </summary>
/// <remarks>
/// <para>トークン（ID トークン・ckWebAuthToken）、URL のクエリ、秘密の値は一切書きません。</para>
/// <para>アカウント ID は <see cref="Redact.AccountId(string?)"/> を通した値だけを渡します。</para>
/// <para>Application の認証セッションモデルは 8100 番台、Windows の資格情報の保存先は 8050 番台です。</para>
/// </remarks>
internal static partial class AuthLog
{
    /// <summary>ロガーです。</summary>
    public static readonly ILogger Logger = DiagnosticLog.For(DiagnosticCategories.Auth);

    /// <summary>ローカルモードのデータを Apple のアカウントへ引き継いだことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="fromAccount">元のアカウント（伏せた値）。</param>
    /// <param name="toAccount">先のアカウント（伏せた値）。</param>
    [LoggerMessage(
        EventId = 8001,
        EventName = "auth.local-data.adopted",
        Level = LogLevel.Information,
        Message = "ローカルモードのデータを引き継ぎました（{FromAccount} → {ToAccount}）")]
    public static partial void LocalDataAdopted(ILogger logger, string fromAccount, string toAccount);

    /// <summary>ローカルモードのデータを引き継げず、次回のサインインに回したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">引き継げなかった理由。</param>
    /// <param name="fromAccount">元のアカウント（伏せた値）。</param>
    /// <param name="toAccount">先のアカウント（伏せた値）。</param>
    [LoggerMessage(
        EventId = 8002,
        EventName = "auth.local-data.adopt.failed",
        Level = LogLevel.Warning,
        Message = "ローカルモードのデータを引き継げませんでした。次のサインインでやり直します（{FromAccount} → {ToAccount}）")]
    public static partial void LocalDataAdoptFailed(ILogger logger, Exception exception, string fromAccount, string toAccount);

    /// <summary>端末内の資格情報の保存先の操作が失敗したことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="item">項目の種類（<c>session</c>）。</param>
    /// <param name="operation">操作（<c>read</c>・<c>write</c>・<c>delete</c>）。</param>
    [LoggerMessage(
        EventId = 8010,
        EventName = "auth.credential.store.failed",
        Level = LogLevel.Error,
        Message = "資格情報の保存先の操作に失敗しました（{Item}、{Operation}）")]
    public static partial void CredentialStoreFailed(ILogger logger, Exception exception, string item, string operation);

    /// <summary>CloudKit のトークンの保存先の操作が失敗したが、メモリの値で続けることを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">失敗の理由。</param>
    /// <param name="operation">操作（<c>read</c>・<c>write</c>・<c>delete</c>）。</param>
    [LoggerMessage(
        EventId = 8011,
        EventName = "auth.cloudkit-token.store.degraded",
        Level = LogLevel.Warning,
        Message = "CloudKit のトークンの保存先を使えませんでした。この実行の間はメモリの値で続けます（{Operation}）")]
    public static partial void CloudKitTokenStoreDegraded(ILogger logger, Exception exception, string operation);

    /// <summary>Apple のサインインがブラウザーを開けず使えないことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    [LoggerMessage(
        EventId = 8020,
        EventName = "auth.apple.browser.launch-failed",
        Level = LogLevel.Warning,
        Message = "Apple のサインインのためにブラウザーを開けませんでした")]
    public static partial void AppleBrowserLaunchFailed(ILogger logger);

    /// <summary>Apple のサインインがやめられた、または時間内に戻らなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="timedOut">時間切れか（false ならやめた）。</param>
    [LoggerMessage(
        EventId = 8021,
        EventName = "auth.apple.sign-in.cancelled",
        Level = LogLevel.Debug,
        Message = "Apple のサインインが終わりました（時間切れ={TimedOut}）")]
    public static partial void AppleSignInCancelled(ILogger logger, bool timedOut);

    /// <summary>Apple からの戻りの URL を受け付けなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="reason">理由（固定の短い名前）。</param>
    [LoggerMessage(
        EventId = 8022,
        EventName = "auth.apple.callback.rejected",
        Level = LogLevel.Warning,
        Message = "Apple からの戻りを受け付けませんでした（{Reason}）")]
    public static partial void AppleCallbackRejected(ILogger logger, string reason);

    /// <summary>Apple の ID トークンを確かめられなかったことを書きます（トークンの中身は書かない）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">確かめられなかった理由。</param>
    [LoggerMessage(
        EventId = 8023,
        EventName = "auth.apple.id-token.failed",
        Level = LogLevel.Error,
        Message = "Apple の ID トークンを確かめられませんでした")]
    public static partial void AppleIdTokenFailed(ILogger logger, Exception exception);

    /// <summary>CloudKit のサインインがブラウザーを開けなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="redirect">サインインの URL（クエリを落とした値）。</param>
    [LoggerMessage(
        EventId = 8030,
        EventName = "auth.cloudkit.browser.launch-failed",
        Level = LogLevel.Warning,
        Message = "CloudKit のサインインのためにブラウザーを開けませんでした: {Redirect}")]
    public static partial void CloudKitBrowserLaunchFailed(ILogger logger, string redirect);

    /// <summary>CloudKit のサインインが時間内に終わらなかったことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exception">時間切れの例外。</param>
    [LoggerMessage(
        EventId = 8031,
        EventName = "auth.cloudkit.sign-in.timeout",
        Level = LogLevel.Warning,
        Message = "CloudKit のサインインが時間内に終わりませんでした")]
    public static partial void CloudKitSignInTimedOut(ILogger logger, Exception exception);

    /// <summary>CloudKit のサインインが必要で、ブラウザーを開くことを書きます。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="redirect">サインインの URL（クエリを落とした値）。</param>
    [LoggerMessage(
        EventId = 8032,
        EventName = "auth.cloudkit.sign-in.required",
        Level = LogLevel.Debug,
        Message = "CloudKit のサインインが必要です: {Redirect}")]
    public static partial void CloudKitSignInRequired(ILogger logger, string redirect);
}
