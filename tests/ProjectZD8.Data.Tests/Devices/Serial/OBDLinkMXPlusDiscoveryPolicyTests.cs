using ProjectZD8.Data.Devices.Serial;

namespace ProjectZD8.Data.Tests.Devices.Serial;

/// <summary>OBDLink MX+探索候補を名称とペアリング状態で保守的に限定できることを検証します。</summary>
public sealed class OBDLinkMXPlusDiscoveryPolicyTests
{
    /// <summary>ペアリング済みの公式名称をMX+候補として受け入れます。</summary>
    /// <remarks>責務: 基本のMX+名称がペアリング完了後だけ候補になることを確認します。</remarks>
    [Fact]
    public void AcceptsPairedOfficialName()
    {
        Assert.True(new OBDLinkMXPlusDiscoveryPolicy().Accepts("OBDLink MX+", isPaired: true));
    }

    /// <summary>製品名の後ろに機器固有接尾辞がある名称も受け入れます。</summary>
    /// <remarks>責務: MX+名称接頭辞と機器固有接尾辞を持つペアリング済み候補を確認します。</remarks>
    [Fact]
    public void AcceptsPairedNameWithDeviceSuffix()
    {
        Assert.True(new OBDLinkMXPlusDiscoveryPolicy().Accepts("OBDLink MX+ 48318", isPaired: true));
    }

    /// <summary>正しい名称でも未ペアリングなら候補へ追加しません。</summary>
    /// <remarks>責務: 接続前提を満たさないMX+を選択可能状態へ進めないことを確認します。</remarks>
    [Fact]
    public void RejectsUnpairedMXPlus()
    {
        Assert.False(new OBDLinkMXPlusDiscoveryPolicy().Accepts("OBDLink MX+", isPaired: false));
    }

    /// <summary>ペアリング済みでも他製品はMX+候補へ追加しません。</summary>
    /// <remarks>責務: OBDLinkの別モデルや名称の似た機器をMX+と誤認しないことを確認します。</remarks>
    [Fact]
    public void RejectsOtherBluetoothProducts()
    {
        var policy = new OBDLinkMXPlusDiscoveryPolicy();

        Assert.False(policy.Accepts("OBDLink CX", isPaired: true));
        Assert.False(policy.Accepts("OBDLink MX", isPaired: true));
        Assert.False(policy.Accepts("OBDLink MX+Clone", isPaired: true));
    }
}
