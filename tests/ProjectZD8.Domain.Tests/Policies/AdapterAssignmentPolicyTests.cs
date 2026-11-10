using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// アダプター接続役割の二重割当規則を検証します。
/// </summary>
public sealed class AdapterAssignmentPolicyTests
{
    /// <summary>
    /// 同じ安定識別子を別役割へ割り当てる競合を検出することを検証します。
    /// </summary>
    [Fact]
    public void TestConflictingRoleFindsSameAdapterAssignedToAnotherRole()
    {
        var adapter = MakeAdapter("shared");

        var result = AdapterAssignmentPolicy.ConflictingRole(
            adapter,
            AdapterConnectionRole.Secondary,
            new Dictionary<AdapterConnectionRole, DiscoveredAdapter> { [AdapterConnectionRole.Primary] = adapter });

        Assert.Equal(AdapterConnectionRole.Primary, result);
    }

    /// <summary>
    /// 異なる物理アダプターの割当を競合としないことを検証します。
    /// </summary>
    [Fact]
    public void TestConflictingRoleAllowsDifferentPhysicalAdapter()
    {
        var primary = MakeAdapter("primary");
        var secondary = MakeAdapter("secondary");

        var result = AdapterAssignmentPolicy.ConflictingRole(
            secondary,
            AdapterConnectionRole.Secondary,
            new Dictionary<AdapterConnectionRole, DiscoveredAdapter> { [AdapterConnectionRole.Primary] = primary });

        Assert.Null(result);
    }

    /// <summary>
    /// 指定識別子を持つテスト用アダプター候補を生成します。
    /// </summary>
    /// <param name="id">候補の安定識別子。</param>
    /// <returns>未接続のBluetooth候補。</returns>
    private static DiscoveredAdapter MakeAdapter(string id) =>
        new(
            id: id,
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: id,
            systemIdentifier: id,
            isConnected: false);
}
