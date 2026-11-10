namespace ProjectZD8.Data.Configuration;

/// <summary>
/// Sign in with Apple（Web）と CloudKit Web Services に使う設定です。コミットしない値（API トークンなど）を含みます。
/// </summary>
/// <remarks>
/// 値は <see cref="CloudServiceConfigurationLoader"/> が <c>appsettings.Local.json</c> と環境変数 <c>PROJECTZD8_*</c> から読みます。
/// 必要な値がそろわないとき（<see cref="IsConfigured"/> が <c>false</c>）は、アプリはアカウントなしのローカルモードで動きます。
/// </remarks>
public sealed record CloudServiceConfiguration
{
    /// <summary>既定のカスタム URL スキームです。</summary>
    public const string DefaultCallbackScheme = "revtorque-insight";

    /// <summary>iOS 版と同じ CloudKit コンテナー ID です。</summary>
    public const string DefaultCloudKitContainerId = "iCloud.Ryokugyoku.ProjectZD8";

    /// <summary>CloudKit Web Services の既定の接続先です。</summary>
    public static readonly Uri DefaultCloudKitBaseUri = new("https://api.apple-cloudkit.com/");

    /// <summary>Sign in with Apple の Services ID（Web 用の client_id）。iOS アプリと同じ Team・同じ主 App ID に紐づけます。</summary>
    public string? AppleServicesId { get; init; }

    /// <summary>Apple がサインイン後に戻す https のページ（Services ID の Return URL に登録したもの）。</summary>
    public Uri? AppleRedirectUri { get; init; }

    /// <summary>ブラウザーからアプリへ戻すカスタム URL スキーム。</summary>
    public string CallbackScheme { get; init; } = DefaultCallbackScheme;

    /// <summary>CloudKit のコンテナー ID。</summary>
    public string CloudKitContainerId { get; init; } = DefaultCloudKitContainerId;

    /// <summary>CloudKit の環境（<c>development</c> または <c>production</c>）。</summary>
    public string CloudKitEnvironment { get; init; } = "production";

    /// <summary>CloudKit Console で作った API トークン（ckAPIToken）。</summary>
    public string? CloudKitApiToken { get; init; }

    /// <summary>CloudKit Web Services の接続先（テストで差し替えるため）。</summary>
    public Uri CloudKitBaseUri { get; init; } = DefaultCloudKitBaseUri;

    /// <summary>
    /// Sign in with Apple と CloudKit の両方に必要な値がそろっているかを返します。
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppleServicesId)
        && AppleRedirectUri is { IsAbsoluteUri: true } redirect
        && string.Equals(redirect.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(CallbackScheme)
        && !string.IsNullOrWhiteSpace(CloudKitContainerId)
        && (string.Equals(CloudKitEnvironment, "development", StringComparison.Ordinal)
            || string.Equals(CloudKitEnvironment, "production", StringComparison.Ordinal))
        && !string.IsNullOrWhiteSpace(CloudKitApiToken);

    /// <summary>設定のない状態（ローカルモード）です。</summary>
    public static CloudServiceConfiguration NotConfigured { get; } = new();

    /// <summary>
    /// Sign in with Apple の戻り先（<c>revtorque-insight://auth/apple</c>）を返します。
    /// </summary>
    public Uri AppleCallbackUri => new($"{CallbackScheme}://auth/apple");

    /// <summary>
    /// CloudKit のサインインの戻り先（<c>revtorque-insight://auth/cloudkit</c>）を返します。
    /// </summary>
    public Uri CloudKitCallbackUri => new($"{CallbackScheme}://auth/cloudkit");

    /// <summary>
    /// 秘密の値を伏せた文字列を返します（ログに API トークンを出さないため）。
    /// </summary>
    /// <remarks>責務: 設定の概要を、秘密を含まない文字列へ変換します。</remarks>
    /// <returns>伏せ字の入った設定の説明。</returns>
    public override string ToString() =>
        $"CloudServiceConfiguration {{ IsConfigured = {IsConfigured}, AppleServicesId = {AppleServicesId}, "
        + $"AppleRedirectUri = {AppleRedirectUri}, CallbackScheme = {CallbackScheme}, "
        + $"CloudKitContainerId = {CloudKitContainerId}, CloudKitEnvironment = {CloudKitEnvironment}, "
        + $"CloudKitApiToken = {(string.IsNullOrEmpty(CloudKitApiToken) ? "(なし)" : "***")} }}";
}
