namespace ProjectZD8.Data.Diagnostics;

/// <summary>アプリversion由来の取得runtime evidenceを生成できない理由です。</summary>
/// <remarks>iOS版 <c>BundleConnectionSessionAcquisitionEvidenceError</c> に相当します。</remarks>
public enum AssemblyConnectionSessionAcquisitionEvidenceError
{
    /// <summary>marketing versionまたはbuild versionが存在しません。</summary>
    VersionUnavailable,
}

/// <summary><see cref="AssemblyConnectionSessionAcquisitionEvidenceError"/> を包む例外です。</summary>
public sealed class AssemblyConnectionSessionAcquisitionEvidenceException : Exception
{
    /// <summary>理由を指定して生成します。</summary>
    /// <remarks>責務: エラー理由を例外へ包みます。</remarks>
    /// <param name="reason">エラー理由。</param>
    public AssemblyConnectionSessionAcquisitionEvidenceException(AssemblyConnectionSessionAcquisitionEvidenceError reason)
        : base($"{nameof(AssemblyConnectionSessionAcquisitionEvidenceError)}.{reason}")
    {
        Reason = reason;
    }

    /// <summary>エラー理由です。</summary>
    public AssemblyConnectionSessionAcquisitionEvidenceError Reason { get; }
}
