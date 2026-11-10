namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit の Web 認証トークン（ckWebAuthToken）を端末に保存する境界です。
/// </summary>
/// <remarks>トークンは応答ごとに新しくなることがあるので、そのたびに上書き保存されます。</remarks>
public interface ICloudKitWebAuthTokenStore
{
    /// <summary>
    /// 保存したトークンを読みます。
    /// </summary>
    /// <remarks>責務: 1 件の保存済みトークンを返します。</remarks>
    /// <returns>トークン。未保存なら <c>null</c>。</returns>
    string? Load();

    /// <summary>
    /// トークンを置き換えて保存します。
    /// </summary>
    /// <remarks>責務: 1 件の新しいトークンを保存します。</remarks>
    /// <param name="token">空でないトークン。</param>
    void Save(string token);

    /// <summary>
    /// トークンを消します。
    /// </summary>
    /// <remarks>責務: 保存済みのトークンをない状態にします。</remarks>
    void Remove();
}
