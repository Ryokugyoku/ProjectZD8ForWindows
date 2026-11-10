namespace ProjectZD8.Application.DeviceConnection.Ports;

/// <summary>
/// 車両接続中の端末アイドル移行抑止状態をOS境界へ反映します。
/// </summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public interface IVehicleConnectionSystemSleepInhibiting
{
    /// <summary>
    /// 車両接続状態に合わせて端末固有のアイドル移行抑止を切り替えます。
    /// </summary>
    /// <remarks>責務: 1件の車両接続状態を端末固有のアイドル移行抑止境界へ通知します。</remarks>
    /// <param name="isActive">車両接続中としてアイドル移行を抑止する場合は <c>true</c>。</param>
    void SetVehicleConnectionActive(bool isActive);
}
