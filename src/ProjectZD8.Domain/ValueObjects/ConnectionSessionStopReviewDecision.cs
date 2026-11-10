namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 自動判別できない接続終了をユーザーがどのように確認したかを表します。
/// </summary>
public enum ConnectionSessionStopReviewDecision
{
    /// <summary>
    /// ユーザーが意図してイグニッションを切るかデバイスを外した終了です。
    /// </summary>
    UserInitiated,
}

/// <summary>
/// <see cref="ConnectionSessionStopReviewDecision"/> の raw value を扱います。
/// </summary>
public static class ConnectionSessionStopReviewDecisionRawValue
{
    /// <summary>
    /// 確認結果を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の終了確認結果を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する確認結果。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this ConnectionSessionStopReviewDecision value) => value switch
    {
        ConnectionSessionStopReviewDecision.UserInitiated => "userInitiated",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から確認結果を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して終了確認結果へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた確認結果。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out ConnectionSessionStopReviewDecision value)
    {
        switch (rawValue)
        {
            case "userInitiated": value = ConnectionSessionStopReviewDecision.UserInitiated; return true;
            default: value = default; return false;
        }
    }
}
