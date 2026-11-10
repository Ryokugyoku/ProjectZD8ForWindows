using System.Collections.Generic;
using System.Globalization;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】iOS 版の <c>StandardOBDPIDSeed</c> のうち、変換式を確認済みの Service 01 PID の一部を写した初期データです。
/// </summary>
/// <remarks>
/// 画面の確認用に、主要な PID だけを選んでいます。永続化担当が Data 層に本物の seed
/// （掲載だけの PID や ZD8 専用 PID を含む全件）を移したら、このファイルは消します。
/// </remarks>
public static class InterimStandardOBDPIDSeed
{
    /// <summary>出典の URI です。</summary>
    private const string SourceURI = "https://www.csselectronics.com/pages/obd2-pid-table-on-board-diagnostics-j1979";

    /// <summary>確認済みの PID 定義です（PID 番号の昇順）。値は iOS 版の seed と同じです。</summary>
    public static IReadOnlyList<OBDPIDDefinition> Definitions { get; } =
    [
        Definition(0x04, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x05, 1, "A - 40", "°C", -40, 215),
        Definition(0x06, 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x07, 1, "(A - 128) * 100 / 128", "%", -100, 99.22),
        Definition(0x0A, 1, "A * 3", "kPa", 0, 765),
        Definition(0x0B, 1, "A", "kPa", 0, 255),
        Definition(0x0C, 2, "(A * 256 + B) / 4", "rpm", 0, 16383.75),
        Definition(0x0D, 1, "A", "km/h", 0, 255),
        Definition(0x0E, 1, "A / 2 - 64", "°", -64, 63.5),
        Definition(0x0F, 1, "A - 40", "°C", -40, 215),
        Definition(0x10, 2, "(A * 256 + B) / 100", "g/s", 0, 655.35),
        Definition(0x11, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x14, 2, "A / 200", "V", 0, 1.275),
        Definition(0x1F, 2, "A * 256 + B", "s", 0, 65535),
        Definition(0x21, 2, "A * 256 + B", "km", 0, 65535),
        Definition(0x2C, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x2F, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x31, 2, "A * 256 + B", "km", 0, 65535),
        Definition(0x33, 1, "A", "kPa", 0, 255),
        Definition(0x3C, 2, "(A * 256 + B) / 10 - 40", "°C", -40, 6513.5),
        Definition(0x42, 2, "(A * 256 + B) / 1000", "V", 0, 65.535),
        Definition(0x43, 2, "(A * 256 + B) * 100 / 255", "%", 0, 25700),
        Definition(0x45, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x46, 1, "A - 40", "°C", -40, 215),
        Definition(0x47, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x49, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x4C, 1, "A * 100 / 255", "%", 0, 100),
        Definition(0x5C, 1, "A - 40", "°C", -40, 215),
        Definition(0x5E, 2, "(A * 256 + B) * 0.05", "L/h", 0, 3276.75),
        Definition(0x61, 1, "A - 125", "%", -125, 130),
        Definition(0x62, 1, "A - 125", "%", -125, 130),
        Definition(0xA6, 4, "(A * 16777216 + B * 65536 + C * 256 + D) / 10", "km", 0, 429496729.5),
    ];

    /// <summary>
    /// 1 件の Service 01 PID 定義を作ります。
    /// </summary>
    /// <remarks>責務: iOS 版と同じ名前キー・解説キー・改訂番号で 1 件の定義を組み立てます。</remarks>
    /// <param name="pid">PID 番号。</param>
    /// <param name="requiredByteCount">必要なデータバイト数。</param>
    /// <param name="formula">変換式。</param>
    /// <param name="unit">単位。</param>
    /// <param name="minimumValue">最小値。</param>
    /// <param name="maximumValue">最大値。</param>
    /// <returns>PID 定義。</returns>
    private static OBDPIDDefinition Definition(
        byte pid,
        int requiredByteCount,
        string formula,
        string unit,
        double minimumValue,
        double maximumValue)
    {
        var category = OBDPIDCategoryExtensions.Category(new OBDPIDRequest(0x01, pid)).ToRawValue();
        return new OBDPIDDefinition(
            service: 0x01,
            pid: pid,
            nameKey: "obd.pid.01." + pid.ToString("X2", CultureInfo.InvariantCulture) + ".name",
            requiredByteCount: requiredByteCount,
            formula: formula,
            unit: unit,
            minimumValue: minimumValue,
            maximumValue: maximumValue,
            sourceURI: SourceURI,
            revision: 2,
            summaryKey: $"obd.pid.help.{category}.summary",
            highValueKey: $"obd.pid.help.{category}.high",
            lowValueKey: $"obd.pid.help.{category}.low",
            correlationKey: $"obd.pid.help.{category}.correlation");
    }
}
