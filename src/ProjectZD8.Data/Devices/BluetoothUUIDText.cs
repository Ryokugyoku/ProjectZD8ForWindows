namespace ProjectZD8.Data.Devices;

/// <summary>Bluetooth の UUID を CoreBluetooth の <c>CBUUID.uuidString</c> と同じ表記へ変換します。</summary>
/// <remarks>
/// Windows の GATT API は常に128bitの <see cref="Guid"/> を返しますが、iOS 版の UART 構成と候補判定は
/// Bluetooth Base UUID 上の値を16bit（4桁）または32bit（8桁）の短縮形で比較します。そのための変換です。
/// </remarks>
public static class BluetoothUUIDText
{
    /// <summary>Bluetooth Base UUID の後半（<c>-0000-1000-8000-00805F9B34FB</c>）です。</summary>
    private const string BaseUUIDSuffix = "-0000-1000-8000-00805F9B34FB";

    /// <summary>UUID を大文字の短縮表記または完全表記へ変換します。</summary>
    /// <remarks>責務: 1件の Guid を CoreBluetooth と比較できる文字列へ変換します。</remarks>
    /// <param name="uuid">変換する UUID。</param>
    /// <returns>Base UUID 上の値なら4桁または8桁、それ以外はハイフン付きの36文字（すべて大文字）。</returns>
    public static string Format(Guid uuid)
    {
        var text = uuid.ToString("D").ToUpperInvariant();
        if (!text.EndsWith(BaseUUIDSuffix, StringComparison.Ordinal))
        {
            return text;
        }

        var prefix = text[..8];
        return prefix.StartsWith("0000", StringComparison.Ordinal) ? prefix[4..] : prefix;
    }
}
