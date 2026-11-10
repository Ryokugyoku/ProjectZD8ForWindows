namespace ProjectZD8.Application.LiveTelemetry.Ports;

/// <summary>主要PID読取を完了できなかった理由です。</summary>
public enum OBDPIDTelemetryError
{
    /// <summary>PID定義DBが利用不能または空です。</summary>
    DefinitionCatalogUnavailable,

    /// <summary>実車読取実装が現在のプラットフォームにありません。</summary>
    Unavailable,

    /// <summary>要求PIDが固定許可リストにありません。</summary>
    UnsupportedPID,

    /// <summary>アダプターまたは車両が要求を拒否しました。</summary>
    CommandRejected,

    /// <summary>要求と一致する完全な正応答を復元できません。</summary>
    MalformedResponse,

    /// <summary>要求した全PIDの応答が揃いません。</summary>
    IncompleteResponse,

    /// <summary>要求したPID群に対するECU応答が1件もありません。</summary>
    NoVehicleResponse,

    /// <summary>アダプターまたは車両との通信境界が失われました。</summary>
    ConnectionLost,
}

/// <summary>主要PID読取を完了できなかったことを表す例外です。</summary>
/// <remarks>Swift の <c>OBDPIDTelemetryError</c> を <see cref="Reason"/> に持ちます。</remarks>
public sealed class OBDPIDTelemetryException : Exception
{
    /// <summary>読取を完了できなかった理由です。</summary>
    public OBDPIDTelemetryError Reason { get; }

    /// <summary>理由を指定して生成します。</summary>
    /// <remarks>責務: 1件の読取失敗理由を例外として送出できる形へ包みます。</remarks>
    /// <param name="reason">読取を完了できなかった理由。</param>
    public OBDPIDTelemetryException(OBDPIDTelemetryError reason)
        : base($"OBDPIDTelemetryError.{reason}")
    {
        Reason = reason;
    }
}
