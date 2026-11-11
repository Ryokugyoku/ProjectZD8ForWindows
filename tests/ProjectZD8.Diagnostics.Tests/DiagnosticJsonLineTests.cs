using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>1 行の JSON の形（docs/LOGGING_STRATEGY.md の 3）を確かめます。</summary>
public sealed class DiagnosticJsonLineTests
{
    /// <summary>文書のとおりのキーが、文書の順で並ぶ。</summary>
    [Fact]
    public void Format_WritesDocumentedKeysInOrder()
    {
        var exception = Capture(() => throw new InvalidOperationException("外側", new TimeoutException("内側")));
        var diagnosticEvent = new DiagnosticEvent(
            LogLevel.Error,
            "obd.connect",
            "obd.connect.failed",
            "接続に失敗しました",
            [new("transport", "ble"), new("elapsedMs", 10012L), new("ok", false), new("ratio", 0.5), new("none", null)],
            DiagnosticExceptionInfo.From(exception),
            sessionId: "3F2A");
        diagnosticEvent.Stamp(new DateTimeOffset(2026, 11, 11, 12, 34, 56, TimeSpan.Zero).AddTicks(7890123), "7f3a9c2e", 1842);

        var text = Format(diagnosticEvent, fromBuffer: false);

        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', text.TrimEnd('\n'));
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal(
            ["ts", "lvl", "cat", "evt", "run", "seq", "sid", "msg", "props", "ex"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("2026-11-11T12:34:56.7890123Z", root.GetProperty("ts").GetString());
        Assert.Equal("error", root.GetProperty("lvl").GetString());
        Assert.Equal("obd.connect", root.GetProperty("cat").GetString());
        Assert.Equal("obd.connect.failed", root.GetProperty("evt").GetString());
        Assert.Equal("7f3a9c2e", root.GetProperty("run").GetString());
        Assert.Equal(1842, root.GetProperty("seq").GetInt64());
        Assert.Equal("3F2A", root.GetProperty("sid").GetString());
        Assert.Equal("接続に失敗しました", root.GetProperty("msg").GetString());

        var props = root.GetProperty("props");
        Assert.Equal("ble", props.GetProperty("transport").GetString());
        Assert.Equal(JsonValueKind.Number, props.GetProperty("elapsedMs").ValueKind);
        Assert.Equal(10012, props.GetProperty("elapsedMs").GetInt64());
        Assert.Equal(JsonValueKind.False, props.GetProperty("ok").ValueKind);
        Assert.Equal(0.5, props.GetProperty("ratio").GetDouble());
        Assert.Equal(JsonValueKind.Null, props.GetProperty("none").ValueKind);

        var ex = root.GetProperty("ex");
        Assert.Equal("System.InvalidOperationException", ex.GetProperty("type").GetString());
        Assert.Equal("外側", ex.GetProperty("msg").GetString());
        Assert.Contains(nameof(Format_WritesDocumentedKeysInOrder), ex.GetProperty("stack").GetString(), StringComparison.Ordinal);
        Assert.Equal("System.TimeoutException", ex.GetProperty("inner").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, ex.GetProperty("inner").GetProperty("inner").ValueKind);
    }

    /// <summary>sid・props・ex は、ないときは書かない。日本語はそのまま書く。</summary>
    [Fact]
    public void Format_OmitsOptionalKeysAndKeepsJapaneseReadable()
    {
        var diagnosticEvent = new DiagnosticEvent(LogLevel.Information, "app", "app.start", "アプリを起動しました");
        diagnosticEvent.Stamp(DateTimeOffset.UnixEpoch, "00000000", 1);

        var text = Format(diagnosticEvent, fromBuffer: false);

        Assert.Contains("アプリを起動しました", text, StringComparison.Ordinal);
        var names = JsonDocument.Parse(text).RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(["ts", "lvl", "cat", "evt", "run", "seq", "msg"], names);
    }

    /// <summary>リングバッファから吐き出した行には ring が付く。</summary>
    [Fact]
    public void Format_MarksBufferedLines()
    {
        var diagnosticEvent = new DiagnosticEvent(LogLevel.Debug, "acq", "acq.batch.saved", "x");
        diagnosticEvent.Stamp(DateTimeOffset.UnixEpoch, "00000000", 1);

        var root = JsonDocument.Parse(Format(diagnosticEvent, fromBuffer: true)).RootElement;

        Assert.True(root.GetProperty("ring").GetBoolean());
    }

    /// <summary>レベルの名前は文書の 6 つ。</summary>
    /// <param name="level">レベル。</param>
    /// <param name="expected">名前。</param>
    [Theory]
    [InlineData(LogLevel.Trace, "trace")]
    [InlineData(LogLevel.Debug, "debug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Critical, "fatal")]
    public void LevelName_UsesDocumentedNames(LogLevel level, string expected) =>
        Assert.Equal(expected, DiagnosticJsonLine.LevelName(level));

    /// <summary>JSON に入れると壊れる文字（引用符・改行・制御文字）も 1 行に収まる。</summary>
    [Fact]
    public void Format_EscapesCharactersThatWouldBreakTheLine()
    {
        var diagnosticEvent = new DiagnosticEvent(LogLevel.Warning, "obd.wire", "obd.wire.received", "a\"b\nc\r\u0001", [new("raw", "41 0C\r\n>")]);
        diagnosticEvent.Stamp(DateTimeOffset.UnixEpoch, "00000000", 1);

        var text = Format(diagnosticEvent, fromBuffer: false);

        Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal("a\"b\nc\r\u0001", root.GetProperty("msg").GetString());
        Assert.Equal("41 0C\r\n>", root.GetProperty("props").GetProperty("raw").GetString());
    }

    /// <summary>1 行にします。</summary>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <param name="fromBuffer">リングバッファからか。</param>
    /// <returns>1 行。</returns>
    private static string Format(DiagnosticEvent diagnosticEvent, bool fromBuffer) =>
        Encoding.UTF8.GetString(new DiagnosticJsonLine().Format(diagnosticEvent, fromBuffer).Span);

    /// <summary>投げた例外を捕まえて返します（スタックを持たせるため）。</summary>
    /// <param name="action">投げる処理。</param>
    /// <returns>例外。</returns>
    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("投げませんでした");
    }
}
