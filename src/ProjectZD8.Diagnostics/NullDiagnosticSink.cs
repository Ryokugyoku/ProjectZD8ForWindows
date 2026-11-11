using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 何もしない出力先です（差し込む前の既定、テスト）。
/// </summary>
public sealed class NullDiagnosticSink : IDiagnosticSink
{
    /// <summary>共有のインスタンスです。</summary>
    public static NullDiagnosticSink Instance { get; } = new();

    /// <summary>作ります。</summary>
    private NullDiagnosticSink()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled(string category, LogLevel level) => false;

    /// <inheritdoc />
    public void Emit(DiagnosticEvent diagnosticEvent)
    {
    }

    /// <inheritdoc />
    public bool Flush(TimeSpan timeout) => true;

    /// <inheritdoc />
    public void WriteCrash(DiagnosticEvent crash)
    {
    }
}
