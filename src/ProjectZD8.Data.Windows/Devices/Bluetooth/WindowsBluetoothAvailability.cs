using ProjectZD8.Application.DeviceConnection.Ports;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace ProjectZD8.Data.Windows.Devices.Bluetooth;

/// <summary>Bluetooth アダプターと無線の状態を探索エラーへ変換します。</summary>
/// <remarks>
/// iOS 版の CoreBluetooth 状態通知（poweredOff、unauthorized、unsupported、状態確定の2秒期限）に対応します。
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
            throw new AdapterDiscoveryException(AdapterDiscoveryError.BluetoothStateUnavailable);
        }

        if (await checking is { } error)
        {
            throw new AdapterDiscoveryException(error);
        }
    }

    /// <summary>アダプターと無線の状態を読み取ります。</summary>
    /// <remarks>責務: OS の状態を探索エラーの理由（なければ <c>null</c>）へ写像します。</remarks>
    /// <param name="requiresLowEnergy">BLE 中央機能が必要かどうか。</param>
    /// <returns>使えない理由。使える場合は <c>null</c>。</returns>
    private static async Task<AdapterDiscoveryError?> Check(bool requiresLowEnergy)
    {
        BluetoothAdapter? adapter;
        try
        {
            adapter = await BluetoothAdapter.GetDefaultAsync();
        }
        catch (Exception)
        {
            return AdapterDiscoveryError.BluetoothStateUnavailable;
        }

        if (adapter is null)
        {
            return AdapterDiscoveryError.BluetoothUnsupported;
        }

        if (requiresLowEnergy ? !adapter.IsLowEnergySupported || !adapter.IsCentralRoleSupported : !adapter.IsClassicSupported)
        {
            return AdapterDiscoveryError.BluetoothUnsupported;
        }

        Radio? radio;
        try
        {
            radio = await adapter.GetRadioAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return AdapterDiscoveryError.BluetoothUnauthorized;
        }
        catch (Exception)
        {
            return AdapterDiscoveryError.BluetoothStateUnavailable;
        }

        return radio?.State switch
        {
            RadioState.On => null,
            RadioState.Off or RadioState.Disabled => AdapterDiscoveryError.BluetoothPoweredOff,
            _ => AdapterDiscoveryError.BluetoothStateUnavailable,
        };
    }
}
