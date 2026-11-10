using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// session内の1回のpolling batchを識別します。
/// </summary>
public readonly record struct AcquisitionBatchIdentity
{
    /// <summary>
    /// session内で0から単調増加するbatch番号です。
    /// </summary>
    public long Ordinal { get; }

    /// <summary>
    /// 非負のbatch番号を固定します。
    /// </summary>
    /// <remarks>責務: 1件のsession内batch番号を再試行可能な不変identityへ変換します。</remarks>
    /// <param name="ordinal">session内で重複しない非負のbatch番号。</param>
    /// <exception cref="AcquisitionBatchEvidenceException">負数の場合は <c>AcquisitionBatchEvidenceError.InvalidBatchOrdinal</c>。</exception>
    public AcquisitionBatchIdentity(long ordinal)
    {
        if (ordinal < 0)
        {
            throw new AcquisitionBatchEvidenceException(AcquisitionBatchEvidenceError.InvalidBatchOrdinal);
        }

        Ordinal = ordinal;
    }
}
