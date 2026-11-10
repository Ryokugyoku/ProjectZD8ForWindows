namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>Appleアカウント認証境界が返す区別可能な失敗です。</summary>
public enum AppleAccountAuthorizationError
{
    /// <summary>ユーザーがシステム認証をキャンセルしました。</summary>
    Cancelled,

    /// <summary>認証UIまたはApple認証サービスを利用できません。</summary>
    Unavailable,

    /// <summary>Apple認証がその他の理由で失敗しました。</summary>
    Failed,
}

/// <summary>Appleアカウント認証境界の失敗を表す例外です。</summary>
public sealed class AppleAccountAuthorizationException : Exception
{
    /// <summary>Appleアカウント認証境界が返した失敗理由です。</summary>
    public AppleAccountAuthorizationError Reason { get; }

    /// <summary>失敗理由を指定して例外を生成します。</summary>
    /// <remarks>責務: 1件のApple認証失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">Appleアカウント認証境界の失敗理由。</param>
    public AppleAccountAuthorizationException(AppleAccountAuthorizationError reason)
        : base($"{nameof(AppleAccountAuthorizationError)}.{reason}")
    {
        Reason = reason;
    }
}
