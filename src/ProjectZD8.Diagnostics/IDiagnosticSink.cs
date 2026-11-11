using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログの出力先です。<see cref="DiagnosticLog.Install"/> で差し込みます。
/// </summary>
/// <remarks>
/// どのスレッドからも同時に呼ばれます。<see cref="Emit"/> は呼び出し側を待たせてはいけません（ファイルへの書込みは背景で行う）。
/// </remarks>
public interface IDiagnosticSink
{
    /// <summary>
    /// その分類とレベルのイベントを受け取るかを返します。
    /// </summary>
    /// <remarks>受け取らないなら、ロガーはイベントを作りません（割り当てなし）。</remarks>
    /// <param name="category">分類。</param>
    /// <param name="level">レベル。</param>
    /// <returns>受け取るなら true。</returns>
    bool IsEnabled(string category, LogLevel level);

    /// <summary>
    /// イベントを受け取ります。
    /// </summary>
    /// <param name="diagnosticEvent">イベント。</param>
    void Emit(DiagnosticEvent diagnosticEvent);

    /// <summary>
    /// 受け取ったイベントを書き終えるまで、最大で指定の時間だけ待ちます。
    /// </summary>
    /// <param name="timeout">待つ上限。</param>
    /// <returns>書き終えたら true。</returns>
    bool Flush(TimeSpan timeout);

    /// <summary>
    /// 捕まえられなかった例外を、直前の出来事とともに同期で書きます。
    /// </summary>
    /// <remarks>プロセスが落ちる直前に呼ばれます。戻るまでにディスクへ書き終えます。</remarks>
    /// <param name="crash">クラッシュのイベント。</param>
    void WriteCrash(DiagnosticEvent crash);
}
