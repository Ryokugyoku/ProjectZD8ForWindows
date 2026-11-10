using System.Text.Json;

namespace ProjectZD8.Data.Configuration;

/// <summary>
/// <c>appsettings.Local.json</c> と環境変数 <c>PROJECTZD8_*</c> から <see cref="CloudServiceConfiguration"/> を読みます。
/// </summary>
/// <remarks>
/// JSON の形（すべて省略可）:
/// <code>
/// {
///   "Apple":    { "ServicesId": "...", "RedirectUri": "https://.../apple-callback.html" },
///   "CloudKit": { "ContainerId": "iCloud.Ryokugyoku.ProjectZD8", "Environment": "production", "ApiToken": "..." },
///   "CallbackScheme": "revtorque-insight"
/// }
/// </code>
/// 環境変数は JSON より優先します：<c>PROJECTZD8_APPLE_SERVICES_ID</c>、<c>PROJECTZD8_APPLE_REDIRECT_URI</c>、
/// <c>PROJECTZD8_CALLBACK_SCHEME</c>、<c>PROJECTZD8_CLOUDKIT_CONTAINER_ID</c>、<c>PROJECTZD8_CLOUDKIT_ENVIRONMENT</c>、
/// <c>PROJECTZD8_CLOUDKIT_API_TOKEN</c>。ファイルがない・壊れているときは、その部分を無視します（落ちません）。
/// </remarks>
public static class CloudServiceConfigurationLoader
{
    /// <summary>Apple の Services ID の環境変数名です。</summary>
    public const string AppleServicesIdVariable = "PROJECTZD8_APPLE_SERVICES_ID";

    /// <summary>Apple のリダイレクト先の環境変数名です。</summary>
    public const string AppleRedirectUriVariable = "PROJECTZD8_APPLE_REDIRECT_URI";

    /// <summary>カスタム URL スキームの環境変数名です。</summary>
    public const string CallbackSchemeVariable = "PROJECTZD8_CALLBACK_SCHEME";

    /// <summary>CloudKit コンテナー ID の環境変数名です。</summary>
    public const string CloudKitContainerIdVariable = "PROJECTZD8_CLOUDKIT_CONTAINER_ID";

    /// <summary>CloudKit 環境の環境変数名です。</summary>
    public const string CloudKitEnvironmentVariable = "PROJECTZD8_CLOUDKIT_ENVIRONMENT";

    /// <summary>CloudKit API トークンの環境変数名です。</summary>
    public const string CloudKitApiTokenVariable = "PROJECTZD8_CLOUDKIT_API_TOKEN";

    /// <summary>
    /// 設定ファイルとプロセスの環境変数から設定を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の設定ファイルと環境変数を合わせて、1 件の設定にします。</remarks>
    /// <param name="jsonPath"><c>appsettings.Local.json</c> のパス。<c>null</c> なら環境変数だけを読みます。</param>
    /// <returns>読んだ設定。値がそろわなければ <see cref="CloudServiceConfiguration.IsConfigured"/> が <c>false</c>。</returns>
    public static CloudServiceConfiguration Load(string? jsonPath) =>
        Load(jsonPath, Environment.GetEnvironmentVariable);

    /// <summary>
    /// 設定ファイルと、注入した環境変数の読み取り関数から設定を読みます。
    /// </summary>
    /// <remarks>責務: テストで環境変数を差し替えられる形で、設定ファイルと環境変数を合わせます。</remarks>
    /// <param name="jsonPath"><c>appsettings.Local.json</c> のパス。<c>null</c> なら環境変数だけ。</param>
    /// <param name="environment">環境変数を名前から読む関数。</param>
    /// <returns>読んだ設定。</returns>
    public static CloudServiceConfiguration Load(string? jsonPath, Func<string, string?> environment)
    {
        var file = ReadFile(jsonPath);
        string? Pick(string variable, string? fromFile) =>
            NonEmpty(environment(variable)) ?? NonEmpty(fromFile);

        var defaults = CloudServiceConfiguration.NotConfigured;
        var redirect = Pick(AppleRedirectUriVariable, file.RedirectUri);
        return new CloudServiceConfiguration
        {
            AppleServicesId = Pick(AppleServicesIdVariable, file.ServicesId),
            AppleRedirectUri = redirect is not null && Uri.TryCreate(redirect, UriKind.Absolute, out var uri) ? uri : null,
            CallbackScheme = Pick(CallbackSchemeVariable, file.CallbackScheme) ?? defaults.CallbackScheme,
            CloudKitContainerId = Pick(CloudKitContainerIdVariable, file.ContainerId) ?? defaults.CloudKitContainerId,
            CloudKitEnvironment = Pick(CloudKitEnvironmentVariable, file.Environment)?.ToLowerInvariant()
                ?? defaults.CloudKitEnvironment,
            CloudKitApiToken = Pick(CloudKitApiTokenVariable, file.ApiToken),
        };
    }

    /// <summary>
    /// 空白だけの文字列を <c>null</c> にします。
    /// </summary>
    /// <remarks>責務: 未設定と空の値を同じに扱います。</remarks>
    /// <param name="value">値。</param>
    /// <returns>前後の空白を除いた値。空なら <c>null</c>。</returns>
    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 設定ファイルを読みます。
    /// </summary>
    /// <remarks>責務: 1 件の JSON ファイルから設定の値を取り出します。ないか壊れていれば空を返します。</remarks>
    /// <param name="jsonPath">ファイルのパス。</param>
    /// <returns>ファイルの値。</returns>
    private static FileValues ReadFile(string? jsonPath)
    {
        if (jsonPath is null || !File.Exists(jsonPath))
        {
            return new FileValues();
        }
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllBytes(jsonPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            return new FileValues
            {
                ServicesId = Get(root, "Apple", "ServicesId"),
                RedirectUri = Get(root, "Apple", "RedirectUri"),
                ContainerId = Get(root, "CloudKit", "ContainerId"),
                Environment = Get(root, "CloudKit", "Environment"),
                ApiToken = Get(root, "CloudKit", "ApiToken"),
                CallbackScheme = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("CallbackScheme", out var scheme)
                    && scheme.ValueKind == JsonValueKind.String
                        ? scheme.GetString()
                        : null,
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new FileValues();
        }
    }

    /// <summary>
    /// 2 段のキーの文字列を読みます。
    /// </summary>
    /// <remarks>責務: <c>section.key</c> の文字列を、なければ <c>null</c> として読みます。</remarks>
    /// <param name="root">ルート要素。</param>
    /// <param name="section">節の名前。</param>
    /// <param name="key">キー。</param>
    /// <returns>文字列。</returns>
    private static string? Get(JsonElement root, string section, string key) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(section, out var node)
        && node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// 設定ファイルから読んだ値です。
    /// </summary>
    private sealed record FileValues
    {
        /// <summary>Services ID。</summary>
        public string? ServicesId { get; init; }

        /// <summary>リダイレクト先。</summary>
        public string? RedirectUri { get; init; }

        /// <summary>コンテナー ID。</summary>
        public string? ContainerId { get; init; }

        /// <summary>環境。</summary>
        public string? Environment { get; init; }

        /// <summary>API トークン。</summary>
        public string? ApiToken { get; init; }

        /// <summary>カスタム URL スキーム。</summary>
        public string? CallbackScheme { get; init; }
    }
}
