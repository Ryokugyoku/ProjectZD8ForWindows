using ProjectZD8.Data.Persistence.LogStore.Format;
using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>取得batchとPID要求証拠（LogStore）の保存を検証します。</summary>
/// <remarks>
/// iOS版 <c>GRDBConnectionSessionAcquisitionBatchRepositoryTests</c> の移植です。iOS版はSQLiteのtriggerで書込み失敗を注入しますが、
/// Windows版のbatch・requestはLogStoreにあるため、追記直前の差込み口（<c>BeforeLogAppend</c>）で失敗を注入します。
/// 正応答はRawとrequest終端が1レコードなので、Raw挿入・request更新・readbackのどの失敗でも同じく全体が取り消されます。
/// </remarks>
public sealed class SqliteConnectionSessionAcquisitionBatchRepositoryTests
{
    /// <summary>テスト用のセッションIDです。</summary>
    private static readonly ConnectionSessionID SessionID = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    /// <summary>completed batchが既存の正応答Rawを参照することを確かめます。</summary>
    [Fact]
    public void CompletedBatchReferencesExistingRespondedRaw()
    {
        using var fixture = MakeFixture();
        var repository = fixture.Repository;
        var open = MakeOpenBatch(1);
        repository.BeginBatch(open, SessionID);
        repository.MarkRequestDispatchBegun(0, open.Identity, SessionID);
        var request = SaveResponded(fixture);
        var completed = MakeTerminalBatch(AcquisitionBatchCompletionState.Completed, null, [request]);

        repository.FinishBatch(completed, SessionID);

        Assert.Equal([completed], repository.Batches(SessionID));
        Assert.Equal(0L, request.RawSequence);
        Assert.Equal(new byte[] { 0x1F, 0x40 }, repository.Entries(SessionID)[0].Payload);
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Duplicate, () => SaveResponded(fixture));
    }

    /// <summary>Raw追記の失敗がrequest終端も取り消すことを確かめます。</summary>
    [Fact]
    public void RawInsertFailureRollsBackRequestTerminalUpdate() => AssertRespondedFailureRollsBack();

    /// <summary>request更新の失敗がRaw追記も取り消すことを確かめます。</summary>
    [Fact]
    public void RequestUpdateFailureRollsBackRawInsert() => AssertRespondedFailureRollsBack();

    /// <summary>canonical readbackの不一致がすべてを取り消すことを確かめます。</summary>
    [Fact]
    public void CanonicalReadbackMismatchRollsBackEverything() => AssertRespondedFailureRollsBack();

    /// <summary>dispatch前の正応答をRawなしで拒否することを確かめます。</summary>
    [Fact]
    public void RespondedBeforeDispatchIsRejectedWithoutRaw()
    {
        using var fixture = MakeFixture();
        fixture.Repository.BeginBatch(MakeOpenBatch(1), SessionID);

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Conflict, () => SaveResponded(fixture));
        Assert.Empty(fixture.Repository.Entries(SessionID));
    }

    /// <summary>sealed batchが正応答の追記を拒否することを確かめます。</summary>
    [Fact]
    public void SealedBatchRejectsRespondedAppend()
    {
        using var fixture = MakeDispatchedFixture();
        var request = fixture.Repository.SaveNonRespondedRequest(
            PIDRequestTransportOutcome.TimedOut,
            100,
            "response_timeout",
            0,
            new AcquisitionBatchIdentity(0),
            SessionID);
        fixture.Repository.FinishBatch(
            MakeTerminalBatch(AcquisitionBatchCompletionState.Failed, AcquisitionBatchFailure.TransportUnavailable, [request]),
            SessionID);

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Conflict, () => SaveResponded(fixture));
        Assert.Empty(fixture.Repository.Entries(SessionID));
    }

    /// <summary>正応答のretryがduplicateとconflictを区別することを確かめます。</summary>
    [Fact]
    public void RespondedRetryDistinguishesDuplicateFromConflict()
    {
        using var fixture = MakeDispatchedFixture();
        _ = SaveResponded(fixture);

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Duplicate, () => SaveResponded(fixture));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Conflict,
            () => SaveResponded(fixture, observation: RawObservation([0x00])));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Conflict,
            () => SaveResponded(fixture, valueOutcome: PIDRequestValueOutcome.InvalidValue));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Conflict,
            () => SaveResponded(fixture, elapsedNanoseconds: 101));
        Assert.Single(fixture.Repository.Entries(SessionID));
    }

    /// <summary>RawとrequestのPIDが違う場合を拒否することを確かめます。</summary>
    [Fact]
    public void RawRequestOwnershipMismatchIsRejected()
    {
        using var fixture = MakeDispatchedFixture();

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Conflict,
            () => SaveResponded(fixture, observation: new OBDRawResponseObservation(TestDates.Unix(11), 100, new OBDPIDRequest(1, 13), [0x20])));
        Assert.Empty(fixture.Repository.Entries(SessionID));
    }

    /// <summary>別セッションのRaw sequenceを参照する終端を拒否することを確かめます。</summary>
    /// <remarks>iOS版はSQLの直接更新を外部キーで拒否します。Windows版はbatch終端の保存時にRawの存在を検証します。</remarks>
    [Fact]
    public void RawReferenceRejectsDifferentSessionSequence()
    {
        using var fixture = MakeDispatchedFixture();
        var otherSession = new ConnectionSession(
            "account",
            id: new ConnectionSessionID(Guid.Parse("10000000-0000-0000-0000-000000000002")),
            startedAt: TestDates.Unix(1));
        fixture.Repository.Save(otherSession);
        fixture.Repository.Append(RawObservation(), otherSession.Id);
        var responded = new PIDRequestEvidence(0, 0, PIDRequestDispatchState.Terminal, PIDRequestTransportOutcome.Responded, PIDRequestValueOutcome.DecodedValid, 0, 100, null);

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Unavailable,
            () => fixture.Repository.FinishBatch(MakeTerminalBatch(AcquisitionBatchCompletionState.Completed, null, [responded]), SessionID));
        Assert.Equal(PIDRequestDispatchState.DispatchBegun, fixture.Repository.Batches(SessionID)[0].Requests[0].DispatchState);
    }

    /// <summary>終了処理がselected-onlyとdispatch済みunknownを区別したまま残すことを確かめます。</summary>
    [Fact]
    public void TerminationKeepsSelectedOnlyRequestDistinctFromUnknownDispatch()
    {
        using var fixture = MakeFixture();
        var open = MakeOpenBatch(2);
        fixture.Repository.BeginBatch(open, SessionID);
        fixture.Repository.MarkRequestDispatchBegun(0, open.Identity, SessionID);
        var unknown = new PIDRequestEvidence(0, 0, PIDRequestDispatchState.Terminal, PIDRequestTransportOutcome.UnknownAfterTermination, PIDRequestValueOutcome.NotEvaluated, null, 100, "process_terminated_after_dispatch");
        var selectedOnly = PendingRequest(1, 1);
        var terminated = MakeTerminalBatch(AcquisitionBatchCompletionState.TerminatedUnknown, AcquisitionBatchFailure.ProcessTerminated, [unknown, selectedOnly]);

        fixture.Repository.FinishBatch(terminated, SessionID);

        var stored = fixture.Repository.Batches(SessionID)[0];
        Assert.Equal(PIDRequestTransportOutcome.UnknownAfterTermination, stored.Requests[0].TransportOutcome);
        Assert.Equal(PIDRequestDispatchState.SelectedOnly, stored.Requests[1].DispatchState);
        Assert.Null(stored.Requests[1].TransportOutcome);
    }

    /// <summary>存在しないRawを参照する終端がbatchを変えないことを確かめます。</summary>
    [Fact]
    public void MissingRespondedRawRollsBackTerminalUpdate()
    {
        using var fixture = MakeFixture();
        var open = MakeOpenBatch(1);
        fixture.Repository.BeginBatch(open, SessionID);
        var request = new PIDRequestEvidence(0, 0, PIDRequestDispatchState.Terminal, PIDRequestTransportOutcome.Responded, PIDRequestValueOutcome.DecodedValid, 99, 100, null);
        var completed = MakeTerminalBatch(AcquisitionBatchCompletionState.Completed, null, [request]);

        Assert.ThrowsAny<Exception>(() => fixture.Repository.FinishBatch(completed, SessionID));

        Assert.Equal([open], fixture.Repository.Batches(SessionID));
    }

    /// <summary>batch開始とdispatchの重複・競合・欠落の分類を確かめます。</summary>
    [Fact]
    public void BeginAndDispatchClassifyDuplicateConflictAndMissing()
    {
        using var fixture = MakeFixture();
        var open = MakeOpenBatch(1);
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.NotFound,
            () => fixture.Repository.MarkRequestDispatchBegun(0, open.Identity, SessionID));
        fixture.Repository.BeginBatch(open, SessionID);

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Duplicate, () => fixture.Repository.BeginBatch(open, SessionID));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Conflict, () => fixture.Repository.BeginBatch(MakeOpenBatch(2), SessionID));
        fixture.Repository.MarkRequestDispatchBegun(0, open.Identity, SessionID);
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.Duplicate,
            () => fixture.Repository.MarkRequestDispatchBegun(0, open.Identity, SessionID));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.NotFound,
            () => fixture.Repository.MarkRequestDispatchBegun(1, open.Identity, SessionID));
    }

    /// <summary>manifestのないセッションではbatchを開始できないことを確かめます。</summary>
    [Fact]
    public void BeginBatchWithoutManifestIsStartEvidenceMissing()
    {
        using var fixture = new ConnectionSessionStorageFixture();
        fixture.Repository.Save(new ConnectionSession("account", id: SessionID, startedAt: TestDates.Unix(1)));

        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(
            ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing,
            () => fixture.Repository.BeginBatch(MakeOpenBatch(1), SessionID));
    }

    /// <summary>batch証拠がLogStoreの再オープン後も同じに読めることを確かめます。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BatchesSurviveReopen(bool cleanClose)
    {
        using var fixture = MakeDispatchedFixture();
        var request = SaveResponded(fixture);
        var completed = MakeTerminalBatch(AcquisitionBatchCompletionState.Completed, null, [request]);
        fixture.Repository.FinishBatch(completed, SessionID);

        fixture.Reopen(close: cleanClose);

        Assert.Equal([completed], fixture.Repository.Batches(SessionID));
        Assert.Single(fixture.Repository.Entries(SessionID));
        SqliteConnectionSessionAcquisitionRepositoryTests.AssertReason(ConnectionSessionAcquisitionRepositoryError.Duplicate, () => fixture.Repository.FinishBatch(completed, SessionID));
    }

    /// <summary>正応答の保存失敗が何も残さず、失敗を外せば同じ要求を保存できることを確かめます。</summary>
    /// <remarks>責務: iOS版の3つのrollbackテスト（Raw挿入、request更新、readback）に共通する確認を行います。</remarks>
    private static void AssertRespondedFailureRollsBack()
    {
        using var fixture = MakeDispatchedFixture();
        fixture.Repository.BeforeLogAppend = record =>
        {
            if (record is SessionLogRecord.Responded)
            {
                throw new IOException("injected failure");
            }
        };

        Assert.ThrowsAny<Exception>(() => SaveResponded(fixture));

        Assert.Empty(fixture.Repository.Entries(SessionID));
        Assert.Equal(0, fixture.Repository.Sessions("account")[0].RawLogSummary.RecordCount);
        Assert.Equal(PIDRequestDispatchState.DispatchBegun, fixture.Repository.Batches(SessionID)[0].Requests[0].DispatchState);
        fixture.Repository.BeforeLogAppend = null;
        Assert.Equal(PIDRequestTransportOutcome.Responded, SaveResponded(fixture).TransportOutcome);
    }

    /// <summary>manifestを保存した保存一式を作ります。</summary>
    /// <remarks>責務: セッションとmanifest（要求 1/12、1/13）を保存します。</remarks>
    /// <returns>保存一式。</returns>
    private static ConnectionSessionStorageFixture MakeFixture()
    {
        var fixture = new ConnectionSessionStorageFixture();
        fixture.Repository.Save(new ConnectionSession("account", id: SessionID, startedAt: TestDates.Unix(1)));
        fixture.Repository.SaveStartOnce(MakeManifest(), TestDates.Unix(2), SessionID);
        return fixture;
    }

    /// <summary>batchを開始してrequest 0をdispatch済みにした保存一式を作ります。</summary>
    /// <remarks>責務: 正応答保存の前提状態を作ります。</remarks>
    /// <returns>保存一式。</returns>
    private static ConnectionSessionStorageFixture MakeDispatchedFixture()
    {
        var fixture = MakeFixture();
        var open = MakeOpenBatch(1);
        fixture.Repository.BeginBatch(open, SessionID);
        fixture.Repository.MarkRequestDispatchBegun(0, open.Identity, SessionID);
        return fixture;
    }

    /// <summary>テスト用の正応答を作ります。</summary>
    /// <remarks>責務: rpm要求の正応答を作ります。</remarks>
    /// <param name="payload">応答データ。省略時は 1F 40。</param>
    /// <returns>正応答。</returns>
    private static OBDRawResponseObservation RawObservation(byte[]? payload = null) =>
        new(TestDates.Unix(11), 100, new OBDPIDRequest(1, 12), payload ?? [0x1F, 0x40]);

    /// <summary>batch 0 のrequest 0へ正応答を保存します。</summary>
    /// <remarks>責務: 引数で1項目だけ変えた正応答保存を行います。</remarks>
    /// <param name="fixture">保存一式。</param>
    /// <param name="observation">正応答。</param>
    /// <param name="valueOutcome">値評価結果。</param>
    /// <param name="elapsedNanoseconds">経過時間。</param>
    /// <returns>保存したrequest証拠。</returns>
    private static PIDRequestEvidence SaveResponded(
        ConnectionSessionStorageFixture fixture,
        OBDRawResponseObservation? observation = null,
        PIDRequestValueOutcome valueOutcome = PIDRequestValueOutcome.DecodedValid,
        ulong elapsedNanoseconds = 100) =>
        fixture.Repository.SaveRespondedRequest(
            observation ?? RawObservation(),
            valueOutcome,
            elapsedNanoseconds,
            null,
            0,
            new AcquisitionBatchIdentity(0),
            SessionID);

    /// <summary>テスト用のmanifestを作ります。</summary>
    /// <remarks>責務: 要求 1/12（2バイト）と 1/13（1バイト）のmanifestを作ります。</remarks>
    /// <returns>manifest。</returns>
    private static ConnectionSessionAcquisitionManifest MakeManifest()
    {
        OBDPIDRequest[] requests = [new(1, 12), new(1, 13)];
        return new ConnectionSessionAcquisitionManifest(
            1,
            new AcquisitionApplicationVersion("1", "1"),
            1,
            1,
            new OrderedAcquisitionPIDSet(requests),
            requests.Select(request => new AcquisitionPIDDefinitionSnapshot(
                request,
                AcquisitionPIDCapabilitySupport.Supported,
                true,
                1,
                request.Pid == 12 ? 2 : 1,
                new AcquisitionPIDDefinitionIdentity(1, "A"),
                "unit",
                AcquisitionPIDValidityRange.NotDeclared)).ToArray(),
            ConnectionSessionAcquisitionPlatform.MacOS,
            1);
    }

    /// <summary>open batchを作ります。</summary>
    /// <remarks>責務: 指定件数のselected-only requestを持つbatch 0を作ります。</remarks>
    /// <param name="requestCount">request件数。</param>
    /// <returns>open batch。</returns>
    private static AcquisitionBatchEvidence MakeOpenBatch(int requestCount) =>
        new(
            new AcquisitionBatchIdentity(0),
            1,
            0,
            true,
            TestDates.Unix(10),
            null,
            null,
            null,
            Enumerable.Range(0, requestCount).Select(ordinal => PendingRequest(ordinal, ordinal)).ToArray());

    /// <summary>terminal batchを作ります。</summary>
    /// <remarks>責務: 指定状態とrequest列のbatch 0を作ります。</remarks>
    /// <param name="state">終端状態。</param>
    /// <param name="failure">失敗理由。</param>
    /// <param name="requests">request列。</param>
    /// <returns>terminal batch。</returns>
    private static AcquisitionBatchEvidence MakeTerminalBatch(
        AcquisitionBatchCompletionState state,
        AcquisitionBatchFailure? failure,
        PIDRequestEvidence[] requests) =>
        new(new AcquisitionBatchIdentity(0), 1, 0, true, TestDates.Unix(10), state, TestDates.Unix(12), failure, requests);

    /// <summary>selected-only requestを作ります。</summary>
    /// <remarks>責務: 未dispatchのrequest証拠を作ります。</remarks>
    /// <param name="requestOrdinal">要求順。</param>
    /// <param name="manifestPIDOrdinal">manifest PID位置。</param>
    /// <returns>request証拠。</returns>
    private static PIDRequestEvidence PendingRequest(int requestOrdinal, int manifestPIDOrdinal) =>
        new(requestOrdinal, manifestPIDOrdinal, PIDRequestDispatchState.SelectedOnly, null, PIDRequestValueOutcome.NotEvaluated, null, null, null);
}
