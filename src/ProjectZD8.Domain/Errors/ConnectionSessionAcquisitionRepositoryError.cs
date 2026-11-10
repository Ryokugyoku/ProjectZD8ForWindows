namespace ProjectZD8.Domain.Errors;

/// <summary>
/// session取得evidence repositoryの排他的失敗理由です。
/// </summary>
public enum ConnectionSessionAcquisitionRepositoryError
{
    /// <summary>
    /// 同じsessionへ同じmanifestと境界eventの組が既に保存されています。
    /// </summary>
    Duplicate,

    /// <summary>
    /// 同じsessionへ異なるmanifestまたは同種の境界eventが既に保存されています。
    /// </summary>
    Conflict,

    /// <summary>
    /// 指定sessionの取得evidenceが登録されていません。
    /// </summary>
    NotFound,

    /// <summary>
    /// 開始eventがないsessionへ終了eventを追記しようとしました。
    /// </summary>
    StartEvidenceMissing,

    /// <summary>
    /// 終了時刻が保存済み開始時刻より前です。
    /// </summary>
    EndBeforeStart,

    /// <summary>
    /// 保存先を利用できません。
    /// </summary>
    Unavailable,
}

/// <summary>
/// session取得evidence repositoryの失敗を表す例外です。
/// </summary>
public sealed class ConnectionSessionAcquisitionRepositoryException : Exception
{
    /// <summary>
    /// session取得evidence repositoryの排他的失敗理由です。
    /// </summary>
    public ConnectionSessionAcquisitionRepositoryError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件のsession取得evidence repository失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">session取得evidence repositoryの失敗理由。</param>
    public ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError reason)
        : base($"{nameof(ConnectionSessionAcquisitionRepositoryError)}.{reason}")
    {
        Reason = reason;
    }
}
