using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>車両別学習抽出でセッション境界を保持する未デコードRawログです。</summary>
public sealed record VehicleConnectionSessionRawLogEntry
{
    /// <summary>ログを所有する登録車両IDです。</summary>
    public VehicleID VehicleID { get; }
    /// <summary>ログを所有する接続セッションIDです。</summary>
    public ConnectionSessionID SessionID { get; }
    /// <summary>セッションの開始日時です。</summary>
    public DateTimeOffset SessionStartedAt { get; }
    /// <summary>セッション内順序を保持する未デコード応答です。</summary>
    public ConnectionSessionRawLogEntry Entry { get; }

    /// <summary>車両、セッション、Raw応答を1件の学習抽出値として生成します。</summary>
    /// <remarks>責務: 1件のRaw応答へ車両とセッションの安定した所属情報を付与します。</remarks>
    /// <param name="vehicleID">ログを所有する登録車両ID。</param>
    /// <param name="sessionID">ログを所有する接続セッションID。</param>
    /// <param name="sessionStartedAt">セッション開始日時。</param>
    /// <param name="entry">セッション内順序を保持する未デコード応答。</param>
    public VehicleConnectionSessionRawLogEntry(
        VehicleID vehicleID,
        ConnectionSessionID sessionID,
        DateTimeOffset sessionStartedAt,
        ConnectionSessionRawLogEntry entry)
    {
        VehicleID = vehicleID;
        SessionID = sessionID;
        SessionStartedAt = sessionStartedAt;
        Entry = entry;
    }
}
