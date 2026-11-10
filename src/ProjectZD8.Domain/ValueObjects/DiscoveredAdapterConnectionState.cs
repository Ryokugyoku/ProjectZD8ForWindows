namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// アダプター候補を検出した時点のシステム接続状態を表します。
/// </summary>
public enum DiscoveredAdapterConnectionState
{
    /// <summary>
    /// 周辺機器が切断されています。
    /// </summary>
    Disconnected,

    /// <summary>
    /// 周辺機器へ接続中です。
    /// </summary>
    Connecting,

    /// <summary>
    /// 周辺機器へ接続済みです。
    /// </summary>
    Connected,

    /// <summary>
    /// 周辺機器を切断中です。
    /// </summary>
    Disconnecting,

    /// <summary>
    /// システムから接続状態を判定できません。
    /// </summary>
    Unknown,
}

/// <summary>
/// <see cref="DiscoveredAdapterConnectionState"/> の raw value を扱います。
/// </summary>
public static class DiscoveredAdapterConnectionStateRawValue
{
    /// <summary>
    /// 接続状態を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の検出時接続状態を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する接続状態。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this DiscoveredAdapterConnectionState value) => value switch
    {
        DiscoveredAdapterConnectionState.Disconnected => "disconnected",
        DiscoveredAdapterConnectionState.Connecting => "connecting",
        DiscoveredAdapterConnectionState.Connected => "connected",
        DiscoveredAdapterConnectionState.Disconnecting => "disconnecting",
        DiscoveredAdapterConnectionState.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から接続状態を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して検出時接続状態へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた接続状態。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out DiscoveredAdapterConnectionState value)
    {
        switch (rawValue)
        {
            case "disconnected": value = DiscoveredAdapterConnectionState.Disconnected; return true;
            case "connecting": value = DiscoveredAdapterConnectionState.Connecting; return true;
            case "connected": value = DiscoveredAdapterConnectionState.Connected; return true;
            case "disconnecting": value = DiscoveredAdapterConnectionState.Disconnecting; return true;
            case "unknown": value = DiscoveredAdapterConnectionState.Unknown; return true;
            default: value = default; return false;
        }
    }
}
