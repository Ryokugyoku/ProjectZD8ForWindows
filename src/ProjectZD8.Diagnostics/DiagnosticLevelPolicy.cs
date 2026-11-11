using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// どのレベルをファイルへ書き、どのレベルをリングバッファに持つかの決まりです。
/// </summary>
/// <remarks>
/// 分類の指定は、完全一致か、点で区切った先頭部分の一致で当てます（<c>obd</c> の指定は <c>obd.wire</c> にも効く。長い方が優先）。
/// 分類ごとの結果は覚えておくので、2 回目からは辞書を 1 回引くだけです（割り当てなし）。
/// </remarks>
public sealed class DiagnosticLevelPolicy
{
    /// <summary>Stable のリングバッファの件数です。</summary>
    public const int StableBufferCapacity = 2000;

    /// <summary>レベルに関係なくファイルへ書くイベントです（起動・終了・異常終了の検出・捨てた件数）。</summary>
    private static readonly HashSet<string> AlwaysWrittenEvents = new(StringComparer.Ordinal)
    {
        DiagnosticEventNames.AppStart,
        DiagnosticEventNames.AppExit,
        DiagnosticEventNames.PreviousRunCrashed,
        DiagnosticEventNames.Dropped,
    };

    /// <summary>分類ごとに解いた最低レベルです。</summary>
    private readonly ConcurrentDictionary<string, ResolvedLevels> _resolved = new(StringComparer.Ordinal);

    /// <summary>
    /// 決まりを作ります。
    /// </summary>
    /// <param name="fileLevel">ファイルへ書く最低レベル。</param>
    /// <param name="fileCategoryLevels">分類ごとのファイルの最低レベル。</param>
    /// <param name="bufferLevel">リングバッファに持つ最低レベル。持たないなら null。</param>
    /// <param name="bufferCategoryLevels">分類ごとのリングバッファの最低レベル。</param>
    /// <param name="bufferCapacity">リングバッファの件数。</param>
    public DiagnosticLevelPolicy(
        LogLevel fileLevel,
        IReadOnlyDictionary<string, LogLevel>? fileCategoryLevels = null,
        LogLevel? bufferLevel = null,
        IReadOnlyDictionary<string, LogLevel>? bufferCategoryLevels = null,
        int bufferCapacity = 0)
    {
        FileLevel = fileLevel;
        FileCategoryLevels = fileCategoryLevels ?? new Dictionary<string, LogLevel>();
        BufferLevel = bufferCapacity > 0 ? bufferLevel : null;
        BufferCategoryLevels = bufferCategoryLevels ?? new Dictionary<string, LogLevel>();
        BufferCapacity = BufferLevel is null ? 0 : bufferCapacity;
    }

    /// <summary>ファイルへ書く最低レベルです。</summary>
    public LogLevel FileLevel { get; }

    /// <summary>分類ごとのファイルの最低レベルです。</summary>
    public IReadOnlyDictionary<string, LogLevel> FileCategoryLevels { get; }

    /// <summary>リングバッファに持つ最低レベルです。持たないなら null。</summary>
    public LogLevel? BufferLevel { get; }

    /// <summary>分類ごとのリングバッファの最低レベルです。</summary>
    public IReadOnlyDictionary<string, LogLevel> BufferCategoryLevels { get; }

    /// <summary>リングバッファの件数です（持たないなら 0）。</summary>
    public int BufferCapacity { get; }

    /// <summary>リングバッファを持つかです。</summary>
    public bool HasBuffer => BufferCapacity > 0;

    /// <summary>
    /// チャンネルの既定の決まりを返します（docs/LOGGING_STRATEGY.md の 2 の表）。
    /// </summary>
    /// <param name="channel">チャンネル。</param>
    /// <returns>決まり。</returns>
    public static DiagnosticLevelPolicy ForChannel(DiagnosticsChannel channel) => channel switch
    {
        DiagnosticsChannel.Dev => new DiagnosticLevelPolicy(LogLevel.Trace),
        DiagnosticsChannel.Beta => Verbose(),
        _ => new DiagnosticLevelPolicy(
            LogLevel.Warning,
            bufferLevel: LogLevel.Debug,
            bufferCategoryLevels: WireAtTrace(),
            bufferCapacity: StableBufferCapacity),
    };

    /// <summary>
    /// 最低レベルを明示したときの決まりを返します（環境変数・<c>appsettings.Local.json</c> の上書き）。
    /// </summary>
    /// <remarks>
    /// ファイルの最低レベルを <paramref name="level"/> にします。Debug 以下なら <c>obd.wire</c> は Trace（Beta と同じ考え方）。
    /// Stable で Info 以上を指定したときは、リングバッファを残します（Debug 以下なら全部書くので持たない）。
    /// </remarks>
    /// <param name="channel">チャンネル。</param>
    /// <param name="level">ファイルへ書く最低レベル。</param>
    /// <returns>決まり。</returns>
    public static DiagnosticLevelPolicy ForLevel(DiagnosticsChannel channel, LogLevel level)
    {
        var fileCategories = level <= LogLevel.Debug ? WireAtTrace() : null;
        return channel == DiagnosticsChannel.Stable && level > LogLevel.Debug
            ? new DiagnosticLevelPolicy(level, fileCategories, LogLevel.Debug, WireAtTrace(), StableBufferCapacity)
            : new DiagnosticLevelPolicy(level, fileCategories);
    }

    /// <summary>
    /// 「詳細ログ」の決まり（Beta と同じ量）を返します。
    /// </summary>
    /// <returns>Debug 以上（<c>obd.wire</c> は Trace）を全部書く決まり。</returns>
    public static DiagnosticLevelPolicy Verbose() => new(LogLevel.Debug, WireAtTrace());

    /// <summary>
    /// ロガーがイベントを作るべきか（ファイルかリングバッファのどちらかが受け取るか）を返します。
    /// </summary>
    /// <param name="category">分類。</param>
    /// <param name="level">レベル。</param>
    /// <returns>受け取るなら true。</returns>
    public bool IsEnabled(string category, LogLevel level) => level >= Resolve(category).Enabled;

    /// <summary>
    /// ファイルへ書くかを返します。
    /// </summary>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <returns>書くなら true。</returns>
    public bool ShouldWrite(DiagnosticEvent diagnosticEvent) =>
        diagnosticEvent.Level >= Resolve(diagnosticEvent.Category).File
        || AlwaysWrittenEvents.Contains(diagnosticEvent.EventName);

    /// <summary>
    /// リングバッファに持つかを返します。
    /// </summary>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <returns>持つなら true。</returns>
    public bool ShouldBuffer(DiagnosticEvent diagnosticEvent) =>
        HasBuffer && diagnosticEvent.Level >= Resolve(diagnosticEvent.Category).Buffer;

    /// <summary>分類の最低レベルを解きます（覚えておく）。</summary>
    /// <param name="category">分類。</param>
    /// <returns>ファイル・リングバッファ・両方の最低レベル。</returns>
    private ResolvedLevels Resolve(string category)
    {
        if (_resolved.TryGetValue(category, out var levels))
        {
            return levels;
        }

        var file = Lookup(FileCategoryLevels, category) ?? FileLevel;
        var buffer = BufferLevel is null ? LogLevel.None : Lookup(BufferCategoryLevels, category) ?? BufferLevel.Value;
        levels = new ResolvedLevels(file, buffer, file < buffer ? file : buffer);
        return _resolved.GetOrAdd(category, levels);
    }

    /// <summary>完全一致か、点で区切った先頭部分の一致（長い方から）で引きます。</summary>
    /// <param name="levels">分類ごとの最低レベル。</param>
    /// <param name="category">分類。</param>
    /// <returns>見つかった最低レベル。なければ null。</returns>
    private static LogLevel? Lookup(IReadOnlyDictionary<string, LogLevel> levels, string category)
    {
        var name = category;
        while (true)
        {
            if (levels.TryGetValue(name, out var level))
            {
                return level;
            }

            var dot = name.LastIndexOf('.');
            if (dot <= 0)
            {
                return null;
            }

            name = name[..dot];
        }
    }

    /// <summary><c>obd.wire</c> を Trace にする分類の指定です。</summary>
    /// <returns>分類ごとの最低レベル。</returns>
    private static Dictionary<string, LogLevel> WireAtTrace() =>
        new(StringComparer.Ordinal) { [DiagnosticCategories.ObdWire] = LogLevel.Trace };

    /// <summary>分類ごとに解いた最低レベルです。</summary>
    /// <param name="File">ファイル。</param>
    /// <param name="Buffer">リングバッファ（持たないなら None）。</param>
    /// <param name="Enabled">両方のうち低い方。</param>
    private readonly record struct ResolvedLevels(LogLevel File, LogLevel Buffer, LogLevel Enabled);
}
