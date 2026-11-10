using System.Text;
using ProjectZD8.Data.Persistence.LogStore;
using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.LogStore;

/// <summary>生ログと取得証拠のストア（LogStore）の形式・範囲読取・クラッシュ回復を検証します。</summary>
/// <remarks>iOS版にはないWindows版の追加テストです。</remarks>
public sealed class ConnectionSessionLogStoreTests
{
    /// <summary>テスト用のセッションIDです。</summary>
    private static readonly ConnectionSessionID SessionID = new(Guid.Parse("30000000-0000-0000-0000-000000000003"));

    /// <summary>CRC-32が標準の検査値と一致することを確かめます。</summary>
    [Fact]
    public void Crc32MatchesStandardCheckValue()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0u, Crc32.Compute([]));
    }

    /// <summary>全種類のレコードが符号化と復号で往復することを確かめます。</summary>
    [Fact]
    public void EveryRecordKindRoundTripsThroughCodec()
    {
        var entry = new ConnectionSessionRawLogEntry(7, TestDates.UnixTicks(123_456_789), 42, 0x01, 0x0C, [0x1A, 0xF8]);
        var terminal = new PIDRequestEvidence(1, 3, PIDRequestDispatchState.Terminal, PIDRequestTransportOutcome.TimedOut, PIDRequestValueOutcome.NotEvaluated, null, 99, "response_timeout");
        var responded = new PIDRequestEvidence(0, 2, PIDRequestDispatchState.Terminal, PIDRequestTransportOutcome.Responded, PIDRequestValueOutcome.DecodedValid, 7, 11, null);
        var batch = new AcquisitionBatchEvidence(
            new AcquisitionBatchIdentity(5),
            ulong.MaxValue,
            9,
            true,
            TestDates.UnixTicks(10),
            AcquisitionBatchCompletionState.Failed,
            TestDates.UnixTicks(20),
            AcquisitionBatchFailure.Cancelled,
            [responded, terminal]);
        SessionLogRecord[] records =
        [
            new SessionLogRecord.Raw(entry),
            new SessionLogRecord.BatchBegun(5, 1, 2, true, 100, [2, 3]),
            new SessionLogRecord.DispatchBegun(5, 1),
            new SessionLogRecord.RequestTerminal(5, terminal),
            new SessionLogRecord.Responded(entry, 5, responded),
            new SessionLogRecord.BatchSealed(5, AcquisitionBatchCompletionState.Failed, 20, AcquisitionBatchFailure.Cancelled, [terminal]),
            new SessionLogRecord.BatchSnapshot(batch),
        ];

        foreach (var record in records)
        {
            var frame = SessionLogRecordCodec.EncodeFrame(record);
            Assert.True(SessionLogRecordCodec.TryReadFrameHeader(frame, out var length, out var checksum));
            Assert.Equal(frame.Length - SessionLogRecordCodec.FrameHeaderSize, length);
            Assert.Equal(Crc32.Compute(frame.AsSpan(SessionLogRecordCodec.FrameHeaderSize)), checksum);
            var decoded = SessionLogRecordCodec.Decode(frame.AsSpan(SessionLogRecordCodec.FrameHeaderSize));
            Assert.Equal(Describe(record), Describe(decoded));
        }
    }

    /// <summary>sequence範囲の読取が索引のブロック境界をまたいでも正しいことを確かめます。</summary>
    [Fact]
    public void SequenceRangeReadsAcrossIndexBlocks()
    {
        using var directory = new TemporaryDirectory();
        using var store = ConnectionSessionLogStore.Open(directory.Path, new LogStoreOptions { IndexStride = 16 });
        var log = store.Get(SessionID);
        var appended = Enumerable.Range(0, 1_000).Select(index => log.AppendRaw(Observation(index))).ToList();

        Assert.Equal(appended, log.ReadAll());
        Assert.Equal(appended.Skip(15).Take(20), log.ReadSequenceRange(15, 35, int.MaxValue));
        Assert.Equal(appended.Skip(990), log.ReadSequenceRange(990, long.MaxValue, int.MaxValue));
        Assert.Equal(appended.Skip(100).Take(5), log.ReadSequenceRange(100, 200, 5));
        Assert.Empty(log.ReadSequenceRange(1_000, 2_000, int.MaxValue));
        Assert.Equal(appended[512], log.ReadEntry(512));
        Assert.Null(log.ReadEntry(5_000));
        Assert.Equal(1_000, log.Count);
        Assert.Equal(2_000, log.ByteCount);
    }

    /// <summary>観測日時の範囲読取が単調でない日時でも漏れなく返すことを確かめます。</summary>
    [Fact]
    public void TimeRangeReadsReturnEveryMatchingEntryEvenWhenTimestampsAreNotMonotonic()
    {
        using var directory = new TemporaryDirectory();
        using var store = ConnectionSessionLogStore.Open(directory.Path, new LogStoreOptions { IndexStride = 8 });
        var log = store.Get(SessionID);
        var appended = new List<ConnectionSessionRawLogEntry>();
        for (var index = 0; index < 500; index++)
        {
            var seconds = index % 50 == 49 ? 10.0 : 100.0 + index;
            appended.Add(log.AppendRaw(Observation(index, seconds)));
        }

        var window = log.ReadTimeRange(TestDates.Unix(200), TestDates.Unix(250));
        Assert.Equal(appended.Where(entry => entry.ObservedAt >= TestDates.Unix(200) && entry.ObservedAt <= TestDates.Unix(250)), window);
        var early = log.ReadTimeRange(TestDates.Unix(0), TestDates.Unix(20));
        Assert.Equal(10, early.Count);
        Assert.All(early, entry => Assert.Equal(TestDates.Unix(10), entry.ObservedAt));
        Assert.Empty(log.ReadTimeRange(TestDates.Unix(5_000), TestDates.Unix(6_000)));
    }

    /// <summary>書きかけの末尾レコード（途中までのバイト）を開くときに切り詰めることを確かめます。</summary>
    [Fact]
    public void OpenTruncatesTornTailRecord()
    {
        using var directory = new TemporaryDirectory();
        var appended = WriteAndAbandon(directory.Path, 100, LogStoreOptions.Default);
        var segment = SegmentPath(directory.Path, 0);
        var validLength = new FileInfo(segment).Length;
        var torn = SessionLogRecordCodec.EncodeFrame(new SessionLogRecord.Raw(appended[^1]));
        using (var stream = new FileStream(segment, FileMode.Append, FileAccess.Write))
        {
            stream.Write(torn, 0, torn.Length / 2);
        }

        using var store = ConnectionSessionLogStore.Open(directory.Path);
        var log = store.Get(SessionID);

        Assert.Equal(appended, log.ReadAll());
        Assert.Equal(validLength, new FileInfo(segment).Length);
        var next = log.AppendRaw(Observation(100));
        Assert.Equal(100, next.Sequence);
        Assert.Equal(101, log.ReadAll().Count);
    }

    /// <summary>途中で切れた最後のレコードを捨て、その前までを残すことを確かめます。</summary>
    [Fact]
    public void OpenDropsPartiallyWrittenLastRecord()
    {
        using var directory = new TemporaryDirectory();
        var appended = WriteAndAbandon(directory.Path, 100, LogStoreOptions.Default);
        var segment = SegmentPath(directory.Path, 0);
        using (var stream = new FileStream(segment, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(stream.Length - 3);
        }

        using var store = ConnectionSessionLogStore.Open(directory.Path);

        Assert.Equal(appended.Take(99), store.Get(SessionID).ReadAll());
    }

    /// <summary>CRCが合わない最後のレコードを捨てることを確かめます。</summary>
    [Fact]
    public void OpenDropsTailRecordWithChecksumMismatch()
    {
        using var directory = new TemporaryDirectory();
        var appended = WriteAndAbandon(directory.Path, 10, LogStoreOptions.Default);
        var segment = SegmentPath(directory.Path, 0);
        using (var stream = new FileStream(segment, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(-1, SeekOrigin.End);
            var last = stream.ReadByte();
            stream.Seek(-1, SeekOrigin.End);
            stream.WriteByte((byte)(last ^ 0xFF));
        }

        using var store = ConnectionSessionLogStore.Open(directory.Path);

        Assert.Equal(appended.Take(9), store.Get(SessionID).ReadAll());
    }

    /// <summary>ヘッダーだけ作って落ちた空のセグメントを回復することを確かめます。</summary>
    [Fact]
    public void OpenRecoversEmptySegmentFile()
    {
        using var directory = new TemporaryDirectory();
        var generation = Path.Combine(directory.Path, SessionID.RawValue.ToString("D"), "g000001");
        Directory.CreateDirectory(generation);
        File.WriteAllBytes(Path.Combine(generation, "seg-000000.log"), []);

        using var store = ConnectionSessionLogStore.Open(directory.Path);
        var log = store.Get(SessionID);

        Assert.Empty(log.ReadAll());
        Assert.Equal(0, log.AppendRaw(Observation(0)).Sequence);
        Assert.Single(log.ReadAll());
    }

    /// <summary>セグメントの切替と圧縮のあとも、全件と範囲を正しく読めることを確かめます。</summary>
    [Fact]
    public void RotatedAndCompressedSegmentsRemainReadableAcrossReopen()
    {
        using var directory = new TemporaryDirectory();
        var options = new LogStoreOptions { SegmentMaxBytes = 4 * 1024, IndexStride = 32, CompressSealedSegments = true };
        List<ConnectionSessionRawLogEntry> appended;
        using (var store = ConnectionSessionLogStore.Open(directory.Path, options))
        {
            var log = store.Get(SessionID);
            appended = Enumerable.Range(0, 2_000).Select(index => log.AppendRaw(Observation(index))).ToList();
            Assert.Equal(appended, log.ReadAll());
        }
        var generation = Path.Combine(directory.Path, SessionID.RawValue.ToString("D"), "g000001");
        Assert.NotEmpty(Directory.GetFiles(generation, "*.logz"));
        Assert.True(File.Exists(Path.Combine(generation, "index.bin")));

        using (var store = ConnectionSessionLogStore.Open(directory.Path, options))
        {
            var log = store.Get(SessionID);
            Assert.Equal(appended, log.ReadAll());
            Assert.Equal(appended.Skip(1_234).Take(100), log.ReadSequenceRange(1_234, 1_334, int.MaxValue));
            Assert.Equal(appended.Skip(500).Take(11), log.ReadTimeRange(TestDates.Unix(1_500), TestDates.Unix(1_510)));
            var next = log.AppendRaw(Observation(2_000));
            Assert.Equal(2_000, next.Sequence);
        }

        File.Delete(Path.Combine(generation, "index.bin"));
        using (var store = ConnectionSessionLogStore.Open(directory.Path, options))
        {
            var all = store.Get(SessionID).ReadAll();
            Assert.Equal(2_001, all.Count);
            Assert.Equal(appended, all.Take(2_000));
        }
    }

    /// <summary>索引ファイルとセグメントが食い違う場合は走査し直すことを確かめます。</summary>
    [Fact]
    public void StaleSidecarIsIgnored()
    {
        using var directory = new TemporaryDirectory();
        List<ConnectionSessionRawLogEntry> appended;
        using (var store = ConnectionSessionLogStore.Open(directory.Path))
        {
            var log = store.Get(SessionID);
            appended = Enumerable.Range(0, 10).Select(index => log.AppendRaw(Observation(index))).ToList();
        }
        var segment = SegmentPath(directory.Path, 0);
        using (var stream = new FileStream(segment, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(stream.Length - 1);
        }

        using var reopened = ConnectionSessionLogStore.Open(directory.Path);
        Assert.Equal(appended.Take(9), reopened.Get(SessionID).ReadAll());
    }

    /// <summary>索引ファイルから開いたログへ追記する前に索引ファイルを消し、落ちても追記分を失わないことを確かめます。</summary>
    [Fact]
    public void SidecarIsInvalidatedBeforeAppend()
    {
        using var directory = new TemporaryDirectory();
        List<ConnectionSessionRawLogEntry> appended;
        using (var store = ConnectionSessionLogStore.Open(directory.Path))
        {
            var log = store.Get(SessionID);
            appended = Enumerable.Range(0, 10).Select(index => log.AppendRaw(Observation(index))).ToList();
        }
        var extra = WriteAndAbandonAppending(directory.Path, 10, 5);

        using var reopened = ConnectionSessionLogStore.Open(directory.Path);
        Assert.Equal(appended.Concat(extra), reopened.Get(SessionID).ReadAll());
    }

    /// <summary>どの確定方針でも同じ内容が残ることを確かめます。</summary>
    [Theory]
    [InlineData(LogStoreFlushPolicy.EveryGroup)]
    [InlineData(LogStoreFlushPolicy.Periodic)]
    [InlineData(LogStoreFlushPolicy.OperatingSystem)]
    public void EveryFlushPolicyPersistsAppendedEntries(LogStoreFlushPolicy policy)
    {
        using var directory = new TemporaryDirectory();
        var options = new LogStoreOptions { FlushPolicy = policy, FlushInterval = TimeSpan.FromMilliseconds(5) };
        var appended = WriteAndAbandon(directory.Path, 300, options);

        using var store = ConnectionSessionLogStore.Open(directory.Path, options);
        Assert.Equal(appended, store.Get(SessionID).ReadAll());
    }

    /// <summary>作りかけの世代ディレクトリーを開くときに捨てることを確かめます。</summary>
    [Fact]
    public void OpenDiscardsUnfinishedGeneration()
    {
        using var directory = new TemporaryDirectory();
        var appended = WriteAndAbandon(directory.Path, 5, LogStoreOptions.Default);
        var unfinished = Path.Combine(directory.Path, SessionID.RawValue.ToString("D"), "g000002.tmp");
        Directory.CreateDirectory(unfinished);
        File.WriteAllBytes(Path.Combine(unfinished, "seg-000000.log"), [0x01, 0x02]);

        using var store = ConnectionSessionLogStore.Open(directory.Path);

        Assert.Equal(appended, store.Get(SessionID).ReadAll());
        Assert.False(Directory.Exists(unfinished));
    }

    /// <summary>生ログの置換が新しい世代へ原子的に切り替わり、取消もできることを確かめます。</summary>
    [Fact]
    public void ReplacementCommitsOrRollsBackGeneration()
    {
        using var directory = new TemporaryDirectory();
        using var store = ConnectionSessionLogStore.Open(directory.Path);
        var log = store.Get(SessionID);
        var original = Enumerable.Range(0, 3).Select(index => log.AppendRaw(Observation(index))).ToList();
        var replacement = Enumerable.Range(0, 5)
            .Select(index => new ConnectionSessionRawLogEntry(index, TestDates.Unix(500 + index), 1, 0x01, 0x0D, [(byte)index]))
            .ToList();

        var rolledBack = log.PrepareReplacement(replacement);
        rolledBack.Rollback();
        Assert.Equal(original, log.ReadAll());

        var committed = log.PrepareReplacement(replacement);
        committed.Commit();
        Assert.Equal(replacement, log.ReadAll());
        Assert.Equal(5, log.NextSequence);
        Assert.Equal(["g000002"], Directory.GetDirectories(Path.Combine(directory.Path, SessionID.RawValue.ToString("D"))).Select(Path.GetFileName));
    }

    /// <summary>エントリを書いて、索引ファイルを書かずにストアを捨てます。</summary>
    /// <remarks>責務: プロセスの異常終了直前の状態（書込みは確定済み、索引ファイルなし）を作ります。</remarks>
    /// <param name="directory">ストアのディレクトリー。</param>
    /// <param name="count">書く件数。</param>
    /// <param name="options">ストアの設定。</param>
    /// <returns>書いたエントリ。</returns>
    private static List<ConnectionSessionRawLogEntry> WriteAndAbandon(string directory, int count, LogStoreOptions options)
    {
        var store = ConnectionSessionLogStore.Open(directory, options);
        var log = store.Get(SessionID);
        var appended = Enumerable.Range(0, count).Select(index => log.AppendRaw(Observation(index))).ToList();
        log.WaitDurable();
        store.Abandon();
        return appended;
    }

    /// <summary>既存のログへ追記して、索引ファイルを書かずにストアを捨てます。</summary>
    /// <remarks>責務: 索引ファイルを書いたあとに追記されて落ちた状態を作ります。</remarks>
    /// <param name="directory">ストアのディレクトリー。</param>
    /// <param name="start">最初のエントリの番号。</param>
    /// <param name="count">書く件数。</param>
    /// <returns>書いたエントリ。</returns>
    private static List<ConnectionSessionRawLogEntry> WriteAndAbandonAppending(string directory, int start, int count)
    {
        var store = ConnectionSessionLogStore.Open(directory);
        var log = store.Get(SessionID);
        var appended = Enumerable.Range(start, count).Select(index => log.AppendRaw(Observation(index))).ToList();
        log.WaitDurable();
        store.Abandon();
        return appended;
    }

    /// <summary>最初の世代のセグメントのパスを返します。</summary>
    /// <remarks>責務: テストが直接壊すファイルの場所を求めます。</remarks>
    /// <param name="directory">ストアのディレクトリー。</param>
    /// <param name="segment">セグメント番号。</param>
    /// <returns>ファイルパス。</returns>
    private static string SegmentPath(string directory, int segment) =>
        SegmentFileFormat.PlainPath(Path.Combine(directory, SessionID.RawValue.ToString("D"), "g000001"), segment);

    /// <summary>テスト用の未デコード応答を作ります。</summary>
    /// <remarks>責務: 番号から観測時刻とデータを決めた応答を作ります。</remarks>
    /// <param name="index">番号。</param>
    /// <param name="seconds">観測時刻（省略時は 1000 + 番号）。</param>
    /// <returns>未デコード応答。</returns>
    private static OBDRawResponseObservation Observation(int index, double? seconds = null) =>
        new(TestDates.Unix(seconds ?? 1_000 + index), (ulong)index, new OBDPIDRequest(0x01, 0x0C), [(byte)index, (byte)(index >> 8)]);

    /// <summary>レコードを比較用の文字列にします。</summary>
    /// <remarks>責務: 配列を含むレコードの値を比較できる形へ変換します。</remarks>
    /// <param name="record">レコード。</param>
    /// <returns>文字列表現。</returns>
    private static string Describe(SessionLogRecord record) => record switch
    {
        SessionLogRecord.Raw raw => $"raw:{DescribeEntry(raw.Entry)}",
        SessionLogRecord.BatchBegun begun => $"begun:{begun.Ordinal}:{begun.Generation}:{begun.PolicyTick}:{begun.IsSelectionEvaluationComplete}:{begun.StartedTicks}:{string.Join(",", begun.ManifestOrdinals)}",
        SessionLogRecord.DispatchBegun dispatch => $"dispatch:{dispatch.Ordinal}:{dispatch.RequestOrdinal}",
        SessionLogRecord.RequestTerminal terminal => $"terminal:{terminal.Ordinal}:{terminal.Request}",
        SessionLogRecord.Responded responded => $"responded:{DescribeEntry(responded.Entry)}:{responded.Ordinal}:{responded.Request}",
        SessionLogRecord.BatchSealed sealedBatch => $"sealed:{sealedBatch.Ordinal}:{sealedBatch.CompletionState}:{sealedBatch.CompletedTicks}:{sealedBatch.Failure}:{string.Join(",", sealedBatch.Updates.Select(update => update.ToString()))}",
        SessionLogRecord.BatchSnapshot snapshot => $"snapshot:{snapshot.Batch.GetHashCode()}:{snapshot.Batch.Identity.Ordinal}:{string.Join(",", snapshot.Batch.Requests)}",
        _ => throw new ArgumentOutOfRangeException(nameof(record)),
    };

    /// <summary>生ログを比較用の文字列にします。</summary>
    /// <remarks>責務: 応答データを含めて生ログを文字列へ変換します。</remarks>
    /// <param name="entry">生ログ。</param>
    /// <returns>文字列表現。</returns>
    private static string DescribeEntry(ConnectionSessionRawLogEntry entry) =>
        $"{entry.Sequence}:{entry.ObservedAt.UtcTicks}:{entry.BatchElapsedNanoseconds}:{entry.Service}:{entry.Pid}:{Convert.ToHexString(entry.Payload.ToArray())}";
}
