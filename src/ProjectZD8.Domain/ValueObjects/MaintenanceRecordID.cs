namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 車両別整備記録を端末間で安定して識別するIDです。
/// </summary>
/// <param name="RawValue">永続化と同期に使用するUUIDです。</param>
public readonly record struct MaintenanceRecordID(Guid RawValue)
{
    /// <summary>
    /// 新規UUIDから整備記録IDを生成します。
    /// </summary>
    /// <remarks>責務: 1件のUUIDを整備記録専用の型へ包みます。</remarks>
    /// <returns>新しいUUIDを保持する整備記録ID。</returns>
    public static MaintenanceRecordID New() => new(Guid.NewGuid());
}
