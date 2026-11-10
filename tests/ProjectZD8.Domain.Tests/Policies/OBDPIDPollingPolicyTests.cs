using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// PID更新方針の高優先周期と全件周期を検証します。
/// </summary>
public sealed class OBDPIDPollingPolicyTests
{
    /// <summary>
    /// 高優先tickでは回転数と車速だけを選びます。
    /// </summary>
    [Fact]
    public void TestNormalTickSelectsHighPriorityDefinitions()
    {
        var selected = new OBDPIDPollingPolicy().DefinitionsToPoll(Definitions, tick: 1);

        Assert.Equal(new byte[] { 0x0C, 0x0D }, selected.Select(d => d.Pid));
    }

    /// <summary>
    /// 8tickごとに低優先PIDも読み直します。
    /// </summary>
    [Fact]
    public void TestEighthTickIncludesAllSupportedDefinitions()
    {
        var selected = new OBDPIDPollingPolicy().DefinitionsToPoll(Definitions, tick: 8);

        Assert.Equal(new byte[] { 0x0C, 0x0D, 0x05 }, selected.Select(d => d.Pid));
    }

    /// <summary>
    /// 優先度検証に使う3件のPID定義です。
    /// </summary>
    private static IReadOnlyList<OBDPIDDefinition> Definitions =>
        new[] { Definition(0x05), Definition(0x0C), Definition(0x0D) };

    /// <summary>
    /// 指定PIDの最小テスト定義を生成します。
    /// </summary>
    /// <param name="pid">生成するService 01 PID番号。</param>
    /// <returns>恒等式を持つテスト定義。</returns>
    private static OBDPIDDefinition Definition(byte pid) =>
        new(
            service: 0x01,
            pid: pid,
            nameKey: "test",
            requiredByteCount: 1,
            formula: "A",
            unit: "",
            minimumValue: null,
            maximumValue: null,
            sourceURI: "test://polling",
            revision: 1);
}
