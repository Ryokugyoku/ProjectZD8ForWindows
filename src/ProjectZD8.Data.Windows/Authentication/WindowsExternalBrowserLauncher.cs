using ProjectZD8.Data.Authentication;

namespace ProjectZD8.Data.Windows.Authentication;

/// <summary>
/// <c>Windows.System.Launcher</c> で既定のブラウザーを開きます。
/// </summary>
public sealed class WindowsExternalBrowserLauncher : IExternalBrowserLauncher
{
    /// <summary>
    /// URL を既定のブラウザーで開きます。
    /// </summary>
    /// <remarks>責務: 1 件の https の URL を OS へ渡します。https 以外は開きません（サインインのページだけを開くため）。</remarks>
    /// <param name="uri">開く URL。</param>
    /// <returns>開けたら <c>true</c>。</returns>
    public async Task<bool> Launch(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return await global::Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
