using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 1 つの分類のロガーです。書くたびに <see cref="DiagnosticLog.Sink"/> を見ます。
/// </summary>
/// <remarks>
/// 無効なレベルでは <see cref="IsEnabled"/> が false を返すだけで、何も割り当てません
/// （<c>[LoggerMessage]</c> の生成コードは先に <see cref="IsEnabled"/> を見る）。
/// </remarks>
/// <param name="category">分類。</param>
internal sealed class DiagnosticLogger(string category) : ILogger
{
    /// <summary><c>[LoggerMessage]</c> が値の一覧に入れる書式の元の文字列のキーです（props には入れない）。</summary>
    private const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>分類です。</summary>
    public string Category { get; } = category;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && DiagnosticLog.Sink.IsEnabled(Category, logLevel);

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Key, "sid", StringComparison.Ordinal))
                {
                    return DiagnosticLog.BeginConnectionSession(pair.Value?.ToString());
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var sink = DiagnosticLog.Sink;
        if (logLevel == LogLevel.None || !sink.IsEnabled(Category, logLevel))
        {
            return;
        }

        var message = formatter is null ? state?.ToString() ?? string.Empty : formatter(state, exception);
        var eventName = string.IsNullOrEmpty(eventId.Name) ? Category + ".unnamed" : eventId.Name;
        sink.Emit(new DiagnosticEvent(
            logLevel,
            Category,
            eventName,
            message,
            CopyProperties(state),
            DiagnosticExceptionInfo.From(exception),
            DiagnosticLog.CurrentSessionId));
    }

    /// <summary>
    /// 値の一覧を、背景で書いても変わらない形に写します。
    /// </summary>
    /// <remarks>責務: 書式の元の文字列を除き、値を <see cref="DiagnosticPropertyValue.Normalize"/> で固定します。</remarks>
    /// <typeparam name="TState">状態の型。</typeparam>
    /// <param name="state">ログの状態。</param>
    /// <returns>写した値の一覧。なければ null。</returns>
    private static KeyValuePair<string, object?>[]? CopyProperties<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> list || list.Count == 0)
        {
            return null;
        }

        var count = 0;
        for (var index = 0; index < list.Count; index++)
        {
            if (!string.Equals(list[index].Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return null;
        }

        var copy = new KeyValuePair<string, object?>[count];
        var position = 0;
        for (var index = 0; index < list.Count; index++)
        {
            var pair = list[index];
            if (!string.Equals(pair.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                copy[position++] = new(pair.Key, DiagnosticPropertyValue.Normalize(pair.Value));
            }
        }

        return copy;
    }
}
