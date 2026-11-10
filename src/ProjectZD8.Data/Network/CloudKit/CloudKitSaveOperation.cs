namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// <c>records/modify</c> の保存の種類です。
/// </summary>
public enum CloudKitSaveOperation
{
    /// <summary><c>create</c>：新しく作る。同じ名前があれば失敗します。</summary>
    Create,

    /// <summary><c>update</c>：変更タグが一致するときだけ、指定したフィールドを変えます（iOS の既定の保存と同じく競合を検出）。</summary>
    Update,

    /// <summary><c>forceReplace</c>：変更タグを見ずにレコード全体を置き換えます。なければ作ります（指定しないフィールドは空になります）。</summary>
    ForceReplace,
}
