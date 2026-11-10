namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// OBDアダプターまでの物理通信方式です。
/// </summary>
/// <remarks>
/// Swift 版では <c>OBDConnectionEndpoint.Transport</c> という入れ子型です。
/// C# では入れ子型とプロパティ <see cref="OBDConnectionEndpoint.Transport"/> が同名にできないため、トップレベルの型にしています。
/// </remarks>
public enum OBDConnectionEndpointTransport
{
    /// <summary>
    /// Bluetooth Low EnergyのUARTサービスを使用します。
    /// </summary>
    BluetoothLowEnergy,

    /// <summary>
    /// Bluetooth ClassicのSerial Port Profileを使用します。
    /// </summary>
    BluetoothClassic,

    /// <summary>
    /// macOSから公開されたシリアルデバイスを使用します。
    /// </summary>
    Serial,
}

/// <summary>
/// <see cref="OBDConnectionEndpointTransport"/> の raw value と振る舞いを扱います。
/// </summary>
public static class OBDConnectionEndpointTransportRawValue
{
    /// <summary>
    /// 物理通信方式を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の物理通信方式を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する物理通信方式。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this OBDConnectionEndpointTransport value) => value switch
    {
        OBDConnectionEndpointTransport.BluetoothLowEnergy => "bluetoothLowEnergy",
        OBDConnectionEndpointTransport.BluetoothClassic => "bluetoothClassic",
        OBDConnectionEndpointTransport.Serial => "serial",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から物理通信方式を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して物理通信方式へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた物理通信方式。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out OBDConnectionEndpointTransport value)
    {
        switch (rawValue)
        {
            case "bluetoothLowEnergy": value = OBDConnectionEndpointTransport.BluetoothLowEnergy; return true;
            case "bluetoothClassic": value = OBDConnectionEndpointTransport.BluetoothClassic; return true;
            case "serial": value = OBDConnectionEndpointTransport.Serial; return true;
            default: value = default; return false;
        }
    }

    /// <summary>
    /// ELM/STNの連続バイトストリームを提供できる終端かどうかを返します。
    /// </summary>
    /// <remarks>責務: 物理通信方式がELM/STNのバイトストリーム通信に対応するかを判定します。</remarks>
    /// <param name="value">判定する物理通信方式。</param>
    /// <returns>連続バイトストリームを提供できる場合は <c>true</c>。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static bool SupportsELMByteStream(this OBDConnectionEndpointTransport value) => value switch
    {
        OBDConnectionEndpointTransport.BluetoothLowEnergy
            or OBDConnectionEndpointTransport.BluetoothClassic
            or OBDConnectionEndpointTransport.Serial => true,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}

/// <summary>
/// OBDアダプターへ接続するためのフレームワーク非依存な物理終端です。
/// </summary>
public sealed record OBDConnectionEndpoint
{
    /// <summary>
    /// 使用する物理通信方式です。
    /// </summary>
    public OBDConnectionEndpointTransport Transport { get; }

    /// <summary>
    /// OSが接続対象へ割り当てた識別子です。
    /// </summary>
    public string SystemIdentifier { get; }

    /// <summary>
    /// ユーザーが識別できるアダプター名称です。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 検出済みアダプターからOBD接続終端を生成します。
    /// </summary>
    /// <remarks>責務: 1件の検出結果を通信層へ渡せる最小接続情報へ変換します。</remarks>
    /// <param name="adapter">接続対象として選択されたアダプター。</param>
    public OBDConnectionEndpoint(DiscoveredAdapter adapter)
    {
        Transport = adapter.ConnectionTransport;
        SystemIdentifier = adapter.SystemIdentifier;
        DisplayName = adapter.DisplayName;
    }

    /// <summary>
    /// 永続化済み接続情報からOBD接続終端を生成します。
    /// </summary>
    /// <remarks>責務: 保存済み物理識別情報を変更せず接続終端へ固定します。</remarks>
    /// <param name="transport">使用する物理通信方式。</param>
    /// <param name="systemIdentifier">OSが割り当てた接続先識別子。</param>
    /// <param name="displayName">ユーザーが識別できる名称。</param>
    public OBDConnectionEndpoint(OBDConnectionEndpointTransport transport, string systemIdentifier, string displayName)
    {
        Transport = transport;
        SystemIdentifier = systemIdentifier;
        DisplayName = displayName;
    }
}
