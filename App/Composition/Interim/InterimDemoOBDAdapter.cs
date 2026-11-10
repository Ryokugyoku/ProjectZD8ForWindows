using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】iOS 版の <c>DemoOBDAdapter</c> を写した、実機なしで画面を確かめるためのデモ用アダプターの定義です。
/// </summary>
/// <remarks>デバイス担当が Data 層にデモアダプターを移したら、そちらに差し替えます。識別子は iOS 版と同じです。</remarks>
public static class InterimDemoOBDAdapter
{
    /// <summary>USB デモの識別子です。</summary>
    private static readonly string[] UsbSystemIdentifiers =
    [
        "projectzd8://demo-usb",
        "projectzd8://demo-usb-2",
        "projectzd8://demo-usb-3",
    ];

    /// <summary>Bluetooth デモの識別子です。</summary>
    private static readonly string[] BluetoothSystemIdentifiers =
    [
        "projectzd8://demo-bluetooth",
        "projectzd8://demo-bluetooth-2",
        "projectzd8://demo-bluetooth-3",
    ];

    /// <summary>USB デモの候補です。</summary>
    public static IReadOnlyList<DiscoveredAdapter> UsbCandidates { get; } = UsbSystemIdentifiers
        .Select((systemIdentifier, index) => new DiscoveredAdapter(
            id: index == 0 ? "usb:projectzd8-demo" : $"usb:projectzd8-demo-{index + 1}",
            transportMode: AdapterTransportMode.Usb,
            displayName: index == 0 ? "DEMO USB" : $"DEMO USB {index + 1}",
            systemIdentifier: systemIdentifier,
            isConnected: true,
            manufacturerName: "RevTorque Insight",
            productName: "Demo OBD Adapter",
            serialNumber: string.Format(CultureInfo.InvariantCulture, "ZD8-DEMO-{0:D4}", index + 1),
            vendorIdentifier: "DEMO",
            productIdentifier: "USB"))
        .ToList();

    /// <summary>Bluetooth デモの候補です。</summary>
    public static IReadOnlyList<DiscoveredAdapter> BluetoothCandidates { get; } = BluetoothSystemIdentifiers
        .Select((systemIdentifier, index) => new DiscoveredAdapter(
            id: index == 0 ? "bluetooth-low-energy:projectzd8-demo" : $"bluetooth-low-energy:projectzd8-demo-{index + 1}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: index == 0 ? "DEMO Bluetooth" : $"DEMO Bluetooth {index + 1}",
            systemIdentifier: systemIdentifier,
            isConnected: true,
            manufacturerName: "RevTorque Insight",
            productName: "Demo BLE OBD Adapter",
            advertisementLocalName: index == 0 ? "DEMO Bluetooth" : $"DEMO Bluetooth {index + 1}",
            hasManufacturerData: false,
            serialNumber: string.Format(CultureInfo.InvariantCulture, "ZD8-DEMO-BLE-{0:D4}", index + 1)))
        .ToList();

    /// <summary>接続先がデモアダプターかどうかを返します。</summary>
    /// <remarks>責務: 接続先の識別子がデモの識別子と一致するかを判定します。</remarks>
    /// <param name="endpoint">接続先。</param>
    /// <returns>デモなら true。</returns>
    public static bool Matches(OBDConnectionEndpoint endpoint) =>
        UsbSystemIdentifiers.Concat(BluetoothSystemIdentifiers).Contains(endpoint.SystemIdentifier, StringComparer.Ordinal);

    /// <summary>デモ用の合成 VIN を返します。</summary>
    /// <remarks>責務: デモアダプターの並び順から、決まった形の合成 VIN を作ります。</remarks>
    /// <param name="endpoint">接続先。</param>
    /// <returns>合成 VIN。</returns>
    public static string SyntheticVIN(OBDConnectionEndpoint endpoint)
    {
        var index = Array.IndexOf(UsbSystemIdentifiers.Concat(BluetoothSystemIdentifiers).ToArray(), endpoint.SystemIdentifier);
        return index < 0
            ? "TESTZD8CXR0000000"
            : "TESTZD8CXR" + (index + 1).ToString("D7", CultureInfo.InvariantCulture);
    }
}
