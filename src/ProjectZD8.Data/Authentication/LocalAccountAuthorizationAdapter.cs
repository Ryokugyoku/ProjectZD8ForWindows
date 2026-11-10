using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Data.Persistence.Preferences;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// 設定がない（ローカルモード）ときに、Apple アカウントの代わりに端末だけの安定したアカウントでサインインさせます。
/// </summary>
/// <remarks>
/// <para>識別子は <c>local-&lt;UUID&gt;</c> で、最初のサインインで作って端末内に残し、以後は同じものを返します。
/// 車両・接続履歴・設定はこの識別子に結び付きます。Apple の識別子とは混ざらず、同期もしません。</para>
/// <para>後で設定を入れて Apple でサインインすると別のアカウントになります（ローカルのデータは自動では移りません）。</para>
/// </remarks>
public sealed class LocalAccountAuthorizationAdapter : IAppleAccountAuthorizationPort
{
    /// <summary>識別子の接頭辞です。</summary>
    public const string IdentifierPrefix = "local-";

    /// <summary>識別子を保存するキーです。</summary>
    public const string IdentifierKey = "authentication.local-account.v1";

    /// <summary>識別子の保存先です。</summary>
    private readonly ILocalKeyValueStore _defaults;

    /// <summary>新しい UUID を作る処理です。</summary>
    private readonly Func<Guid> _newIdentifier;

    /// <summary>
    /// 保存先を注入して作ります。
    /// </summary>
    /// <remarks>責務: ローカルアカウントの識別子の保存先を固定します。</remarks>
    /// <param name="defaults">端末内の保存先。</param>
    /// <param name="newIdentifier">新しい UUID を作る処理。<c>null</c> なら <see cref="Guid.NewGuid"/>。</param>
    public LocalAccountAuthorizationAdapter(ILocalKeyValueStore defaults, Func<Guid>? newIdentifier = null)
    {
        _defaults = defaults;
        _newIdentifier = newIdentifier ?? Guid.NewGuid;
    }

    /// <summary>
    /// 端末だけのアカウントのセッションを返します。
    /// </summary>
    /// <remarks>責務: 保存済みの識別子を返し、なければ作って保存します。</remarks>
    /// <returns>ローカルアカウントのセッション。</returns>
    public Task<AppleAccountSession> Authorize()
    {
        if (_defaults.GetString(IdentifierKey) is not { Length: > 0 } identifier)
        {
            identifier = IdentifierPrefix + _newIdentifier().ToString("D").ToLowerInvariant();
            _defaults.SetString(IdentifierKey, identifier);
        }
        return Task.FromResult(new AppleAccountSession(identifier));
    }

    /// <summary>
    /// ローカルアカウントの資格状態を返します。
    /// </summary>
    /// <remarks>責務: 保存済みの識別子と同じなら Authorized、違えば NotFound を返します。</remarks>
    /// <param name="userIdentifier">保存済みのユーザー識別子。</param>
    /// <returns>資格状態。</returns>
    public Task<AppleAccountCredentialState> CredentialState(string userIdentifier) =>
        Task.FromResult(
            string.Equals(_defaults.GetString(IdentifierKey), userIdentifier, StringComparison.Ordinal)
                ? AppleAccountCredentialState.Authorized
                : AppleAccountCredentialState.NotFound);
}
