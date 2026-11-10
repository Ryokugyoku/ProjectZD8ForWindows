using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.UseCases;

/// <summary>HOME接続要求から車両接続開始ワークフローを起動します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class StartVehicleConnectionUseCase
{
    /// <summary>車両識別開始を通知する注入済み処理です。</summary>
    private readonly Action<OBDConnectionEndpoint> identifyVehicle;

    /// <summary>車両識別の通知先を保持して生成します。</summary>
    /// <remarks>責務: HOME接続開始を構成する車両識別通知先を固定します。</remarks>
    /// <param name="identifyVehicle">VehicleManagementへ識別開始を通知する処理。</param>
    public StartVehicleConnectionUseCase(Action<OBDConnectionEndpoint> identifyVehicle)
    {
        this.identifyVehicle = identifyVehicle;
    }

    /// <summary>指定終端に対する車両識別を開始します。</summary>
    /// <remarks>責務: 1件のHOME接続要求を車両確定前の識別段階へ進めます。</remarks>
    /// <param name="endpoint">接続対象として選択されたOBD終端。</param>
    public void Execute(OBDConnectionEndpoint endpoint)
    {
        identifyVehicle(endpoint);
    }
}
