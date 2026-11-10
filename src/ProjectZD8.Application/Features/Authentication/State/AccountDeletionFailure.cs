namespace ProjectZD8.Application.Authentication.State;

/// <summary>アカウント削除画面が区別して表示する失敗です。</summary>
public enum AccountDeletionFailure
{
    /// <summary>CloudKit、ローカルデータ、または保存済みログイン識別子を完全に削除できませんでした。</summary>
    DeletionFailed,
}
