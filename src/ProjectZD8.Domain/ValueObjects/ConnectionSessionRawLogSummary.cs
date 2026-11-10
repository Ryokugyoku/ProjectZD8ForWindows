namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 端末内Rawログの保管状態です。
/// </summary>
public enum ConnectionSessionLocalRawState
{
    /// <summary>
    /// Raw応答がまだ記録されていません。
    /// </summary>
    Empty,

    /// <summary>
    /// Raw応答が現在端末に保存されています。
    /// </summary>
    Available,

    /// <summary>
    /// セッション概要を残して現在端末のRaw応答だけを除去しました。
    /// </summary>
    Removed,
}

/// <summary>
/// <see cref="ConnectionSessionLocalRawState"/> の raw value を扱います。
/// </summary>
public static class ConnectionSessionLocalRawStateRawValue
{
    /// <summary>
    /// 保管状態を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の端末内Rawログ保管状態を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する保管状態。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this ConnectionSessionLocalRawState value) => value switch
    {
        ConnectionSessionLocalRawState.Empty => "empty",
        ConnectionSessionLocalRawState.Available => "available",
        ConnectionSessionLocalRawState.Removed => "removed",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から保管状態を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して端末内Rawログ保管状態へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた保管状態。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out ConnectionSessionLocalRawState value)
    {
        switch (rawValue)
        {
            case "empty": value = ConnectionSessionLocalRawState.Empty; return true;
            case "available": value = ConnectionSessionLocalRawState.Available; return true;
            case "removed": value = ConnectionSessionLocalRawState.Removed; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>
/// セッション転送の現在状態です。
/// </summary>
public enum ConnectionSessionCloudSyncState
{
    /// <summary>
    /// 転送対象のRaw応答がまだありません。
    /// </summary>
    NotUploaded,

    /// <summary>
    /// CloudKitへの転送を待っています。
    /// </summary>
    Pending,

    /// <summary>
    /// CloudKitへ検証可能なManifestとともに保存しました。
    /// </summary>
    Uploaded,

    /// <summary>
    /// 直近のCloudKit転送が失敗し再試行を待っています。
    /// </summary>
    Failed,
}

/// <summary>
/// <see cref="ConnectionSessionCloudSyncState"/> の raw value を扱います。
/// </summary>
public static class ConnectionSessionCloudSyncStateRawValue
{
    /// <summary>
    /// 転送状態を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件のセッション転送状態を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する転送状態。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this ConnectionSessionCloudSyncState value) => value switch
    {
        ConnectionSessionCloudSyncState.NotUploaded => "notUploaded",
        ConnectionSessionCloudSyncState.Pending => "pending",
        ConnectionSessionCloudSyncState.Uploaded => "uploaded",
        ConnectionSessionCloudSyncState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から転送状態を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別してセッション転送状態へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた転送状態。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out ConnectionSessionCloudSyncState value)
    {
        switch (rawValue)
        {
            case "notUploaded": value = ConnectionSessionCloudSyncState.NotUploaded; return true;
            case "pending": value = ConnectionSessionCloudSyncState.Pending; return true;
            case "uploaded": value = ConnectionSessionCloudSyncState.Uploaded; return true;
            case "failed": value = ConnectionSessionCloudSyncState.Failed; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>
/// Macがセッションを永続的に取り込んだことを示す受領証です。
/// </summary>
public sealed record ConnectionSessionMacImportReceipt
{
    /// <summary>
    /// 取り込んだMacの安定したインストール識別子です。
    /// </summary>
    public string DeviceID { get; }

    /// <summary>
    /// ユーザーが判別できるMac表示名です。
    /// </summary>
    public string DeviceName { get; }

    /// <summary>
    /// Macで読み戻し検証まで完了した日時です。
    /// </summary>
    public DateTimeOffset ImportedAt { get; }

    /// <summary>
    /// Macが検証した転送PayloadのSHA-256です。
    /// </summary>
    public string ManifestDigest { get; }

    /// <summary>
    /// 検証済みMac取込結果を受領証として生成します。
    /// </summary>
    /// <remarks>責務: 1台のMacによる完全取込結果をセッションManifestへ固定します。</remarks>
    /// <param name="deviceID">Macの安定したインストール識別子。</param>
    /// <param name="deviceName">ユーザー向けMac表示名。</param>
    /// <param name="importedAt">読み戻し検証まで完了した日時。</param>
    /// <param name="manifestDigest">検証した転送PayloadのSHA-256。</param>
    public ConnectionSessionMacImportReceipt(
        string deviceID,
        string deviceName,
        DateTimeOffset importedAt,
        string manifestDigest)
    {
        DeviceID = deviceID;
        DeviceName = deviceName;
        ImportedAt = importedAt;
        ManifestDigest = manifestDigest;
    }
}

/// <summary>
/// 接続履歴へ表示するRawログ件数と端末間保管状態です。
/// </summary>
public sealed record ConnectionSessionRawLogSummary
{
    /// <summary>
    /// セッションへ記録されたRaw応答件数です。
    /// </summary>
    public long RecordCount { get; init; }

    /// <summary>
    /// セッションへ記録されたRaw応答Payload合計バイト数です。
    /// </summary>
    public long ByteCount { get; init; }

    /// <summary>
    /// 現在端末におけるRawログ保管状態です。
    /// </summary>
    public ConnectionSessionLocalRawState LocalState { get; init; }

    /// <summary>
    /// CloudKit転送の現在状態です。
    /// </summary>
    public ConnectionSessionCloudSyncState CloudState { get; init; }

    /// <summary>
    /// 転送Payloadを識別するSHA-256です。
    /// </summary>
    public string? ManifestDigest { get; init; }

    /// <summary>
    /// Macによる永続取込を確認できた場合の受領証です。
    /// </summary>
    public ConnectionSessionMacImportReceipt? MacImportReceipt { get; init; }

    /// <summary>
    /// 現在端末でRawログを最後に閲覧可能状態へした日時です。
    /// </summary>
    public DateTimeOffset? LastAccessedAt { get; init; }

    /// <summary>
    /// Raw応答未記録の標準状態です。
    /// </summary>
    public static ConnectionSessionRawLogSummary Empty { get; } = new(
        recordCount: 0,
        byteCount: 0,
        localState: ConnectionSessionLocalRawState.Empty,
        cloudState: ConnectionSessionCloudSyncState.NotUploaded,
        manifestDigest: null,
        macImportReceipt: null,
        lastAccessedAt: null);

    /// <summary>
    /// セッションのRawログ件数と保管状態を生成します。
    /// </summary>
    /// <remarks>責務: Rawログの集計値と端末間保管証跡を1件の履歴表示値へまとめます。</remarks>
    /// <param name="recordCount">Raw応答件数。</param>
    /// <param name="byteCount">Raw応答Payload合計バイト数。</param>
    /// <param name="localState">現在端末の保管状態。</param>
    /// <param name="cloudState">CloudKit転送状態。</param>
    /// <param name="manifestDigest">転送PayloadのSHA-256。</param>
    /// <param name="macImportReceipt">Mac取込済みの場合の受領証。</param>
    /// <param name="lastAccessedAt">現在端末でRawログを最後に閲覧可能状態へした日時。</param>
    public ConnectionSessionRawLogSummary(
        long recordCount,
        long byteCount,
        ConnectionSessionLocalRawState localState,
        ConnectionSessionCloudSyncState cloudState,
        string? manifestDigest,
        ConnectionSessionMacImportReceipt? macImportReceipt,
        DateTimeOffset? lastAccessedAt = null)
    {
        RecordCount = recordCount;
        ByteCount = byteCount;
        LocalState = localState;
        CloudState = cloudState;
        ManifestDigest = manifestDigest;
        MacImportReceipt = macImportReceipt;
        LastAccessedAt = lastAccessedAt;
    }

    /// <summary>
    /// 現在のManifestと一致するMac受領証があるかを返します。
    /// </summary>
    public bool IsDurablyImportedByMac
    {
        get
        {
            if (ManifestDigest is not { } manifestDigest || MacImportReceipt is not { } receipt)
            {
                return false;
            }

            return string.Equals(receipt.ManifestDigest, manifestDigest, StringComparison.Ordinal);
        }
    }
}
