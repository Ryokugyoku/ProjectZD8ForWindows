using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Policies;

/// <summary>動力区分と車両が保持するエネルギー源の整合規則です。</summary>
public static class VehicleEnergySourcePolicy
{
    /// <summary>指定した動力区分で利用可能なエネルギー源を返します。</summary>
    /// <remarks>責務: 1件の動力区分を対応するエネルギー源候補へ変換します。</remarks>
    /// <param name="powertrain">候補を求める動力区分。</param>
    /// <returns>指定した動力区分で利用可能なエネルギー源。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の動力区分の場合（Swift では発生しない）。</exception>
    public static IReadOnlyList<VehicleEnergySource> AvailableSources(VehiclePowertrainKind powertrain) => powertrain switch
    {
        VehiclePowertrainKind.Combustion =>
            new[] { VehicleEnergySource.GasolinePremium, VehicleEnergySource.GasolineRegular, VehicleEnergySource.Diesel },
        VehiclePowertrainKind.Hybrid or VehiclePowertrainKind.PlugInHybrid =>
            new[]
            {
                VehicleEnergySource.GasolinePremium,
                VehicleEnergySource.GasolineRegular,
                VehicleEnergySource.Diesel,
                VehicleEnergySource.Electricity,
            },
        VehiclePowertrainKind.BatteryElectric => new[] { VehicleEnergySource.Electricity },
        VehiclePowertrainKind.FuelCell => new[] { VehicleEnergySource.Hydrogen },
        VehiclePowertrainKind.Other => new[] { VehicleEnergySource.Other },
        _ => throw new ArgumentOutOfRangeException(nameof(powertrain), powertrain, null),
    };

    /// <summary>保持中のエネルギー源を動力区分で利用可能な範囲へ整えます。</summary>
    /// <remarks>責務: 利用可能な既存値を失わずに1件以上のエネルギー源を保証します。</remarks>
    /// <param name="sources">現在保持しているエネルギー源。</param>
    /// <param name="powertrain">適合先の動力区分。</param>
    /// <returns>既存順を保った利用可能値、または動力区分の既定値1件。</returns>
    public static IReadOnlyList<VehicleEnergySource> NormalizedSources(
        IReadOnlyList<VehicleEnergySource> sources,
        VehiclePowertrainKind powertrain)
    {
        var available = AvailableSources(powertrain);
        var retained = sources.Where(available.Contains).ToList();
        if (retained.Count != 0) { return retained; }
        if (powertrain == VehiclePowertrainKind.Hybrid || powertrain == VehiclePowertrainKind.PlugInHybrid)
        {
            return new[] { VehicleEnergySource.Electricity };
        }
        return new[] { available.Count > 0 ? available[0] : VehicleEnergySource.Other };
    }

    /// <summary>編集開始時に保存済みエネルギー源を非破壊で準備します。</summary>
    /// <remarks>責務: 保存済み値を変更せず、空の入力だけを動力区分の既定値で補完します。</remarks>
    /// <param name="sources">保存済みのエネルギー源。</param>
    /// <param name="powertrain">空の入力を補完する動力区分。</param>
    /// <returns>保存済み値、または動力区分の既定値1件。</returns>
    public static IReadOnlyList<VehicleEnergySource> SourcesForEditing(
        IReadOnlyList<VehicleEnergySource> sources,
        VehiclePowertrainKind powertrain)
    {
        if (sources.Count != 0) { return sources; }
        return NormalizedSources(Array.Empty<VehicleEnergySource>(), powertrain);
    }
}
