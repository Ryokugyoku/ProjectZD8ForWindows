namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 端末間で同期する小さな値の保存先です（iOS の <c>UbiquitousKeyValueStorePort</c> にあたるもの）。
/// </summary>
/// <remarks>
/// iOS は iCloud Key-Value Store を使いますが、Windows からは使えないので、Windows では CloudKit のレコードで代わりにします
/// （<c>CloudKitSynchronizedKeyValueStore</c>）。同期は非同期に進み、ほかの端末の変更は <see cref="ChangedExternally"/> で届きます。
/// </remarks>
public interface ISynchronizedKeyValueStore
{
    /// <summary>
    /// ほかの端末の変更が届いたときに、変わったキーの一覧とともに発生します（iOS の <c>didChangeExternallyNotification</c>）。
    /// </summary>
    event Action<IReadOnlyList<string>>? ChangedExternally;

    /// <summary>
    /// キーの値を返します。
    /// </summary>
    /// <remarks>責務: 1 件のキーの、今わかっている同期値を返します。</remarks>
    /// <param name="key">キー。</param>
    /// <returns>値。なければ <c>null</c>。</returns>
    byte[]? Data(string key);

    /// <summary>
    /// キーへ値を保存します。
    /// </summary>
    /// <remarks>責務: 1 件の同期値を置き換えて保存します。</remarks>
    /// <param name="key">キー。</param>
    /// <param name="value">値。</param>
    void SetData(string key, byte[] value);

    /// <summary>
    /// キーの値を消します。
    /// </summary>
    /// <remarks>責務: 1 件のキーの同期値をない状態にします。</remarks>
    /// <param name="key">キー。</param>
    void RemoveObject(string key);

    /// <summary>
    /// 保留中の変更の送信と、最新の値の取得を始めます（終わるのを待ちません）。
    /// </summary>
    /// <remarks>責務: 1 回の同期要求を受け付けます。</remarks>
    /// <returns>同期要求を受け付けたら <c>true</c>。</returns>
    bool Synchronize();
}
