using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.UseCases;

/// <summary>保存済みBLEアダプターの到来をユーザー承認付き接続要求へ変換します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class PromptForDefaultAdapterConnectionUseCase
{
    /// <summary>保存済み既定アダプターを読み込むユースケースです。</summary>
    private readonly DefaultAdapterPreferenceUseCase defaultAdapterPreference;

    /// <summary>BLEアダプターの到来を監視する境界です。</summary>
    private readonly IDefaultAdapterArrivalMonitoring arrivalMonitor;

    /// <summary>ユーザーへ接続確認を提示する境界です。</summary>
    private readonly IDefaultAdapterConnectionPrompting connectionPrompt;

    /// <summary>承認済み終端を接続ワークフローへ渡す処理です。</summary>
    private readonly Action<OBDConnectionEndpoint> connectionRequested;

    /// <summary>通知準備の世代を識別する値です。</summary>
    private int preparationGeneration;

    /// <summary>既定設定、到来監視、確認通知を注入して生成します。</summary>
    /// <remarks>責務: 既定BLE到来からユーザー承認済み接続要求までの依存関係を固定します。</remarks>
    /// <param name="defaultAdapterPreference">保存済み既定アダプターの読込元。</param>
    /// <param name="arrivalMonitor">BLE到来監視境界。</param>
    /// <param name="connectionPrompt">接続確認通知境界。</param>
    /// <param name="connectionRequested">ユーザーが接続を了承した終端の通知先。</param>
    public PromptForDefaultAdapterConnectionUseCase(
        DefaultAdapterPreferenceUseCase defaultAdapterPreference,
        IDefaultAdapterArrivalMonitoring arrivalMonitor,
        IDefaultAdapterConnectionPrompting connectionPrompt,
        Action<OBDConnectionEndpoint> connectionRequested)
    {
        this.defaultAdapterPreference = defaultAdapterPreference;
        this.arrivalMonitor = arrivalMonitor;
        this.connectionPrompt = connectionPrompt;
        this.connectionRequested = connectionRequested;
    }

    /// <summary>保存済みBLEアダプターへの到来監視と接続確認通知を有効化します。</summary>
    /// <remarks>責務: 現在の既定BLE設定を通知許可済みの到来監視へ反映します。</remarks>
    public void Start()
    {
        unchecked
        {
            preparationGeneration += 1;
        }

        var generation = preparationGeneration;
        arrivalMonitor.StopMonitoring();

        var preference = defaultAdapterPreference.Load();
        if (preference is null
            || preference.ConnectionTransport != OBDConnectionEndpointTransport.BluetoothLowEnergy)
        {
            return;
        }

        _ = PrepareAndMonitor(preference, generation);
    }

    /// <summary>既定BLEアダプターの到来監視を無効化します。</summary>
    /// <remarks>責務: 通知準備世代を無効化して現在の到来監視を停止します。</remarks>
    public void Stop()
    {
        unchecked
        {
            preparationGeneration += 1;
        }

        arrivalMonitor.StopMonitoring();
    }

    /// <summary>通知許可を準備し、最新世代のときだけ到来監視を開始します。</summary>
    /// <remarks>責務: Swift の準備タスク本体として、通知許可と世代を確かめてから監視を開始します。</remarks>
    /// <param name="preference">監視対象の既定アダプター設定。</param>
    /// <param name="generation">開始時に記録した通知準備世代。</param>
    /// <returns>準備と監視開始の完了を表すタスク。</returns>
    private async Task PrepareAndMonitor(DefaultAdapterPreference preference, int generation)
    {
        if (!await connectionPrompt.Prepare(connectionRequested) || generation != preparationGeneration)
        {
            return;
        }

        arrivalMonitor.StartMonitoring(preference, endpoint => connectionPrompt.Present(endpoint));
    }
}
