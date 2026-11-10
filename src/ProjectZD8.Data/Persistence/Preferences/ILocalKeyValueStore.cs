namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 端末内の小さな値（iOS の <c>UserDefaults</c> にあたるもの）を保存する境界です。
/// </summary>
public interface ILocalKeyValueStore
{
    /// <summary>
    /// バイト列を読みます。
    /// </summary>
    /// <remarks>責務: 1 件のキーのバイト列を返します。</remarks>
    /// <param name="key">キー。</param>
    /// <returns>保存済みのバイト列。なければ <c>null</c>。</returns>
    byte[]? GetData(string key);

    /// <summary>
    /// バイト列を保存します。
    /// </summary>
    /// <remarks>責務: 1 件のキーへバイト列を置き換えて保存します。</remarks>
    /// <param name="key">キー。</param>
    /// <param name="value">バイト列。</param>
    void SetData(string key, byte[] value);

    /// <summary>
    /// 文字列を読みます。
    /// </summary>
    /// <remarks>責務: 1 件のキーの文字列を返します。</remarks>
    /// <param name="key">キー。</param>
    /// <returns>保存済みの文字列。なければ <c>null</c>。</returns>
    string? GetString(string key);

    /// <summary>
    /// 文字列を保存します。
    /// </summary>
    /// <remarks>責務: 1 件のキーへ文字列を置き換えて保存します。</remarks>
    /// <param name="key">キー。</param>
    /// <param name="value">文字列。</param>
    void SetString(string key, string value);

    /// <summary>
    /// 値を消します。
    /// </summary>
    /// <remarks>責務: 1 件のキーの値をない状態にします。</remarks>
    /// <param name="key">キー。</param>
    void Remove(string key);
}
