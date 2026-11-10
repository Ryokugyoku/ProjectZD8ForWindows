using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices;

/// <summary>USB、Bluetooth Classic、Bluetooth Low Energy の候補を1つの探索境界へ統合します。</summary>
/// <remarks>
/// iOS 版の <c>MacOSAdapterDiscovery</c> に対応します。OS に依存しない統合規則だけなので Data に置き、
/// Windows の探索実装は組み立てが注入します。UI スレッドから呼ぶ前提です。
/// </remarks>
public sealed class CombinedAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>USBとBluetooth Classicを取得するシステム探索境界です。</summary>
    private readonly IAdapterDiscoveryPort systemDiscovery;

    /// <summary>BLE候補を取得する探索境界です。</summary>
    private readonly IAdapterDiscoveryPort lowEnergyDiscovery;

    /// <summary>システム探索とBLE探索を注入して生成します。</summary>
    /// <remarks>責務: 2種類の探索境界を接続方式別に利用できる状態へ固定します。</remarks>
    /// <param name="systemDiscovery">USBとBluetooth Classicを取得する探索境界。</param>
    /// <param name="lowEnergyDiscovery">BLE候補を取得する探索境界。</param>
    public CombinedAdapterDiscovery(IAdapterDiscoveryPort systemDiscovery, IAdapterDiscoveryPort lowEnergyDiscovery)
    {
        this.systemDiscovery = systemDiscovery;
        this.lowEnergyDiscovery = lowEnergyDiscovery;
    }

    /// <summary>USBはシステム探索へ渡し、BluetoothはClassicとBLEを並行取得します。</summary>
    /// <remarks>責務: 1回の探索要求を物理方式に対応する候補集合へ変換します。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>USB候補、または識別子で重複を除いたClassicとBLEの候補。</returns>
    /// <exception cref="AdapterDiscoveryException">Bluetooth探索を両方とも開始・完了できない場合は <c>BluetoothStateUnavailable</c>。</exception>
    public async Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode)
    {
        if (mode != AdapterTransportMode.Bluetooth)
        {
            return await systemDiscovery.DiscoverAdapters(mode);
        }

        var systemCandidates = Candidates(systemDiscovery, mode);
        var lowEnergyCandidates = Candidates(lowEnergyDiscovery, mode);
        var systemResult = await systemCandidates;
        var lowEnergyResult = await lowEnergyCandidates;

        if (systemResult is null && lowEnergyResult is null)
        {
            throw new AdapterDiscoveryException(AdapterDiscoveryError.BluetoothStateUnavailable);
        }

        return Unique([.. systemResult ?? [], .. lowEnergyResult ?? []]);
    }

    /// <summary>1つの探索境界を候補または利用不能へ正規化します。</summary>
    /// <remarks>責務: 一方のBluetooth方式の失敗を他方の成功から独立して扱える任意候補へ変換します。</remarks>
    /// <param name="discovery">候補を取得する探索境界。</param>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>探索に成功した候補。失敗した場合は <c>null</c>。</returns>
    private static async Task<IReadOnlyList<DiscoveredAdapter>?> Candidates(IAdapterDiscoveryPort discovery, AdapterTransportMode mode)
    {
        try
        {
            return await discovery.DiscoverAdapters(mode);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>複数方式から同じ識別子で届いた候補を先着優先で除外します。</summary>
    /// <remarks>責務: 1件の候補列を識別子ごとの初出候補へ縮約します。</remarks>
    /// <param name="adapters">重複除外前の候補列。</param>
    /// <returns>入力順を維持した重複なし候補列。</returns>
    private static IReadOnlyList<DiscoveredAdapter> Unique(IReadOnlyList<DiscoveredAdapter> adapters)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        return adapters.Where(adapter => identifiers.Add(adapter.Id)).ToArray();
    }
}
