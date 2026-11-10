namespace ProjectZD8.Data.Authentication;

/// <summary>
/// 既定のブラウザーで URL を開く境界です（Windows の実装は <c>Windows.System.Launcher</c>）。
/// </summary>
public interface IExternalBrowserLauncher
{
    /// <summary>
    /// URL を既定のブラウザーで開きます。
    /// </summary>
    /// <remarks>責務: 1 件の https の URL を OS のブラウザーへ渡します。</remarks>
    /// <param name="uri">開く URL。</param>
    /// <returns>開けたら <c>true</c>。</returns>
    Task<bool> Launch(Uri uri);
}
