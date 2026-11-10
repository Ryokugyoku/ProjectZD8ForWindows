using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】デモアダプターだけを見つけるアダプター探索です。
/// </summary>
/// <remarks>
/// iOS 版の macOS 構成（<c>DemoIncludedAdapterDiscovery</c> で包んだ USB・BLE 探索）の代わり。
/// デバイス担当の Windows 探索（シリアルポート・BLE）ができたら、それをデモ候補付きで包む形に差し替えます。
/// </remarks>
public sealed class InterimDemoAdapterDiscovery : IAdapterDiscoveryPort
{
    /// <summary>通信方式に合うデモ候補を返します。</summary>
    /// <remarks>責務: 指定方式のデモアダプター候補を返します。</remarks>
    /// <param name="mode">通信方式。</param>
    /// <returns>候補の一覧。</returns>
    public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode) =>
        Task.FromResult<IReadOnlyList<DiscoveredAdapter>>(
            mode == AdapterTransportMode.Usb
                ? InterimDemoOBDAdapter.UsbCandidates.ToList()
                : []);
}
