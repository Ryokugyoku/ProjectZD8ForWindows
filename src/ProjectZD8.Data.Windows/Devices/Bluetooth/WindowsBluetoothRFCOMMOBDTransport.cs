using System.Diagnostics;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace ProjectZD8.Data.Windows.Devices.Bluetooth;

/// <summary>Bluetooth ClassicのSerial Port Profile（RFCOMM）をELM/STNバイトストリームとして扱います。</summary>
/// <remarks>
/// iOS 版の <c>MacOSBluetoothRFCOMMOBDTransport</c> に対応します。ペアリング済み機器の SerialPort サービスを
/// <see cref="BluetoothDevice.GetRfcommServicesForIdAsync(RfcommServiceId, BluetoothCacheMode)"/> で解決し、
/// <see cref="StreamSocket"/> で接続します。受信はバックグラウンドの読取ループがバッファーへ溜め、
/// <see cref="ReadUntilPrompt"/> が10ミリ秒ごとに確認します（macOS 版と同じ方式）。
/// </remarks>
public sealed class WindowsBluetoothRFCOMMOBDTransport : IOBDCommandTransport
{
    /// <summary>SDP（サービス）照会を待つ最大時間です。</summary>
    private static readonly TimeSpan ServiceQueryTimeout = TimeSpan.FromSeconds(10);

    /// <summary>接続対象のBluetoothアドレス文字列です。</summary>
    private readonly string deviceAddress;

    /// <summary>1応答を待つ最大時間です。</summary>
    private readonly TimeSpan responseTimeout;

    /// <summary>受信バッファーと閉鎖状態を守るロックです。</summary>
    private readonly Lock gate = new();

    /// <summary>プロンプト境界まで保持する未消費受信バイトです。</summary>
    private readonly List<byte> receiveBuffer = [];

    /// <summary>現在の接続に使っているソケットです。</summary>
    private StreamSocket? socket;

    /// <summary>送信に使う書込み器です。</summary>
    private DataWriter? writer;

    /// <summary>接続先の機器です。</summary>
    private BluetoothDevice? device;

    /// <summary>現在のチャネルが閉じているかどうかです。</summary>
    private bool closed = true;

    /// <summary>Bluetoothアドレスと応答期限を保持して生成します。</summary>
    /// <remarks>責務: 1件のBluetooth Classic終端に必要なRFCOMM接続設定を固定します。</remarks>
    /// <param name="deviceAddress">接続対象のBluetoothアドレス（<see cref="BluetoothAddressText"/> の形）。</param>
    /// <param name="responseTimeout"><c>&gt;</c> プロンプトを待つ最大時間。<c>null</c> の場合は12秒。</param>
    public WindowsBluetoothRFCOMMOBDTransport(string deviceAddress, TimeSpan? responseTimeout = null)
    {
        this.deviceAddress = deviceAddress;
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(12);
    }

    /// <summary>ペアリング済み機器のSerial Port Profileを照会してRFCOMMチャネルを開きます。</summary>
    /// <remarks>責務: 1件のBluetoothアドレスを動的に解決したSerial Port Profileチャネルへ接続します。</remarks>
    /// <returns>接続の完了。</returns>
    /// <exception cref="VehicleIdentificationException">アドレス不正、未ペアリング、SDP照会、またはRFCOMM接続に失敗した場合は <c>ConnectionFailed</c>。</exception>
    public async Task Open()
    {
        if (socket is not null || !BluetoothAddressText.TryParse(deviceAddress, out var address))
        {
            throw ConnectionFailed();
        }

        try
        {
            var candidate = await BluetoothDevice.FromBluetoothAddressAsync(address);
            if (candidate is null || !candidate.DeviceInformation.Pairing.IsPaired)
            {
                candidate?.Dispose();
                throw ConnectionFailed();
            }

            var service = await SerialPortService(candidate);
            if (service is null)
            {
                candidate.Dispose();
                throw ConnectionFailed();
            }

            var opened = new StreamSocket();
            try
            {
                await opened.ConnectAsync(
                    service.ConnectionHostName,
                    service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
            }
            catch
            {
                opened.Dispose();
                candidate.Dispose();
                throw;
            }

            lock (gate)
            {
                receiveBuffer.Clear();
                closed = false;
            }

            device = candidate;
            socket = opened;
            writer = new DataWriter(opened.OutputStream);
            _ = ReceiveLoop(opened);
        }
        catch (VehicleIdentificationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw ConnectionFailed();
        }
    }

    /// <summary>完全なASCIIコマンドをRFCOMMチャネルへ書き込みます。</summary>
    /// <remarks>責務: 1件のコマンドに属する全バイトをソケットへ送信します（分割は OS が行います）。</remarks>
    /// <param name="data">復帰文字終端のASCIIコマンド。</param>
    /// <returns>書込みの完了。</returns>
    /// <exception cref="VehicleIdentificationException">未接続または書込み失敗の場合は <c>ConnectionFailed</c>。</exception>
    public async Task Write(byte[] data)
    {
        var activeWriter = writer;
        if (activeWriter is null || IsClosed)
        {
            throw ConnectionFailed();
        }

        try
        {
            activeWriter.WriteBytes(data);
            await activeWriter.StoreAsync();
        }
        catch (Exception)
        {
            throw ConnectionFailed();
        }
    }

    /// <summary>次のELM/STNプロンプトまでRFCOMM受信バイトを読み取ります。</summary>
    /// <remarks>責務: 現在コマンドのRFCOMM応答を最初の <c>&gt;</c> 境界まで欠落なく収集します。</remarks>
    /// <returns><c>&gt;</c> を含む加工前応答。</returns>
    /// <exception cref="VehicleIdentificationException">期限切れは <c>ResponseTimedOut</c>、切断は <c>ConnectionFailed</c>。</exception>
    public async Task<byte[]> ReadUntilPrompt()
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < responseTimeout)
        {
            if (TakeResponseThroughPrompt() is { } response)
            {
                return response;
            }

            if (IsClosed)
            {
                throw ConnectionFailed();
            }

            await Task.Delay(10);
        }

        throw new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut());
    }

    /// <summary>開いているRFCOMMチャネルを閉じます。</summary>
    /// <remarks>責務: 現在のBluetooth Classic接続を1回だけ閉じて受信状態を破棄します。</remarks>
    /// <returns>閉じる処理の完了。</returns>
    public Task Close()
    {
        lock (gate)
        {
            receiveBuffer.Clear();
            closed = true;
        }

        writer?.Dispose();
        writer = null;
        socket?.Dispose();
        socket = null;
        device?.Dispose();
        device = null;
        return Task.CompletedTask;
    }

    /// <summary>チャネルが閉じているかどうかです。</summary>
    private bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    /// <summary>機器の Serial Port サービスを、キャッシュ、なければ期限付きの照会で取得します。</summary>
    /// <remarks>責務: 1件の機器から標準 Serial Port Profile サービスだけを選びます。</remarks>
    /// <param name="candidate">ペアリング済みの機器。</param>
    /// <returns>Serial Port サービス。非対応または期限切れの場合は <c>null</c>。</returns>
    private static async Task<RfcommDeviceService?> SerialPortService(BluetoothDevice candidate)
    {
        var cached = await candidate.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached);
        if (cached.Error == BluetoothError.Success && cached.Services.Count > 0)
        {
            return cached.Services[0];
        }

        var query = candidate.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached).AsTask();
        var finished = await Task.WhenAny(query, Task.Delay(ServiceQueryTimeout));
        if (finished != query)
        {
            return null;
        }

        var uncached = await query;
        return uncached.Error == BluetoothError.Success && uncached.Services.Count > 0 ? uncached.Services[0] : null;
    }

    /// <summary>ソケットから届いたバイトを受信バッファーへ溜め続けます。</summary>
    /// <remarks>責務: 1件の接続の受信を切断まで読み、切断を読取側が検出できる閉鎖状態へ変換します。</remarks>
    /// <param name="connected">読み取るソケット。</param>
    /// <returns>読取ループの完了。</returns>
    private async Task ReceiveLoop(StreamSocket connected)
    {
        using var reader = new DataReader(connected.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        try
        {
            while (true)
            {
                var count = await reader.LoadAsync(512);
                if (count == 0)
                {
                    break;
                }

                var received = new byte[count];
                reader.ReadBytes(received);
                lock (gate)
                {
                    receiveBuffer.AddRange(received);
                }
            }
        }
        catch (Exception)
        {
            // 切断や Close によるソケット破棄で読取が失敗します。下の閉鎖状態として扱います。
        }

        lock (gate)
        {
            if (ReferenceEquals(socket, connected) || socket is null)
            {
                closed = true;
            }
        }
    }

    /// <summary>最初のプロンプトまでの受信済みバイトを取り出します。</summary>
    /// <remarks>責務: 同期保護された受信バッファーから最初の <c>&gt;</c> を含む1応答だけを消費します。</remarks>
    /// <returns>完成した1応答。プロンプト未受信の場合は <c>null</c>。</returns>
    private byte[]? TakeResponseThroughPrompt()
    {
        lock (gate)
        {
            var promptIndex = receiveBuffer.IndexOf(0x3E);
            if (promptIndex < 0)
            {
                return null;
            }

            var response = receiveBuffer.GetRange(0, promptIndex + 1).ToArray();
            receiveBuffer.RemoveRange(0, promptIndex + 1);
            return response;
        }
    }

    /// <summary>接続失敗の識別エラーを生成します。</summary>
    /// <remarks>責務: RFCOMM境界の失敗を共通の型付きエラーへ変換します。</remarks>
    /// <returns>送出できる識別エラー。</returns>
    private static VehicleIdentificationException ConnectionFailed() =>
        new(new VehicleIdentificationError.ConnectionFailed());
}
