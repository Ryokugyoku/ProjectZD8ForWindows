using ProjectZD8.Diagnostics;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Settings.Ports;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// Sign in with Apple に成功したとき、この PC のローカルモードで貯めたデータを、サインインした Apple のアカウントへ 1 回だけ引き継ぎます。
/// </summary>
/// <remarks>
/// ローカルモードのアカウント識別子（<see cref="LocalAccountAuthorizationAdapter"/> が作る <c>local-…</c>）と Apple の sub は別なので、
/// そのままではサインイン後にローカルモードの車両・走行ログ・整備記録・設定が見えなくなります。
/// 引き継ぎの規則:
/// <list type="bullet">
/// <item>対象は、この PC で最初に Apple でサインインしたアカウントだけ（2 つ目以降の Apple アカウントには引き継がない）。</item>
/// <item>車両・写真・接続セッション（生ログと取得証跡を含む）・整備記録の持ち主を付け替える。設定は、引き継ぎ先にまだ設定がなければ写す。</item>
/// <item>引き継いだあとは、いつもの同期（車両カタログ・整備カタログ・セッションのアップロード）で iCloud に上がる。</item>
/// <item>失敗したら印を付けず、次のサインインでやり直す（付け替えは 1 回のトランザクションなので中途半端には残らない）。</item>
/// <item>サインインそのものは、引き継ぎに失敗しても成功として返す。</item>
/// </list>
/// 保存済みのセッションの復元（<see cref="CredentialState"/>）では引き継がない。
/// </remarks>
public sealed class LocalAccountDataAdoptingAuthorizationPort : IAppleAccountAuthorizationPort
{
    /// <summary>引き継ぎ済みの Apple アカウントを記録するキーです。</summary>
    public const string AdoptedByKey = "authentication.local-account.adopted-by.v1";

    /// <summary>実際の Apple の認可です。</summary>
    private readonly IAppleAccountAuthorizationPort _inner;

    /// <summary>ローカルモードの識別子と引き継ぎの印を持つ端末内の値です。</summary>
    private readonly ILocalKeyValueStore _defaults;

    /// <summary>端末内のデータの持ち主を付け替える処理です。</summary>
    private readonly Func<string, string, AccountDataReassignmentResult> _reassign;

    /// <summary>アカウントごとの設定の保存先です。</summary>
    private readonly IAccountSettingsStorePort _settings;

    /// <summary>
    /// 包む認可と、引き継ぎに使う保存先を受け取ります。
    /// </summary>
    /// <remarks>責務: 引き継ぎの依存を保持します。</remarks>
    /// <param name="inner">実際の Apple の認可（Web 版の Sign in with Apple）。</param>
    /// <param name="defaults">ローカルモードの識別子を持つ端末内の値。</param>
    /// <param name="reassign">データの持ち主を (元, 先) で付け替える処理（<see cref="SqliteAccountDataReassignment.Reassign"/>）。</param>
    /// <param name="settings">アカウントごとの設定の保存先。</param>
    public LocalAccountDataAdoptingAuthorizationPort(
        IAppleAccountAuthorizationPort inner,
        ILocalKeyValueStore defaults,
        Func<string, string, AccountDataReassignmentResult> reassign,
        IAccountSettingsStorePort settings)
    {
        _inner = inner;
        _defaults = defaults;
        _reassign = reassign;
        _settings = settings;
    }

    /// <summary>
    /// Apple でサインインし、まだならローカルモードのデータを引き継ぎます。
    /// </summary>
    /// <remarks>責務: 包んだ認可の結果を返す前に、条件を満たせば 1 回だけ引き継ぎを行います。</remarks>
    /// <returns>サインインしたアカウント。</returns>
    public async Task<AppleAccountSession> Authorize()
    {
        var session = await _inner.Authorize().ConfigureAwait(true);
        AdoptLocalDataIfNeeded(session.UserIdentifier);
        return session;
    }

    /// <inheritdoc />
    public Task<AppleAccountCredentialState> CredentialState(string userIdentifier) =>
        _inner.CredentialState(userIdentifier);

    /// <summary>
    /// ローカルモードのデータを、条件を満たせば Apple のアカウントへ引き継ぎます。
    /// </summary>
    /// <remarks>責務: 引き継ぎの条件を確かめ、付け替え・設定の写し・印の記録を行います。失敗は記録して次回に回します。</remarks>
    /// <param name="appleAccountIdentifier">サインインした Apple のアカウント（sub）。</param>
    /// <returns>引き継いだなら true。</returns>
    public bool AdoptLocalDataIfNeeded(string appleAccountIdentifier)
    {
        var localAccountIdentifier = _defaults.GetString(LocalAccountAuthorizationAdapter.IdentifierKey);
        if (string.IsNullOrEmpty(localAccountIdentifier)
            || string.IsNullOrEmpty(appleAccountIdentifier)
            || string.Equals(localAccountIdentifier, appleAccountIdentifier, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(_defaults.GetString(AdoptedByKey)))
        {
            return false;
        }

        try
        {
            _reassign(localAccountIdentifier, appleAccountIdentifier);
            if (_settings.Load(appleAccountIdentifier) is null && _settings.Load(localAccountIdentifier) is { } localSettings)
            {
                _settings.Save(localSettings, appleAccountIdentifier);
            }

            _defaults.SetString(AdoptedByKey, appleAccountIdentifier);
            AuthLog.LocalDataAdopted(AuthLog.Logger, Redact.AccountId(localAccountIdentifier), Redact.AccountId(appleAccountIdentifier));
            return true;
        }
        catch (Exception exception)
        {
            AuthLog.LocalDataAdoptFailed(AuthLog.Logger, exception, Redact.AccountId(localAccountIdentifier), Redact.AccountId(appleAccountIdentifier));
            return false;
        }
    }
}
