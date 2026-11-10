using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.Ports;

/// <summary>保存済みBLEアダプターが接続可能になったことを監視する能力です。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public interface IDefaultAdapterArrivalMonitoring
{
    /// <summary>指定された既定アダプターの到来監視を開始します。</summary>
    /// <remarks>責務: 1件の既定BLE識別情報を到来通知へ結び付けます。副作用: プラットフォームのBluetooth監視を開始します。</remarks>
    /// <param name="preference">監視対象の既定アダプター設定。</param>
    /// <param name="arrival">対象アダプターを検出したときに通知する処理。</param>
    void StartMonitoring(DefaultAdapterPreference preference, Action<OBDConnectionEndpoint> arrival);

    /// <summary>現在の既定アダプター到来監視を終了します。</summary>
    /// <remarks>責務: 進行中の既定BLE監視を停止します。副作用: プラットフォームのBluetooth監視を停止します。</remarks>
    void StopMonitoring();
}
