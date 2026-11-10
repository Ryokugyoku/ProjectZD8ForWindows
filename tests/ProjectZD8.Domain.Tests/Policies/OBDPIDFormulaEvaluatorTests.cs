using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Policies;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// PID定義式の構文、バイト境界、算術失敗を検証します。
/// </summary>
public sealed class OBDPIDFormulaEvaluatorTests
{
    /// <summary>
    /// 冷却水温の1バイトoffset式を評価します。
    /// </summary>
    [Fact]
    public void TestEvaluatesCoolantTemperatureFormula()
    {
        Assert.Equal(83, new OBDPIDFormulaEvaluator().Evaluate(MakeDefinition(1, "A - 40"), new byte[] { 0x7B }));
    }

    /// <summary>
    /// 回転数の2バイト式と演算優先順位を評価します。
    /// </summary>
    [Fact]
    public void TestEvaluatesEngineSpeedFormula()
    {
        Assert.Equal(
            1726,
            new OBDPIDFormulaEvaluator().Evaluate(MakeDefinition(2, "(A * 256 + B) / 4"), new byte[] { 0x1A, 0xF8 }));
    }

    /// <summary>
    /// 必要バイト数未満の応答を拒否します。
    /// </summary>
    [Fact]
    public void TestRejectsInsufficientBytes()
    {
        var error = Assert.Throws<OBDPIDFormulaException>(
            () => new OBDPIDFormulaEvaluator().Evaluate(MakeDefinition(2, "A + B"), new byte[] { 1 }));
        Assert.Equal(new OBDPIDFormulaError.InsufficientBytes(Required: 2, Actual: 1), error.Reason);
    }

    /// <summary>
    /// 存在しない変数参照を拒否します。
    /// </summary>
    [Fact]
    public void TestRejectsUnavailableVariable()
    {
        var error = Assert.Throws<OBDPIDFormulaException>(
            () => new OBDPIDFormulaEvaluator().Evaluate(MakeDefinition(1, "B"), new byte[] { 1 }));
        Assert.Equal(new OBDPIDFormulaError.UnavailableVariable('B'), error.Reason);
    }

    /// <summary>
    /// ゼロ除算を拒否します。
    /// </summary>
    [Fact]
    public void TestRejectsDivisionByZero()
    {
        var error = Assert.Throws<OBDPIDFormulaException>(
            () => new OBDPIDFormulaEvaluator().Evaluate(MakeDefinition(1, "1 / A"), new byte[] { 0 }));
        Assert.Equal(new OBDPIDFormulaError.DivisionByZero(), error.Reason);
    }

    /// <summary>
    /// 任意関数や識別子を許可しません。
    /// </summary>
    [Fact]
    public void TestRejectsUnsupportedSyntax()
    {
        var error = Assert.Throws<OBDPIDFormulaException>(
            () => new OBDPIDFormulaEvaluator().Evaluate(MakeDefinition(1, "sqrt(A)"), new byte[] { 4 }));
        Assert.Equal(new OBDPIDFormulaError.InvalidExpression(), error.Reason);
    }

    /// <summary>
    /// テスト対象の式だけを差し替えた定義を生成します。
    /// </summary>
    /// <param name="bytes">必要バイト数。</param>
    /// <param name="formula">評価対象式。</param>
    /// <returns>Service 01 PID 05相当のテスト定義。</returns>
    private static OBDPIDDefinition MakeDefinition(int bytes, string formula) =>
        new(
            service: 1,
            pid: 5,
            nameKey: "test",
            requiredByteCount: bytes,
            formula: formula,
            unit: "u",
            minimumValue: null,
            maximumValue: null,
            sourceURI: "test://source",
            revision: 1);
}
