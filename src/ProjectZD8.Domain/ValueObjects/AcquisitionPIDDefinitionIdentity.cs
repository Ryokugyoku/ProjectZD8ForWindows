namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 取得時PID定義の式を再構築可能な値として固定します。
/// </summary>
public sealed record AcquisitionPIDDefinitionIdentity
{
    /// <summary>
    /// 式文字列の解釈規則を示す明示的な契約versionです。
    /// </summary>
    public int CanonicalizationVersion { get; }

    /// <summary>
    /// 取得時定義に保存されていた制限付き式の完全な値です。
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// canonicalization契約と完全な式を固定します。
    /// </summary>
    /// <remarks>責務: digestへ意味を委ねずに取得時の変換式を再構築可能なidentityとして保持します。</remarks>
    /// <param name="canonicalizationVersion">式文字列の比較規則を示す正のversion。</param>
    /// <param name="expression">取得時定義に保存されていた空でない制限付き式。</param>
    /// <exception cref="AcquisitionPIDDefinitionIdentityException">versionが正でない場合または式が空の場合は <see cref="AcquisitionPIDDefinitionIdentityError.InvalidValue"/>。</exception>
    public AcquisitionPIDDefinitionIdentity(int canonicalizationVersion, string expression)
    {
        if (canonicalizationVersion <= 0 || string.IsNullOrEmpty(expression))
        {
            throw new AcquisitionPIDDefinitionIdentityException(AcquisitionPIDDefinitionIdentityError.InvalidValue);
        }

        CanonicalizationVersion = canonicalizationVersion;
        Expression = expression;
    }
}

/// <summary>
/// PID定義identityを確定できない理由です。
/// </summary>
public enum AcquisitionPIDDefinitionIdentityError
{
    /// <summary>
    /// versionまたは式がidentityの前提を満たしません。
    /// </summary>
    InvalidValue,
}

/// <summary>
/// PID定義identityを確定できないことを表す例外です。
/// </summary>
public sealed class AcquisitionPIDDefinitionIdentityException : Exception
{
    /// <summary>
    /// PID定義identityを確定できない理由です。
    /// </summary>
    public AcquisitionPIDDefinitionIdentityError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件のPID定義identity失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">PID定義identityを確定できない理由。</param>
    public AcquisitionPIDDefinitionIdentityException(AcquisitionPIDDefinitionIdentityError reason)
        : base($"{nameof(AcquisitionPIDDefinitionIdentityError)}.{reason}")
    {
        Reason = reason;
    }
}
