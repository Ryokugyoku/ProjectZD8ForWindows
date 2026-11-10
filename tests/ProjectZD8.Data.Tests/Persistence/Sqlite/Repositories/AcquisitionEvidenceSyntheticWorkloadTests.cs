using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>合成した取得workloadで、取得証拠と生ログの整合を検証します。</summary>
/// <remarks>
/// iOS版 <c>GRDBAcquisitionEvidenceSyntheticMeasurementTests</c> の検証部分の移植です。
/// 計測マトリクス（PHASE4F_RUN_MEASUREMENTS）と統計のテストは、SQLiteの計測用なので移さず、
/// LogStoreの性能は <c>tools/ProjectZD8.LogStoreBenchmark</c> で測ります。
/// </remarks>
public sealed class AcquisitionEvidenceSyntheticWorkloadTests
{
    /// <summary>応答の混ざり方です。</summary>
    public enum TransportMix
    {
        /// <summary>すべて正応答です。</summary>
        Responded100,
        /// <summary>4件に1件がtimeoutです。</summary>
        Responded75TimedOut25,
        /// <summary>後半が各種の失敗です。</summary>
        Responded50MixedFailures,
        /// <summary>前半だけdispatchするpartial batchです。</summary>
        PartialBatch,
    }

    /// <summary>代表的なworkloadが期待どおりの件数を作ることを確かめます。</summary>
    [Theory]
    [InlineData(1, TransportMix.Responded100)]
    [InlineData(4, TransportMix.Responded75TimedOut25)]
    [InlineData(8, TransportMix.Responded50MixedFailures)]
    [InlineData(16, TransportMix.PartialBatch)]
    public void RepresentativeWorkloadsProduceExpectedRows(int requestCount, TransportMix mix)
    {
        using var workload = new Workload(requestCount);
        workload.PersistBatch(0, mix, 8);
        var plan = OutcomePlan(requestCount, mix);
        var snapshot = workload.Snapshot();

        Assert.Equal(1, snapshot.BatchCount);
        Assert.Equal(requestCount, snapshot.RequestCount);
        Assert.Equal(plan.Count(outcome => outcome == PIDRequestTransportOutcome.Responded), snapshot.RawCount);
        Assert.True(snapshot.SequenceIsContiguous);
        Assert.True(snapshot.SummaryMatchesRaw);
    }

    /// <summary>100 batchのcanonical readbackとsequenceの連続を確かめます。</summary>
    [Fact]
    public void HundredBatchesPreserveCanonicalReadbackAndSequenceContinuity()
    {
        using var workload = new Workload(8);
        for (var batch = 0; batch < 100; batch++)
        {
            workload.PersistBatch(batch, TransportMix.Responded75TimedOut25, 256);
        }

        var snapshot = workload.Snapshot();
        Assert.Equal(100, snapshot.BatchCount);
        Assert.Equal(800, snapshot.RequestCount);
        Assert.Equal(600, snapshot.RawCount);
        Assert.True(snapshot.SequenceIsContiguous);
        Assert.True(snapshot.SummaryMatchesRaw);
        Assert.All(workload.Repository.Batches(workload.Session.Id), batch => Assert.NotNull(batch.CompletionState));
    }

    /// <summary>retryが行もファイル容量も増やさないことを確かめます。</summary>
    [Fact]
    public void RetriesDoNotAddRowsOrFileCapacity()
    {
        using var workload = new Workload(1);
        var batch = workload.OpenBatch(0, 1);
        workload.Repository.BeginBatch(batch, workload.Session.Id);
        workload.Repository.MarkRequestDispatchBegun(0, batch.Identity, workload.Session.Id);
        _ = workload.SaveResponded(0, 0, 256);
        var before = workload.Snapshot();
        var bytesBefore = workload.LogBytes();

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Duplicate, () => workload.SaveResponded(0, 0, 256));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Conflict, () => workload.SaveResponded(0, 0, 257));

        Assert.Equal(before, workload.Snapshot());
        Assert.Equal(bytesBefore, workload.LogBytes());
    }

    /// <summary>注入した失敗が保存前の状態を保ち、失敗を外せば保存できることを確かめます。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureInjectionsPreservePretransactionState(bool batchSeal)
    {
        using var workload = new Workload(1);
        var open = workload.OpenBatch(0, 1);
        workload.Repository.BeginBatch(open, workload.Session.Id);
        workload.Repository.MarkRequestDispatchBegun(0, open.Identity, workload.Session.Id);
        if (batchSeal)
        {
            _ = workload.SaveResponded(0, 0, 8);
        }
        workload.Repository.BeforeLogAppend = record =>
        {
            if ((batchSeal && record is SessionLogRecord.BatchSealed) || (!batchSeal && record is SessionLogRecord.Responded))
            {
                throw new IOException("injected failure");
            }
        };
        var before = workload.Snapshot();

        if (batchSeal)
        {
            var terminal = workload.TerminalBatch(0, workload.Repository.Batches(workload.Session.Id)[0].Requests, AcquisitionBatchCompletionState.Completed, null);
            Assert.ThrowsAny<Exception>(() => workload.Repository.FinishBatch(terminal, workload.Session.Id));
        }
        else
        {
            Assert.ThrowsAny<Exception>(() => workload.SaveResponded(0, 0, 8));
        }

        Assert.Equal(before, workload.Snapshot());
        workload.Repository.BeforeLogAppend = null;
        if (batchSeal)
        {
            var terminal = workload.TerminalBatch(0, workload.Repository.Batches(workload.Session.Id)[0].Requests, AcquisitionBatchCompletionState.Completed, null);
            workload.Repository.FinishBatch(terminal, workload.Session.Id);
            Assert.Equal(AcquisitionBatchCompletionState.Completed, workload.Repository.Batches(workload.Session.Id)[0].CompletionState);
        }
        else
        {
            Assert.Equal(PIDRequestTransportOutcome.Responded, workload.SaveResponded(0, 0, 8).TransportOutcome);
            Assert.Equal(1, workload.Snapshot().RawCount);
        }
    }

    /// <summary>workloadのrequestごとの結果の計画を返します。</summary>
    /// <remarks>責務: iOS版と同じ規則で、requestごとの結果（dispatchしない場合は <see langword="null"/>）を決めます。</remarks>
    /// <param name="requestCount">request件数。</param>
    /// <param name="mix">応答の混ざり方。</param>
    /// <returns>requestごとの結果。</returns>
    internal static PIDRequestTransportOutcome?[] OutcomePlan(int requestCount, TransportMix mix) =>
        Enumerable.Range(0, requestCount).Select(ordinal => mix switch
        {
            TransportMix.Responded100 => PIDRequestTransportOutcome.Responded,
            TransportMix.Responded75TimedOut25 => ordinal % 4 == 3 ? PIDRequestTransportOutcome.TimedOut : PIDRequestTransportOutcome.Responded,
            TransportMix.Responded50MixedFailures => ordinal < (requestCount + 1) / 2
                ? PIDRequestTransportOutcome.Responded
                : new[]
                {
                    PIDRequestTransportOutcome.TimedOut,
                    PIDRequestTransportOutcome.Cancelled,
                    PIDRequestTransportOutcome.TransportFailure,
                    PIDRequestTransportOutcome.UnclassifiedResponse,
                }[(ordinal - (requestCount + 1) / 2) % 4],
            TransportMix.PartialBatch => ordinal < (requestCount + 1) / 2
                ? (ordinal % 2 == 0 ? PIDRequestTransportOutcome.Responded : PIDRequestTransportOutcome.TimedOut)
                : (PIDRequestTransportOutcome?)null,
            _ => throw new ArgumentOutOfRangeException(nameof(mix)),
        }).ToArray();

    /// <summary>件数と並びの要約です。</summary>
    /// <param name="BatchCount">batch数。</param>
    /// <param name="RequestCount">request数。</param>
    /// <param name="RawCount">生ログ件数。</param>
    /// <param name="SequenceIsContiguous">sequenceが0から連続しているか。</param>
    /// <param name="SummaryMatchesRaw">セッション概要の件数・バイト数が生ログと一致するか。</param>
    /// <param name="State">batch・requestの状態の文字列表現。</param>
    internal sealed record LogicalSnapshot(int BatchCount, int RequestCount, int RawCount, bool SequenceIsContiguous, bool SummaryMatchesRaw, string State);

    /// <summary>合成workloadの保存一式です。</summary>
    internal sealed class Workload : IDisposable
    {
        /// <summary>保存一式です。</summary>
        private readonly ConnectionSessionStorageFixture _fixture = new();

        /// <summary>manifestの要求です。</summary>
        private readonly OBDPIDRequest[] _requests;

        /// <summary>request件数を指定して、セッションとmanifestを用意します。</summary>
        /// <remarks>責務: Service 240の合成PIDを要求するmanifestを保存します。</remarks>
        /// <param name="requestCount">request件数。</param>
        public Workload(int requestCount)
        {
            _requests = Enumerable.Range(0, requestCount).Select(ordinal => new OBDPIDRequest(240, (byte)ordinal)).ToArray();
            Session = new ConnectionSession("phase4f-synthetic-account", id: ConnectionSessionID.New(), startedAt: TestDates.Unix(1));
            Repository.Save(Session);
            Repository.SaveStartOnce(
                new ConnectionSessionAcquisitionManifest(
                    1,
                    new AcquisitionApplicationVersion("synthetic", "1"),
                    1,
                    1,
                    new OrderedAcquisitionPIDSet(_requests),
                    _requests.Select(request => new AcquisitionPIDDefinitionSnapshot(
                        request,
                        AcquisitionPIDCapabilitySupport.Supported,
                        true,
                        1,
                        1,
                        new AcquisitionPIDDefinitionIdentity(1, "A"),
                        "raw",
                        AcquisitionPIDValidityRange.NotDeclared)).ToArray(),
                    ConnectionSessionAcquisitionPlatform.MacOS,
                    1),
                TestDates.Unix(2),
                Session.Id);
        }

        /// <summary>保存一式です。</summary>
        public SqliteConnectionSessionRepository Repository => _fixture.Repository;

        /// <summary>対象のセッションです。</summary>
        public ConnectionSession Session { get; }

        /// <summary>open batchを作ります。</summary>
        /// <remarks>責務: 指定件数のselected-only requestを持つbatchを作ります。</remarks>
        /// <param name="ordinal">batch番号。</param>
        /// <param name="requestCount">request件数。</param>
        /// <returns>open batch。</returns>
        public AcquisitionBatchEvidence OpenBatch(int ordinal, int requestCount) =>
            new(
                new AcquisitionBatchIdentity(ordinal),
                1,
                (ulong)ordinal,
                true,
                TestDates.Unix(10 + ordinal * 2),
                null,
                null,
                null,
                Enumerable.Range(0, requestCount)
                    .Select(request => new PIDRequestEvidence(request, request, PIDRequestDispatchState.SelectedOnly, null, PIDRequestValueOutcome.NotEvaluated, null, null, null))
                    .ToArray());

        /// <summary>terminal batchを作ります。</summary>
        /// <remarks>責務: 指定状態とrequest列のbatchを作ります。</remarks>
        /// <param name="ordinal">batch番号。</param>
        /// <param name="requests">request列。</param>
        /// <param name="state">終端状態。</param>
        /// <param name="failure">失敗理由。</param>
        /// <returns>terminal batch。</returns>
        public AcquisitionBatchEvidence TerminalBatch(int ordinal, IReadOnlyList<PIDRequestEvidence> requests, AcquisitionBatchCompletionState state, AcquisitionBatchFailure? failure) =>
            new(new AcquisitionBatchIdentity(ordinal), 1, (ulong)ordinal, true, TestDates.Unix(10 + ordinal * 2), state, TestDates.Unix(11 + ordinal * 2), failure, requests);

        /// <summary>正応答を保存します。</summary>
        /// <remarks>責務: 指定batch・requestへ合成データの正応答を保存します。</remarks>
        /// <param name="batch">batch番号。</param>
        /// <param name="request">要求順。</param>
        /// <param name="payloadSize">応答データのバイト数。</param>
        /// <returns>保存したrequest証拠。</returns>
        public PIDRequestEvidence SaveResponded(int batch, int request, int payloadSize) =>
            Repository.SaveRespondedRequest(
                new OBDRawResponseObservation(
                    TestDates.Unix(11 + batch * 2 + request / 100.0),
                    (ulong)(request + 1) * 100,
                    _requests[request],
                    Enumerable.Range(0, payloadSize).Select(index => (byte)((index + request) % 251)).ToArray()),
                PIDRequestValueOutcome.DecodedValid,
                (ulong)(request + 1) * 100,
                null,
                request,
                new AcquisitionBatchIdentity(batch),
                Session.Id);

        /// <summary>1 batchを計画どおりに保存します。</summary>
        /// <remarks>責務: 開始、dispatch、各終端、batch終端を順に保存します。</remarks>
        /// <param name="ordinal">batch番号。</param>
        /// <param name="mix">応答の混ざり方。</param>
        /// <param name="payloadSize">応答データのバイト数。</param>
        public void PersistBatch(int ordinal, TransportMix mix, int payloadSize)
        {
            var open = OpenBatch(ordinal, _requests.Length);
            Repository.BeginBatch(open, Session.Id);
            var requests = open.Requests.ToArray();
            var plan = OutcomePlan(_requests.Length, mix);
            for (var request = 0; request < _requests.Length; request++)
            {
                if (plan[request] is not { } outcome)
                {
                    continue;
                }
                Repository.MarkRequestDispatchBegun(request, open.Identity, Session.Id);
                requests[request] = outcome == PIDRequestTransportOutcome.Responded
                    ? SaveResponded(ordinal, request, payloadSize)
                    : Repository.SaveNonRespondedRequest(outcome, (ulong)(request + 1) * 100, $"synthetic_{outcome}", request, open.Identity, Session.Id);
            }
            AcquisitionBatchFailure? failure = mix switch
            {
                TransportMix.Responded100 => null,
                TransportMix.Responded75TimedOut25 => requests.Any(request => request.TransportOutcome == PIDRequestTransportOutcome.TimedOut) ? AcquisitionBatchFailure.TransportUnavailable : null,
                TransportMix.PartialBatch => AcquisitionBatchFailure.TransportUnavailable,
                _ => requests.Any(request => request.TransportOutcome == PIDRequestTransportOutcome.Cancelled) ? AcquisitionBatchFailure.Cancelled : AcquisitionBatchFailure.TransportUnavailable,
            };
            Repository.FinishBatch(
                TerminalBatch(ordinal, requests, failure is null ? AcquisitionBatchCompletionState.Completed : AcquisitionBatchCompletionState.Failed, failure),
                Session.Id);
        }

        /// <summary>件数と並びの要約を取ります。</summary>
        /// <remarks>責務: batch、request、生ログ、概要を読み、比較できる値へまとめます。</remarks>
        /// <returns>要約。</returns>
        public LogicalSnapshot Snapshot()
        {
            var batches = Repository.Batches(Session.Id);
            var entries = Repository.Entries(Session.Id);
            var summary = Repository.Sessions(Session.AccountIdentifier).Single().RawLogSummary;
            var state = string.Join(
                "|",
                batches.Select(batch => $"{batch.Identity.Ordinal}:{batch.CompletionState}:{batch.Failure}:" +
                    string.Join(",", batch.Requests.Select(request => $"{request.DispatchState}/{request.TransportOutcome}/{request.RawSequence}"))));
            return new LogicalSnapshot(
                batches.Count,
                batches.Sum(batch => batch.Requests.Count),
                entries.Count,
                entries.Select((entry, offset) => entry.Sequence == offset).All(matches => matches),
                summary.RecordCount == entries.Count && summary.ByteCount == entries.Sum(entry => (long)entry.Payload.Count),
                state);
        }

        /// <summary>LogStoreのディレクトリーの合計バイト数を返します。</summary>
        /// <remarks>責務: retryでファイルが増えないことを確かめるため、書込みを確定してから大きさを測ります。</remarks>
        /// <returns>合計バイト数。</returns>
        public long LogBytes()
        {
            _ = Repository.Entries(Session.Id);
            return Directory.EnumerateFiles(_fixture.LogDirectory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
        }

        /// <summary>保存一式を閉じます。</summary>
        /// <remarks>責務: テストの後片付けをします。</remarks>
        public void Dispose() => _fixture.Dispose();
    }
}
