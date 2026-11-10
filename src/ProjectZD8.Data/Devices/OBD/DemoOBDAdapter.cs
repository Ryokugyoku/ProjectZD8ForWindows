using System.Globalization;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>物理通信を行わない製品内デモOBDアダプターを定義します。</summary>
public static class DemoOBDAdapter
{
    /// <summary>デモUSB終端を識別する予約済みシステム識別子一覧です。</summary>
    private static readonly string[] UsbSystemIdentifiers =
    [
        "projectzd8://demo-usb",
        "projectzd8://demo-usb-2",
        "projectzd8://demo-usb-3",
    ];

    /// <summary>デモBluetooth終端を識別する予約済みシステム識別子一覧です。</summary>
    private static readonly string[] BluetoothSystemIdentifiers =
    [
        "projectzd8://demo-bluetooth",
        "projectzd8://demo-bluetooth-2",
        "projectzd8://demo-bluetooth-3",
    ];

    /// <summary>USB探索結果へ常時追加する3件のデモ候補です。</summary>
    public static IReadOnlyList<DiscoveredAdapter> UsbCandidates { get; } = UsbSystemIdentifiers
        .Select((systemIdentifier, index) => new DiscoveredAdapter(
            id: index == 0 ? "usb:projectzd8-demo" : $"usb:projectzd8-demo-{index + 1}",
            transportMode: AdapterTransportMode.Usb,
            displayName: index == 0 ? "DEMO USB" : $"DEMO USB {index + 1}",
            systemIdentifier: systemIdentifier,
            isConnected: true,
            manufacturerName: "RevTorque Insight",
            productName: "Demo OBD Adapter",
            serialNumber: string.Create(CultureInfo.InvariantCulture, $"ZD8-DEMO-{index + 1:D4}"),
            vendorIdentifier: "DEMO",
            productIdentifier: "USB"))
        .ToArray();

    /// <summary>Bluetooth探索結果へ常時追加する3件のデモ候補です。</summary>
    public static IReadOnlyList<DiscoveredAdapter> BluetoothCandidates { get; } = BluetoothSystemIdentifiers
        .Select((systemIdentifier, index) => new DiscoveredAdapter(
            id: index == 0
                ? "bluetooth-low-energy:projectzd8-demo"
                : $"bluetooth-low-energy:projectzd8-demo-{index + 1}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: index == 0 ? "DEMO Bluetooth" : $"DEMO Bluetooth {index + 1}",
            systemIdentifier: systemIdentifier,
            isConnected: true,
            manufacturerName: "RevTorque Insight",
            productName: "Demo BLE OBD Adapter",
            advertisementLocalName: index == 0 ? "DEMO Bluetooth" : $"DEMO Bluetooth {index + 1}",
            hasManufacturerData: false,
            serialNumber: string.Create(CultureInfo.InvariantCulture, $"ZD8-DEMO-BLE-{index + 1:D4}")))
        .ToArray();

    /// <summary>既存参照との互換性を保つ先頭USBデモ候補です。</summary>
    public static DiscoveredAdapter Candidate => UsbCandidates[0];

    /// <summary>既存参照との互換性を保つ先頭Bluetoothデモ候補です。</summary>
    public static DiscoveredAdapter BluetoothCandidate => BluetoothCandidates[0];

    /// <summary>指定終端が製品内デモ通信を表すか判定します。</summary>
    /// <remarks>責務: 1件のOBD終端を予約済みデモ識別子と照合します。</remarks>
    /// <param name="endpoint">判定するOBD接続終端。</param>
    /// <returns>デモ終端と一致する場合は <c>true</c>。</returns>
    public static bool Matches(OBDConnectionEndpoint endpoint) =>
        AllIdentifiers().Contains(endpoint.SystemIdentifier, StringComparer.Ordinal);

    /// <summary>デモ終端から再現可能な合成VINを生成します。</summary>
    /// <remarks>責務: 1件のデモ終端を重複しない固定VINへ変換します。</remarks>
    /// <param name="endpoint">合成VINを採番するデモ終端。</param>
    /// <returns>17文字のデモVIN。</returns>
    public static string SyntheticVIN(OBDConnectionEndpoint endpoint)
    {
        if (EnumeratedPrefixIndex(endpoint) is not { } index)
        {
            return "TESTZD8CXR0000000";
        }

        return "TESTZD8CXR" + index.ToString("D7", CultureInfo.InvariantCulture);
    }

    /// <summary>1件のデモ終端を17文字VINの連番へ収束します。</summary>
    /// <remarks>責務: 1件のデモ候補をシステム識別子順の固定番号へ変換します。</remarks>
    /// <param name="endpoint">変換対象のOBD終端。</param>
    /// <returns>1から6までのシリアル番号。見つからない場合は <c>null</c>。</returns>
    private static int? EnumeratedPrefixIndex(OBDConnectionEndpoint endpoint)
    {
        var index = Array.IndexOf(AllIdentifiers(), endpoint.SystemIdentifier);
        return index < 0 ? null : index + 1;
    }

    /// <summary>USBとBluetoothの予約済み識別子をこの順で連結します。</summary>
    /// <remarks>責務: デモ識別子の照合と採番に使う一覧を返します。</remarks>
    /// <returns>USB、Bluetoothの順の識別子一覧。</returns>
    private static string[] AllIdentifiers() => [.. UsbSystemIdentifiers, .. BluetoothSystemIdentifiers];
}
