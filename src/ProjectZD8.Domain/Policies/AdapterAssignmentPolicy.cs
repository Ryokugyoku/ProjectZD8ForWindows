using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Policies;

/// <summary>物理アダプターを接続役割へ割り当てる際の競合規則です。</summary>
public static class AdapterAssignmentPolicy
{
    /// <summary>候補が割当先以外の接続役割で使用済みかを判定します。</summary>
    /// <remarks>責務: 1件の候補について同じ安定識別子を複数の接続役割へ割り当てる競合を検出します。</remarks>
    /// <param name="adapter">新たに割り当てるアダプター候補。</param>
    /// <param name="role">候補を割り当てる接続役割。</param>
    /// <param name="assignments">現在の接続役割別割当。</param>
    /// <returns>同じ候補が割り当て済みの別接続役割。競合しない場合は <see langword="null"/>。</returns>
    public static AdapterConnectionRole? ConflictingRole(
        DiscoveredAdapter adapter,
        AdapterConnectionRole role,
        IReadOnlyDictionary<AdapterConnectionRole, DiscoveredAdapter> assignments)
    {
        foreach (var assignment in assignments)
        {
            if (assignment.Key != role
                && string.Equals(assignment.Value.Id, adapter.Id, StringComparison.Ordinal))
            {
                return assignment.Key;
            }
        }
        return null;
    }
}
