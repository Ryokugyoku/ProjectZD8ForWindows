using ProjectZD8.Data.Tests.TestSupport;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Sqlite.Repositories;

/// <summary>セッション終了と中断回復の原子的な保存を検証します。</summary>
/// <remarks>iOS版 <c>GRDBConnectionSessionAcquisitionTerminationRepositoryTests</c> の移植です（ファイルDBを使います）。</remarks>
public sealed class SqliteConnectionSessionAcquisitionTerminationRepositoryTests
{
    /// <summary>明示的な終了理由がopenな取得状態をすべてsealすることを確かめます。</summary>
    [Theory]
    [InlineData(ConnectionSessionEndReason.UserDisconnected)]
    [InlineData(ConnectionSessionEndReason.ConnectionLost)]
    [InlineData(ConnectionSessionEndReason.AcquisitionFailed)]
    public void ExplicitEndReasonsSealEveryOpenAcquisitionState(ConnectionSessionEndReason reason)
    {
        using var fixture = new ConnectionSessionStorageFixture(fileDatabase: true);
        var session = Prepare(fixture);
        var endedAt = TestDates.Unix(10);

        var ended = fixture.Repository.FinishSessionAcquisition(session, endedAt, reason);

        Assert.Equal(endedAt, ended.EndedAt);
        Assert.Equal(reason, ended.EndReason);
        var batch = fixture.Repository.Batches(session.Id)[0];
        Assert.Equal(AcquisitionBatchCompletionState.Failed, batch.CompletionState);
        Assert.Equal(AcquisitionBatchFailure.PersistenceFailure, batch.Failure);
        Assert.Equal(endedAt, batch.CompletedAt);
        Assert.Equal(PIDRequestDispatchState.DispatchBegun, batch.Requests[0].DispatchState);
        Assert.Null(batch.Requests[0].TransportOutcome);
        Assert.Empty(fixture.Repository.Entries(session.Id));
        Assert.Equal(
            [new AcquisitionRawBoundaryEvidence.Started(TestDates.Unix(2)), new AcquisitionRawBoundaryEvidence.Ended(endedAt, reason)],
            fixture.Repository.BoundaryEvidence(session.Id));
    }

    /// <summary>SQLiteの終了失敗がセッション・境界・batchをopenのまま残すことを確かめます。</summary>
    [Fact]
    public void FinishRollbackKeepsSessionBoundaryAndBatchOpen()
    {
        using var fixture = new ConnectionSessionStorageFixture(fileDatabase: true);
        var session = Prepare(fixture);
        SqliteConnectionSessionAcquisitionRepositoryTests.Execute(
            fixture.Database,
            """
            CREATE TRIGGER phase4g_fail_session_finish
            BEFORE UPDATE OF endedAt ON connection_sessions
            BEGIN
                SELECT RAISE(ABORT, 'phase4g injected finish failure');
            END
            """);

        Assert.ThrowsAny<Exception>(() => fixture.Repository.FinishSessionAcquisition(session, TestDates.Unix(10), ConnectionSessionEndReason.UserDisconnected));

        Assert.Null(fixture.Repository.Sessions(session.AccountIdentifier)[0].EndedAt);
        var boundaries = fixture.Repository.BoundaryEvidence(session.Id);
        Assert.IsType<AcquisitionRawBoundaryEvidence.Started>(Assert.Single(boundaries));
        Assert.Null(fixture.Repository.Batches(session.Id)[0].CompletionState);
    }

    /// <summary>回復が中断した取得を原子的に閉じることを確かめます。</summary>
    [Fact]
    public void RecoveryAtomicallyClosesInterruptedAcquisition()
    {
        using var fixture = new ConnectionSessionStorageFixture(fileDatabase: true);
        var session = Prepare(fixture);

        var recovered = fixture.Repository.RecoverInterruptedSessionAcquisitions(session.AccountIdentifier, TestDates.Unix(10));

        Assert.Single(recovered);
        Assert.Equal(ConnectionSessionEndReason.UnexpectedTermination, recovered[0].EndReason);
        var boundaries = fixture.Repository.BoundaryEvidence(session.Id);
        Assert.Equal(2, boundaries.Count);
        Assert.IsType<AcquisitionRawBoundaryEvidence.Started>(boundaries[0]);
        Assert.IsType<AcquisitionRawBoundaryEvidence.Ended>(boundaries[1]);
        var batch = fixture.Repository.Batches(session.Id)[0];
        Assert.Equal(AcquisitionBatchCompletionState.TerminatedUnknown, batch.CompletionState);
        Assert.Equal(PIDRequestTransportOutcome.UnknownAfterTermination, batch.Requests[0].TransportOutcome);
        Assert.Empty(fixture.Repository.Entries(session.Id));
    }

    /// <summary>プロセスが異常終了したあとの起動で、回復が中断した取得を閉じることを確かめます。</summary>
    [Fact]
    public void RecoveryAfterCrashClosesInterruptedAcquisition()
    {
        using var fixture = new ConnectionSessionStorageFixture(fileDatabase: true);
        var session = Prepare(fixture);
        fixture.Repository.SaveRespondedRequest(
            new OBDRawResponseObservation(TestDates.Unix(4), 10, new OBDPIDRequest(1, 12), [0x42]),
            PIDRequestValueOutcome.DecodedValid,
            10,
            null,
            0,
            new AcquisitionBatchIdentity(0),
            session.Id);

        fixture.Reopen(close: false);
        var recovered = fixture.Repository.RecoverInterruptedSessionAcquisitions(session.AccountIdentifier, TestDates.Unix(10));

        Assert.Single(recovered);
        var batch = fixture.Repository.Batches(session.Id)[0];
        Assert.Equal(AcquisitionBatchCompletionState.TerminatedUnknown, batch.CompletionState);
        Assert.Equal(PIDRequestTransportOutcome.Responded, batch.Requests[0].TransportOutcome);
        Assert.Equal(new byte[] { 0x42 }, fixture.Repository.Entries(session.Id)[0].Payload);
        Assert.Equal(1, fixture.Repository.Sessions(session.AccountIdentifier)[0].RawLogSummary.RecordCount);
    }

    /// <summary>セッション、manifest、dispatch済みのopen batchを用意します。</summary>
    /// <remarks>責務: iOS版の <c>makeFixture</c> と同じ状態を作ります。</remarks>
    /// <param name="fixture">保存一式。</param>
    /// <returns>保存したセッション。</returns>
    private static ConnectionSession Prepare(ConnectionSessionStorageFixture fixture)
    {
        var repository = fixture.Repository;
        var session = new ConnectionSession("phase4g-recovery", id: ConnectionSessionID.New(), startedAt: TestDates.Unix(1));
        repository.Save(session);
        var request = new OBDPIDRequest(1, 12);
        repository.SaveStartOnce(
            new ConnectionSessionAcquisitionManifest(
                1,
                new AcquisitionApplicationVersion("phase4g", "local"),
                1,
                1,
                new OrderedAcquisitionPIDSet([request]),
                [
                    new AcquisitionPIDDefinitionSnapshot(
                        request,
                        AcquisitionPIDCapabilitySupport.Supported,
                        true,
                        1,
                        1,
                        new AcquisitionPIDDefinitionIdentity(1, "A"),
                        "unit",
                        AcquisitionPIDValidityRange.Inclusive(0, 255)),
                ],
                ConnectionSessionAcquisitionPlatform.MacOS,
                1),
            TestDates.Unix(2),
            session.Id);
        var identity = new AcquisitionBatchIdentity(0);
        repository.BeginBatch(
            new AcquisitionBatchEvidence(
                identity,
                1,
                0,
                true,
                TestDates.Unix(3),
                null,
                null,
                null,
                [new PIDRequestEvidence(0, 0, PIDRequestDispatchState.SelectedOnly, null, PIDRequestValueOutcome.NotEvaluated, null, null, null)]),
            session.Id);
        repository.MarkRequestDispatchBegun(0, identity, session.Id);
        return session;
    }
}
