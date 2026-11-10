using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices;

/// <summary>Bluetooth Low Energy の検出値をDomain候補へ変換します。</summary>
/// <remarks>
/// iOS 版の <c>AppleBluetoothAdvertisementMapper</c> に対応します。iOS ではペリフェラル UUID を識別子にしますが、
/// Windows 版では呼び出し側が <see cref="BluetoothAddressText.Format"/> で作ったアドレス文字列を渡します。
/// </remarks>
public sealed class BluetoothAdvertisementMapper
{
    /// <summary>1件の検出値をアダプター候補へ変換します。</summary>
    /// <remarks>責務: Advertisement Local Name、周辺機器名、識別子の優先順で1件のBLE候補を構築します。</remarks>
    /// <param name="peripheralIdentifier">周辺機器の安定識別子（Windows では Bluetooth アドレスの文字列）。</param>
    /// <param name="advertisementLocalName">アドバタイズメントから取得したローカル名称。</param>
    /// <param name="peripheralName">OS が保持している周辺機器名称。</param>
    /// <param name="connectionState">検出時点の周辺機器接続状態。</param>
    /// <param name="hasManufacturerData">アドバタイズメントにManufacturer Dataが含まれていたかどうか。</param>
    /// <param name="serviceIdentifiers">アドバタイズメントから取得したService UUID一覧。</param>
    /// <returns>Manufacturer Dataのバイト列を名称へ変換しないBluetooth候補。</returns>
    public DiscoveredAdapter MakeAdapter(
        string peripheralIdentifier,
        string? advertisementLocalName,
        string? peripheralName,
        DiscoveredAdapterConnectionState connectionState,
        bool hasManufacturerData,
        IReadOnlyList<string>? serviceIdentifiers = null)
    {
        var localName = Normalized(advertisementLocalName);
        var systemName = Normalized(peripheralName);
        var displayName = localName ?? systemName ?? peripheralIdentifier;

        return new DiscoveredAdapter(
            id: $"bluetooth-low-energy:{peripheralIdentifier}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: displayName,
            systemIdentifier: peripheralIdentifier,
            isConnected: connectionState == DiscoveredAdapterConnectionState.Connected,
            productName: systemName,
            advertisementLocalName: localName,
            hasManufacturerData: hasManufacturerData,
            bluetoothServiceIdentifiers: (serviceIdentifiers ?? [])
                .Select(identifier => identifier.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            connectionState: connectionState);
    }

    /// <summary>空白だけのシステム名称を取得不能として正規化します。</summary>
    /// <remarks>責務: 1件の任意名称から表示可能な非空文字列だけを返します。</remarks>
    /// <param name="value">OSから取得した任意名称。</param>
    /// <returns>前後の空白を除いた名称。空または取得不能の場合は <c>null</c>。</returns>
    private static string? Normalized(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
