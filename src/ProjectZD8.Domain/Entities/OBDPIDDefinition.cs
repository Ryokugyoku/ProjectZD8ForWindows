namespace ProjectZD8.Domain.Entities;

/// <summary>1件の標準OBD PIDを数値へ変換するための永続化可能な定義です。</summary>
public sealed record OBDPIDDefinition
{
    /// <summary>OBD要求のService番号です。</summary>
    public byte Service { get; }
    /// <summary>Service内のPID番号です。</summary>
    public byte Pid { get; }
    /// <summary>送信先ECUを固定する11bit CANヘッダーです。標準機能アドレスの場合は <see langword="null"/> です。</summary>
    public ushort? Header { get; }
    /// <summary>この定義を使用できる車両型式です。標準PIDの場合は <see langword="null"/> です。</summary>
    public string? VehicleModelCode { get; }
    /// <summary>表示名へ解決できる安定キーです。</summary>
    public string NameKey { get; }
    /// <summary>数式が参照する応答データの必要バイト数です。</summary>
    public int? RequiredByteCount { get; }
    /// <summary><c>A</c>から<c>H</c>までのバイト変数を使用する制限付き数式です。</summary>
    public string? Formula { get; }
    /// <summary>計算結果の単位です。</summary>
    public string Unit { get; }
    /// <summary>規格上または定義上の最小値です。</summary>
    public double? MinimumValue { get; }
    /// <summary>規格上または定義上の最大値です。</summary>
    public double? MaximumValue { get; }
    /// <summary>定義を確認した一次資料のURI文字列です。</summary>
    public string SourceURI { get; }
    /// <summary>同じService/PID定義を更新するときの単調増加改訂番号です。</summary>
    public int Revision { get; }
    /// <summary>項目概要を表示するローカライズキーです。</summary>
    public string SummaryKey { get; }
    /// <summary>高値時の確認観点を表示するローカライズキーです。</summary>
    public string HighValueKey { get; }
    /// <summary>低値時の確認観点を表示するローカライズキーです。</summary>
    public string LowValueKey { get; }
    /// <summary>関連項目を表示するローカライズキーです。</summary>
    public string CorrelationKey { get; }

    /// <summary>数式を含むPID定義を生成します。</summary>
    /// <remarks>責務: 1件のPID識別子と変換契約を不変値として固定します。</remarks>
    /// <param name="service">OBD要求のService番号。</param>
    /// <param name="pid">Service内のPID番号。</param>
    /// <param name="nameKey">表示名の安定キー。</param>
    /// <param name="requiredByteCount">数式評価に必要なバイト数。</param>
    /// <param name="formula">制限付き数式。</param>
    /// <param name="unit">計算結果の単位。</param>
    /// <param name="minimumValue">許容最小値。</param>
    /// <param name="maximumValue">許容最大値。</param>
    /// <param name="sourceURI">一次資料のURI文字列。</param>
    /// <param name="revision">定義の改訂番号。</param>
    /// <param name="header">送信先ECUを固定する11bit CANヘッダー。</param>
    /// <param name="vehicleModelCode">この定義を使用できる車両型式。</param>
    /// <param name="summaryKey">項目概要のローカライズキー。</param>
    /// <param name="highValueKey">高値時説明のローカライズキー。</param>
    /// <param name="lowValueKey">低値時説明のローカライズキー。</param>
    /// <param name="correlationKey">相関項目説明のローカライズキー。</param>
    public OBDPIDDefinition(
        byte service,
        byte pid,
        string nameKey,
        int? requiredByteCount,
        string? formula,
        string unit,
        double? minimumValue,
        double? maximumValue,
        string sourceURI,
        int revision,
        ushort? header = null,
        string? vehicleModelCode = null,
        string summaryKey = "obd.pid.help.unconfirmed.summary",
        string highValueKey = "obd.pid.help.unconfirmed.high",
        string lowValueKey = "obd.pid.help.unconfirmed.low",
        string correlationKey = "obd.pid.help.unconfirmed.correlation")
    {
        Service = service;
        Pid = pid;
        Header = header;
        VehicleModelCode = vehicleModelCode;
        NameKey = nameKey;
        RequiredByteCount = requiredByteCount;
        Formula = formula;
        Unit = unit;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        SourceURI = sourceURI;
        Revision = revision;
        SummaryKey = summaryKey;
        HighValueKey = highValueKey;
        LowValueKey = lowValueKey;
        CorrelationKey = correlationKey;
    }

    /// <summary>定義式で安全に数値化できるPIDかどうかです。</summary>
    public bool IsDecodable => RequiredByteCount is not null && Formula is { Length: > 0 };

    /// <summary>特定車両だけへ要求できる拡張PIDかどうかです。</summary>
    public bool IsVehicleSpecific => Header is not null && VehicleModelCode is { Length: > 0 };
}
