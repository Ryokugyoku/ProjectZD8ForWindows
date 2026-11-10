using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>ホーム画面の操作です（iOS 版の <c>MacOSHomeAction</c>）。</summary>
public abstract record WindowsHomeAction
{
    /// <summary>アダプター設定へ移ります。</summary>
    public sealed record AdapterSetupRequested : WindowsHomeAction;

    /// <summary>車両との接続を始めます。</summary>
    /// <param name="Endpoint">接続先。</param>
    public sealed record VehicleConnectionRequested(OBDConnectionEndpoint Endpoint) : WindowsHomeAction;

    /// <summary>車両との接続を切ります。</summary>
    public sealed record VehicleDisconnectionRequested : WindowsHomeAction;
}
