namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 接続セッションを取得したAppleプラットフォームです。
/// </summary>
public enum ConnectionSessionAcquisitionPlatform
{
    /// <summary>
    /// iPhoneでログを取得しました。
    /// </summary>
    IPhone,

    /// <summary>
    /// iPadでログを取得しました。
    /// </summary>
    IPad,

    /// <summary>
    /// macOSでログを取得しました。
    /// </summary>
    MacOS,
}

/// <summary>
/// <see cref="ConnectionSessionAcquisitionPlatform"/> の raw value を扱います。
/// </summary>
public static class ConnectionSessionAcquisitionPlatformRawValue
{
    /// <summary>
    /// 取得プラットフォームを raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の取得プラットフォームを Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する取得プラットフォーム。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this ConnectionSessionAcquisitionPlatform value) => value switch
    {
        ConnectionSessionAcquisitionPlatform.IPhone => "iPhone",
        ConnectionSessionAcquisitionPlatform.IPad => "iPad",
        ConnectionSessionAcquisitionPlatform.MacOS => "macOS",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から取得プラットフォームを復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して取得プラットフォームへ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた取得プラットフォーム。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out ConnectionSessionAcquisitionPlatform value)
    {
        switch (rawValue)
        {
            case "iPhone": value = ConnectionSessionAcquisitionPlatform.IPhone; return true;
            case "iPad": value = ConnectionSessionAcquisitionPlatform.IPad; return true;
            case "macOS": value = ConnectionSessionAcquisitionPlatform.MacOS; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>
/// 接続セッションの取得元端末を示す不変スナップショットです。
/// </summary>
public sealed record ConnectionSessionAcquisitionDevice
{
    /// <summary>
    /// ログ取得時に動作していたAppleプラットフォームです。
    /// </summary>
    public ConnectionSessionAcquisitionPlatform Platform { get; }

    /// <summary>
    /// ログ取得時に取得できたユーザー向け端末名です。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 取得プラットフォームと端末名を固定して生成します。
    /// </summary>
    /// <remarks>責務: 1件の取得元端末情報をセッション転送可能なスナップショットへまとめます。</remarks>
    /// <param name="platform">ログ取得時に動作していたAppleプラットフォーム。</param>
    /// <param name="name">ログ取得時に取得できたユーザー向け端末名。</param>
    public ConnectionSessionAcquisitionDevice(ConnectionSessionAcquisitionPlatform platform, string name)
    {
        Platform = platform;
        Name = name;
    }
}
