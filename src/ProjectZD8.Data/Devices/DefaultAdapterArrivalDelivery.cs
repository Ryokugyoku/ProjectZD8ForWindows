using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices;

/// <summary>既定アダプターの到来通知を、監視の世代ごとに高々1回だけ呼び出し元のスレッドへ届けます。</summary>
/// <remarks>
/// iOS 版の <c>IOSCoreBluetoothDefaultAdapterArrivalMonitor</c> のうち、OS に依存しない照合と重複防止の部分です。
/// Windows のデバイス監視はバックグラウンドのスレッドから通知するため、ロックで状態を守り、
/// 開始時の <see cref="SynchronizationContext"/>（UI スレッド）へ通知を戻します。
/// </remarks>
public sealed class DefaultAdapterArrivalDelivery
{
    /// <summary>状態を守るロックです。</summary>
    private readonly Lock gate = new();

    /// <summary>現在監視している既定アダプター設定です。</summary>
    private DefaultAdapterPreference? preference;

    /// <summary>対象を検出したときの通知先です。</summary>
    private Action<OBDConnectionEndpoint>? arrival;

    /// <summary>通知を戻す先の同期コンテキストです。</summary>
    private SynchronizationContext? context;

    /// <summary>現在の監視世代で到来通知を送信済みかどうかです。</summary>
    private bool hasDeliveredArrival;

    /// <summary>監視を始めるたびに増える世代番号です。</summary>
    private ulong generation;

    /// <summary>監視の対象にできる設定かを返します。</summary>
    /// <remarks>責務: Windows で到来を検出できる物理方式（USBシリアルとBLE）と識別子の形だけを受理します。</remarks>
    /// <param name="preference">判定する既定アダプター設定。</param>
    /// <returns>監視できる場合は <c>true</c>。</returns>
    public static bool IsMonitorable(DefaultAdapterPreference preference) => preference.ConnectionTransport switch
    {
        OBDConnectionEndpointTransport.Serial =>
            preference.SystemIdentifier.StartsWith("COM", StringComparison.OrdinalIgnoreCase),
        OBDConnectionEndpointTransport.BluetoothLowEnergy =>
            BluetoothAddressText.TryParse(preference.SystemIdentifier, out _),
        _ => false,
    };

    /// <summary>新しい監視世代を始めます。</summary>
    /// <remarks>責務: 1件の既定設定と通知先を、未通知の新しい世代として保持します。</remarks>
    /// <param name="preference">監視対象の既定設定。</param>
    /// <param name="arrival">対象を検出したときの通知先。</param>
    /// <param name="context">通知を戻す同期コンテキスト。<c>null</c> の場合は検出したスレッドで呼びます。</param>
    /// <returns>始めた世代の番号。</returns>
    public ulong Start(
        DefaultAdapterPreference preference,
        Action<OBDConnectionEndpoint> arrival,
        SynchronizationContext? context)
    {
        lock (gate)
        {
            this.preference = preference;
            this.arrival = arrival;
            this.context = context;
            hasDeliveredArrival = false;
            generation += 1;
            return generation;
        }
    }

    /// <summary>監視の状態と通知先を破棄します。</summary>
    /// <remarks>責務: 以後の検出を通知しない空の状態へ戻します。</remarks>
    public void Stop()
    {
        lock (gate)
        {
            preference = null;
            arrival = null;
            context = null;
            hasDeliveredArrival = false;
            generation += 1;
        }
    }

    /// <summary>検出した機器が既定設定と一致すれば到来を通知します。</summary>
    /// <remarks>責務: 1件の検出を現在の世代の既定設定と照合し、重複のない1件の接続終端通知へ変換します。</remarks>
    /// <param name="observedGeneration">検出した監視の世代番号。</param>
    /// <param name="transport">検出した機器の物理方式。</param>
    /// <param name="systemIdentifier">検出した機器のシステム識別子。</param>
    /// <returns>通知した場合は <c>true</c>。</returns>
    public bool TryDeliver(ulong observedGeneration, OBDConnectionEndpointTransport transport, string systemIdentifier)
    {
        Action<OBDConnectionEndpoint> callback;
        SynchronizationContext? target;
        OBDConnectionEndpoint endpoint;
        lock (gate)
        {
            if (observedGeneration != generation
                || hasDeliveredArrival
                || preference is null
                || arrival is null
                || !Matches(preference, transport, systemIdentifier))
            {
                return false;
            }

            hasDeliveredArrival = true;
            callback = arrival;
            target = context;
            endpoint = new OBDConnectionEndpoint(preference.ConnectionTransport, preference.SystemIdentifier, preference.DisplayName);
        }

        if (target is null)
        {
            callback(endpoint);
        }
        else
        {
            target.Post(_ => callback(endpoint), null);
        }

        return true;
    }

    /// <summary>検出した機器が既定設定と同じかを返します。</summary>
    /// <remarks>責務: COMポート名は大文字小文字を無視し、Bluetooth アドレスは数値として比較します。</remarks>
    /// <param name="preference">既定設定。</param>
    /// <param name="transport">検出した物理方式。</param>
    /// <param name="systemIdentifier">検出したシステム識別子。</param>
    /// <returns>一致する場合は <c>true</c>。</returns>
    private static bool Matches(DefaultAdapterPreference preference, OBDConnectionEndpointTransport transport, string systemIdentifier)
    {
        if (preference.ConnectionTransport != transport)
        {
            return false;
        }

        return transport switch
        {
            OBDConnectionEndpointTransport.Serial =>
                string.Equals(preference.SystemIdentifier, systemIdentifier, StringComparison.OrdinalIgnoreCase),
            OBDConnectionEndpointTransport.BluetoothLowEnergy =>
                BluetoothAddressText.TryParse(preference.SystemIdentifier, out var expected)
                && BluetoothAddressText.TryParse(systemIdentifier, out var observed)
                && expected == observed,
            _ => false,
        };
    }
}
