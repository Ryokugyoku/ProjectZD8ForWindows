using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 診断ログの静的な入口です。ロガーを取り、出力先を差し込み、接続セッション ID を流れに付けます。
/// </summary>
/// <remarks>
/// <para>
/// 使い方：クラスに <c>private static readonly ILogger Log = DiagnosticLog.For&lt;Foo&gt;();</c> を置き、
/// <c>[LoggerMessage]</c> で書いたメソッドから書きます。コンストラクターに足さないので、静的な組み立て（DI なし）と既存のテストに手を入れずに済みます。
/// </para>
/// <para>
/// 既定の出力先は何もしません（<see cref="NullDiagnosticSink"/>）。アプリは組み立ての最初で <see cref="DiagnosticSession.Start"/> が本物を差し込みます。
/// ロガーは書くたびに今の出力先を見るので、差し込む前に取ったロガーもそのまま使えます。
/// </para>
/// <para>
/// テストでは <see cref="Install"/> で差し替え、戻り値を Dispose して元に戻します。出力先は 1 つのプロセスで共有なので、
/// 差し替えるテストは並行に走らせないこと（xUnit の <c>[Collection]</c> で <c>DisableParallelization</c> にする）。
/// </para>
/// </remarks>
public static class DiagnosticLog
{
    /// <summary>今の出力先です。</summary>
    private static IDiagnosticSink s_sink = NullDiagnosticSink.Instance;

    /// <summary>流れに付いている接続セッション ID です。</summary>
    private static readonly AsyncLocal<string?> s_sessionId = new();

    /// <summary>分類ごとのロガーです（同じ分類なら同じインスタンスを返す）。</summary>
    private static readonly ConcurrentDictionary<string, DiagnosticLogger> s_loggers = new(StringComparer.Ordinal);

    /// <summary>最後に記録したクラッシュの例外です（同じ例外を 2 つのハンドラーから二重に書かないため）。</summary>
    private static Exception? s_lastCrash;

    /// <summary>今の出力先です。</summary>
    public static IDiagnosticSink Sink => Volatile.Read(ref s_sink);

    /// <summary>今の実行の診断ログのセッションです。差し込む前（テストなど）は null。</summary>
    public static DiagnosticSession? Session { get; internal set; }

    /// <summary>流れに付いている接続セッション ID です（JSON の <c>sid</c>）。なければ null。</summary>
    public static string? CurrentSessionId => s_sessionId.Value;

    /// <summary>
    /// 型に付いた <see cref="DiagnosticCategoryAttribute"/> の分類（なければ型の完全名）のロガーを返します。
    /// </summary>
    /// <typeparam name="T">ログを書く型。</typeparam>
    /// <returns>ロガー。</returns>
    public static ILogger For<T>() => CategoryLogger<T>.Instance;

    /// <summary>
    /// 分類を名前で指定してロガーを返します。
    /// </summary>
    /// <param name="category">分類（<see cref="DiagnosticCategories"/>）。</param>
    /// <returns>ロガー。</returns>
    public static ILogger For(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);
        return s_loggers.GetOrAdd(category, static name => new DiagnosticLogger(name));
    }

    /// <summary>
    /// 出力先を差し込みます。
    /// </summary>
    /// <remarks>責務: 出力先を入れ替え、Dispose で差し込む前の出力先に戻せるようにします。</remarks>
    /// <param name="sink">出力先。</param>
    /// <returns>Dispose すると元の出力先に戻すもの。</returns>
    public static IDisposable Install(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var previous = Interlocked.Exchange(ref s_sink, sink);
        return new Restoration(sink, previous);
    }

    /// <summary>
    /// これ以降の流れ（async の続きを含む）に接続セッション ID を付けます。
    /// </summary>
    /// <remarks>
    /// 接続セッションの開始で呼び、終わりで Dispose します。<see cref="ILogger.BeginScope{TState}"/> に
    /// <c>sid</c> を含む値の一覧を渡しても同じです。
    /// </remarks>
    /// <param name="sessionId">接続セッションの ID。</param>
    /// <returns>Dispose すると前の ID に戻すもの。</returns>
    public static IDisposable BeginConnectionSession(string? sessionId)
    {
        var previous = s_sessionId.Value;
        s_sessionId.Value = sessionId;
        return new SessionScope(previous);
    }

    /// <summary>
    /// 捕まえられなかった例外を、直前の出来事とともに同期で書きます。
    /// </summary>
    /// <remarks>
    /// 責務: クラッシュの 3 つのハンドラー（XAML・AppDomain・TaskScheduler）から呼ばれ、
    /// <c>crash-*.jsonl</c> と通常のログへ書きます。ここでは例外を投げません。
    /// </remarks>
    /// <param name="exception">例外。</param>
    /// <param name="source">受けたハンドラー。</param>
    public static void ReportCrash(Exception exception, DiagnosticCrashSource source)
    {
        if (exception is null || ReferenceEquals(Interlocked.Exchange(ref s_lastCrash, exception), exception))
        {
            return;
        }

        try
        {
            var terminating = source != DiagnosticCrashSource.UnobservedTask;
            var crash = new DiagnosticEvent(
                terminating ? LogLevel.Critical : LogLevel.Error,
                DiagnosticCategories.App,
                terminating ? DiagnosticEventNames.CrashUnhandled : DiagnosticEventNames.UnobservedTask,
                terminating ? "捕まえられなかった例外でアプリが終了します" : "待たれなかったタスクで例外が起きました",
                [
                    new("source", source.ToRawValue()),
                    new("terminating", terminating),
                ],
                DiagnosticExceptionInfo.From(exception),
                CurrentSessionId);
            Sink.WriteCrash(crash);
        }
        catch (Exception)
        {
            // クラッシュの記録で別の例外を起こさない。
        }
    }

    /// <summary>型ごとのロガーを 1 回だけ作ります。</summary>
    /// <typeparam name="T">ログを書く型。</typeparam>
    private static class CategoryLogger<T>
    {
        /// <summary>この型のロガーです。</summary>
        public static readonly ILogger Instance = For(
            typeof(T).GetCustomAttribute<DiagnosticCategoryAttribute>()?.Category
            ?? typeof(T).FullName
            ?? typeof(T).Name);
    }

    /// <summary>差し込んだ出力先を元に戻します。</summary>
    /// <param name="installed">差し込んだ出力先。</param>
    /// <param name="previous">差し込む前の出力先。</param>
    private sealed class Restoration(IDiagnosticSink installed, IDiagnosticSink previous) : IDisposable
    {
        /// <summary>戻したか。</summary>
        private int _disposed;

        /// <summary>差し込んだものがまだ今の出力先なら、元に戻します。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.CompareExchange(ref s_sink, previous, installed);
            }
        }
    }

    /// <summary>接続セッション ID を前の値に戻します。</summary>
    /// <param name="previous">前の ID。</param>
    private sealed class SessionScope(string? previous) : IDisposable
    {
        /// <summary>前の ID に戻します。</summary>
        public void Dispose() => s_sessionId.Value = previous;
    }
}
