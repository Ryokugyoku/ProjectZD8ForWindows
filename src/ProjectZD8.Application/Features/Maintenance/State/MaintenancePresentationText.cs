using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Application.Maintenance.State;

/// <summary>整備区分・部品・作業種別を固定ローカライズキーへ写像します。</summary>
/// <remarks>Swift では各列挙の extension でしたが、C# では拡張メソッドにまとめています。</remarks>
public static class MaintenancePresentationText
{
    /// <summary>整備区分の画面表示に使用する固定String Catalogキーです。</summary>
    /// <remarks>責務: 1件の整備区分を固定ローカライズキーへ変換します。</remarks>
    /// <param name="kind">変換する整備区分。</param>
    /// <returns>ローカライズキー。</returns>
    public static string LocalizationKey(this MaintenanceKind kind) => kind switch
    {
        MaintenanceKind.Light => "maintenance.kind.light",
        MaintenanceKind.Heavy => "maintenance.kind.heavy",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>整備部品の画面表示に使用する固定String Catalogキーです。</summary>
    /// <remarks>責務: 1件の整備部品を固定ローカライズキーへ変換します。</remarks>
    /// <param name="component">変換する整備部品。</param>
    /// <returns>ローカライズキー。</returns>
    public static string LocalizationKey(this MaintenanceComponent component) => component switch
    {
        MaintenanceComponent.EngineOil => "maintenance.component.engine_oil",
        MaintenanceComponent.OilFilter => "maintenance.component.oil_filter",
        MaintenanceComponent.AirFilter => "maintenance.component.air_filter",
        MaintenanceComponent.Tires => "maintenance.component.tires",
        MaintenanceComponent.BrakePads => "maintenance.component.brake_pads",
        MaintenanceComponent.AuxiliaryBattery => "maintenance.component.auxiliary_battery",
        MaintenanceComponent.Wipers => "maintenance.component.wipers",
        MaintenanceComponent.Coolant => "maintenance.component.coolant",
        MaintenanceComponent.TransmissionFluid => "maintenance.component.transmission_fluid",
        MaintenanceComponent.DifferentialFluid => "maintenance.component.differential_fluid",
        MaintenanceComponent.SparkPlugs => "maintenance.component.spark_plugs",
        MaintenanceComponent.Belts => "maintenance.component.belts",
        MaintenanceComponent.Bulbs => "maintenance.component.bulbs",
        MaintenanceComponent.EngineAssembly => "maintenance.component.engine_assembly",
        MaintenanceComponent.Transmission => "maintenance.component.transmission",
        MaintenanceComponent.Driveline => "maintenance.component.driveline",
        MaintenanceComponent.Suspension => "maintenance.component.suspension",
        MaintenanceComponent.Steering => "maintenance.component.steering",
        MaintenanceComponent.BrakingSystem => "maintenance.component.braking_system",
        MaintenanceComponent.Exhaust => "maintenance.component.exhaust",
        MaintenanceComponent.FuelSystem => "maintenance.component.fuel_system",
        MaintenanceComponent.CoolingSystem => "maintenance.component.cooling_system",
        MaintenanceComponent.Electrical => "maintenance.component.electrical",
        MaintenanceComponent.AirConditioning => "maintenance.component.air_conditioning",
        MaintenanceComponent.SafetySystem => "maintenance.component.safety_system",
        MaintenanceComponent.HighVoltageBattery => "maintenance.component.high_voltage_battery",
        MaintenanceComponent.TractionMotor => "maintenance.component.traction_motor",
        MaintenanceComponent.BodyFrame => "maintenance.component.body_frame",
        MaintenanceComponent.Other => "maintenance.component.other",
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, null),
    };

    /// <summary>整備作業種別の画面表示に使用する固定String Catalogキーです。</summary>
    /// <remarks>責務: 1件の整備作業種別を固定ローカライズキーへ変換します。</remarks>
    /// <param name="operation">変換する整備作業種別。</param>
    /// <returns>ローカライズキー。</returns>
    public static string LocalizationKey(this MaintenanceOperation operation) => operation switch
    {
        MaintenanceOperation.Inspection => "maintenance.operation.inspection",
        MaintenanceOperation.Replacement => "maintenance.operation.replacement",
        MaintenanceOperation.Repair => "maintenance.operation.repair",
        MaintenanceOperation.Adjustment => "maintenance.operation.adjustment",
        MaintenanceOperation.Overhaul => "maintenance.operation.overhaul",
        MaintenanceOperation.Cleaning => "maintenance.operation.cleaning",
        MaintenanceOperation.Diagnosis => "maintenance.operation.diagnosis",
        MaintenanceOperation.Fabrication => "maintenance.operation.fabrication",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };
}
