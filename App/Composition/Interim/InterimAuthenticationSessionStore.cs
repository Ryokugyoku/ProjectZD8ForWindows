using ProjectZD8.Application.Authentication.Ports;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】サインイン済みのアカウント識別子をメモリだけに持つ保存先です。アプリを閉じると忘れます。
/// </summary>
/// <remarks>iOS 版の <c>KeychainAuthenticationSessionStore</c> の代わり。ネットワーク担当の資格情報保存（Windows.Security.Credentials）ができたら差し替えます。</remarks>
public sealed class InterimAuthenticationSessionStore : IAuthenticationSessionStorePort
{
    /// <summary>保存済みの識別子です。</summary>
    private string? _userIdentifier;

    /// <summary>保存済みの識別子を返します。</summary>
    /// <remarks>責務: メモリ上の識別子を返します。</remarks>
    /// <returns>識別子。なければ null。</returns>
    public string? LoadUserIdentifier() => _userIdentifier;

    /// <summary>識別子を保存します。</summary>
    /// <remarks>責務: サインインしたアカウントの識別子をメモリへ置きます。</remarks>
    /// <param name="userIdentifier">識別子。</param>
    public void SaveUserIdentifier(string userIdentifier) => _userIdentifier = userIdentifier;

    /// <summary>識別子を消します。</summary>
    /// <remarks>責務: ログアウト・アカウント削除時に識別子を消します。</remarks>
    public void RemoveUserIdentifier() => _userIdentifier = null;
}
