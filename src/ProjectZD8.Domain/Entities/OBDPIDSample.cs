using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>検証済み定義で数値化した1件のOBD PID観測です。</summary>
public sealed record OBDPIDSample
{
    /// <summary>Service/PIDの複合識別子です。</summary>
    public OBDPIDRequest Request { get; }
    /// <summary>表示名へ解決できる安定キーです。</summary>
    public string NameKey { get; }
    /// <summary>定義式を適用した数値です。</summary>
    public double Value { get; }
    /// <summary>定義に含まれる単位です。</summary>
    public string Unit { get; }
    /// <summary>車種専用PIDの場合に表示する型式です。</summary>
    public string? VehicleModelCode { get; }
    /// <summary>観測完了日時です。</summary>
    public DateTimeOffset ObservedAt { get; }
    /// <summary>項目概要のローカライズキーです。</summary>
    public string SummaryKey { get; }
    /// <summary>高値時説明のローカライズキーです。</summary>
    public string HighValueKey { get; }
    /// <summary>低値時説明のローカライズキーです。</summary>
    public string LowValueKey { get; }
    /// <summary>相関項目説明のローカライズキーです。</summary>
    public string CorrelationKey { get; }

    /// <summary>表示で使用するService/PID識別子です。</summary>
    public OBDPIDRequest Id => Request;

    /// <summary>数値化済みPID観測を生成します。</summary>
    /// <remarks>責務: 1件のService/PID数値を名称、単位、観測日時と同じ不変値へ固定します。</remarks>
    /// <param name="request">Service/PIDの複合識別子。</param>
    /// <param name="nameKey">ローカライズ用名称キー。</param>
    /// <param name="value">定義式適用後の数値。</param>
    /// <param name="unit">数値の単位。</param>
    /// <param name="observedAt">観測完了日時。</param>
    /// <param name="vehicleModelCode">車種専用PIDの適用型式。</param>
    /// <param name="summaryKey">項目概要のローカライズキー。</param>
    /// <param name="highValueKey">高値時説明のローカライズキー。</param>
    /// <param name="lowValueKey">低値時説明のローカライズキー。</param>
    /// <param name="correlationKey">相関項目説明のローカライズキー。</param>
    public OBDPIDSample(
        OBDPIDRequest request,
        string nameKey,
        double value,
        string unit,
        DateTimeOffset observedAt,
        string? vehicleModelCode = null,
        string summaryKey = "obd.pid.help.unconfirmed.summary",
        string highValueKey = "obd.pid.help.unconfirmed.high",
        string lowValueKey = "obd.pid.help.unconfirmed.low",
        string correlationKey = "obd.pid.help.unconfirmed.correlation")
    {
        Request = request;
        NameKey = nameKey;
        Value = value;
        Unit = unit;
        VehicleModelCode = vehicleModelCode;
        ObservedAt = observedAt;
        SummaryKey = summaryKey;
        HighValueKey = highValueKey;
        LowValueKey = lowValueKey;
        CorrelationKey = correlationKey;
    }
}
