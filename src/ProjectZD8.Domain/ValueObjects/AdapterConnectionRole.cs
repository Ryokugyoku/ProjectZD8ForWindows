namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// OBDアダプターへ割り当てる製品上の接続役割です。
/// </summary>
public enum AdapterConnectionRole
{
    /// <summary>
    /// 通常のOBD診断通信に必須のプライマリー役割を表します。
    /// </summary>
    Primary,

    /// <summary>
    /// Raw CAN受信専用の任意セカンダリー役割を表します。
    /// </summary>
    Secondary,
}

/// <summary>
/// <see cref="AdapterConnectionRole"/> の raw value と全ケースを扱います。
/// </summary>
public static class AdapterConnectionRoleRawValue
{
    /// <summary>
    /// 宣言順の全ケースです。
    /// </summary>
    public static IReadOnlyList<AdapterConnectionRole> AllCases { get; } =
        [AdapterConnectionRole.Primary, AdapterConnectionRole.Secondary];

    /// <summary>
    /// 接続役割を raw value の文字列へ変換します。
    /// </summary>
    /// <remarks>責務: 1件の接続役割を Swift 版と同じ raw value 文字列へ変換します。</remarks>
    /// <param name="value">変換する接続役割。</param>
    /// <returns>Swift 版と同じ raw value 文字列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this AdapterConnectionRole value) => value switch
    {
        AdapterConnectionRole.Primary => "primary",
        AdapterConnectionRole.Secondary => "secondary",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// raw value の文字列から接続役割を復元します。
    /// </summary>
    /// <remarks>責務: raw value 文字列を大文字小文字を区別して接続役割へ変換します。</remarks>
    /// <param name="rawValue">復元する raw value 文字列。</param>
    /// <param name="value">復元できた接続役割。</param>
    /// <returns>復元できた場合は <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out AdapterConnectionRole value)
    {
        switch (rawValue)
        {
            case "primary": value = AdapterConnectionRole.Primary; return true;
            case "secondary": value = AdapterConnectionRole.Secondary; return true;
            default: value = default; return false;
        }
    }

    /// <summary>
    /// 接続役割を一意に識別する安定識別子を返します。
    /// </summary>
    /// <remarks>責務: <c>Identifiable</c> の <c>id</c> に相当する raw value を返します。</remarks>
    /// <param name="value">識別子を求める接続役割。</param>
    /// <returns>raw value と同じ安定識別子。</returns>
    public static string Id(this AdapterConnectionRole value) => value.ToRawValue();
}
