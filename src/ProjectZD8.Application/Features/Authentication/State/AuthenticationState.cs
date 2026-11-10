namespace ProjectZD8.Application.Authentication.State;

/// <summary>認証入口とルート画面が描画するApplication状態です。</summary>
public sealed record AuthenticationState
{
    /// <summary>現在の認証進行段階です。</summary>
    public AuthenticationPhase Phase { get; init; }

    /// <summary>設定などのアカウントスコープへ渡す現在の認証済みセッションです。</summary>
    public AppleAccountSession? Session { get; init; }

    /// <summary>免責事項の確認画面を表示するかどうかです。</summary>
    public bool IsDisclaimerPresented { get; init; }

    /// <summary>ユーザーへ再試行を案内する直近の失敗です。</summary>
    public AuthenticationFailure? Failure { get; init; }

    /// <summary>アカウント削除確認と実行の現在段階です。</summary>
    public AccountDeletionPhase AccountDeletionPhase { get; init; }

    /// <summary>アカウント削除を完了できなかった直近の理由です。</summary>
    public AccountDeletionFailure? AccountDeletionFailure { get; init; }

    /// <summary>認証入口の初期状態を生成します。</summary>
    /// <remarks>責務: 認証入口の進行段階と対応するAppleセッションを一貫した初期状態へ設定します。</remarks>
    /// <param name="phase">初期の認証進行段階。</param>
    /// <param name="session">初期状態で認証済みとするAppleセッション。</param>
    /// <param name="isDisclaimerPresented">初期表示で免責事項を提示するかどうか。</param>
    /// <param name="failure">初期表示で案内する認証失敗。</param>
    /// <param name="accountDeletionPhase">アカウント削除確認の初期段階。</param>
    /// <param name="accountDeletionFailure">初期表示で案内する削除失敗。</param>
    public AuthenticationState(
        AuthenticationPhase phase = AuthenticationPhase.CheckingSession,
        AppleAccountSession? session = null,
        bool isDisclaimerPresented = false,
        AuthenticationFailure? failure = null,
        AccountDeletionPhase accountDeletionPhase = AccountDeletionPhase.Idle,
        AccountDeletionFailure? accountDeletionFailure = null)
    {
        Phase = phase;
        Session = session;
        IsDisclaimerPresented = isDisclaimerPresented;
        Failure = failure;
        AccountDeletionPhase = accountDeletionPhase;
        AccountDeletionFailure = accountDeletionFailure;
    }
}
