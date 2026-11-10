using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.LiveTelemetry.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>
/// ホーム画面の既定アダプターと接続操作の表示状態です（iOS 版の <c>MacOSHomeState</c>）。
/// </summary>
public sealed record WindowsHomeState
{
    /// <summary>設定画面とライブ取得の状態から、ホームの表示状態を作ります。</summary>
    /// <param name="settingsState">設定画面の状態。</param>
    /// <param name="liveTelemetryState">ライブ取得の状態。</param>
    public WindowsHomeState(WindowsSettingsState settingsState, LiveTelemetryState liveTelemetryState)
    {
        settingsState.SelectedAdapters.TryGetValue(AdapterConnectionRole.Primary, out var primary);
        DefaultAdapterAvailability = DefaultAdapterAvailability.Create(settingsState.DefaultAdapterPreference, primary);
        ConnectionEndpoint = primary is not null
            && settingsState.DefaultAdapterPreference?.Matches(primary) == true
            && primary.TransportMode == AdapterTransportMode.Usb
            ? new OBDConnectionEndpoint(primary)
            : null;
        IsConnectionActive = liveTelemetryState.IsConnectionActive;
        IsDisconnecting = liveTelemetryState.IsDisconnecting;
    }

    /// <summary>既定アダプターの設定と検出の状態です。</summary>
    public DefaultAdapterAvailability DefaultAdapterAvailability { get; }

    /// <summary>接続に使える既定アダプターの接続先です（USB で検出済みのときだけ）。</summary>
    public OBDConnectionEndpoint? ConnectionEndpoint { get; }

    /// <summary>車両と接続中かどうかです。</summary>
    public bool IsConnectionActive { get; }

    /// <summary>切断中かどうかです。</summary>
    public bool IsDisconnecting { get; }
}
