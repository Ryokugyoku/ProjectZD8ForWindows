using System.Globalization;
using Microsoft.Extensions.Logging;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Application.Tests.Logging;

namespace ProjectZD8.Application.Tests.Logging.UseCases;

/// <summary>取得の証拠とバッチの診断ログ（<c>acq.evidence.*</c> / <c>acq.batch.*</c>）を検証します。</summary>
/// <remarks>出力先を差し替えるため、このクラスのテストはほかと並行させません。</remarks>
[Collection(AcquisitionDiagnosticLogCollection.Name)]
public sealed partial class ConnectionSessionAcquisitionControllerIntegrationTests
{
    /// <summary>取得の開始とバッチの要約に、接続セッションの <c>sid</c> が付くことを確かめます。</summary>
    /// <remarks>責務: <c>acq.evidence.started</c> と <c>acq.batch.completed</c>（Debug、応答 1 件）の <c>sid</c> と値を確認します。</remarks>
    [Fact]
    public async Task StartAndBatchSummaryCarrySessionId()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.Responded([42])]);
        var expectedSessionId = fixture.Session.Id.RawValue.ToString("D", CultureInfo.InvariantCulture);

        await fixture.Controller.Start(fixture.StartInput);
        _ = await fixture.Controller.Acquire(fixture.BatchInput(ordinal: 0, tick: 0));

        var started = Assert.Single(capture.Named("acq.evidence.started"));
        Assert.Equal(expectedSessionId, started.SessionId);
        Assert.Equal(1, started.Prop("Requests"));
        var batch = Assert.Single(capture.Named("acq.batch.completed"));
        Assert.Equal(LogLevel.Debug, batch.Level);
        Assert.Equal(expectedSessionId, batch.SessionId);
        Assert.Equal(1, batch.Prop("Requests"));
        Assert.Equal(1, batch.Prop("Responded"));
        Assert.Equal(1, batch.Prop("Samples"));
        Assert.Equal(expectedSessionId, fixture.Controller.DiagnosticSessionId);
    }

    /// <summary>期限切れの要求が、バッチの要約の期限切れの件数に数えられることを確かめます。</summary>
    /// <remarks>責務: PID 1 件ごとには書かず、バッチの要約に結果の種類ごとの件数が入ることを確認します。</remarks>
    [Fact]
    public async Task BatchSummaryCountsTimedOutRequests()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: [new OBDPIDRequestTransportOutcome.TimedOut()]);

        await fixture.Controller.Start(fixture.StartInput);
        _ = await fixture.Controller.Acquire(fixture.BatchInput(ordinal: 0, tick: 0));

        var batch = Assert.Single(capture.Named("acq.batch.completed"));
        Assert.Equal(1, batch.Prop("TimedOut"));
        Assert.Equal(0, batch.Prop("Responded"));
    }

    /// <summary>開始前のバッチを落としたことが Warning で出ることを確かめます。</summary>
    /// <remarks>責務: 古い世代のバッチが <c>acq.batch.dropped</c>（<c>stale-generation</c>）になることを確認します。</remarks>
    [Fact]
    public async Task StaleBatchIsLoggedAsDropped()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: []);

        await Assert.ThrowsAsync<PersistAcquisitionBatchException>(
            () => fixture.Controller.Acquire(fixture.BatchInput(ordinal: 3, tick: 3)));

        var dropped = Assert.Single(capture.Named("acq.batch.dropped"));
        Assert.Equal(LogLevel.Warning, dropped.Level);
        Assert.Equal(3L, dropped.Prop("Batch"));
        Assert.Equal("stale-generation", dropped.Prop("Reason"));
    }

    /// <summary>接続セッションがないとき、取得を始められないことが Warning で出ることを確かめます。</summary>
    /// <remarks>責務: <c>acq.evidence.no-session</c> を確認します。</remarks>
    [Fact]
    public async Task MissingSessionIsLogged()
    {
        using var capture = new DiagnosticLogCapture();
        var fixture = MakeFixture(outcomes: [], exposesActiveSession: false);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Controller.Start(fixture.StartInput));

        Assert.Single(capture.Named("acq.evidence.no-session"));
        Assert.Empty(capture.Named("acq.evidence.started"));
    }
}
