namespace ProjectZD8.Diagnostics;

/// <summary>
/// 捕まえられなかった例外を受けたハンドラーです。
/// </summary>
public enum DiagnosticCrashSource
{
    /// <summary>XAML の <c>Application.UnhandledException</c>（UI スレッド）。</summary>
    Xaml,

    /// <summary><c>AppDomain.CurrentDomain.UnhandledException</c>（UI スレッド以外）。</summary>
    AppDomain,

    /// <summary><c>TaskScheduler.UnobservedTaskException</c>（プロセスは落ちない）。</summary>
    UnobservedTask,
}

/// <summary>
/// <see cref="DiagnosticCrashSource"/> をログに書く文字列にします。
/// </summary>
public static class DiagnosticCrashSourceRawValue
{
    /// <summary>
    /// ログに書く文字列を返します。
    /// </summary>
    /// <param name="source">ハンドラー。</param>
    /// <returns><c>xaml</c> / <c>appdomain</c> / <c>task</c>。</returns>
    public static string ToRawValue(this DiagnosticCrashSource source) => source switch
    {
        DiagnosticCrashSource.Xaml => "xaml",
        DiagnosticCrashSource.AppDomain => "appdomain",
        _ => "task",
    };
}
