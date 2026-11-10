using ProjectZD8.Data.Configuration;

namespace ProjectZD8.Data.Tests.Configuration;

/// <summary>
/// 設定ファイルと環境変数からの読み込みを確かめます。
/// </summary>
public sealed class CloudServiceConfigurationLoaderTests : IDisposable
{
    /// <summary>テスト専用の一時フォルダーです。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ProjectZD8.Data.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 一時フォルダーを消します。
    /// </summary>
    /// <remarks>責務: テストの後始末をします。</remarks>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// 設定がなければローカルモード（IsConfigured が false）になることを確かめます。
    /// </summary>
    /// <remarks>責務: 既定値と未設定の扱いを確かめます。</remarks>
    [Fact]
    public void MissingFileAndVariablesMeansLocalMode()
    {
        var configuration = CloudServiceConfigurationLoader.Load(Path.Combine(_directory, "missing.json"), _ => null);

        Assert.False(configuration.IsConfigured);
        Assert.Equal("revtorque-insight", configuration.CallbackScheme);
        Assert.Equal("iCloud.Ryokugyoku.ProjectZD8", configuration.CloudKitContainerId);
        Assert.Equal(new Uri("revtorque-insight://auth/apple"), configuration.AppleCallbackUri);
    }

    /// <summary>
    /// 設定ファイルを読み、環境変数で上書きできることを確かめます。
    /// </summary>
    /// <remarks>責務: appsettings.Local.json と PROJECTZD8_* の優先順位を確かめます。</remarks>
    [Fact]
    public void FileValuesAreOverriddenByEnvironment()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "appsettings.Local.json");
        File.WriteAllText(path, """
            {
              // コメントも書けます
              "Apple": { "ServicesId": "com.example.web", "RedirectUri": "https://example.com/cb.html" },
              "CloudKit": { "Environment": "Development", "ApiToken": "from-file" },
            }
            """);
        var environment = new Dictionary<string, string> { ["PROJECTZD8_CLOUDKIT_API_TOKEN"] = "from-env" };

        var configuration = CloudServiceConfigurationLoader.Load(path, name => environment.GetValueOrDefault(name));

        Assert.True(configuration.IsConfigured);
        Assert.Equal("com.example.web", configuration.AppleServicesId);
        Assert.Equal(new Uri("https://example.com/cb.html"), configuration.AppleRedirectUri);
        Assert.Equal("development", configuration.CloudKitEnvironment);
        Assert.Equal("from-env", configuration.CloudKitApiToken);
        Assert.DoesNotContain("from-env", configuration.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// リダイレクト先が https でない・壊れたファイルのときは、設定済みにならず落ちないことを確かめます。
    /// </summary>
    /// <remarks>責務: 不正な値への耐性を確かめます。</remarks>
    [Fact]
    public void InvalidValuesAreNotConfigured()
    {
        Directory.CreateDirectory(_directory);
        var broken = Path.Combine(_directory, "broken.json");
        File.WriteAllText(broken, "{");
        var environment = new Dictionary<string, string>
        {
            ["PROJECTZD8_APPLE_SERVICES_ID"] = "id",
            ["PROJECTZD8_APPLE_REDIRECT_URI"] = "http://example.com/cb.html",
            ["PROJECTZD8_CLOUDKIT_API_TOKEN"] = "token",
        };

        var configuration = CloudServiceConfigurationLoader.Load(broken, name => environment.GetValueOrDefault(name));

        Assert.False(configuration.IsConfigured);
    }

    /// <summary>
    /// リポジトリの例のファイルが、そのままでは本物の値を含まないことを確かめます（形だけは読めること）。
    /// </summary>
    /// <remarks>責務: appsettings.Local.example.json が読めることを確かめます。</remarks>
    [Fact]
    public void ExampleFileIsReadable()
    {
        var directory = AppContext.BaseDirectory;
        string? example = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "appsettings.Local.example.json");
            if (File.Exists(candidate))
            {
                example = candidate;
                break;
            }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.NotNull(example);

        var configuration = CloudServiceConfigurationLoader.Load(example, _ => null);

        Assert.Equal("com.example.revtorque.web", configuration.AppleServicesId);
        Assert.StartsWith("<", configuration.CloudKitApiToken, StringComparison.Ordinal);
        Assert.False(configuration.AppleSignInEnabled);
        Assert.Equal(AppleSignInAvailability.ComingSoon, configuration.AppleSignInAvailability);
    }

    /// <summary>フラグがなければ無効（Coming Soon）になることを確かめます。</summary>
    [Fact]
    public void FlagDefaultsToDisabled()
    {
        var path = WriteSettings("""{ "Apple": { "ServicesId": "com.example.web" } }""");

        var configuration = CloudServiceConfigurationLoader.Load(path, _ => null);

        Assert.False(configuration.AppleSignInEnabled);
    }

    /// <summary>ファイルの AppleSignIn:Enabled（真偽値と文字列）を読むことを確かめます。</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"no\"", false)]
    [InlineData("1", false)]
    public void FlagIsReadFromFile(string json, bool expected)
    {
        var path = WriteSettings($$"""{ "AppleSignIn": { "Enabled": {{json}} } }""");

        var configuration = CloudServiceConfigurationLoader.Load(path, _ => null);

        Assert.Equal(expected, configuration.AppleSignInEnabled);
    }

    /// <summary>環境変数のフラグがファイルより優先されることを確かめます（有効にも無効にもできる）。</summary>
    [Theory]
    [InlineData("false", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("true", "1", true)]
    public void FlagEnvironmentOverridesFile(string fileValue, string environmentValue, bool expected)
    {
        var path = WriteSettings($$"""{ "AppleSignIn": { "Enabled": {{fileValue}} } }""");
        var environment = new Dictionary<string, string> { [CloudServiceConfigurationLoader.AppleSignInEnabledVariable] = environmentValue };

        var configuration = CloudServiceConfigurationLoader.Load(path, name => environment.GetValueOrDefault(name));

        Assert.Equal(expected, configuration.AppleSignInEnabled);
    }

    /// <summary>設定がそろい、フラグも有効なら Active になることを確かめます（Services ID が出たら有効にする手順の確認）。</summary>
    [Fact]
    public void CompleteSettingsWithFlagBecomeActive()
    {
        var path = WriteSettings("""
            {
              "AppleSignIn": { "Enabled": true },
              "Apple": { "ServicesId": "com.example.web", "RedirectUri": "https://example.com/cb.html" },
              "CloudKit": { "Environment": "production", "ApiToken": "token" }
            }
            """);

        var configuration = CloudServiceConfigurationLoader.Load(path, _ => null);

        Assert.Equal(AppleSignInAvailability.Active, configuration.AppleSignInAvailability);
        Assert.Same(configuration, configuration.ForActiveFeatures());
    }

    /// <summary>
    /// テスト用の設定ファイルを書きます。
    /// </summary>
    /// <remarks>責務: 一時フォルダに appsettings.Local.json を書き、そのパスを返します。</remarks>
    /// <param name="json">ファイルの中身。</param>
    /// <returns>ファイルのパス。</returns>
    private string WriteSettings(string json)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "appsettings.Local.json");
        File.WriteAllText(path, json);
        return path;
    }
}
