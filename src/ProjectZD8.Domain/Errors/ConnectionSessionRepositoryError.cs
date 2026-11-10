namespace ProjectZD8.Domain.Errors;

/// <summary>
/// 接続セッション保存先が提供できない状態です。
/// </summary>
public enum ConnectionSessionRepositoryError
{
    /// <summary>
    /// 製品用の永続化境界を準備できませんでした。
    /// </summary>
    Unavailable,

    /// <summary>
    /// 指定操作を現在のセッション状態では実行できません。
    /// </summary>
    InvalidState,

    /// <summary>
    /// 同じ安定IDに異なるセッションまたはManifestが存在します。
    /// </summary>
    IntegrityConflict,
}

/// <summary>
/// 接続セッション保存先の失敗を表す例外です。
/// </summary>
public sealed class ConnectionSessionRepositoryException : Exception
{
    /// <summary>
    /// 接続セッション保存先が提供できない状態です。
    /// </summary>
    public ConnectionSessionRepositoryError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件の接続セッション保存先の失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">接続セッション保存先の失敗理由。</param>
    public ConnectionSessionRepositoryException(ConnectionSessionRepositoryError reason)
        : base($"{nameof(ConnectionSessionRepositoryError)}.{reason}")
    {
        Reason = reason;
    }
}
