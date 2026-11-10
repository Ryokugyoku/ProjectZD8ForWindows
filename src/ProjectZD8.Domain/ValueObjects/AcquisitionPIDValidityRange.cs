namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// PID数値の取得時有効範囲を表します。
/// </summary>
public sealed record AcquisitionPIDValidityRange
{
    /// <summary>
    /// 有効範囲の下限です。<c>null</c> は定義上の範囲なしを表します。
    /// </summary>
    public double? Minimum { get; }

    /// <summary>
    /// 有効範囲の上限です。<c>null</c> は定義上の範囲なしを表します。
    /// </summary>
    public double? Maximum { get; }

    /// <summary>
    /// 定義上の有効範囲が明示されていないことを示す値です。
    /// </summary>
    public static AcquisitionPIDValidityRange NotDeclared { get; } = new(null, null);

    /// <summary>
    /// 有限かつ非減少の両端から有効範囲を生成します。
    /// </summary>
    /// <remarks>責務: PID定義の有効範囲を比較可能な閉区間へ固定します。</remarks>
    /// <param name="minimum">有効範囲に含める有限な下限。</param>
    /// <param name="maximum">有効範囲に含める有限な上限。</param>
    /// <returns>両端を含む有効範囲。</returns>
    /// <exception cref="AcquisitionPIDValidityRangeException">非有限値または下限が上限を超える場合は <see cref="AcquisitionPIDValidityRangeError.InvalidBounds"/>。</exception>
    public static AcquisitionPIDValidityRange Inclusive(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || !(minimum <= maximum))
        {
            throw new AcquisitionPIDValidityRangeException(AcquisitionPIDValidityRangeError.InvalidBounds);
        }

        return new AcquisitionPIDValidityRange(minimum, maximum);
    }

    /// <summary>
    /// 検証済みの両端または範囲なしを保持します。
    /// </summary>
    /// <remarks>責務: 有効範囲の内部表現を両端の組へ固定します。</remarks>
    /// <param name="minimum">検証済み下限、または範囲なしを示す <c>null</c>。</param>
    /// <param name="maximum">検証済み上限、または範囲なしを示す <c>null</c>。</param>
    private AcquisitionPIDValidityRange(double? minimum, double? maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }
}

/// <summary>
/// PID有効範囲を確定できない理由です。
/// </summary>
public enum AcquisitionPIDValidityRangeError
{
    /// <summary>
    /// 有効範囲の両端が有限な非減少区間ではありません。
    /// </summary>
    InvalidBounds,
}

/// <summary>
/// PID有効範囲を確定できないことを表す例外です。
/// </summary>
public sealed class AcquisitionPIDValidityRangeException : Exception
{
    /// <summary>
    /// PID有効範囲を確定できない理由です。
    /// </summary>
    public AcquisitionPIDValidityRangeError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件のPID有効範囲失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">PID有効範囲を確定できない理由。</param>
    public AcquisitionPIDValidityRangeException(AcquisitionPIDValidityRangeError reason)
        : base($"{nameof(AcquisitionPIDValidityRangeError)}.{reason}")
    {
        Reason = reason;
    }
}
