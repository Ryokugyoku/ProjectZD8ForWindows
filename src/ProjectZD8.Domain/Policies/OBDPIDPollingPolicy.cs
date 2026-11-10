using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Policies;

/// <summary>リアルタイムPID更新の優先順位と間引き周期を決定します。</summary>
public sealed class OBDPIDPollingPolicy
{
    /// <summary>高頻度更新する一般用途PIDです。</summary>
    private static readonly IReadOnlySet<OBDPIDRequest> HighPriorityRequests = new HashSet<OBDPIDRequest>
    {
        new(0x01, 0x04),
        new(0x01, 0x0C),
        new(0x01, 0x0D),
        new(0x01, 0x11),
    };

    /// <summary>対応済みPIDから現在tickで照会する定義を選びます。</summary>
    /// <remarks>責務: 高優先PIDを毎tick、その他を8tickごとに含む読取順へ変換します。</remarks>
    /// <param name="definitions">初回探索で応答が確認できたPID定義。</param>
    /// <param name="tick">0から単調増加する更新番号。</param>
    /// <returns>高優先順、Service/PID順に整列した今回の読取対象。</returns>
    public IReadOnlyList<OBDPIDDefinition> DefinitionsToPoll(IReadOnlyList<OBDPIDDefinition> definitions, ulong tick)
    {
        var high = definitions.Where(d => HighPriorityRequests.Contains(RequestOf(d))).ToList();
        var normal = definitions.Where(d => !HighPriorityRequests.Contains(RequestOf(d))).ToList();
        if (tick % 8 == 0 || high.Count == 0) { return high.Concat(normal).ToList(); }
        return high;
    }

    /// <summary>PID定義から読取要求を復元します。</summary>
    /// <remarks>責務: この定義が表すService/PID要求を返します（Swift の private extension の <c>request</c>）。</remarks>
    /// <param name="definition">要求を復元するPID定義。</param>
    /// <returns>定義のService/PID要求。</returns>
    private static OBDPIDRequest RequestOf(OBDPIDDefinition definition) =>
        new(definition.Service, definition.Pid);
}
