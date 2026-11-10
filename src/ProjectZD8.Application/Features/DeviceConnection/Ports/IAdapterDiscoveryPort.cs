using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.Ports;

/// <summary>アダプター候補をシステムのデバイス情報から取得する境界です。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public interface IAdapterDiscoveryPort
{
    /// <summary>指定された接続方式で現在参照できるアダプター候補を取得します。</summary>
    /// <remarks>責務: 1種類の接続方式に対応する現在のアダプター候補一覧を返します。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>システムから取得できたアダプター候補。</returns>
    /// <exception cref="Exception">システムのデバイス情報へアクセスできない場合の探索エラー。</exception>
    Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode);
}
