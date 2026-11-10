using System.Runtime.InteropServices;
using ProjectZD8.Application.DeviceConnection.Ports;

namespace ProjectZD8.Data.Windows.Devices;

/// <summary>車両接続中だけディスプレイ消灯を妨げずにシステムスリープを抑止します。</summary>
/// <remarks>
/// iOS 版の <c>ProcessInfoVehicleConnectionSystemSleepInhibitor</c>（<c>idleSystemSleepDisabled</c>）に対応し、
/// Win32 の <c>SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)</c> を使います。
/// この設定は呼んだスレッドに属するため、開始と終了は同じスレッド（UI スレッド）から呼ぶ前提です。
/// </remarks>
public sealed class SetThreadExecutionStateVehicleConnectionSystemSleepInhibitor : IVehicleConnectionSystemSleepInhibiting
{
    /// <summary>設定を次に変えるまで保持します。</summary>
    private const uint EsContinuous = 0x80000000;

    /// <summary>システムのアイドルスリープを抑止します。</summary>
    private const uint EsSystemRequired = 0x00000001;

    /// <summary>スリープを抑止しているかどうかです。</summary>
    private bool isActive;

    /// <summary>未抑止状態の境界を生成します。</summary>
    /// <remarks>責務: 車両接続用システムスリープ抑止境界を未開始状態で構築します。</remarks>
    public SetThreadExecutionStateVehicleConnectionSystemSleepInhibitor()
    {
    }

    /// <summary>車両接続状態に合わせてシステムスリープの抑止を開始または終了します。</summary>
    /// <remarks>責務: 1件の車両接続状態を重複しないシステムスリープ抑止へ変換します。</remarks>
    /// <param name="isActive">システムスリープ抑止を保持する場合は <c>true</c>。</param>
    public void SetVehicleConnectionActive(bool isActive)
    {
        if (isActive)
        {
            if (this.isActive)
            {
                return;
            }

            // 戻り値 0 は失敗です。失敗しても接続は続けられるため、次の呼び出しで再試行できる状態に保ちます。
            this.isActive = SetThreadExecutionState(EsContinuous | EsSystemRequired) != 0;
        }
        else if (this.isActive)
        {
            SetThreadExecutionState(EsContinuous);
            this.isActive = false;
        }
    }

    /// <summary>スレッドの実行状態を OS へ伝えます。</summary>
    /// <remarks>責務: Win32 の <c>SetThreadExecutionState</c> を呼び出します。</remarks>
    /// <param name="flags">実行状態のフラグ。</param>
    /// <returns>直前の状態。失敗した場合は 0。</returns>
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
