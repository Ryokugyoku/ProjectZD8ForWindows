using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices;

/// <summary>実デバイス探索へ指定されたデモ候補一覧を追加します。</summary>
/// <remarks>UI スレッドから呼ぶ前提です。</remarks>
public sealed class DemoIncludedAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>OS由来候補を返す探索境界です。</summary>
    private readonly IAdapterDiscoveryPort wrapped;

    /// <summary>実探索結果へ追加するデモ候補一覧です。</summary>
    private readonly IReadOnlyList<DiscoveredAdapter> demoCandidates;

    /// <summary>実探索境界と追加対象デモ候補一覧を保持して生成します。</summary>
    /// <remarks>責務: 1件の実探索境界を指定接続方式のデモ候補一覧追加処理へ固定します。</remarks>
    /// <param name="wrapping">OS由来候補を取得する探索境界。</param>
    /// <param name="demoCandidates">一致する接続方式へ常時追加するデモ候補一覧。</param>
    public DemoIncludedAdapterDiscovery(IAdapterDiscoveryPort wrapping, IReadOnlyList<DiscoveredAdapter> demoCandidates)
    {
        wrapped = wrapping;
        this.demoCandidates = demoCandidates.ToArray();
    }

    /// <summary>一致する接続方式の探索へデモ候補を追加します。</summary>
    /// <remarks>責務: 1回の探索結果を指定接続方式だけデモ候補一覧付き結果へ変換します。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>OS由来候補と重複しないデモ候補一覧。実探索失敗時も一致方式ならデモ候補だけを返します。</returns>
    /// <exception cref="Exception">デモ対象外の接続方式で実探索境界が通知した探索エラー。</exception>
    public async Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode)
    {
        var matchingCandidates = demoCandidates.Where(candidate => candidate.TransportMode == mode).ToArray();
        if (matchingCandidates.Length == 0)
        {
            return await wrapped.DiscoverAdapters(mode);
        }

        try
        {
            var discovered = await wrapped.DiscoverAdapters(mode);
            var demoIDs = matchingCandidates.Select(candidate => candidate.Id).ToHashSet(StringComparer.Ordinal);
            return [.. discovered.Where(adapter => !demoIDs.Contains(adapter.Id)), .. matchingCandidates];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            OBDDiscoveryLog.SourceFailed(OBDDiscoveryLog.Logger, error, "live", mode.ToString());
            return matchingCandidates;
        }
    }
}
