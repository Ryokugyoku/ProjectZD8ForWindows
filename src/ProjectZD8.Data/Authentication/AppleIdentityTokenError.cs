namespace ProjectZD8.Data.Authentication;

/// <summary>
/// ID トークンの検証に失敗した理由です。
/// </summary>
public enum AppleIdentityTokenError
{
    /// <summary>JWT の形が正しくない。</summary>
    Malformed,

    /// <summary>署名の方式が RS256 ではない。</summary>
    UnsupportedAlgorithm,

    /// <summary>署名の鍵が Apple の公開鍵にない。</summary>
    UnknownKey,

    /// <summary>署名が正しくない。</summary>
    InvalidSignature,

    /// <summary>発行者（iss）が Apple ではない。</summary>
    InvalidIssuer,

    /// <summary>対象（aud）がこのアプリの Services ID ではない。</summary>
    InvalidAudience,

    /// <summary>有効期限（exp）が過ぎているか、発行日時（iat）が未来すぎる。</summary>
    Expired,

    /// <summary>nonce が要求と一致しない。</summary>
    NonceMismatch,

    /// <summary>ユーザー識別子（sub）がない。</summary>
    MissingSubject,
}

/// <summary>
/// ID トークンの検証に失敗したときの例外です。
/// </summary>
public sealed class AppleIdentityTokenException : Exception
{
    /// <summary>
    /// 理由から作ります。
    /// </summary>
    /// <remarks>責務: 検証の失敗の理由を保持します。</remarks>
    /// <param name="reason">理由。</param>
    public AppleIdentityTokenException(AppleIdentityTokenError reason)
        : base($"{nameof(AppleIdentityTokenError)}.{reason}")
    {
        Reason = reason;
    }

    /// <summary>理由。</summary>
    public AppleIdentityTokenError Reason { get; }
}
