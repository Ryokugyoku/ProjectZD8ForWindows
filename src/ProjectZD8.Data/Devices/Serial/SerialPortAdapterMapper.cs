using System.Globalization;
using System.Text.RegularExpressions;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.Serial;

/// <summary>Windows が報告したシリアル（COM）ポートの情報をUSB候補へ変換します。</summary>
/// <remarks>
/// iOS 版の <c>MacOSSystemAdapterDiscovery.makeUSBAdapter</c> の規則に対応します。
/// macOS と同じく USB 由来のポートだけを候補にし、Bluetooth の仮想COMポート（<c>BTHENUM</c>）は除きます。
/// ペアリング済みの SPP 機器は Bluetooth Classic の探索で RFCOMM として一覧に出すため、二重に並べないためです。
/// </remarks>
public sealed partial class SerialPortAdapterMapper
{
    /// <summary>1件のCOMポートを表示可能なUSB候補へ変換します。</summary>
    /// <remarks>責務: ポート名、デバイスID、名称から1件のアダプター候補を構築します。</remarks>
    /// <param name="portName">COMポート名（例: <c>COM3</c>）。</param>
    /// <param name="deviceIdentifier">デバイスインスタンスIDまたはインターフェイスのパス。</param>
    /// <param name="manufacturer">OSから取得できたメーカー名称。</param>
    /// <param name="product">OSから取得できた製品名称（バスが報告した名称、なければフレンドリ名）。</param>
    /// <returns>USBシリアルとして確認できた候補。それ以外は <c>null</c>。</returns>
    public DiscoveredAdapter? MakeAdapter(
        string? portName,
        string? deviceIdentifier,
        string? manufacturer,
        string? product)
    {
        var systemIdentifier = portName?.Trim();
        if (string.IsNullOrEmpty(systemIdentifier)
            || !systemIdentifier.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var identifier = deviceIdentifier ?? string.Empty;
        if (identifier.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var vendorValue = HexValue(VendorPattern().Match(identifier));
        var productValue = HexValue(ProductPattern().Match(identifier));
        if (vendorValue is null && !identifier.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new DiscoveredAdapter(
            id: $"usb:{systemIdentifier}",
            transportMode: AdapterTransportMode.Usb,
            displayName: DisplayName(manufacturer, product, systemIdentifier),
            systemIdentifier: systemIdentifier,
            isConnected: true,
            manufacturerName: manufacturer,
            productName: product,
            serialNumber: SerialNumber(identifier),
            vendorIdentifier: HexadecimalIdentifier(vendorValue),
            productIdentifier: HexadecimalIdentifier(productValue));
    }

    /// <summary>「Ports」クラスのデバイスのフレンドリ名からCOMポート名を取り出します。</summary>
    /// <remarks>責務: <c>USB Serial Port (COM3)</c> のような名前の末尾の括弧からポート名を読み取ります。</remarks>
    /// <param name="friendlyName">デバイスのフレンドリ名。</param>
    /// <returns>ポート名。含まれない場合は <c>null</c>。</returns>
    public static string? PortNameFromFriendlyName(string? friendlyName)
    {
        if (friendlyName is null)
        {
            return null;
        }

        var match = FriendlyNamePortPattern().Match(friendlyName);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>候補から外した理由を返します（診断ログ用）。</summary>
    /// <remarks>責務: <see cref="MakeAdapter"/> と同じ順で判定し、外した理由を短い固定の文字列にします。</remarks>
    /// <param name="portName">COMポート名。</param>
    /// <param name="deviceIdentifier">デバイスインスタンスIDまたはインターフェイスのパス。</param>
    /// <returns><c>not-com-port</c> / <c>bluetooth-virtual-port</c> / <c>not-usb</c>。候補になる場合は <c>usb-serial</c>。</returns>
    internal static string DiagnosticReason(string? portName, string? deviceIdentifier)
    {
        var systemIdentifier = portName?.Trim();
        if (string.IsNullOrEmpty(systemIdentifier)
            || !systemIdentifier.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return "not-com-port";
        }

        var identifier = deviceIdentifier ?? string.Empty;
        if (identifier.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
        {
            return "bluetooth-virtual-port";
        }

        return VendorPattern().Match(identifier).Success || identifier.Contains("USB", StringComparison.OrdinalIgnoreCase)
            ? "usb-serial"
            : "not-usb";
    }

    /// <summary>フレンドリ名の末尾の <c>(COMn)</c> を探す正規表現です。</summary>
    /// <returns>ポート名を1番目のグループに持つ正規表現。</returns>
    [GeneratedRegex(@"\((COM\d+)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FriendlyNamePortPattern();

    /// <summary>USBのベンダーIDを探す正規表現です。</summary>
    /// <returns>4桁16進の <c>VID_xxxx</c> に一致する正規表現。</returns>
    [GeneratedRegex("VID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VendorPattern();

    /// <summary>USBのプロダクトIDを探す正規表現です。</summary>
    /// <returns>4桁16進の <c>PID_xxxx</c> に一致する正規表現。</returns>
    [GeneratedRegex("PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductPattern();

    /// <summary>正規表現の一致から16進値を取り出します。</summary>
    /// <remarks>責務: 1件の一致を数値の識別子へ変換します。</remarks>
    /// <param name="match">VIDまたはPIDの一致。</param>
    /// <returns>一致した値。一致しない場合は <c>null</c>。</returns>
    private static ulong? HexValue(Match match) =>
        match.Success
            ? ulong.Parse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
            : null;

    /// <summary>数値をUSB識別子向けの固定幅16進表記へ変換します。</summary>
    /// <remarks>責務: 任意のUSB数値識別子を一貫した16進文字列へ整形します。</remarks>
    /// <param name="value">変換対象の数値。</param>
    /// <returns><c>0x</c> 接頭辞付きの4桁以上の16進文字列。値がない場合は <c>null</c>。</returns>
    private static string? HexadecimalIdentifier(ulong? value) =>
        value is { } number ? "0x" + number.ToString("X4", CultureInfo.InvariantCulture) : null;

    /// <summary>USBデバイスIDからシリアル番号を取り出します。</summary>
    /// <remarks>責務: <c>USB\VID_xxxx&amp;PID_yyyy\SERIAL</c> 形式の3番目の要素を、OS が採番した値でない場合だけ返します。</remarks>
    /// <param name="identifier">デバイスインスタンスIDまたはインターフェイスのパス。</param>
    /// <returns>シリアル番号。取得できない場合は <c>null</c>。</returns>
    private static string? SerialNumber(string identifier)
    {
        var segments = identifier.Split(['\\', '#'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!segments[index].Contains("VID_", StringComparison.OrdinalIgnoreCase)
                || segments[index].Contains('+', StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = segments[index + 1];
            return candidate.Contains('&', StringComparison.Ordinal) || candidate.StartsWith('{') ? null : candidate;
        }

        return null;
    }

    /// <summary>メーカー名と製品名から一覧表示名を決定します。</summary>
    /// <remarks>責務: システムから取得できた最も判別しやすい名称を1件の表示名へまとめます。</remarks>
    /// <param name="manufacturer">システムから取得できたメーカー名称。</param>
    /// <param name="product">システムから取得できた製品名称。</param>
    /// <param name="fallback">名称を取得できなかった場合の代替識別子。</param>
    /// <returns>空白を除去した名称または代替識別子。</returns>
    private static string DisplayName(string? manufacturer, string? product, string fallback)
    {
        var components = new[] { manufacturer, product }
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .ToArray();
        return components.Length == 0 ? fallback : string.Join(" · ", components);
    }
}
