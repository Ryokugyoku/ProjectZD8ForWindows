namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>認証セッション識別子の安全な保存境界で発生する失敗です。</summary>
public enum AuthenticationSessionStoreError
{
    /// <summary>セッション識別子の読込、保存、削除を完了できませんでした。</summary>
    Unavailable,
}

/// <summary>認証セッション識別子の安全な保存境界の失敗を表す例外です。</summary>
public sealed class AuthenticationSessionStoreException : Exception
{
    /// <summary>安全な保存境界で発生した失敗理由です。</summary>
    public AuthenticationSessionStoreError Reason { get; }

    /// <summary>失敗理由を指定して例外を生成します。</summary>
    /// <remarks>責務: 1件の認証セッション保存失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">安全な保存境界の失敗理由。</param>
    public AuthenticationSessionStoreException(AuthenticationSessionStoreError reason)
        : base($"{nameof(AuthenticationSessionStoreError)}.{reason}")
    {
        Reason = reason;
    }
}
