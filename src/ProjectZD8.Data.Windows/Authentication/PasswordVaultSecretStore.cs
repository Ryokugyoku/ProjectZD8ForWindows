using System.Runtime.InteropServices;
using ProjectZD8.Data.Authentication;
using Windows.Security.Credentials;

namespace ProjectZD8.Data.Windows.Authentication;

/// <summary>
/// Windows の資格情報マネージャー（<c>Windows.Security.Credentials.PasswordVault</c>）に秘密を保存します（iOS の Keychain にあたるもの）。
/// </summary>
/// <remarks>リソース名は既定で <c>Ryokugyoku.ProjectZD8</c>、項目名をユーザー名として 1 件ずつ保存します。値はユーザーごとに保護されます。</remarks>
public sealed class PasswordVaultSecretStore : ISecretStore
{
    /// <summary>既定のリソース名です。</summary>
    public const string DefaultResource = "Ryokugyoku.ProjectZD8";

    /// <summary>項目が見つからないときの HRESULT（ERROR_NOT_FOUND）です。</summary>
    private const int ElementNotFound = unchecked((int)0x80070490);

    /// <summary>資格情報のリソース名です。</summary>
    private readonly string _resource;

    /// <summary>資格情報の保管庫です。</summary>
    private readonly PasswordVault _vault = new();

    /// <summary>
    /// リソース名を指定して作ります。
    /// </summary>
    /// <remarks>責務: 秘密の保存先を 1 件のリソース名に固定します。</remarks>
    /// <param name="resource">リソース名（テストでは一意な名前）。</param>
    public PasswordVaultSecretStore(string resource = DefaultResource)
    {
        _resource = resource;
    }

    /// <inheritdoc />
    public string? Read(string name)
    {
        try
        {
            var credential = _vault.Retrieve(_resource, name);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (COMException exception) when (exception.HResult == ElementNotFound)
        {
            return null;
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            throw new SecretStoreException("資格情報マネージャーを読めません。", exception);
        }
    }

    /// <inheritdoc />
    public void Write(string name, string value)
    {
        try
        {
            RemoveIfPresent(name);
            _vault.Add(new PasswordCredential(_resource, name, value));
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or ArgumentException)
        {
            throw new SecretStoreException("資格情報マネージャーに保存できません。", exception);
        }
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        try
        {
            RemoveIfPresent(name);
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
        {
            throw new SecretStoreException("資格情報マネージャーから消せません。", exception);
        }
    }

    /// <summary>
    /// 項目があれば消します。
    /// </summary>
    /// <remarks>責務: 1 件の資格情報を、なくても失敗せずに消します。</remarks>
    /// <param name="name">項目名。</param>
    private void RemoveIfPresent(string name)
    {
        try
        {
            _vault.Remove(_vault.Retrieve(_resource, name));
        }
        catch (COMException exception) when (exception.HResult == ElementNotFound)
        {
        }
    }
}
