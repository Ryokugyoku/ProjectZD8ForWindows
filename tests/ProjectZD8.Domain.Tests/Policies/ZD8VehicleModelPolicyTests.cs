using ProjectZD8.Domain.Policies;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// ZD8型式の国内識別子と海外VIN照合を検証します。
/// </summary>
public sealed class ZD8VehicleModelPolicyTests
{
    /// <summary>
    /// 国内型式接頭辞とJF1ZD VINだけを受理します。
    /// </summary>
    [Fact]
    public void TestMatchesOnlyZD8Identifiers()
    {
        var policy = new ZD8VehicleModelPolicy();

        Assert.True(policy.Matches("ZD8-012345"));
        Assert.True(policy.Matches("JF1ZD8ABCDEF12345"));
        Assert.False(policy.Matches("ZN8-012345"));
        Assert.False(policy.Matches("JF1ZC6ABCDEF12345"));
        Assert.False(policy.Matches((string?)null));
    }
}
