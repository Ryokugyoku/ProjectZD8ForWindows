namespace ProjectZD8.Diagnostics;

/// <summary>
/// ログに書く例外の中身（型・メッセージ・スタック・内側の例外）です。
/// </summary>
/// <param name="Type">例外の型の完全名。</param>
/// <param name="Message">例外のメッセージ。</param>
/// <param name="StackTrace">スタック。なければ null。</param>
/// <param name="Inner">内側の例外。なければ null。</param>
public sealed record DiagnosticExceptionInfo(
    string Type,
    string Message,
    string? StackTrace,
    DiagnosticExceptionInfo? Inner)
{
    /// <summary>内側の例外をたどる深さの上限です（循環や巨大な連鎖を防ぐ）。</summary>
    private const int MaxDepth = 8;

    /// <summary>
    /// 例外から中身を取り出します。
    /// </summary>
    /// <remarks>
    /// 責務: 呼び出し側のスレッドで例外の文字列を固定します（あとで投げ直されてスタックが変わらないように）。
    /// <see cref="AggregateException"/> は最初の内側の例外をたどります。
    /// </remarks>
    /// <param name="exception">例外。</param>
    /// <returns>例外の中身。null なら null。</returns>
    public static DiagnosticExceptionInfo? From(Exception? exception) => From(exception, 0);

    /// <summary>深さを数えながら取り出します。</summary>
    /// <param name="exception">例外。</param>
    /// <param name="depth">今の深さ。</param>
    /// <returns>例外の中身。</returns>
    private static DiagnosticExceptionInfo? From(Exception? exception, int depth)
    {
        if (exception is null || depth >= MaxDepth)
        {
            return null;
        }

        var inner = exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
            ? aggregate.InnerExceptions[0]
            : exception.InnerException;
        return new DiagnosticExceptionInfo(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace,
            From(inner, depth + 1));
    }
}
