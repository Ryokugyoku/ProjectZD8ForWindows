namespace ProjectZD8.Domain.Errors;

/// <summary>
/// PID変換式を安全に評価できなかった理由です。
/// </summary>
public abstract record OBDPIDFormulaError
{
    /// <summary>
    /// 派生ケース以外からの継承を防ぎます。
    /// </summary>
    /// <remarks>責務: ケースを入れ子の sealed record だけに限定します。</remarks>
    private OBDPIDFormulaError()
    {
    }

    /// <summary>
    /// 定義が要求する応答バイト数を満たしていません。
    /// </summary>
    /// <param name="Required">定義が要求する応答バイト数。</param>
    /// <param name="Actual">実際の応答バイト数。</param>
    public sealed record InsufficientBytes(int Required, int Actual) : OBDPIDFormulaError;

    /// <summary>
    /// 数式に許可されていない文字または構文があります。
    /// </summary>
    public sealed record InvalidExpression : OBDPIDFormulaError;

    /// <summary>
    /// 数式が実データに存在しないバイト変数を参照しています。
    /// </summary>
    /// <param name="Variable">参照されたバイト変数。</param>
    public sealed record UnavailableVariable(char Variable) : OBDPIDFormulaError;

    /// <summary>
    /// ゼロ除算が発生しました。
    /// </summary>
    public sealed record DivisionByZero : OBDPIDFormulaError;

    /// <summary>
    /// 計算結果が有限値になりませんでした。
    /// </summary>
    public sealed record NonFiniteResult : OBDPIDFormulaError;
}

/// <summary>
/// PID変換式を安全に評価できなかったことを表す例外です。
/// </summary>
public sealed class OBDPIDFormulaException : Exception
{
    /// <summary>
    /// PID変換式を安全に評価できなかった理由です。
    /// </summary>
    public OBDPIDFormulaError Reason { get; }

    /// <summary>
    /// 失敗理由を指定して例外を生成します。
    /// </summary>
    /// <remarks>責務: 1件のPID変換式評価の失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">PID変換式を評価できなかった理由。</param>
    public OBDPIDFormulaException(OBDPIDFormulaError reason)
        : base($"{nameof(OBDPIDFormulaError)}.{reason}")
    {
        Reason = reason;
    }
}
