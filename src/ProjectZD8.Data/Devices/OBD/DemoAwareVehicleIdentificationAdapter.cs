using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>デモ終端と実終端を対応する車両識別境界へ振り分けます。</summary>
public sealed class DemoAwareVehicleIdentificationAdapter : IVehicleIdentificationPort
{
    /// <summary>実OBD通信を行う識別境界です。</summary>
    private readonly IVehicleIdentificationPort live;

    /// <summary>物理通信なしで観測を返す識別境界です。</summary>
    private readonly IVehicleIdentificationPort demo;

    /// <summary>実通信境界とデモ境界を保持して生成します。</summary>
    /// <remarks>責務: 2種類の車両識別境界を終端識別子による振分けへ固定します。</remarks>
    /// <param name="live">実終端を処理する識別境界。</param>
    /// <param name="demo">デモ終端を処理する識別境界。</param>
    public DemoAwareVehicleIdentificationAdapter(IVehicleIdentificationPort live, IVehicleIdentificationPort demo)
    {
        this.live = live;
        this.demo = demo;
    }

    /// <summary>終端識別子に対応する識別境界を実行します。</summary>
    /// <remarks>責務: 1件のOBD終端をデモまたは実車の識別処理へ振り分けます。</remarks>
    /// <param name="endpoint">識別対象のOBD接続終端。</param>
    /// <returns>選択した境界が返した車両識別観測。</returns>
    public Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint) =>
        DemoOBDAdapter.Matches(endpoint) ? demo.IdentifyVehicle(endpoint) : live.IdentifyVehicle(endpoint);
}
