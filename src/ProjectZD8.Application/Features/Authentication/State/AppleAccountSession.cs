namespace ProjectZD8.Application.Authentication.State;

/// <summary>Appleアカウント認証から得たアプリ固有セッション識別情報です。</summary>
public sealed record AppleAccountSession
{
    /// <summary>Appleがこのアプリへ割り当てた安定ユーザー識別子です。</summary>
    public string UserIdentifier { get; }

    /// <summary>検証済みのAppleユーザー識別子からセッションを生成します。</summary>
    /// <remarks>責務: Appleユーザー識別子をアプリ内セッション表現へ固定します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てた空でないユーザー識別子。</param>
    public AppleAccountSession(string userIdentifier)
    {
        UserIdentifier = userIdentifier;
    }
}
