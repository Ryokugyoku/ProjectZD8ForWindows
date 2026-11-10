namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// アダプター探索で利用する物理接続方式を表します。
/// </summary>
public enum AdapterTransportMode
{
    /// <summary>
    /// USB接続されたデバイスを対象にします。
    /// </summary>
    Usb,

    /// <summary>
    /// macOSから参照できるBluetoothデバイスを対象にします。
    /// </summary>
    Bluetooth,
}

/// <summary>
/// <see cref="AdapterTransportMode"/> の raw value と全ケースを扱います。
/// </summary>
public static class AdapterTransportModeRawValue
{
    /// <summary>
    /// 宣言順の全ケースです。
    /// </summary>
    public static IReadOnlyList<AdapterTransportMode> AllCases { get; } =
        [AdapterTransportMode.Usb, AdapterTransportMode.Bluetooth];

    /// <summary>
    /// 接続方式を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の接続方式を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する接続方式。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this AdapterTransportMode value) => value switch
    {
        AdapterTransportMode.Usb => "usb",
        AdapterTransportMode.Bluetooth => "bluetooth",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から接続方式を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して接続方式へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた接続方式。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out AdapterTransportMode value)
    {
        switch (rawValue)
        {
            case "usb": value = AdapterTransportMode.Usb; return true;
            case "bluetooth": value = AdapterTransportMode.Bluetooth; return true;
            default: value = default; return false;
        }
    }

    /// <summary>
    /// 接続方式を一意に識別する安定識別子を返します。
    /// </summary>
    /// <remarks>責務: <c>Identifiable</c> の <c>id</c> に相当する raw value を返します。</remarks>
    /// <param name="value">識別子を求める接続方式。</param>
    /// <returns>raw value と同じ安定識別子。</returns>
    public static string Id(this AdapterTransportMode value) => value.ToRawValue();
}
