namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// 登録車両をVINなどの外部識別値から独立して識別します。
/// </summary>
/// <param name="RawValue">アプリが生成した安定した車両識別値です。</param>
public readonly record struct VehicleID(Guid RawValue)
{
    /// <summary>
    /// <c>Identifiable</c> が使用する安定識別値です。
    /// </summary>
    public Guid Id => RawValue;

    /// <summary>
    /// 新しいUUIDで車両識別値を生成します。
    /// </summary>
    /// <remarks>責務: 外部識別値へ依存しない車両識別値を1件生成します。</remarks>
    /// <returns>新しいUUIDを保持する車両識別値。</returns>
    public static VehicleID New() => new(Guid.NewGuid());
}
