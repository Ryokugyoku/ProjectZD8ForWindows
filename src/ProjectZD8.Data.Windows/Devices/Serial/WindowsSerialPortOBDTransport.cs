using System.Diagnostics;
using System.IO.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;

namespace ProjectZD8.Data.Windows.Devices.Serial;

/// <summary>選択済みOBDのCOMポートを115200 bps、8N1で扱います。</summary>
/// <remarks>
/// iOS 版の <c>MacOS115200BaudOBDSerialTransport</c> に対応します。USBシリアル変換（OBDLink EX など）が作る
/// COM ポートを <see cref="SerialPort"/> で開きます。UI スレッドから1件ずつ呼ぶ前提です。
/// </remarks>
public sealed class WindowsSerialPortOBDTransport : IOBDCommandTransport
{
    /// <summary>診断ログに書く Transport の種類です。</summary>
    private const string Transport = OBDDiagnosticText.TransportSerial;

    /// <summary>通信速度です。</summary>
    private const int BaudRate = 115_200;

    /// <summary>選択されたCOMポート名です。</summary>
    private readonly string portName;

    /// <summary>1応答を待つ最大時間です。</summary>
    private readonly TimeSpan responseTimeout;

    /// <summary>現在開いているポートです。</summary>
    private SerialPort? port;

    /// <summary>COMポート名と応答期限を保持して生成します。</summary>
    /// <remarks>責務: 1件のCOM終端に必要な読取設定を固定します。</remarks>
    /// <param name="portName">Windows が報告したCOMポート名（例: <c>COM3</c>）。</param>
    /// <param name="responseTimeout"><c>&gt;</c> プロンプトを待つ最大時間。<c>null</c> の場合は12秒。</param>
    public WindowsSerialPortOBDTransport(string portName, TimeSpan? responseTimeout = null)
    {
        this.portName = portName;
        this.responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(12);
    }

    /// <summary>選択されたシリアル終端を115200 bps、8N1で開きます。</summary>
    /// <remarks>責務: 1件の検証済みCOMポートを115200 bpsの読書き状態へ遷移させます。</remarks>
    /// <returns>開く処理の完了。</returns>
    /// <exception cref="VehicleIdentificationException">ポート名不正、または開けない場合は <c>ConnectionFailed</c>。</exception>
    public Task Open()
    {
        if (port is not null || !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            OBDConnectionLog.TransportStep(OBDConnectionLog.Logger, Transport, "open", port is not null ? "already-open" : "invalid-port-name");
            throw ConnectionFailed();
        }

        var candidate = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            // POSIX の open と同じく DTR/RTS を上げた状態にします（macOS 版は termios で制御線を変えていません）。
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = (int)responseTimeout.TotalMilliseconds,
        };
        try
        {
            candidate.Open();
            candidate.DiscardInBuffer();
            candidate.DiscardOutBuffer();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // UnauthorizedAccessException はほかのアプリがポートを開いているときに起きます。
            OBDConnectionLog.TransportFailed(OBDConnectionLog.Logger, error, Transport, "open");
            candidate.Dispose();
            throw ConnectionFailed();
        }

        port = candidate;
        OBDConnectionLog.TransportStep(OBDConnectionLog.Logger, Transport, "open", "115200 bps 8N1、DTR/RTS あり");
        return Task.CompletedTask;
    }

    /// <summary>完全なASCIIコマンドをシリアル終端へ書き込みます。</summary>
    /// <remarks>責務: 1件のコマンドに属する全バイトを順序を保ってアダプターへ送ります。</remarks>
    /// <param name="data">復帰文字終端のASCIIコマンド。</param>
    /// <returns>書込みの完了。</returns>
    /// <exception cref="VehicleIdentificationException">切断、書込み失敗の場合は <c>ConnectionFailed</c>。</exception>
    public async Task Write(byte[] data)
    {
        var active = port ?? throw ConnectionFailed();
        try
        {
            await active.BaseStream.WriteAsync(data);
            await active.BaseStream.FlushAsync();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException)
        {
            OBDConnectionLog.TransportFailed(OBDConnectionLog.Logger, error, Transport, "write");
            throw ConnectionFailed();
        }
    }

    /// <summary>次のELM/STNプロンプトまでシリアルバイトを読み取ります。</summary>
    /// <remarks>責務: 現在コマンドの応答を最初の <c>&gt;</c> 境界まで欠落なく収集します（macOS 版と同じく10ミリ秒ごとに確認します）。</remarks>
    /// <returns><c>&gt;</c> を含む加工前応答。</returns>
    /// <exception cref="VehicleIdentificationException">期限切れは <c>ResponseTimedOut</c>、読取失敗は <c>ConnectionFailed</c>。</exception>
    public async Task<byte[]> ReadUntilPrompt()
    {
        var active = port ?? throw ConnectionFailed();
        var stopwatch = Stopwatch.StartNew();
        var response = new List<byte>();
        var buffer = new byte[512];
        while (stopwatch.Elapsed < responseTimeout)
        {
            if (!active.IsOpen)
            {
                OBDConnectionLog.TransportLost(OBDConnectionLog.Logger, Transport, "port-closed");
                throw ConnectionFailed();
            }

            int count;
            try
            {
                var available = active.BytesToRead;
                count = available > 0 ? active.Read(buffer, 0, Math.Min(buffer.Length, available)) : 0;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                OBDConnectionLog.TransportFailed(OBDConnectionLog.Logger, error, Transport, "read");
                throw ConnectionFailed();
            }

            if (count > 0)
            {
                response.AddRange(buffer.AsSpan(0, count));
                if (buffer.AsSpan(0, count).Contains((byte)0x3E))
                {
                    return response.ToArray();
                }
            }
            else
            {
                await Task.Delay(10);
            }
        }

        OBDConnectionLog.TransportTimeout(OBDConnectionLog.Logger, Transport, "read", (long)responseTimeout.TotalMilliseconds, response.Count);
        throw new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut());
    }

    /// <summary>開いているシリアル終端を閉じます。</summary>
    /// <remarks>責務: 現在のCOMポートを1回だけ閉じて無効化します。</remarks>
    /// <returns>閉じる処理の完了。</returns>
    public Task Close()
    {
        var active = port;
        port = null;
        if (active is not null)
        {
            try
            {
                active.Close();
            }
            catch (IOException error)
            {
                // 抜かれたUSB機器では閉じる時点で失敗することがあります。状態は破棄済みです。
                OBDConnectionLog.TransportFailed(OBDConnectionLog.Logger, error, Transport, "close");
            }

            active.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>接続失敗の識別エラーを生成します。</summary>
    /// <remarks>責務: シリアル境界の失敗を共通の型付きエラーへ変換します。</remarks>
    /// <returns>送出できる識別エラー。</returns>
    private static VehicleIdentificationException ConnectionFailed() =>
        new(new VehicleIdentificationError.ConnectionFailed());
}
