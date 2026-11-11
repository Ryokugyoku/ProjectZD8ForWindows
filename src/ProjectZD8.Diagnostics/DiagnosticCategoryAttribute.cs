namespace ProjectZD8.Diagnostics;

/// <summary>
/// <see cref="DiagnosticLog.For{T}"/> で取ったロガーの分類（<c>cat</c>）を決めます。
/// </summary>
/// <remarks>
/// 例：<c>[DiagnosticCategory("obd.wire")]</c>。付けなければ型の完全名が分類になります
/// （絞り込みはチャンネルの既定のレベルになるので、計装する型には付けること）。
/// </remarks>
/// <param name="category">分類（docs/LOGGING_STRATEGY.md の 3 の表）。</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DiagnosticCategoryAttribute(string category) : Attribute
{
    /// <summary>分類です。</summary>
    public string Category { get; } = category;
}
