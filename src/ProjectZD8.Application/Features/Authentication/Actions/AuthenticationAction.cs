namespace ProjectZD8.Application.Authentication.Actions;

/// <summary>認証入口からApplicationへ通知できるユーザー操作を表します。</summary>
public enum AuthenticationAction
{
    /// <summary>認証入口が表示され、保存済みセッションの確認を要求しました。</summary>
    Appeared,

    /// <summary>ユーザーがログイン手続きの開始を要求しました。</summary>
    LoginTapped,

    /// <summary>ユーザーが免責事項を閉じました。</summary>
    DisclaimerDismissed,

    /// <summary>ユーザーが免責事項への同意とApple認証の開始を要求しました。</summary>
    DisclaimerAccepted,

    /// <summary>ユーザーが失敗後の再試行を要求しました。</summary>
    RetryTapped,

    /// <summary>ユーザーが設定画面からアカウント削除確認を開始しました。</summary>
    AccountDeletionRequested,

    /// <summary>ユーザーが最初の警告を確認して削除事項の表示を要求しました。</summary>
    AccountDeletionWarningConfirmed,

    /// <summary>ユーザーがアカウント削除確認を取り消しました。</summary>
    AccountDeletionCancelled,

    /// <summary>ユーザーが削除事項を確認して最終削除を要求しました。</summary>
    AccountDeletionConfirmed,
}
