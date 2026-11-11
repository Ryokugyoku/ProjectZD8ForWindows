using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Application.Tests.TestSupport;

/// <summary>診断ログを全レベルで受け取って覚えておく出力先です（<see cref="DiagnosticLog.Install"/> で差し込む）。</summary>
internal sealed class RecordingDiagnosticSink : IDiagnosticSink
{
    /// <summary>受け取ったイベントです。</summary>
    private readonly List<DiagnosticEvent> _events = [];

    /// <summary>これまでに受け取ったイベントの写しです。</summary>
    public IReadOnlyList<DiagnosticEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>指定のイベント名のイベントだけを返します。</summary>
    /// <param name="eventName">イベント名。</param>
    /// <returns>該当するイベント。</returns>
    public IReadOnlyList<DiagnosticEvent> Named(string eventName) =>
        [.. Events.Where(item => string.Equals(item.EventName, eventName, StringComparison.Ordinal))];

    /// <summary>
    /// 受け取った全イベントのメッセージ・値・例外を 1 つの文字列にします（伏せるべき値が残っていないかの検査用）。
    /// </summary>
    /// <returns>連結した文字列。</returns>
    public string Dump() => string.Join(
        "\n",
        Events.Select(item => item.Message + " " + string.Join(" ", item.Properties.Select(pair => $"{pair.Key}={pair.Value}"))
            + " " + item.Exception?.Message));

    /// <inheritdoc />
    public bool IsEnabled(string category, LogLevel level) => true;

    /// <inheritdoc />
    public void Emit(DiagnosticEvent diagnosticEvent)
    {
        lock (_events)
        {
            _events.Add(diagnosticEvent);
        }
    }

    /// <inheritdoc />
    public bool Flush(TimeSpan timeout) => true;

    /// <inheritdoc />
    public void WriteCrash(DiagnosticEvent crash) => Emit(crash);
}

/// <summary>診断ログの出力先を差し替えるテストをまとめる集まりです（出力先はプロセスで共有なので並行させない）。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiagnosticSinkCollection
{
    /// <summary>集まりの名前です。</summary>
    public const string Name = "DiagnosticLog sink";
}
