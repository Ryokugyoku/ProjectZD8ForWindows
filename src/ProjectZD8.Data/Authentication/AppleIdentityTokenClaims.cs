namespace ProjectZD8.Data.Authentication;

/// <summary>
/// 検証済みの ID トークンの中身です。
/// </summary>
/// <param name="Subject">Apple のユーザー識別子（sub。iOS の <c>credential.user</c> と同じ値）。</param>
/// <param name="ExpiresAt">有効期限。</param>
/// <param name="Email">メールアドレス（スコープを要求しないので通常はありません）。</param>
public sealed record AppleIdentityTokenClaims(string Subject, DateTimeOffset ExpiresAt, string? Email);
