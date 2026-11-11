using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Data.Devices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace ProjectZD8.Data.Windows.Devices.Bluetooth;

/// <summary>Bluetooth アダプターと無線の状態を探索エラーへ変換します。</summary>
/// <remarks>
/// iOS 版の CoreBluetooth 状態通知（poweredOff、unauthorized、unsupported、状態確定の2秒期限）に対応します。
/// 判定の結果は <c>obd.discovery.bluetooth.*</c> に書きます（使えないときは OS の状態の説明つき）。
/// </remarks>
internal static class WindowsBluetoothAvailability
{
    /// <summary>状態の確定を待つ最大時間です（iOS と同じ2秒）。</summary>
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Bluetooth が使える状態かを確かめます。</summary>
    /// <remarks>責務: 既定アダプターの有無、要求方式への対応、無線の電源状態を1件の探索エラーへ変換します。</remarks>
    /// <param name="requiresLowEnergy">BLE 中央機能が必要なら <c>true</c>、Classic なら <c>false</c>。</param>
    /// <returns>確認の完了。</returns>
    /// <exception cref="AdapterDiscoveryException">Bluetooth を使えない理由。</exception>
    public static async Task Ensure(bool requiresLowEnergy)
    {
        var checking = Check(requiresLowEnergy);
        if (await Task.WhenAny(checking, Task.Delay(StateTimeout)) != checking)
        {
            OBDDiscoveryLog.BluetoothUnavailable(
                OBDDiscoveryLog.Logger,
                requiresLowEnergy,
                nameof(AdapterDiscoveryError.BluetoothStateUnavailable),
                "state-timeout");
            throw new AdapterDiscoveryException(AdapterDiscoveryError.BluetoothStateUnavailable);
        }

        var (error, detail) = await checking;
        if (error is { } reason)
        {
            OBDDiscoveryLog.BluetoothUnavailable(OBDDiscoveryLog.Logger, requiresLowEnergy, reason.ToString(), detail);
            throw new AdapterDiscoveryException(reason);
        }

        OBDDiscoveryLog.BluetoothAvailable(OBDDiscoveryLog.Logger, requiresLowEnergy);
    }

    /// <summary>アダプターと無線の状態を読み取ります。</summary>
    /// <remarks>責務: OS の状態を探索エラーの理由（なければ <c>null</c>）と、診断ログ用の短い説明へ写像します。</remarks>
    /// <param name="requiresLowEnergy">BLE 中央機能が必要かどうか。</param>
    /// <returns>使えない理由（使える場合は <c>null</c>）と、OS の状態の説明。</returns>
    private static async Task<(AdapterDiscoveryError? Error, string Detail)> Check(bool requiresLowEnergy)
    {
        BluetoothAdapter? adapter;
        try
        {
            adapter = await BluetoothAdapter.GetDefaultAsync();
        }
        catch (Exception error)
        {
            OBDDiscoveryLog.StepFailed(OBDDiscoveryLog.Logger, error, "bluetooth-adapter");
            return (AdapterDiscoveryError.BluetoothStateUnavailable, "adapter-query-failed");
        }

        if (adapter is null)
        {
            return (AdapterDiscoveryError.BluetoothUnsupported, "no-adapter");
        }

        if (requiresLowEnergy ? !adapter.IsLowEnergySupported || !adapter.IsCentralRoleSupported : !adapter.IsClassicSupported)
        {
            return (
                AdapterDiscoveryError.BluetoothUnsupported,
                $"classic={adapter.IsClassicSupported} le={adapter.IsLowEnergySupported} central={adapter.IsCentralRoleSupported}");
        }

        Radio? radio;
        try
        {
            radio = await adapter.GetRadioAsync();
        }
        catch (UnauthorizedAccessException error)
        {
            OBDDiscoveryLog.StepFailed(OBDDiscoveryLog.Logger, error, "bluetooth-radio");
            return (AdapterDiscoveryError.BluetoothUnauthorized, "radio-access-denied");
        }
        catch (Exception error)
        {
            OBDDiscoveryLog.StepFailed(OBDDiscoveryLog.Logger, error, "bluetooth-radio");
            return (AdapterDiscoveryError.BluetoothStateUnavailable, "radio-query-failed");
        }

        var detail = radio is null ? "radio=none" : $"radio={radio.State}";
        return radio?.State switch
        {
            RadioState.On => (null, detail),
            RadioState.Off or RadioState.Disabled => (AdapterDiscoveryError.BluetoothPoweredOff, detail),
            _ => (AdapterDiscoveryError.BluetoothStateUnavailable, detail),
        };
    }
}
