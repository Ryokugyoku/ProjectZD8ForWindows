using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>
/// 端末内の保存先にアプリのインストール識別子を保持します（iOS の <c>UserDefaultsInstallationIdentityStore</c>）。
/// </summary>
public sealed class LocalKeyValueInstallationIdentityStore
{
    /// <summary>インストール識別子を保存するキーです（iOS と同じ）。</summary>
    private const string IdentifierKey = "installation.identity.v1";

    /// <summary>インストール識別子の保存先です。</summary>
    private readonly ILocalKeyValueStore _defaults;

    /// <summary>新しい識別子を作る処理です（テストで差し替えます）。</summary>
    private readonly Func<Guid> _newIdentifier;

    /// <summary>
    /// 保存先を固定して作ります。
    /// </summary>
    /// <remarks>責務: 1 件の端末内保存先をインストール識別子の保存先へ結び付けます。</remarks>
    /// <param name="defaults">インストール識別子を保存する端末内保存先。</param>
    /// <param name="newIdentifier">新しい UUID を作る処理。<c>null</c> なら <see cref="Guid.NewGuid"/>。</param>
    public LocalKeyValueInstallationIdentityStore(ILocalKeyValueStore defaults, Func<Guid>? newIdentifier = null)
    {
        _defaults = defaults;
        _newIdentifier = newIdentifier ?? Guid.NewGuid;
    }

    /// <summary>
    /// 保存済みの識別子を復元し、未保存のときだけ新しく作ります。
    /// </summary>
    /// <remarks>責務: 現在のインストールを安定した識別子と指定の端末名へ変換します。</remarks>
    /// <param name="displayName">利用者が端末を見分ける表示名。</param>
    /// <returns>再起動後も同じ ID を使うインストール識別情報。</returns>
    public LocalInstallationIdentity Identity(string displayName)
    {
        if (_defaults.GetString(IdentifierKey) is { Length: > 0 } saved)
        {
            return new LocalInstallationIdentity(saved, displayName);
        }
        var generated = _newIdentifier().ToString("D").ToLowerInvariant();
        _defaults.SetString(IdentifierKey, generated);
        return new LocalInstallationIdentity(generated, displayName);
    }
}
