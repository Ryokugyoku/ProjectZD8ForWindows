namespace ProjectZD8.Application.Authentication.State;

/// <summary>保存済みAppleユーザー識別子に対する現在の資格状態です。</summary>
public enum AppleAccountCredentialState
{
    /// <summary>Appleが現在もアプリの利用を許可しています。</summary>
    Authorized,

    /// <summary>ユーザーがアプリへのApple認証許可を取り消しています。</summary>
    Revoked,

    /// <summary>Apple側に対応する資格情報がありません。</summary>
    NotFound,

    /// <summary>アプリ移管に伴うユーザー識別子移行が必要です。</summary>
    Transferred,
}
