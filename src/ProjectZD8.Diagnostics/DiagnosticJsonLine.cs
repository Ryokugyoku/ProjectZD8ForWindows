using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// イベントを JSONL の 1 行（末尾に改行）にします（docs/LOGGING_STRATEGY.md の 3 の「1 行の形」）。
/// </summary>
/// <remarks>
/// キーの順は <c>ts</c> <c>lvl</c> <c>cat</c> <c>evt</c> <c>run</c> <c>seq</c> <c>sid</c> <c>msg</c> <c>props</c> <c>ex</c>。
/// <c>sid</c> はあるときだけ、<c>props</c> は値があるときだけ、<c>ex</c> は例外があるときだけ書きます。
/// リングバッファから吐き出した行には <c>"ring":true</c> を付けます。
/// 1 つのインスタンスは 1 つのスレッドから使います（書込みのバッファを使い回すため）。
/// </remarks>
internal sealed class DiagnosticJsonLine
{
    /// <summary>日本語をそのまま読めるよう、HTML 向けの逃がしをしない設定です（ファイルにしか書かない）。</summary>
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = true,
    };

    /// <summary>改行です。</summary>
    private static readonly byte[] NewLine = "\n"u8.ToArray();

    /// <summary>書込みのバッファです。</summary>
    private readonly ArrayBufferWriter<byte> _buffer = new(1024);

    /// <summary>JSON の書き手です。</summary>
    private readonly Utf8JsonWriter _writer;

    /// <summary>作ります。</summary>
    public DiagnosticJsonLine()
    {
        _writer = new Utf8JsonWriter(_buffer, WriterOptions);
    }

    /// <summary>
    /// 1 行を作ります。
    /// </summary>
    /// <remarks>責務: 決まったキーの順で 1 行の JSON と改行を作り、そのバイト列を返します（次の呼び出しまで有効）。</remarks>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <param name="fromBuffer">リングバッファから吐き出した行なら true。</param>
    /// <returns>1 行の UTF-8 のバイト列。</returns>
    public ReadOnlyMemory<byte> Format(DiagnosticEvent diagnosticEvent, bool fromBuffer)
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
        _writer.WriteStartObject();
        Span<char> time = stackalloc char[28];
        diagnosticEvent.Timestamp.UtcDateTime.TryFormat(
            time,
            out var length,
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            CultureInfo.InvariantCulture);
        _writer.WriteString("ts"u8, time[..length]);
        _writer.WriteString("lvl"u8, LevelName(diagnosticEvent.Level));
        _writer.WriteString("cat"u8, diagnosticEvent.Category);
        _writer.WriteString("evt"u8, diagnosticEvent.EventName);
        _writer.WriteString("run"u8, diagnosticEvent.RunId);
        _writer.WriteNumber("seq"u8, diagnosticEvent.Sequence);
        if (diagnosticEvent.SessionId is { } sessionId)
        {
            _writer.WriteString("sid"u8, sessionId);
        }

        _writer.WriteString("msg"u8, diagnosticEvent.Message);
        if (diagnosticEvent.Properties.Count > 0)
        {
            _writer.WriteStartObject("props"u8);
            foreach (var pair in diagnosticEvent.Properties)
            {
                _writer.WritePropertyName(pair.Key);
                WriteValue(pair.Value);
            }

            _writer.WriteEndObject();
        }

        if (diagnosticEvent.Exception is { } exception)
        {
            _writer.WritePropertyName("ex"u8);
            WriteException(exception);
        }

        if (fromBuffer)
        {
            _writer.WriteBoolean("ring"u8, true);
        }

        _writer.WriteEndObject();
        _writer.Flush();
        _buffer.Write(NewLine);
        return _buffer.WrittenMemory;
    }

    /// <summary>
    /// レベルの名前を返します。
    /// </summary>
    /// <param name="level">レベル。</param>
    /// <returns><c>trace</c> / <c>debug</c> / <c>info</c> / <c>warn</c> / <c>error</c> / <c>fatal</c>。</returns>
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        _ => "fatal",
    };

    /// <summary>値を書きます（数値は数値、真偽は真偽、それ以外は文字列）。</summary>
    /// <param name="value">固定した値。</param>
    private void WriteValue(object? value)
    {
        switch (value)
        {
            case null:
                _writer.WriteNullValue();
                break;
            case string text:
                _writer.WriteStringValue(text);
                break;
            case bool flag:
                _writer.WriteBooleanValue(flag);
                break;
            case int number:
                _writer.WriteNumberValue(number);
                break;
            case long number:
                _writer.WriteNumberValue(number);
                break;
            case short number:
                _writer.WriteNumberValue(number);
                break;
            case byte number:
                _writer.WriteNumberValue(number);
                break;
            case sbyte number:
                _writer.WriteNumberValue(number);
                break;
            case uint number:
                _writer.WriteNumberValue(number);
                break;
            case ulong number:
                _writer.WriteNumberValue(number);
                break;
            case ushort number:
                _writer.WriteNumberValue(number);
                break;
            case double number when double.IsFinite(number):
                _writer.WriteNumberValue(number);
                break;
            case float number when float.IsFinite(number):
                _writer.WriteNumberValue(number);
                break;
            case decimal number:
                _writer.WriteNumberValue(number);
                break;
            default:
                _writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>例外を書きます（内側の例外は入れ子）。</summary>
    /// <param name="exception">例外の中身。</param>
    private void WriteException(DiagnosticExceptionInfo exception)
    {
        _writer.WriteStartObject();
        _writer.WriteString("type"u8, exception.Type);
        _writer.WriteString("msg"u8, exception.Message);
        _writer.WriteString("stack"u8, exception.StackTrace);
        _writer.WritePropertyName("inner"u8);
        if (exception.Inner is { } inner)
        {
            WriteException(inner);
        }
        else
        {
            _writer.WriteNullValue();
        }

        _writer.WriteEndObject();
    }
}
