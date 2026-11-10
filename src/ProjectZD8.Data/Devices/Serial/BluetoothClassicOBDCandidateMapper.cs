using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.Serial;

/// <summary>ペアリング済みBluetooth Classic機器の情報をOBD候補へ変換します。</summary>
/// <remarks>
/// iOS 版の macOS 実装（<c>MacOSOBDLinkMXPlusBluetoothScanner</c>）は OBDLink MX+ だけを候補にします。
/// Windows 版では、Serial Port Profile（RFCOMM）を公開しているペアリング済み機器のうち、
/// MX+ に加えて既知のOBD名称（BLE と同じ接頭辞、または名前に「OBD」「ELM327」を含むもの）も候補にします。
/// 汎用のELM327互換 Classic アダプターが Windows では一般的なためです（iOS との違い）。
/// </remarks>
public sealed class BluetoothClassicOBDCandidateMapper
{
    /// <summary>OBDLink MX+名称とペアリング完了を検証する方針です。</summary>
    private readonly OBDLinkMXPlusDiscoveryPolicy mxPlusPolicy = new();

    /// <summary>1件のBluetooth Classic機器を候補へ変換します。</summary>
    /// <remarks>責務: 名称、ペアリング、SPP公開の有無から1件のClassic候補を構築します。</remarks>
    /// <param name="address">機器のアドレス文字列（<see cref="BluetoothAddressText.Format"/> の形）。</param>
    /// <param name="name">OSが返した機器名称。空の場合はアドレスを名称に使います。</param>
    /// <param name="isPaired">ペアリング済みかどうか。</param>
    /// <param name="hasSerialPortService">RFCOMM の Serial Port サービスを公開しているかどうか。</param>
    /// <param name="isConnected">検出時点で接続済みかどうか。</param>
    /// <returns>OBD候補として扱える場合は候補、それ以外は <c>null</c>。</returns>
    public DiscoveredAdapter? MakeAdapter(
        string address,
        string? name,
        bool isPaired,
        bool hasSerialPortService,
        bool isConnected)
    {
        var normalizedAddress = Normalized(address);
        var normalizedName = Normalized(name) ?? normalizedAddress;
        if (normalizedAddress is null || normalizedName is null || !isPaired || !hasSerialPortService)
        {
            return null;
        }

        var isMXPlus = mxPlusPolicy.Accepts(normalizedName, isPaired);
        if (!isMXPlus && !IsKnownClassicOBDName(normalizedName))
        {
            return null;
        }

        return new DiscoveredAdapter(
            id: $"bluetooth-classic:{normalizedAddress}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: normalizedName,
            systemIdentifier: normalizedAddress,
            isConnected: isConnected,
            connectionTransport: OBDConnectionEndpointTransport.BluetoothClassic,
            manufacturerName: isMXPlus ? "OBD Solutions LLC" : null,
            productName: isMXPlus ? "OBDLink MX+" : null);
    }

    /// <summary>候補をアドレス順に並べ、同じアドレスの重複を除きます。</summary>
    /// <remarks>責務: macOS 実装と同じくシステム識別子の昇順で安定した一覧を返します。</remarks>
    /// <param name="adapters">並べ替える候補。</param>
    /// <returns>アドレス順の重複なし候補。</returns>
    public IReadOnlyList<DiscoveredAdapter> Sorted(IEnumerable<DiscoveredAdapter> adapters) =>
        adapters
            .GroupBy(adapter => adapter.SystemIdentifier, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(adapter => adapter.SystemIdentifier, StringComparer.Ordinal)
            .ToArray();

    /// <summary>MX+以外で Classic のOBD候補として扱う名称かを返します。</summary>
    /// <remarks>責務: 既知製品の接頭辞と汎用ELM327互換機の名称を照合します。</remarks>
    /// <param name="name">前後の空白を除いた名称。</param>
    /// <returns>候補として扱う場合は <c>true</c>。</returns>
    private static bool IsKnownClassicOBDName(string name)
    {
        var upper = name.ToUpperInvariant();
        return OBDBluetoothCandidatePolicy.IsKnownName(name)
            || upper.Contains("OBD", StringComparison.Ordinal)
            || upper.Contains("ELM327", StringComparison.Ordinal);
    }

    /// <summary>空白だけの文字列を除外します。</summary>
    /// <remarks>責務: 1件の任意文字列を空でない前後空白除去済み値へ変換します。</remarks>
    /// <param name="value">OSから取得した任意文字列。</param>
    /// <returns>空でない正規化値。値がない場合は <c>null</c>。</returns>
    private static string? Normalized(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
