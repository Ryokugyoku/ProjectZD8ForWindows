using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.ValueObjects;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace ProjectZD8.Data.Windows.Devices;

/// <summary>保存済みの既定アダプターがつながったこと（USB の差し込み、BLE のアドバタイズ）を監視します。</summary>
/// <remarks>
/// iOS 版の <c>IOSCoreBluetoothDefaultAdapterArrivalMonitor</c> に対応します。iOS は BLE だけを監視しますが、
/// Windows では USB シリアル（COM ポートの <see cref="DeviceWatcher"/>）と BLE（<see cref="BluetoothLEAdvertisementWatcher"/>）を監視します。
/// Bluetooth Classic は接続を試みるまで到来を知る方法がないため、iOS の BLE 以外と同じく監視しません。
/// iOS の状態復元にあたる仕組みはなく、アプリが動いている間だけ監視します。
/// 照合と重複防止は <see cref="DefaultAdapterArrivalDelivery"/> が行い、通知は開始時の UI スレッドへ戻します。
/// </remarks>
public sealed class WindowsDefaultAdapterArrivalMonitor : IDefaultAdapterArrivalMonitoring
{
    /// <summary>COM ポート名のインターフェイス属性です。</summary>
    private const string PortNameProperty = "System.DeviceInterface.Serial.PortName";

    /// <summary>照合と高々1回の通知を担当します。</summary>
    private readonly DefaultAdapterArrivalDelivery delivery = new();

    /// <summary>COM ポートの監視です。</summary>
    private DeviceWatcher? serialWatcher;

    /// <summary>BLE アドバタイズの監視です。</summary>
    private BluetoothLEAdvertisementWatcher? advertisementWatcher;

    /// <summary>指定された既定アダプターの到来監視を開始します。</summary>
    /// <remarks>責務: 1件の既定設定を物理方式に応じた OS の監視へ反映します。</remarks>
    /// <param name="preference">監視対象の既定設定。</param>
    /// <param name="arrival">対象を検出したときの通知先（開始したスレッドで呼ばれます）。</param>
    public void StartMonitoring(DefaultAdapterPreference preference, Action<OBDConnectionEndpoint> arrival)
    {
        StopWatchers();
        var transportName = OBDDiagnosticText.Transport(preference.ConnectionTransport);
        if (!DefaultAdapterArrivalDelivery.IsMonitorable(preference))
        {
            OBDDiscoveryLog.ArrivalNotMonitorable(OBDDiscoveryLog.Logger, transportName);
            StopMonitoring();
            return;
        }

        var current = delivery.Start(preference, arrival, SynchronizationContext.Current);
        try
        {
            if (preference.ConnectionTransport == OBDConnectionEndpointTransport.Serial)
            {
                StartSerialWatcher(current);
            }
            else
            {
                StartAdvertisementWatcher(current);
            }

            OBDDiscoveryLog.ArrivalStarted(OBDDiscoveryLog.Logger, transportName, DiagnosticDevice(preference.ConnectionTransport, preference.SystemIdentifier));
        }
        catch (Exception error)
        {
            OBDDiscoveryLog.ArrivalStartFailed(OBDDiscoveryLog.Logger, error, transportName);
            // Bluetooth が無効などで監視を始められない場合は、監視なしの状態にします（iOS の電源オフ時と同じ）。
            StopWatchers();
        }
    }

    /// <summary>現在の監視と到来通知先を破棄します。</summary>
    /// <remarks>責務: 進行中の既定アダプター監視を停止して監視状態を空にします。</remarks>
    public void StopMonitoring()
    {
        StopWatchers();
        delivery.Stop();
        OBDDiscoveryLog.ArrivalStopped(OBDDiscoveryLog.Logger);
    }

    /// <summary>COM ポートの追加を監視します。</summary>
    /// <remarks>責務: 既に接続されているポートと新しく接続されたポートを照合へ渡します。</remarks>
    /// <param name="current">監視世代。</param>
    private void StartSerialWatcher(ulong current)
    {
        var watcher = DeviceInformation.CreateWatcher(SerialDevice.GetDeviceSelector(), [PortNameProperty]);
        watcher.Added += (sender, information) =>
        {
            var portName = information.Properties.TryGetValue(PortNameProperty, out var value) ? value as string : null;
            if (portName is not null && delivery.TryDeliver(current, OBDConnectionEndpointTransport.Serial, portName))
            {
                OBDDiscoveryLog.ArrivalDetected(OBDDiscoveryLog.Logger, OBDDiagnosticText.TransportSerial, portName);
                StopWatcher(sender);
            }
        };
        // DeviceWatcher は Updated と Removed を購読しないと列挙を続けないため、空の処理を登録します。
        watcher.Updated += (_, _) => { };
        watcher.Removed += (_, _) => { };
        serialWatcher = watcher;
        watcher.Start();
    }

    /// <summary>BLE のアドバタイズを監視します。</summary>
    /// <remarks>責務: 受信したアドバタイズのアドレスを照合へ渡します。</remarks>
    /// <param name="current">監視世代。</param>
    private void StartAdvertisementWatcher(ulong current)
    {
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        watcher.Received += (sender, args) =>
        {
            if (delivery.TryDeliver(current, OBDConnectionEndpointTransport.BluetoothLowEnergy, BluetoothAddressText.Format(args.BluetoothAddress)))
            {
                OBDDiscoveryLog.ArrivalDetected(OBDDiscoveryLog.Logger, OBDDiagnosticText.TransportBLE, Redact.BluetoothAddress(args.BluetoothAddress));
                sender.Stop();
            }
        };
        advertisementWatcher = watcher;
        watcher.Start();
    }

    /// <summary>OS の監視をすべて止めます。</summary>
    /// <remarks>責務: COM と BLE の監視を破棄します。</remarks>
    private void StopWatchers()
    {
        if (serialWatcher is { } serial)
        {
            serialWatcher = null;
            StopWatcher(serial);
        }

        if (advertisementWatcher is { } advertisement)
        {
            advertisementWatcher = null;
            try
            {
                advertisement.Stop();
            }
            catch (Exception error)
            {
                // 既に止まっている場合は何もしません。
                OBDDiscoveryLog.ArrivalStopFailed(OBDDiscoveryLog.Logger, error, OBDDiagnosticText.TransportBLE);
            }
        }
    }

    /// <summary>DeviceWatcher を状態に応じて止めます。</summary>
    /// <remarks>責務: 開始済みまたは列挙済みの監視だけを停止します。</remarks>
    /// <param name="watcher">止める監視。</param>
    private static void StopWatcher(DeviceWatcher watcher)
    {
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception error)
        {
            // 既に止まっている場合は何もしません。
            OBDDiscoveryLog.ArrivalStopFailed(OBDDiscoveryLog.Logger, error, OBDDiagnosticText.TransportSerial);
        }
    }

    /// <summary>診断ログに書く機器の識別子です。</summary>
    /// <remarks>責務: COM ポート名はそのまま、Bluetooth のアドレスは末尾 2 バイトだけにします。</remarks>
    /// <param name="transport">物理方式。</param>
    /// <param name="systemIdentifier">システム識別子。</param>
    /// <returns>伏せた識別子。</returns>
    private static string DiagnosticDevice(OBDConnectionEndpointTransport transport, string systemIdentifier) =>
        transport == OBDConnectionEndpointTransport.Serial ? systemIdentifier : Redact.BluetoothAddress(systemIdentifier);
}
