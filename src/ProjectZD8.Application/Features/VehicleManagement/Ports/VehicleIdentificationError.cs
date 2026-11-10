namespace ProjectZD8.Application.VehicleManagement.Ports;

/// <summary>車両識別処理のうち失敗した単一段階です。</summary>
/// <remarks>Swift の <c>VehicleIdentificationError.Stage</c> に対応します。関連値ありエラーの各ケースと名前がぶつからないよう外へ出しています。</remarks>
public enum VehicleIdentificationErrorStage
{
    /// <summary>選択された接続終端を検証しています。</summary>
    EndpointValidation,

    /// <summary>選択された接続終端のTransportを生成しています。</summary>
    TransportCreation,

    /// <summary>選択されたシリアル終端を開いています。</summary>
    TransportOpen,

    /// <summary>アダプターを既知の初期状態へ戻しています。</summary>
    AdapterReset,

    /// <summary>アダプターの表示・プロトコル設定を適用しています。</summary>
    AdapterConfiguration,

    /// <summary>標準アダプター識別文字列を取得しています。</summary>
    AdapterIdentity,

    /// <summary>Service 09 PID 02へ車両識別子を要求しています。</summary>
    VehicleIdentificationRequest,

    /// <summary>Service 09 PID 02応答を識別子へ復元しています。</summary>
    VehicleIdentificationParsing,

    /// <summary>選択済みOBDプロトコルの説明を取得しています。</summary>
    ProtocolDescription,
}

/// <summary><see cref="VehicleIdentificationErrorStage"/> の計算プロパティを提供します。</summary>
public static class VehicleIdentificationErrorStageExtensions
{
    /// <summary>実機失敗報告で使用する安定した診断コードです。</summary>
    /// <remarks>責務: 1件の車両識別段階を機密情報を含まない短い診断コードへ変換します。</remarks>
    /// <param name="stage">変換する車両識別段階。</param>
    /// <returns>診断コード。</returns>
    public static string DiagnosticCode(this VehicleIdentificationErrorStage stage) => stage switch
    {
        VehicleIdentificationErrorStage.EndpointValidation => "ENDPOINT",
        VehicleIdentificationErrorStage.TransportCreation => "TRANSPORT-CREATE",
        VehicleIdentificationErrorStage.TransportOpen => "TRANSPORT-OPEN",
        VehicleIdentificationErrorStage.AdapterReset => "ATZ",
        VehicleIdentificationErrorStage.AdapterConfiguration => "AT-CONFIG",
        VehicleIdentificationErrorStage.AdapterIdentity => "ATI",
        VehicleIdentificationErrorStage.VehicleIdentificationRequest => "0902-REQUEST",
        VehicleIdentificationErrorStage.VehicleIdentificationParsing => "0902-PARSE",
        VehicleIdentificationErrorStage.ProtocolDescription => "ATDP",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };
}

/// <summary>失敗段階で観測した単一原因です。</summary>
/// <remarks>Swift の <c>VehicleIdentificationError.Cause</c> に対応します。</remarks>
public enum VehicleIdentificationErrorCause
{
    /// <summary>対象処理の実装が利用できません。</summary>
    Unavailable,

    /// <summary>選択された物理通信方式を使用できません。</summary>
    TransportUnsupported,

    /// <summary>物理接続または送受信を完了できませんでした。</summary>
    ConnectionFailed,

    /// <summary>応答が期限内に完了しませんでした。</summary>
    ResponseTimedOut,

    /// <summary>アダプターまたは車両が要求を拒否しました。</summary>
    CommandRejected,

    /// <summary>応答を期待形式として復元できませんでした。</summary>
    MalformedResponse,
}

/// <summary>OBD車両識別処理が完了できなかった理由です。</summary>
public abstract record VehicleIdentificationError
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: 理由の種類を下のケースに限定します。</remarks>
    private VehicleIdentificationError()
    {
    }

    /// <summary>実OBD識別実装がCompositionへまだ提供されていません。</summary>
    public sealed record Unavailable : VehicleIdentificationError;

    /// <summary>選択された物理通信方式をこのプラットフォームでは使用できません。</summary>
    public sealed record TransportUnsupported : VehicleIdentificationError;

    /// <summary>選択済みOBDアダプターのUSBシリアル終端を開けませんでした。</summary>
    public sealed record ConnectionFailed : VehicleIdentificationError;

    /// <summary>アダプターが期限内に完全な応答を返しませんでした。</summary>
    public sealed record ResponseTimedOut : VehicleIdentificationError;

    /// <summary>アダプターが要求を理解しないか車両からデータを取得できませんでした。</summary>
    public sealed record CommandRejected : VehicleIdentificationError;

    /// <summary>応答を規格上の車両識別情報として復元できませんでした。</summary>
    public sealed record MalformedResponse : VehicleIdentificationError;

    /// <summary>PID専用テーブルを安全に準備できませんでした。</summary>
    public sealed record PidCatalogUnavailable : VehicleIdentificationError;

    /// <summary>VINを取得できなかったため登録照合を継続できません。</summary>
    public sealed record VinUnavailable : VehicleIdentificationError;

    /// <summary>車両識別処理が特定可能な段階と原因で失敗しました。</summary>
    /// <param name="Stage">失敗した段階。</param>
    /// <param name="Cause">その段階で観測した原因。</param>
    public sealed record StageFailed(VehicleIdentificationErrorStage Stage, VehicleIdentificationErrorCause Cause)
        : VehicleIdentificationError;
}

/// <summary>OBD車両識別処理の失敗を表す例外です。</summary>
public sealed class VehicleIdentificationException : Exception
{
    /// <summary>OBD車両識別処理が完了できなかった理由です。</summary>
    public VehicleIdentificationError Reason { get; }

    /// <summary>失敗理由を指定して例外を生成します。</summary>
    /// <remarks>責務: 1件の車両識別の失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">車両識別の失敗理由。</param>
    public VehicleIdentificationException(VehicleIdentificationError reason)
        : base($"{nameof(VehicleIdentificationError)}.{reason}")
    {
        Reason = reason;
    }
}
