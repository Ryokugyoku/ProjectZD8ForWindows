using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// 車両エネルギー源の動力区分別整合規則を検証します。
/// </summary>
public sealed class VehicleEnergySourcePolicyTests
{
    /// <summary>
    /// ハイブリッドが保持する燃料と電力を正規化で失わないことを検証します。
    /// </summary>
    [Fact]
    public void TestNormalizationPreservesMultipleValidHybridSources()
    {
        var sources = VehicleEnergySourcePolicy.NormalizedSources(
            new[] { VehicleEnergySource.GasolineRegular, VehicleEnergySource.Electricity },
            VehiclePowertrainKind.Hybrid);

        Assert.Equal(new[] { VehicleEnergySource.GasolineRegular, VehicleEnergySource.Electricity }, sources);
    }

    /// <summary>
    /// 動力区分に適合しない既存値だけを除外することを検証します。
    /// </summary>
    [Fact]
    public void TestNormalizationRemovesOnlyUnavailableSources()
    {
        var sources = VehicleEnergySourcePolicy.NormalizedSources(
            new[] { VehicleEnergySource.Hydrogen, VehicleEnergySource.GasolinePremium, VehicleEnergySource.Electricity },
            VehiclePowertrainKind.PlugInHybrid);

        Assert.Equal(new[] { VehicleEnergySource.GasolinePremium, VehicleEnergySource.Electricity }, sources);
    }

    /// <summary>
    /// ハイブリッドに利用可能な既存値がない場合の既定値を検証します。
    /// </summary>
    [Fact]
    public void TestNormalizationDefaultsHybridToElectricity()
    {
        var sources = VehicleEnergySourcePolicy.NormalizedSources(
            new[] { VehicleEnergySource.Hydrogen },
            VehiclePowertrainKind.Hybrid);

        Assert.Equal(new[] { VehicleEnergySource.Electricity }, sources);
    }

    /// <summary>
    /// 編集開始時にPicker候補外の保存済み値も失わないことを検証します。
    /// </summary>
    [Fact]
    public void TestEditingPreparationPreservesExistingSourcesOutsidePickerCandidates()
    {
        var sources = VehicleEnergySourcePolicy.SourcesForEditing(
            new[] { VehicleEnergySource.Lpg, VehicleEnergySource.Cng },
            VehiclePowertrainKind.Combustion);

        Assert.Equal(new[] { VehicleEnergySource.Lpg, VehicleEnergySource.Cng }, sources);
    }
}
