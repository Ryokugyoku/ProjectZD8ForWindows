using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>診断ログの出力先を差し替えるテストを、ほかのテストと並行させないための集まりです。</summary>
/// <remarks>出力先はプロセスで共有なので、差し替えている間にほかのテストのイベントが混ざらないようにします。</remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OBDDiagnosticLogCollection
{
    /// <summary>集まりの名前です。</summary>
    public const string Name = "obd-diagnostic-log";
}

/// <summary>すべての分類・レベルのイベントを受け取って覚えておく出力先です（Dispose で元に戻す）。</summary>
internal sealed class DiagnosticLogCapture : IDiagnosticSink, IDisposable
{
    /// <summary>受け取ったイベントです。</summary>
    private readonly List<DiagnosticEvent> events = [];

    /// <summary>差し込みを戻すものです。</summary>
    private readonly IDisposable installation;

    /// <summary>出力先として差し込みます。</summary>
    public DiagnosticLogCapture()
    {
        installation = DiagnosticLog.Install(this);
    }

    /// <summary>受け取ったイベントの写しです。</summary>
    public IReadOnlyList<DiagnosticEvent> Events
    {
        get
        {
            lock (events)
            {
                return events.ToArray();
            }
        }
    }

    /// <summary>指定のイベント名のイベントを返します。</summary>
    /// <param name="eventName">イベント名。</param>
    /// <returns>受け取った順のイベント。</returns>
    public IReadOnlyList<DiagnosticEvent> Named(string eventName) =>
        Events.Where(diagnosticEvent => diagnosticEvent.EventName == eventName).ToArray();

    /// <summary>受け取ったイベント名の一覧です。</summary>
    public IReadOnlyList<string> EventNames => Events.Select(diagnosticEvent => diagnosticEvent.EventName).ToArray();

    /// <summary>すべてのイベントの文言と値を 1 つの文字列にします（伏せたかの確認用）。</summary>
    /// <returns>連結した文字列。</returns>
    public string AllText() => string.Join(
        "\n",
        Events.Select(diagnosticEvent => diagnosticEvent.Message + " " + string.Join(
            " ",
            diagnosticEvent.Properties.Select(pair => $"{pair.Key}={pair.Value}"))));

    /// <inheritdoc />
    public bool IsEnabled(string category, LogLevel level) => true;

    /// <inheritdoc />
    public void Emit(DiagnosticEvent diagnosticEvent)
    {
        lock (events)
        {
            events.Add(diagnosticEvent);
        }
    }

    /// <inheritdoc />
    public bool Flush(TimeSpan timeout) => true;

    /// <inheritdoc />
    public void WriteCrash(DiagnosticEvent crash) => Emit(crash);

    /// <summary>元の出力先に戻します。</summary>
    public void Dispose() => installation.Dispose();
}

/// <summary>診断ログのイベントの値を読むための道具です。</summary>
internal static class DiagnosticEventProperties
{
    /// <summary>値を返します。</summary>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <param name="key">値の名前（<c>{Name}</c> の名前）。</param>
    /// <returns>値。なければ null。</returns>
    public static object? Prop(this DiagnosticEvent diagnosticEvent, string key) =>
        diagnosticEvent.Properties.FirstOrDefault(pair => pair.Key == key).Value;
}
