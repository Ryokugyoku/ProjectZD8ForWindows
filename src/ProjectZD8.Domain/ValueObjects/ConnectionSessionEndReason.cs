namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 接続セッションが終了した直接原因です。
/// </summary>
public enum ConnectionSessionEndReason
{
    /// <summary>
    /// HOMEの切断操作によって終了しました。
    /// </summary>
    UserDisconnected,

    /// <summary>
    /// 車両から有効な応答が返らなくなりました。
    /// </summary>
    VehicleNoResponse,

    /// <summary>
    /// アダプターまたは車両との通信境界が失われました。
    /// </summary>
    ConnectionLost,

    /// <summary>
    /// PID定義または読取開始に失敗しました。
    /// </summary>
    AcquisitionFailed,

    /// <summary>
    /// 新しい接続要求によって以前のセッションが置き換えられました。
    /// </summary>
    Superseded,

    /// <summary>
    /// アカウントのサインアウトによって終了しました。
    /// </summary>
    AccountSignedOut,

    /// <summary>
    /// アプリ終了などにより終了処理を完了できませんでした。
    /// </summary>
    UnexpectedTermination,
}

/// <summary>
/// <see cref="ConnectionSessionEndReason"/> の raw value を扱います。
/// </summary>
public static class ConnectionSessionEndReasonRawValue
{
    /// <summary>
    /// 終了原因を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の終了原因を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する終了原因。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this ConnectionSessionEndReason value) => value switch
    {
        ConnectionSessionEndReason.UserDisconnected => "userDisconnected",
        ConnectionSessionEndReason.VehicleNoResponse => "vehicleNoResponse",
        ConnectionSessionEndReason.ConnectionLost => "connectionLost",
        ConnectionSessionEndReason.AcquisitionFailed => "acquisitionFailed",
        ConnectionSessionEndReason.Superseded => "superseded",
        ConnectionSessionEndReason.AccountSignedOut => "accountSignedOut",
        ConnectionSessionEndReason.UnexpectedTermination => "unexpectedTermination",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から終了原因を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して終了原因へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた終了原因。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out ConnectionSessionEndReason value)
    {
        switch (rawValue)
        {
            case "userDisconnected": value = ConnectionSessionEndReason.UserDisconnected; return true;
            case "vehicleNoResponse": value = ConnectionSessionEndReason.VehicleNoResponse; return true;
            case "connectionLost": value = ConnectionSessionEndReason.ConnectionLost; return true;
            case "acquisitionFailed": value = ConnectionSessionEndReason.AcquisitionFailed; return true;
            case "superseded": value = ConnectionSessionEndReason.Superseded; return true;
            case "accountSignedOut": value = ConnectionSessionEndReason.AccountSignedOut; return true;
            case "unexpectedTermination": value = ConnectionSessionEndReason.UnexpectedTermination; return true;
            default: value = default; return false;
        }
    }
}
