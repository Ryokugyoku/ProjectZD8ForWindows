namespace ProjectZD8.Data.Devices.Serial;

/// <summary>OSが報告したBluetooth Classic機器がOBDLink MX+かどうかを判定します。</summary>
/// <remarks>iOS 版の <c>MacOSOBDLinkMXPlusDiscoveryPolicy</c> に対応します。</remarks>
public sealed class OBDLinkMXPlusDiscoveryPolicy
{
    /// <summary>Bluetooth機器の名称とペアリング状態をMX+接続候補として検証します。</summary>
    /// <remarks>責務: 1件のBluetooth機器情報をペアリング済みOBDLink MX+かどうかへ分類します。</remarks>
    /// <param name="name">OSが返したBluetooth機器名称。</param>
    /// <param name="isPaired">OSのペアリングが完了しているかどうか。</param>
    /// <returns>公式MX+名称に一致し、かつペアリング済みの場合は <c>true</c>。</returns>
    public bool Accepts(string name, bool isPaired)
    {
        if (!isPaired)
        {
            return false;
        }

        var normalizedName = name.Trim().ToUpperInvariant();
        return normalizedName == "OBDLINK MX+" || normalizedName.StartsWith("OBDLINK MX+ ", StringComparison.Ordinal);
    }
}
