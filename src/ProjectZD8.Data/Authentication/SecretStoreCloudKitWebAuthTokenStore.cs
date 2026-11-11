using ProjectZD8.Data.Network.CloudKit;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// CloudKit の Web 認証トークン（ckWebAuthToken）を秘密の保存先に保持します。
/// </summary>
/// <remarks>保存先を読めないときは、トークンがない（サインインしていない）ものとして扱います。トークンはログに出しません。</remarks>
public sealed class SecretStoreCloudKitWebAuthTokenStore : ICloudKitWebAuthTokenStore
{
    /// <summary>既定の項目名です。</summary>
    public const string DefaultItemName = "Ryokugyoku.ProjectZD8.cloudkit/web-auth-token";

    /// <summary>秘密の保存先です。</summary>
    private readonly ISecretStore _store;

    /// <summary>項目名です。</summary>
    private readonly string _itemName;

    /// <summary>直近の値です（応答ごとに読み書きするため、保存先への読み込みを減らします）。</summary>
    private string? _cached;

    /// <summary>まだ保存先から読んでいないかどうかです。</summary>
    private bool _loaded;

    /// <summary>状態を守る鍵です。</summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// 保存先と項目名を注入して作ります。
    /// </summary>
    /// <remarks>責務: トークンの保存先を 1 件の項目に固定します。</remarks>
    /// <param name="store">秘密の保存先。</param>
    /// <param name="itemName">項目名。</param>
    public SecretStoreCloudKitWebAuthTokenStore(ISecretStore store, string itemName = DefaultItemName)
    {
        _store = store;
        _itemName = itemName;
    }

    /// <inheritdoc />
    public string? Load()
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                try
                {
                    _cached = _store.Read(_itemName);
                }
                catch (SecretStoreException exception)
                {
                    AuthLog.CloudKitTokenStoreDegraded(AuthLog.Logger, exception, "read");
                    _cached = null;
                }
                _loaded = true;
            }
            return string.IsNullOrEmpty(_cached) ? null : _cached;
        }
    }

    /// <inheritdoc />
    public void Save(string token)
    {
        lock (_lock)
        {
            _cached = token;
            _loaded = true;
            try
            {
                _store.Write(_itemName, token);
            }
            catch (SecretStoreException exception)
            {
                AuthLog.CloudKitTokenStoreDegraded(AuthLog.Logger, exception, "write");
                // 保存できなくても、この実行の間はメモリの値で続けます。
            }
        }
    }

    /// <inheritdoc />
    public void Remove()
    {
        lock (_lock)
        {
            _cached = null;
            _loaded = true;
            try
            {
                _store.Delete(_itemName);
            }
            catch (SecretStoreException exception)
            {
                AuthLog.CloudKitTokenStoreDegraded(AuthLog.Logger, exception, "delete");
                // 消せなくても、この実行の間はサインアウトした状態で続けます。
            }
        }
    }
}
