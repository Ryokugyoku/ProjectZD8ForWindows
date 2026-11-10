namespace ProjectZD8.Application.Authentication.State;

/// <summary>ログイン画面で区別して案内する認証失敗です。</summary>
public enum AuthenticationFailure
{
    /// <summary>他端末からのログアウトを安全な保存領域へ確定できませんでした。</summary>
    RemoteLogoutPersistenceFailed,

    /// <summary>保存済みセッションの確認を完了できませんでした。</summary>
    SessionCheckFailed,

    /// <summary>Apple認証サービスを現在利用できません。</summary>
    ServiceUnavailable,

    /// <summary>Apple認証を完了できませんでした。</summary>
    SignInFailed,
}
