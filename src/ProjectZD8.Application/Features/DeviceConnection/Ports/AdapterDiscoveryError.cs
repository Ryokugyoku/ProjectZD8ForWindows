namespace ProjectZD8.Application.DeviceConnection.Ports;

/// <summary>アダプター探索を実行できない理由をApplication境界で表します。</summary>
public enum AdapterDiscoveryError
{
    /// <summary>実行環境が要求された接続方式を製品機能として提供していません。</summary>
    TransportUnsupported,

    /// <summary>Bluetoothがシステム設定で無効です。</summary>
    BluetoothPoweredOff,

    /// <summary>Bluetooth利用が許可されていません。</summary>
    BluetoothUnauthorized,

    /// <summary>実行環境がBluetooth Low Energy中央デバイス機能に対応していません。</summary>
    BluetoothUnsupported,

    /// <summary>Bluetoothの準備状態を期限内に確定できませんでした。</summary>
    BluetoothStateUnavailable,
}

/// <summary>アダプター探索を実行できないことを表す例外です。</summary>
public sealed class AdapterDiscoveryException : Exception
{
    /// <summary>アダプター探索を実行できない理由です。</summary>
    public AdapterDiscoveryError Reason { get; }

    /// <summary>失敗理由を指定して例外を生成します。</summary>
    /// <remarks>責務: 1件のアダプター探索の失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">アダプター探索を実行できない理由。</param>
    public AdapterDiscoveryException(AdapterDiscoveryError reason)
        : base($"{nameof(AdapterDiscoveryError)}.{reason}")
    {
        Reason = reason;
    }
}
