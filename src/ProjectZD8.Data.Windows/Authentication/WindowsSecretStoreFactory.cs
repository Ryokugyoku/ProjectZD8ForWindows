using ProjectZD8.Data.Authentication;

namespace ProjectZD8.Data.Windows.Authentication;

/// <summary>
/// この PC で使える秘密の保存先を選びます（資格情報マネージャー、だめなら DPAPI のファイル）。
/// </summary>
public static class WindowsSecretStoreFactory
{
    /// <summary>
    /// 使える秘密の保存先を作ります。
    /// </summary>
    /// <remarks>責務: PasswordVault を試しに読み書きし、失敗したら DPAPI のファイルの保存先を返します。</remarks>
    /// <param name="fallbackDirectory">DPAPI のファイルを置くフォルダー（例：LocalFolder の <c>secrets</c>）。</param>
    /// <returns>秘密の保存先。</returns>
    public static ISecretStore Create(string fallbackDirectory)
    {
        try
        {
            var vault = new PasswordVaultSecretStore();
            const string probe = "probe";
            vault.Write(probe, "ok");
            var readable = vault.Read(probe) == "ok";
            vault.Delete(probe);
            if (readable)
            {
                WindowsAuthLog.Selected(WindowsAuthLog.Logger, "password-vault");
                return vault;
            }

            WindowsAuthLog.FellBackToDpapi(WindowsAuthLog.Logger, null);
            WindowsAuthLog.Selected(WindowsAuthLog.Logger, "dpapi-file");
            return new DpapiFileSecretStore(fallbackDirectory);
        }
        catch (Exception exception) when (exception is SecretStoreException or TypeInitializationException
            or System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            // 資格情報マネージャーを使えない環境では、DPAPI のファイルへ切り替えます。
            WindowsAuthLog.FellBackToDpapi(WindowsAuthLog.Logger, exception);
        }

        WindowsAuthLog.Selected(WindowsAuthLog.Logger, "dpapi-file");
        return new DpapiFileSecretStore(fallbackDirectory);
    }
}
