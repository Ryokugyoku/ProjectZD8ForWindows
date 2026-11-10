namespace ProjectZD8.Data.Configuration;

/// <summary>
/// Sign in with Apple（と、それに頼る iCloud 同期）を今使えるかどうかです。
/// </summary>
public enum AppleSignInAvailability
{
    /// <summary>機能フラグが無効。画面では「近日対応（Coming Soon）」と出し、ローカルモードで動く。</summary>
    ComingSoon,

    /// <summary>機能フラグは有効だが、Services ID・リダイレクト先・API トークンなどの設定が足りない。Coming Soon と同じに扱う。</summary>
    MissingSettings,

    /// <summary>機能フラグが有効で、設定もそろっている。</summary>
    Active,
}
