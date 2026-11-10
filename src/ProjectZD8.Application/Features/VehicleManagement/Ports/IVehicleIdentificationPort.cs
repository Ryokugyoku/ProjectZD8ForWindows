using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.Ports;

/// <summary>指定OBD終端へ接続して車両識別情報を取得する能力です。</summary>
public interface IVehicleIdentificationPort
{
    /// <summary>指定されたOBD接続終端から利用可能な識別情報を取得します。</summary>
    /// <remarks>責務: 1件のOBD終端に対する識別要求を改変しない観測結果として返します。</remarks>
    /// <param name="endpoint">ユーザーが選択したOBDアダプターの物理終端。</param>
    /// <returns>VIN候補と取得できた全表示可能フィールド。</returns>
    Task<VehicleIdentificationSnapshot> IdentifyVehicle(OBDConnectionEndpoint endpoint);
}
