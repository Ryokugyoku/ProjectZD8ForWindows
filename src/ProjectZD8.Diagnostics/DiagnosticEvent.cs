using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログの 1 件のイベントです（JSONL の 1 行に当たる）。
/// </summary>
/// <remarks>
/// 呼び出し側のスレッドで作り、中身を固定してから出力先へ渡します（背景のスレッドで書くときに値が変わらないように）。
/// 時刻・実行 ID・通し番号は出力先が付けます（<see cref="Stamp"/>）。
/// </remarks>
public sealed class DiagnosticEvent
{
    /// <summary>空の値の一覧です。</summary>
    private static readonly KeyValuePair<string, object?>[] NoProperties = [];

    /// <summary>
    /// イベントを作ります。
    /// </summary>
    /// <param name="level">レベル。</param>
    /// <param name="category">分類（<c>obd.connect</c> など）。</param>
    /// <param name="eventName">固定のイベント名（<c>分類.対象.結果</c>）。</param>
    /// <param name="message">人が読む短い説明。</param>
    /// <param name="properties">イベントごとの値（数値・短い文字列）。</param>
    /// <param name="exception">例外の中身。なければ null。</param>
    /// <param name="sessionId">接続セッションの ID。なければ null。</param>
    public DiagnosticEvent(
        LogLevel level,
        string category,
        string eventName,
        string message,
        IReadOnlyList<KeyValuePair<string, object?>>? properties = null,
        DiagnosticExceptionInfo? exception = null,
        string? sessionId = null)
    {
        Level = level;
        Category = category;
        EventName = eventName;
        Message = message;
        Properties = properties ?? NoProperties;
        Exception = exception;
        SessionId = sessionId;
    }

    /// <summary>レベルです。</summary>
    public LogLevel Level { get; }

    /// <summary>分類です（JSON の <c>cat</c>）。</summary>
    public string Category { get; }

    /// <summary>固定のイベント名です（JSON の <c>evt</c>）。</summary>
    public string EventName { get; }

    /// <summary>人が読む短い説明です（JSON の <c>msg</c>）。</summary>
    public string Message { get; }

    /// <summary>イベントごとの値です（JSON の <c>props</c>）。</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Properties { get; }

    /// <summary>例外の中身です（JSON の <c>ex</c>）。</summary>
    public DiagnosticExceptionInfo? Exception { get; }

    /// <summary>接続セッションの ID です（JSON の <c>sid</c>）。</summary>
    public string? SessionId { get; }

    /// <summary>記録した時刻（UTC）です（JSON の <c>ts</c>）。出力先が付けます。</summary>
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>実行 ID です（JSON の <c>run</c>）。出力先が付けます。</summary>
    public string RunId { get; private set; } = string.Empty;

    /// <summary>実行内の通し番号です（JSON の <c>seq</c>）。出力先が付けます。</summary>
    public long Sequence { get; private set; }

    /// <summary>
    /// 時刻・実行 ID・通し番号を付けます。
    /// </summary>
    /// <remarks>責務: 出力先が受け取った順に、並べ直しに使う値を付けます。</remarks>
    /// <param name="timestamp">時刻。</param>
    /// <param name="runId">実行 ID。</param>
    /// <param name="sequence">通し番号。</param>
    internal void Stamp(DateTimeOffset timestamp, string runId, long sequence)
    {
        Timestamp = timestamp;
        RunId = runId;
        Sequence = sequence;
    }
}
