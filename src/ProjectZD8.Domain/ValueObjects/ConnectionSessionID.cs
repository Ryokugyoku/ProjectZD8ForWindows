namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 走行接続セッションを外部識別値から独立して識別します。
/// </summary>
/// <param name="RawValue">アプリが生成した安定したセッション識別値です。</param>
public readonly record struct ConnectionSessionID(Guid RawValue)
{
    /// <summary>
    /// <c>Identifiable</c> が使用する安定識別値です。
    /// </summary>
    public Guid Id => RawValue;

    /// <summary>
    /// 新しいUUIDでセッション識別値を生成します。
    /// </summary>
    /// <remarks>責務: 1件の接続セッションへ安定したUUIDを割り当てます。</remarks>
    /// <returns>新しいUUIDを保持するセッション識別値。</returns>
    public static ConnectionSessionID New() => new(Guid.NewGuid());
}
