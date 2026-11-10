using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence.Sqlite.Database;

/// <summary>ユーザー確認済みのZD8専用読取りPID定義を提供します。</summary>
public static class ZD8OBDPIDSeed
{
    /// <summary>重複なく識別できる走行距離とAT油温の定義です。</summary>
    public static IReadOnlyList<OBDPIDDefinition> Definitions { get; } =
    [
        new OBDPIDDefinition(
            service: 0x21,
            pid: 0x02,
            header: 0x7E0,
            vehicleModelCode: ZD8VehicleModelPolicy.ModelCode,
            nameKey: "obd.pid.zd8.21.02.name",
            requiredByteCount: 4,
            formula: "(A * 16777216 + B * 65536 + C * 256 + D) / 10",
            unit: "km",
            minimumValue: 0,
            maximumValue: 429496729.5,
            sourceURI: "projectzd8://requirements/zd8-pids/2026-07-23",
            revision: 1,
            summaryKey: "obd.pid.zd8.21.02.summary",
            highValueKey: "obd.pid.help.unconfirmed.high",
            lowValueKey: "obd.pid.help.unconfirmed.low",
            correlationKey: "obd.pid.zd8.21.02.correlation"),
        new OBDPIDDefinition(
            service: 0x21,
            pid: 0x17,
            header: 0x7E1,
            vehicleModelCode: ZD8VehicleModelPolicy.ModelCode,
            nameKey: "obd.pid.zd8.21.17.name",
            requiredByteCount: 1,
            formula: "A - 58",
            unit: "°C",
            minimumValue: -58,
            maximumValue: 197,
            sourceURI: "projectzd8://requirements/zd8-pids/2026-07-23",
            revision: 1,
            summaryKey: "obd.pid.zd8.21.17.summary",
            highValueKey: "obd.pid.zd8.21.17.high",
            lowValueKey: "obd.pid.zd8.21.17.low",
            correlationKey: "obd.pid.zd8.21.17.correlation"),
    ];

    /// <summary>ZD8専用定義を改訂後退なしで登録します。</summary>
    /// <remarks>責務: 現在バンドルするZD8専用PID定義を永続化します。</remarks>
    /// <param name="repository">定義の保存先。</param>
    public static void Install(IOBDPIDDefinitionRepository repository)
    {
        foreach (var definition in Definitions)
        {
            repository.Upsert(definition);
        }
    }
}
