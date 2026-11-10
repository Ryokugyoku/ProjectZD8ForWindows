namespace ProjectZD8.Data.Authentication;

/// <summary>
/// 秘密の文字列（認証セッション、Web 認証トークン）を OS の安全な保存先に置く境界です（iOS の Keychain にあたるもの）。
/// </summary>
/// <remarks>Windows の実装は <c>ProjectZD8.Data.Windows.Authentication</c>（PasswordVault、使えなければ DPAPI のファイル）。</remarks>
public interface ISecretStore
{
    /// <summary>
    /// 秘密を読みます。
    /// </summary>
    /// <remarks>責務: 1 件の名前の秘密を返します。</remarks>
    /// <param name="name">項目の名前。</param>
    /// <returns>秘密。なければ <c>null</c>。</returns>
    /// <exception cref="SecretStoreException">保存先を読めない場合。</exception>
    string? Read(string name);

    /// <summary>
    /// 秘密を置き換えて保存します。
    /// </summary>
    /// <remarks>責務: 1 件の名前へ秘密を保存します。</remarks>
    /// <param name="name">項目の名前。</param>
    /// <param name="value">秘密。</param>
    /// <exception cref="SecretStoreException">保存できない場合。</exception>
    void Write(string name, string value);

    /// <summary>
    /// 秘密を消します（なければ何もしません）。
    /// </summary>
    /// <remarks>責務: 1 件の名前の秘密をない状態にします。</remarks>
    /// <param name="name">項目の名前。</param>
    /// <exception cref="SecretStoreException">消せない場合。</exception>
    void Delete(string name);
}

/// <summary>
/// 秘密の保存先を使えないときの例外です。
/// </summary>
public sealed class SecretStoreException : Exception
{
    /// <summary>
    /// 原因の例外から作ります。
    /// </summary>
    /// <remarks>責務: OS の保存先のエラーを 1 つの種類にまとめます。</remarks>
    /// <param name="message">説明。</param>
    /// <param name="innerException">原因。</param>
    public SecretStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
