namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログの仕組み自身が書くイベント名です（変えない固定の名前）。
/// </summary>
public static class DiagnosticEventNames
{
    /// <summary>各実行の最初の行。</summary>
    public const string AppStart = "app.start";

    /// <summary>正常終了。</summary>
    public const string AppExit = "app.exit";

    /// <summary>前回の実行に <c>app.exit</c> がなかった。</summary>
    public const string PreviousRunCrashed = "app.previous-run.crashed";

    /// <summary>捕まえられなかった例外でプロセスが落ちる。</summary>
    public const string CrashUnhandled = "app.crash.unhandled";

    /// <summary>待たれなかったタスクの例外。</summary>
    public const string UnobservedTask = "app.task.unobserved";

    /// <summary>キューがあふれて捨てた件数。</summary>
    public const string Dropped = "diag.dropped";

    /// <summary>ファイルへ書けなかった。</summary>
    public const string WriteFailed = "diag.write.failed";
}
