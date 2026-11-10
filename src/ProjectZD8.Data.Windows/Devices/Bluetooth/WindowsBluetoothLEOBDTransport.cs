using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace ProjectZD8.Data.Windows.Devices.Bluetooth;

/// <summary>選択済みBLE機器を既知UART構成へ接続してELM/STNバイトストリームを提供します。</summary>
/// <remarks>
/// iOS 版の <c>AppleCoreBluetoothOBDTransport</c> に対応します。GATT の Service と Characteristic を全件探索し、
/// <see cref="BluetoothUARTProfileResolver"/> で書込先と通知元を1組だけ選び、通知を有効にします。
/// 期限は iOS と同じく、接続12秒、応答あり書込5秒、プロンプト読取10秒です。
/// GATT の通知は OS のスレッドから届くため、受信バッファーと待機中の読取はロックで守ります。
/// </remarks>
public sealed class WindowsBluetoothLEOBDTransport : IOBDCommandTransport
{
    /// <summary>接続処理の期限です。</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(12);

    /// <summary>1チャンクの書込の期限です。</summary>
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    /// <summary>プロンプト読取の期限です。</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>実機能力を既知UART構成へ照合します。</summary>
    private readonly BluetoothUARTProfileResolver profileResolver = new();

    /// <summary>接続対象のBluetoothアドレスです。</summary>
    private readonly ulong address;

    /// <summary>受信バッファー、待機中の読取、接続状態を守るロックです。</summary>
    private readonly Lock gate = new();

    /// <summary>Notifyで分割受信した未返却バイトです。</summary>
    private readonly List<byte> readBuffer = [];

    /// <summary><c>&gt;</c> 終端応答を待っている読取です。</summary>
    private TaskCompletionSource<byte[]>? pendingRead;

    /// <summary>現在の接続です。</summary>
    private GattConnection? connection;

    /// <summary>接続が生きているかどうかです。</summary>
    private bool isConnected;

    /// <summary>選択済みBluetooth Low Energy終端を固定して生成します。</summary>
    /// <remarks>責務: 1件のBLE終端をGATT UART接続条件へ固定します。</remarks>
    /// <param name="endpoint">Bluetoothアドレスを持つ選択済みBLE終端。</param>
    /// <exception cref="VehicleIdentificationException">BLE以外またはアドレス不正の場合は <c>TransportUnsupported</c>。</exception>
    public WindowsBluetoothLEOBDTransport(OBDConnectionEndpoint endpoint)
    {
        if (endpoint.Transport != OBDConnectionEndpointTransport.BluetoothLowEnergy
            || !BluetoothAddressText.TryParse(endpoint.SystemIdentifier, out address))
        {
            throw new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported());
        }
    }

    /// <summary>選択済み機器へ接続して既知UART Characteristicの通知を有効化します。</summary>
    /// <remarks>責務: 1件の選択済みBLE機器をNotify／Write可能な既知UARTセッションへ遷移させます。</remarks>
    /// <returns>接続の完了。</returns>
    /// <exception cref="VehicleIdentificationException">機器不在、接続失敗、未知UUID構成、または期限切れの場合の識別エラー。</exception>
    public async Task Open()
    {
        lock (gate)
        {
            if (isConnected && connection is not null)
            {
                return;
            }
        }

        await Close();
        var opening = Connect();
        if (await Task.WhenAny(opening, Task.Delay(OpenTimeout)) != opening)
        {
            // 期限後に接続できても使わずに閉じます。
            _ = opening.ContinueWith(
                task => task.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
            throw new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut());
        }

        var opened = await opening;
        lock (gate)
        {
            readBuffer.Clear();
            connection = opened;
            isConnected = true;
        }

        opened.Notify.ValueChanged += OnValueChanged;
        opened.Device.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    /// <summary>復帰文字を含むELM/STNコマンドを最大書込長に収まる単位で書き込みます。</summary>
    /// <remarks>責務: 1件の完全なコマンドバイト列をCharacteristic能力と最大書込長に従って順次送信します。</remarks>
    /// <param name="data">復帰文字を含む完全なASCIIコマンド。</param>
    /// <returns>書込みの完了。</returns>
    /// <exception cref="VehicleIdentificationException">未接続、切断は <c>ConnectionFailed</c>、期限切れは <c>ResponseTimedOut</c>。</exception>
    public async Task Write(byte[] data)
    {
        GattConnection active;
        lock (gate)
        {
            if (!isConnected || connection is null)
            {
                throw ConnectionFailed();
            }

            active = connection;
        }

        var maximumLength = Math.Max(1, active.Session.MaxPduSize - 3);
        for (var offset = 0; offset < data.Length; offset += maximumLength)
        {
            var chunk = data.AsSpan(offset, Math.Min(maximumLength, data.Length - offset)).ToArray();
            var writer = new DataWriter();
            writer.WriteBytes(chunk);
            var writing = active.Write.WriteValueWithResultAsync(writer.DetachBuffer(), active.WriteOption).AsTask();
            if (await Task.WhenAny(writing, Task.Delay(WriteTimeout)) != writing)
            {
                throw new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut());
            }

            GattWriteResult result;
            try
            {
                result = await writing;
            }
            catch (Exception)
            {
                throw ConnectionFailed();
            }

            if (result.Status != GattCommunicationStatus.Success)
            {
                throw ConnectionFailed();
            }
        }
    }

    /// <summary>Notifyで受信したバイトを次のELM/STNプロンプトまで返します。</summary>
    /// <remarks>責務: 分割されたBLE通知を1件の <c>&gt;</c> プロンプト終端応答へ結合します。</remarks>
    /// <returns><c>&gt;</c> を含む加工前応答バイト。</returns>
    /// <exception cref="VehicleIdentificationException">同時読取、未接続、切断は <c>ConnectionFailed</c>、期限切れは <c>ResponseTimedOut</c>。</exception>
    public async Task<byte[]> ReadUntilPrompt()
    {
        TaskCompletionSource<byte[]> waiting;
        lock (gate)
        {
            if (ExtractPromptResponse() is { } response)
            {
                return response;
            }

            if (!isConnected || pendingRead is not null)
            {
                throw ConnectionFailed();
            }

            waiting = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRead = waiting;
        }

        if (await Task.WhenAny(waiting.Task, Task.Delay(ReadTimeout)) != waiting.Task)
        {
            FinishRead(waiting, new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut()));
        }

        return await waiting.Task;
    }

    /// <summary>現在のGATT接続と待機中の読取を終了します。</summary>
    /// <remarks>責務: 1件のBLE UARTセッションに属する資源と未返却バイトを破棄します。</remarks>
    /// <returns>閉じる処理の完了。</returns>
    public Task Close()
    {
        GattConnection? closing;
        TaskCompletionSource<byte[]>? waiting;
        lock (gate)
        {
            closing = connection;
            waiting = pendingRead;
            connection = null;
            pendingRead = null;
            isConnected = false;
            readBuffer.Clear();
        }

        waiting?.TrySetException(ConnectionFailed());
        if (closing is not null)
        {
            closing.Notify.ValueChanged -= OnValueChanged;
            closing.Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            closing.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>機器へ接続し、既知UART構成の Characteristic を解決して通知を有効にします。</summary>
    /// <remarks>責務: 実機Service能力から書込先と通知元を1組だけ選びBLE UARTを準備します。</remarks>
    /// <returns>準備できた接続。</returns>
    /// <exception cref="VehicleIdentificationException">接続できない場合は <c>ConnectionFailed</c>、UART構成がない場合は <c>TransportUnsupported</c>。</exception>
    private async Task<GattConnection> Connect()
    {
        BluetoothLEDevice? device;
        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        }
        catch (Exception)
        {
            throw ConnectionFailed();
        }

        if (device is null)
        {
            throw ConnectionFailed();
        }

        var services = new List<GattDeviceService>();
        GattSession? session = null;
        try
        {
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
            session.MaintainConnection = true;
            var serviceResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (serviceResult.Status == GattCommunicationStatus.Unreachable)
            {
                throw ConnectionFailed();
            }

            if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
            {
                throw new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported());
            }

            services.AddRange(serviceResult.Services);
            var characteristicsByService = new Dictionary<string, IReadOnlyList<GattCharacteristic>>(StringComparer.Ordinal);
            foreach (var service in services)
            {
                var characteristicResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                characteristicsByService[BluetoothUUIDText.Format(service.Uuid)] =
                    characteristicResult.Status == GattCommunicationStatus.Success
                        ? characteristicResult.Characteristics.ToArray()
                        : [];
            }

            var (write, notify) = ResolveUARTCharacteristics(characteristicsByService);
            var descriptorValue = notify.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            var status = await notify.WriteClientCharacteristicConfigurationDescriptorAsync(descriptorValue);
            if (status != GattCommunicationStatus.Success)
            {
                throw ConnectionFailed();
            }

            var writeOption = write.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write)
                ? GattWriteOption.WriteWithResponse
                : GattWriteOption.WriteWithoutResponse;
            return new GattConnection(device, session, services, write, notify, writeOption);
        }
        catch (VehicleIdentificationException)
        {
            DisposeAll(device, session, services);
            throw;
        }
        catch (Exception)
        {
            DisposeAll(device, session, services);
            throw ConnectionFailed();
        }
    }

    /// <summary>発見済みCharacteristicを既知UART構成の書込先と通知元へ解決します。</summary>
    /// <remarks>責務: Service別のCharacteristic一覧をOS非依存の能力へ変換して照合します。</remarks>
    /// <param name="characteristicsByService">Service UUIDごとのCharacteristic一覧。</param>
    /// <returns>書込先と通知元のCharacteristic。</returns>
    /// <exception cref="VehicleIdentificationException">既知UART構成がない場合は <c>TransportUnsupported</c>。</exception>
    private (GattCharacteristic Write, GattCharacteristic Notify) ResolveUARTCharacteristics(
        IReadOnlyDictionary<string, IReadOnlyList<GattCharacteristic>> characteristicsByService)
    {
        var capabilities = characteristicsByService.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<BluetoothCharacteristicCapability>)entry.Value
                .Select(characteristic => new BluetoothCharacteristicCapability(
                    BluetoothUUIDText.Format(characteristic.Uuid),
                    supportsNotify: characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
                        || characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate),
                    supportsWriteWithResponse: characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write),
                    supportsWriteWithoutResponse: characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)))
                .ToArray(),
            StringComparer.Ordinal);
        var profile = profileResolver.Resolve(capabilities)
            ?? throw new VehicleIdentificationException(new VehicleIdentificationError.TransportUnsupported());
        var serviceCharacteristics = characteristicsByService
            .First(entry => string.Equals(entry.Key, profile.ServiceUUID, StringComparison.OrdinalIgnoreCase))
            .Value;
        var write = serviceCharacteristics.First(characteristic =>
            string.Equals(BluetoothUUIDText.Format(characteristic.Uuid), profile.WriteCharacteristicUUID, StringComparison.OrdinalIgnoreCase));
        var notify = serviceCharacteristics.First(characteristic =>
            string.Equals(BluetoothUUIDText.Format(characteristic.Uuid), profile.NotifyCharacteristicUUID, StringComparison.OrdinalIgnoreCase));
        return (write, notify);
    }

    /// <summary>通知で受信した分割バイトを応答バッファーへ追加します。</summary>
    /// <remarks>責務: 1件のCharacteristic通知を未返却バイトへ結合し完全なプロンプト応答を待機読取へ返します。</remarks>
    /// <param name="sender">通知元Characteristic。</param>
    /// <param name="args">通知値。</param>
    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var buffer = args.CharacteristicValue;
        var received = new byte[buffer.Length];
        DataReader.FromBuffer(buffer).ReadBytes(received);
        TaskCompletionSource<byte[]>? waiting = null;
        byte[]? response = null;
        lock (gate)
        {
            readBuffer.AddRange(received);
            if (pendingRead is not null && ExtractPromptResponse() is { } extracted)
            {
                waiting = pendingRead;
                pendingRead = null;
                response = extracted;
            }
        }

        if (waiting is not null && response is not null)
        {
            waiting.TrySetResult(response);
        }
    }

    /// <summary>機器の切断を待機中の読取へ返します。</summary>
    /// <remarks>責務: 1件の予期しないBLE切断を待機処理の通信失敗と未接続状態へ変換します。</remarks>
    /// <param name="sender">状態が変わった機器。</param>
    /// <param name="args">使用しません。</param>
    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus != BluetoothConnectionStatus.Disconnected)
        {
            return;
        }

        TaskCompletionSource<byte[]>? waiting;
        lock (gate)
        {
            isConnected = false;
            waiting = pendingRead;
            pendingRead = null;
        }

        waiting?.TrySetException(ConnectionFailed());
    }

    /// <summary>読取の待機を失敗で完了します。</summary>
    /// <remarks>責務: まだ待機中の同じ読取だけを1回だけ失敗させます。</remarks>
    /// <param name="waiting">完了させる読取。</param>
    /// <param name="error">返すエラー。</param>
    private void FinishRead(TaskCompletionSource<byte[]> waiting, Exception error)
    {
        lock (gate)
        {
            if (ReferenceEquals(pendingRead, waiting))
            {
                pendingRead = null;
            }
        }

        waiting.TrySetException(error);
    }

    /// <summary>未返却バッファーから最初のELM/STNプロンプト境界を切り出します。</summary>
    /// <remarks>責務: BLE受信バッファーを最初の <c>&gt;</c> を含む応答と後続バイトへ分割します（ロック内で呼びます）。</remarks>
    /// <returns>完全なプロンプト境界がある場合の応答。未完了の場合は <c>null</c>。</returns>
    private byte[]? ExtractPromptResponse()
    {
        var promptIndex = readBuffer.IndexOf(0x3E);
        if (promptIndex < 0)
        {
            return null;
        }

        var response = readBuffer.GetRange(0, promptIndex + 1).ToArray();
        readBuffer.RemoveRange(0, promptIndex + 1);
        return response;
    }

    /// <summary>接続途中で確保した資源を破棄します。</summary>
    /// <remarks>責務: 失敗した接続の機器、セッション、Service を解放します。</remarks>
    /// <param name="device">機器。</param>
    /// <param name="session">GATT セッション。</param>
    /// <param name="services">取得済みの Service。</param>
    private static void DisposeAll(BluetoothLEDevice device, GattSession? session, IEnumerable<GattDeviceService> services)
    {
        foreach (var service in services)
        {
            service.Dispose();
        }

        session?.Dispose();
        device.Dispose();
    }

    /// <summary>接続失敗の識別エラーを生成します。</summary>
    /// <remarks>責務: GATT境界の失敗を共通の型付きエラーへ変換します。</remarks>
    /// <returns>送出できる識別エラー。</returns>
    private static VehicleIdentificationException ConnectionFailed() =>
        new(new VehicleIdentificationError.ConnectionFailed());

    /// <summary>1件のGATT UART接続に属する資源の組です。</summary>
    /// <param name="Device">接続した機器。</param>
    /// <param name="Session">接続を保つ GATT セッション。</param>
    /// <param name="Services">取得した Service（閉じるときに破棄します）。</param>
    /// <param name="Write">書込先 Characteristic。</param>
    /// <param name="Notify">通知元 Characteristic。</param>
    /// <param name="WriteOption">書込方式（応答ありを優先）。</param>
    private sealed record GattConnection(
        BluetoothLEDevice Device,
        GattSession Session,
        IReadOnlyList<GattDeviceService> Services,
        GattCharacteristic Write,
        GattCharacteristic Notify,
        GattWriteOption WriteOption) : IDisposable
    {
        /// <summary>接続の資源を破棄します。</summary>
        /// <remarks>責務: Service、セッション、機器を解放して接続を終えます。</remarks>
        public void Dispose() => DisposeAll(Device, Session, Services);
    }
}
