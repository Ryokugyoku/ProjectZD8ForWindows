using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>実PID読取手段がないプラットフォームの明示的境界です。</summary>
public sealed class UnavailableOBDPIDTelemetryAdapter : IOBDPIDTelemetryPort
{
    /// <summary>全要求を利用不能として終了します。</summary>
    /// <remarks>責務: 実車PID読取未提供状態を仮成功に変換せず返します。</remarks>
    /// <param name="requests">実行しないPID要求。</param>
    /// <param name="endpoint">使用しない接続終端。</param>
    /// <returns>この実装は常に失敗するため値を返しません。</returns>
    /// <exception cref="OBDPIDTelemetryException">常に <c>Unavailable</c>。</exception>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) =>
        Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable));
}
