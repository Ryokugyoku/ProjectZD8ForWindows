using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices;

/// <summary>BLE探索結果を既知のOBDアダプター信号へ限定する判定規則です。</summary>
/// <remarks>iOS 版の <c>AppleOBDBluetoothCandidatePolicy</c> に対応します（OS に依存しないため名前から Apple を外しています）。</remarks>
public sealed class OBDBluetoothCandidatePolicy
{
    /// <summary>公式製品資料または採用済み参照実装で確認したBLE表示名の接頭辞です。</summary>
    internal static readonly IReadOnlyList<string> KnownNamePrefixes =
    [
        "OBDLINK",
        "VEEPEAK",
        "VGATE",
        "IOS-VLINK",
    ];

    /// <summary>通信実装が明示的に扱う既知UART Service UUIDです。</summary>
    private static readonly HashSet<string> KnownServiceIdentifiers =
        BluetoothUARTProfile.Supported.Select(profile => profile.ServiceUUID).ToHashSet(StringComparer.Ordinal);

    /// <summary>1件のBLE候補が既知のOBD表示名またはUART Service UUIDを持つか判定します。</summary>
    /// <remarks>責務: 1件のBLE探索候補をユーザーへ提示可能な既知OBD候補かどうかへ分類します。</remarks>
    /// <param name="adapter">BLE広告から構築した候補。</param>
    /// <returns>既知名称または既知UART Service UUIDに一致する場合は <c>true</c>。</returns>
    public bool Accepts(DiscoveredAdapter adapter)
    {
        string?[] names = [adapter.AdvertisementLocalName, adapter.ProductName, adapter.DisplayName];
        if (names.OfType<string>().Any(IsKnownName))
        {
            return true;
        }

        return adapter.BluetoothServiceIdentifiers
            .Select(identifier => identifier.ToUpperInvariant())
            .Any(KnownServiceIdentifiers.Contains);
    }

    /// <summary>1件の表示名が既知OBD製品の名称接頭辞を持つか判定します。</summary>
    /// <remarks>責務: 1件の任意表示名を大文字化して既知OBD名称集合と照合します。</remarks>
    /// <param name="name">BLE広告または機器から取得した表示名。</param>
    /// <returns>既知名称接頭辞に一致する場合は <c>true</c>。</returns>
    internal static bool IsKnownName(string name)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return KnownNamePrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
    }
}
