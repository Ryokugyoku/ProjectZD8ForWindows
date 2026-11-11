using Microsoft.Extensions.Logging;
using ProjectZD8.Diagnostics.Tests.Support;

namespace ProjectZD8.Diagnostics.Tests;

/// <summary>リングバッファの順序・上書き・吐き出しを確かめます。</summary>
public sealed class DiagnosticRingBufferTests
{
    /// <summary>いっぱいになったら古いものから上書きし、古い順に返す。</summary>
    [Fact]
    public void Add_OverwritesOldestAndKeepsOrder()
    {
        var ring = new DiagnosticRingBuffer(3);
        var events = Enumerable.Range(1, 5).Select(index => Named(index)).ToList();
        foreach (var diagnosticEvent in events)
        {
            ring.Add(diagnosticEvent, written: false);
        }

        Assert.Equal(3, ring.Count);
        Assert.Equal(["3", "4", "5"], ring.Snapshot().Select(e => e.Message));
    }

    /// <summary>吐き出しはまだ書いていないものだけを返し、2 回目は返さない。</summary>
    [Fact]
    public void TakeUnwritten_ReturnsOnlyUnwrittenOnce()
    {
        var ring = new DiagnosticRingBuffer(10);
        ring.Add(Named(1), written: false);
        ring.Add(Named(2), written: true);
        ring.Add(Named(3), written: false);

        Assert.Equal(["1", "3"], ring.TakeUnwritten().Select(e => e.Message));
        Assert.Empty(ring.TakeUnwritten());
        Assert.Equal(["1", "2", "3"], ring.Snapshot().Select(e => e.Message));
    }

    /// <summary>番号を名前にしたイベントを作ります。</summary>
    /// <param name="index">番号。</param>
    /// <returns>イベント。</returns>
    private static DiagnosticEvent Named(int index) =>
        new(LogLevel.Debug, "acq", "acq.test.debug", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
