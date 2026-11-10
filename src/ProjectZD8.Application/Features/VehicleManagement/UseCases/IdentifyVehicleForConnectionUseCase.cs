using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.UseCases;

/// <summary>OBD識別結果を登録済み車両または新規登録候補へ振り分けます。</summary>
public sealed class IdentifyVehicleForConnectionUseCase
{
    /// <summary>実OBD識別を行う注入済み境界です。</summary>
    private readonly IVehicleIdentificationPort identification;

    /// <summary>OBD識別境界を注入して生成します。</summary>
    /// <remarks>責務: 車両識別取得能力を同種識別子照合ユースケースへ固定します。</remarks>
    /// <param name="identification">OBDから識別観測を取得する境界。</param>
    public IdentifyVehicleForConnectionUseCase(IVehicleIdentificationPort identification)
    {
        this.identification = identification;
    }

    /// <summary>車両識別子の照合結果としてApplicationへ返す分岐です。</summary>
    public abstract record Outcome
    {
        /// <summary>外部からの派生を防ぎます。</summary>
        /// <remarks>責務: 分岐の種類を下の2ケースに限定します。</remarks>
        private Outcome()
        {
        }

        /// <summary>同種の識別子が一致した登録済み車両です。</summary>
        /// <param name="Vehicle">一致した登録済み車両。</param>
        /// <param name="Snapshot">識別観測。</param>
        public sealed record Registered(VehicleProfile Vehicle, VehicleIdentificationSnapshot Snapshot) : Outcome;

        /// <summary>未登録の車両識別子を含む確認対象観測です。</summary>
        /// <param name="Snapshot">確認対象の識別観測。</param>
        public sealed record RequiresRegistration(VehicleIdentificationSnapshot Snapshot) : Outcome;
    }

    /// <summary>OBD観測のVINまたは非VIN識別子を現在の登録車両へ照合します。</summary>
    /// <remarks>
    /// 責務: 1回の識別観測を登録済み接続または新規登録確認へ振り分けます。
    /// 識別子の比較は Swift の <c>caseInsensitiveCompare</c> に合わせて <see cref="StringComparison.OrdinalIgnoreCase"/> で行います。
    /// </remarks>
    /// <param name="endpoint">接続するOBDアダプターの物理終端。</param>
    /// <param name="vehicles">現在のアカウントに属する登録車両。</param>
    /// <returns>登録済み車両または未登録観測。</returns>
    /// <exception cref="VehicleIdentificationException">VINと非VIN識別子のどちらも取得できない場合。</exception>
    public async Task<Outcome> Execute(OBDConnectionEndpoint endpoint, IReadOnlyList<VehicleProfile> vehicles)
    {
        var snapshot = await identification.IdentifyVehicle(endpoint);
        var vin = snapshot.Vin?.Trim();
        var obdIdentifier = snapshot.ObdIdentifier?.Trim();
        if (string.IsNullOrEmpty(vin) && string.IsNullOrEmpty(obdIdentifier))
        {
            throw new VehicleIdentificationException(new VehicleIdentificationError.VinUnavailable());
        }

        var registered = vehicles.FirstOrDefault(vehicle =>
        {
            if (!string.IsNullOrEmpty(vin))
            {
                return vehicle.Vin.Length != 0
                    && string.Equals(vehicle.Vin, vin, StringComparison.OrdinalIgnoreCase);
            }

            if (obdIdentifier is null || vehicle.ObdIdentifier is null)
            {
                return false;
            }

            return string.Equals(vehicle.ObdIdentifier, obdIdentifier, StringComparison.OrdinalIgnoreCase);
        });
        if (registered is not null)
        {
            return new Outcome.Registered(registered, snapshot);
        }

        return new Outcome.RequiresRegistration(snapshot);
    }
}
