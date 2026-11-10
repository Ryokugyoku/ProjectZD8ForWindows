using System.Globalization;

namespace ProjectZD8.Data.Devices;

/// <summary>Bluetooth デバイスアドレス（48bit）とシステム識別子の文字列を相互に変換します。</summary>
/// <remarks>
/// Windows 版では Bluetooth Classic と BLE の両方で、アドレスを macOS の IOBluetooth と同じ
/// <c>00-04-3E-12-34-56</c>（大文字、ハイフン区切り）の形でシステム識別子に使います。
/// iOS 版の BLE はペリフェラル UUID を使いますが、Windows には同じ値がないためです。
/// </remarks>
public static class BluetoothAddressText
{
    /// <summary>アドレスを識別子の文字列へ変換します。</summary>
    /// <remarks>責務: 1件の48bitアドレスを安定した表示・保存用の文字列へ変換します。</remarks>
    /// <param name="address">Windows が返した Bluetooth アドレス。</param>
    /// <returns>大文字16進のハイフン区切り文字列。</returns>
    public static string Format(ulong address)
    {
        var parts = new string[6];
        for (var index = 0; index < 6; index++)
        {
            var value = (byte)((address >> (8 * (5 - index))) & 0xFF);
            parts[index] = value.ToString("X2", CultureInfo.InvariantCulture);
        }

        return string.Join('-', parts);
    }

    /// <summary>識別子の文字列をアドレスへ戻します。</summary>
    /// <remarks>責務: ハイフンまたはコロン区切り、あるいは区切りなし12桁の16進文字列を48bitアドレスへ変換します。</remarks>
    /// <param name="text">変換する文字列。</param>
    /// <param name="address">変換できたアドレス。</param>
    /// <returns>変換できた場合は <c>true</c>。</returns>
    public static bool TryParse(string text, out ulong address)
    {
        address = 0;
        var compact = text.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace(":", string.Empty, StringComparison.Ordinal);
        if (compact.Length != 12 || !compact.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        return ulong.TryParse(compact, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }
}
