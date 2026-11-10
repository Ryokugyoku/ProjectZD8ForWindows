namespace ProjectZD8.Application.Authentication.State;

/// <summary>アカウント削除確認と実行の進行段階です。</summary>
public enum AccountDeletionPhase
{
    /// <summary>削除確認を表示していない通常状態です。</summary>
    Idle,

    /// <summary>削除が取り消せないことを最初に警告している状態です。</summary>
    Warning,

    /// <summary>削除対象事項と最終削除操作を表示している状態です。</summary>
    Reviewing,

    /// <summary>アカウントデータを削除している状態です。</summary>
    Deleting,

    /// <summary>削除を完了できず同じ画面から再試行できる状態です。</summary>
    Failed,
}
