using System.Threading.Tasks;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】Apple にサインインせず、端末だけで使う「ローカルモード」のアカウント認可です。いつも成功します。
/// </summary>
/// <remarks>
/// ネットワーク担当の Sign in with Apple（Web フロー）が入り、<c>CloudServiceConfiguration.IsConfigured</c> が true のときは、
/// そちらに差し替えます。設定がないときのローカルモードとして、この形のまま残す可能性もあります（リードが決める）。
/// </remarks>
public sealed class InterimLocalModeAccountAuthorization : IAppleAccountAuthorizationPort
{
    /// <summary>ローカルモードのアカウント識別子です。</summary>
    public const string LocalUserIdentifier = "local-windows-account";

    /// <summary>ローカルモードのアカウントを返します。</summary>
    /// <remarks>責務: 外部サービスを使わずに、固定のローカルアカウントで認可を完了します。</remarks>
    /// <returns>ローカルアカウントのセッション。</returns>
    public Task<AppleAccountSession> Authorize() => Task.FromResult(new AppleAccountSession(LocalUserIdentifier));

    /// <summary>ローカルアカウントの資格情報を常に有効として返します。</summary>
    /// <remarks>責務: ローカルアカウントは失効しないことを表します。</remarks>
    /// <param name="userIdentifier">アカウント識別子。</param>
    /// <returns>有効な状態。</returns>
    public Task<AppleAccountCredentialState> CredentialState(string userIdentifier) =>
        Task.FromResult(userIdentifier == LocalUserIdentifier
            ? AppleAccountCredentialState.Authorized
            : AppleAccountCredentialState.NotFound);
}
