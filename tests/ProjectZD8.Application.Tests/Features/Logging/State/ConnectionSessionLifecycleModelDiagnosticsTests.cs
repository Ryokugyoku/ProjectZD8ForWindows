using Microsoft.Extensions.Logging;
using ProjectZD8.Application.Logging.Actions;
using ProjectZD8.Application.Logging.State;
using ProjectZD8.Application.Tests.Logging;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Logging.State;

/// <summary>接続セッションの開始・終了の診断ログ（<c>acq.session.*</c>）を検証します。</summary>
/// <remarks>出力先を差し替えるため、このクラスのテストはほかと並行させません。</remarks>
[Collection(AcquisitionDiagnosticLogCollection.Name)]
public sealed partial class ConnectionSessionLifecycleModelTests
{
    /// <summary>開始と終了の行に、生ログのフォルダー名と同じ形の <c>sid</c> が付くことを確かめます。</summary>
    /// <remarks>責務: <c>acq.session.started</c> と理由つきの <c>acq.session.ended</c> を確認します。</remarks>
    [Fact]
    public void SessionStartAndEndCarrySessionId()
    {
        using var capture = new DiagnosticLogCapture();
        var sessionID = new ConnectionSessionID(Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"));
        var model = new ConnectionSessionLifecycleModel(
            repository: new RecordingConnectionSessionRepository(),
            makeID: () => sessionID);

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());
        model.Send(new ConnectionSessionLifecycleAction.EndRequested(ConnectionSessionEndReason.ConnectionLost));

        var started = Assert.Single(capture.Named("acq.session.started"));
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", started.SessionId);
        Assert.Equal(LogLevel.Information, started.Level);
        var ended = Assert.Single(capture.Named("acq.session.ended"));
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", ended.SessionId);
        Assert.Equal(nameof(ConnectionSessionEndReason.ConnectionLost), ended.Prop("Reason"));
        Assert.Null(ProjectZD8.Diagnostics.DiagnosticLog.CurrentSessionId);
    }

    /// <summary>開始の保存に失敗したとき、例外つきの Error を書き、動き（セッションなし）は変えないことを確かめます。</summary>
    /// <remarks>責務: 握りつぶしていた保存の失敗が <c>acq.session.save.failed</c> に残ることを確認します。</remarks>
    [Fact]
    public void SessionSaveFailureIsLoggedWithException()
    {
        using var capture = new DiagnosticLogCapture();
        var model = new ConnectionSessionLifecycleModel(repository: new FailingConnectionSessionRepository());

        model.Send(new ConnectionSessionLifecycleAction.AccountIdentifierChanged("account"));
        model.Send(new ConnectionSessionLifecycleAction.StartRequested());

        Assert.Null(model.ActiveSession);
        var failed = Assert.Single(capture.Named("acq.session.save.failed"));
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal("start", failed.Prop("Operation"));
        Assert.NotNull(failed.Exception);
        Assert.NotNull(failed.SessionId);
    }

    /// <summary>保存に必ず失敗する保存先です。</summary>
    private sealed class FailingConnectionSessionRepository : IConnectionSessionRepository
    {
        /// <summary>保存に失敗します。</summary>
        /// <param name="session">保存しないセッション。</param>
        public void Save(ConnectionSession session) => throw new IOException("disk full");

        /// <summary>空の一覧を返します。</summary>
        /// <param name="accountIdentifier">アカウント。</param>
        /// <returns>空の一覧。</returns>
        public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) => [];
    }
}
