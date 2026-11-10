using ProjectZD8.Data.Configuration;

namespace ProjectZD8.Data.Tests.Configuration;

/// <summary>Sign in with Apple の機能フラグ（Coming Soon の切り替え）を検証します。</summary>
public sealed class CloudServiceConfigurationTests
{
    /// <summary>すべての設定がそろった構成です。</summary>
    private static readonly CloudServiceConfiguration Complete = new()
    {
        AppleServicesId = "com.example.web",
        AppleRedirectUri = new Uri("https://example.com/cb.html"),
        CloudKitEnvironment = "development",
        CloudKitApiToken = "token",
        CallbackScheme = "custom-scheme",
    };

    /// <summary>既定ではフラグが無効で Coming Soon になることを確かめます。</summary>
    [Fact]
    public void DefaultIsComingSoon()
    {
        var configuration = new CloudServiceConfiguration();

        Assert.False(configuration.AppleSignInEnabled);
        Assert.Equal(AppleSignInAvailability.ComingSoon, configuration.AppleSignInAvailability);
        Assert.False(configuration.IsAppleSignInActive);
    }

    /// <summary>設定がそろっていても、フラグが無効なら Coming Soon のままで、実際に使う設定は「設定なし」になることを確かめます。</summary>
    [Fact]
    public void CompleteSettingsWithoutFlagStayComingSoon()
    {
        var configuration = Complete;

        Assert.True(configuration.IsConfigured);
        Assert.Equal(AppleSignInAvailability.ComingSoon, configuration.AppleSignInAvailability);

        var effective = configuration.ForActiveFeatures();
        Assert.False(effective.IsConfigured);
        Assert.Null(effective.AppleServicesId);
        Assert.Null(effective.CloudKitApiToken);
        Assert.Equal("custom-scheme", effective.CallbackScheme);
    }

    /// <summary>フラグが有効でも設定が足りなければ MissingSettings で、実際に使う設定は「設定なし」になることを確かめます。</summary>
    [Fact]
    public void FlagWithMissingSettingsIsNotActive()
    {
        var configuration = Complete with { AppleSignInEnabled = true, CloudKitApiToken = null };

        Assert.Equal(AppleSignInAvailability.MissingSettings, configuration.AppleSignInAvailability);
        Assert.False(configuration.IsAppleSignInActive);
        Assert.False(configuration.ForActiveFeatures().IsConfigured);
    }

    /// <summary>フラグが有効で設定もそろえば Active になり、設定がそのまま使われることを確かめます。</summary>
    [Fact]
    public void FlagWithCompleteSettingsIsActive()
    {
        var configuration = Complete with { AppleSignInEnabled = true };

        Assert.Equal(AppleSignInAvailability.Active, configuration.AppleSignInAvailability);
        Assert.True(configuration.IsAppleSignInActive);
        Assert.Same(configuration, configuration.ForActiveFeatures());
    }

    /// <summary>ログ用の文字列にフラグが出て、API トークンは伏せられることを確かめます。</summary>
    [Fact]
    public void ToStringShowsFlagAndHidesToken()
    {
        var text = (Complete with { AppleSignInEnabled = true }).ToString();

        Assert.Contains("AppleSignInEnabled = True", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text.Replace("ApiToken", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
