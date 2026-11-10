using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.DeviceConnection.UseCases;

/// <summary>既定BLE到来を通知承認付き接続要求へ変換するユースケースを検証します。</summary>
public sealed class PromptForDefaultAdapterConnectionUseCaseTests
{
    /// <summary>通知許可後に既定BLE監視を開始し、了承された終端を接続要求へ渡すことを検証します。</summary>
    /// <remarks>責務: 許可済みBLE到来から接続了承までの正常経路を確認します。</remarks>
    [Fact]
    public async Task StartMonitorsBLEAndForwardsAcceptedEndpoint()
    {
        var preference = MakePreference(OBDConnectionEndpointTransport.BluetoothLowEnergy);
        var monitor = new DefaultAdapterArrivalMonitorSpy();
        var prompt = new DefaultAdapterConnectionPromptSpy(isAuthorized: true);
        OBDConnectionEndpoint? requestedEndpoint = null;
        var useCase = MakeUseCase(preference, monitor, prompt, endpoint => requestedEndpoint = endpoint);

        useCase.Start();
        await Task.Yield();
        var endpoint = new OBDConnectionEndpoint(
            transport: OBDConnectionEndpointTransport.BluetoothLowEnergy,
            systemIdentifier: preference.SystemIdentifier,
            displayName: preference.DisplayName);
        monitor.Arrival?.Invoke(endpoint);
        prompt.Acceptance?.Invoke(endpoint);

        Assert.Equal(preference, monitor.MonitoredPreference);
        Assert.Equal(endpoint, prompt.PresentedEndpoint);
        Assert.Equal(endpoint, requestedEndpoint);
    }

    /// <summary>通知が拒否されている場合にBLE監視を開始しないことを検証します。</summary>
    /// <remarks>責務: 通知不能状態がバックグラウンド監視を残さないことを確認します。</remarks>
    [Fact]
    public async Task StartDoesNotMonitorWhenNotificationsAreDenied()
    {
        var monitor = new DefaultAdapterArrivalMonitorSpy();
        var prompt = new DefaultAdapterConnectionPromptSpy(isAuthorized: false);
        var useCase = MakeUseCase(
            MakePreference(OBDConnectionEndpointTransport.BluetoothLowEnergy),
            monitor,
            prompt,
            _ => { });

        useCase.Start();
        await Task.Yield();

        Assert.Null(monitor.MonitoredPreference);
        Assert.Equal(1, monitor.StopCallCount);
    }

    /// <summary>Bluetooth Classic設定ではBLE到来監視を開始しないことを検証します。</summary>
    /// <remarks>責務: iPhoneのBLE専用自動確認がClassic終端へ誤適用されないことを確認します。</remarks>
    [Fact]
    public async Task StartIgnoresBluetoothClassicPreference()
    {
        var monitor = new DefaultAdapterArrivalMonitorSpy();
        var prompt = new DefaultAdapterConnectionPromptSpy(isAuthorized: true);
        var useCase = MakeUseCase(
            MakePreference(OBDConnectionEndpointTransport.BluetoothClassic),
            monitor,
            prompt,
            _ => { });

        useCase.Start();
        await Task.Yield();

        Assert.Null(monitor.MonitoredPreference);
        Assert.Equal(0, prompt.PrepareCallCount);
    }

    /// <summary>指定した依存関係で到来確認ユースケースを生成します。</summary>
    /// <remarks>責務: 1件のテストシナリオ用依存関係を検証対象へ注入します。</remarks>
    /// <param name="preference">読込時に返す既定設定。</param>
    /// <param name="monitor">到来監視Spy。</param>
    /// <param name="prompt">接続確認Spy。</param>
    /// <param name="connectionRequested">接続了承の通知先。</param>
    /// <returns>注入済みの検証対象。</returns>
    private static PromptForDefaultAdapterConnectionUseCase MakeUseCase(
        DefaultAdapterPreference preference,
        DefaultAdapterArrivalMonitorSpy monitor,
        DefaultAdapterConnectionPromptSpy prompt,
        Action<OBDConnectionEndpoint> connectionRequested) =>
        new(
            defaultAdapterPreference: new DefaultAdapterPreferenceUseCase(
                new DefaultAdapterPreferencePortStub(preference)),
            arrivalMonitor: monitor,
            connectionPrompt: prompt,
            connectionRequested: connectionRequested);

    /// <summary>指定した物理終端の既定Bluetooth設定を生成します。</summary>
    /// <remarks>責務: BLEまたはClassicだけが異なる既定設定を1件構築します。</remarks>
    /// <param name="transport">設定へ保持する物理終端。</param>
    /// <returns>固定UUIDを持つ既定Bluetooth設定。</returns>
    private static DefaultAdapterPreference MakePreference(OBDConnectionEndpointTransport transport) =>
        new(
            adapterID: "bluetooth:test",
            transportMode: AdapterTransportMode.Bluetooth,
            connectionTransport: transport,
            displayName: "Test Adapter",
            systemIdentifier: "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

    /// <summary>到来確認テストへ固定の既定設定を返す保存境界です。</summary>
    private sealed class DefaultAdapterPreferencePortStub : IDefaultAdapterPreferencePort
    {
        /// <summary>読込時に返す既定設定です。</summary>
        private readonly DefaultAdapterPreference preference;

        /// <summary>固定設定を注入して生成します。</summary>
        /// <remarks>責務: 1件の既定設定を読込結果として固定します。</remarks>
        /// <param name="preference">読込時に返す設定。</param>
        public DefaultAdapterPreferencePortStub(DefaultAdapterPreference preference)
        {
            this.preference = preference;
        }

        /// <summary>注入済みの既定設定を返します。</summary>
        /// <remarks>責務: 既定設定読込要求へ固定値で応答します。</remarks>
        /// <returns>初期化時に注入された設定。</returns>
        public DefaultAdapterPreference? Load() => preference;

        /// <summary>テストでは使用しない保存要求を受け取ります。</summary>
        /// <remarks>責務: 保存副作用を持たないテスト境界を提供します。</remarks>
        /// <param name="preference">テストでは保持しない設定。</param>
        public void Save(DefaultAdapterPreference preference)
        {
        }
    }

    /// <summary>既定BLE到来監視の呼出しと通知先を記録します。</summary>
    private sealed class DefaultAdapterArrivalMonitorSpy : IDefaultAdapterArrivalMonitoring
    {
        /// <summary>最後に監視要求された既定設定です。</summary>
        public DefaultAdapterPreference? MonitoredPreference { get; private set; }

        /// <summary>監視停止の呼出し回数です。</summary>
        public int StopCallCount { get; private set; }

        /// <summary>到来をテストから送信する処理です。</summary>
        public Action<OBDConnectionEndpoint>? Arrival { get; private set; }

        /// <summary>監視対象と到来通知先を記録します。</summary>
        /// <remarks>責務: 1回の監視開始要求をテスト観測状態へ保存します。</remarks>
        /// <param name="preference">監視対象の既定設定。</param>
        /// <param name="arrival">到来時の通知先。</param>
        public void StartMonitoring(DefaultAdapterPreference preference, Action<OBDConnectionEndpoint> arrival)
        {
            MonitoredPreference = preference;
            Arrival = arrival;
        }

        /// <summary>監視停止の呼出しを記録します。</summary>
        /// <remarks>責務: 1回の停止要求を呼出し回数へ反映します。</remarks>
        public void StopMonitoring()
        {
            StopCallCount += 1;
            MonitoredPreference = null;
            Arrival = null;
        }
    }

    /// <summary>接続確認通知の許可、提示、了承を記録します。</summary>
    private sealed class DefaultAdapterConnectionPromptSpy : IDefaultAdapterConnectionPrompting
    {
        /// <summary>準備時に返す通知許可状態です。</summary>
        private readonly bool isAuthorized;

        /// <summary>固定通知許可状態を注入して生成します。</summary>
        /// <remarks>責務: 1件の通知許可結果を接続確認テストへ固定します。</remarks>
        /// <param name="isAuthorized">準備時に返す許可状態。</param>
        public DefaultAdapterConnectionPromptSpy(bool isAuthorized)
        {
            this.isAuthorized = isAuthorized;
        }

        /// <summary>通知準備の呼出し回数です。</summary>
        public int PrepareCallCount { get; private set; }

        /// <summary>最後に提示要求された接続終端です。</summary>
        public OBDConnectionEndpoint? PresentedEndpoint { get; private set; }

        /// <summary>接続了承をテストから送信する処理です。</summary>
        public Action<OBDConnectionEndpoint>? Acceptance { get; private set; }

        /// <summary>通知先を記録して固定許可状態を返します。</summary>
        /// <remarks>責務: 1回の通知準備要求を観測して固定許可結果へ変換します。</remarks>
        /// <param name="acceptance">接続了承時の通知先。</param>
        /// <returns>初期化時に指定された通知許可状態。</returns>
        public Task<bool> Prepare(Action<OBDConnectionEndpoint> acceptance)
        {
            PrepareCallCount += 1;
            Acceptance = acceptance;
            return Task.FromResult(isAuthorized);
        }

        /// <summary>提示要求された接続終端を記録します。</summary>
        /// <remarks>責務: 1件の通知提示要求をテスト観測状態へ保存します。</remarks>
        /// <param name="endpoint">提示対象の接続終端。</param>
        public void Present(OBDConnectionEndpoint endpoint)
        {
            PresentedEndpoint = endpoint;
        }
    }
}
