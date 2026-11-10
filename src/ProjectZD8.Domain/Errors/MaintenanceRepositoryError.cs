namespace ProjectZD8.Domain.Errors;

/// <summary>
/// 整備記録の端末内保存を利用できない状態です。
/// </summary>
public enum MaintenanceRepositoryError
{
    /// <summary>
    /// 製品データベースを準備できませんでした。
    /// </summary>
    Unavailable,
}

/// <summary>
/// 整備記録の端末内保存の失敗を表す例外です。
/// </summary>
public sealed class MaintenanceRepositoryException : Exception
{
    /// <summary>
    /// 整備記録の端末内保存を利用できない状態です。
    /// </summary>
    public MaintenanceRepositoryError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件の整備記録保存の失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">整備記録保存の失敗理由。</param>
    public MaintenanceRepositoryException(MaintenanceRepositoryError reason)
        : base($"{nameof(MaintenanceRepositoryError)}.{reason}")
    {
        Reason = reason;
    }
}
