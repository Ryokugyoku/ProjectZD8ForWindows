using ProjectZD8.Data.Devices;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>アプリ内の既定アダプター接続確認が表示要求と了承を正しく受け渡すことを検証します。</summary>
public sealed class InAppDefaultAdapterConnectionPromptTests
{
    /// <summary>テスト用の終端です。</summary>
    private static readonly OBDConnectionEndpoint Endpoint =
        new(OBDConnectionEndpointTransport.BluetoothLowEnergy, "AA-BB-CC-DD-EE-FF", "OBDLink CX");

    /// <summary>表示要求をイベントと保留中の要求として公開します。</summary>
    /// <remarks>責務: 画面が購読済みでも後から購読しても表示できることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task PresentRaisesPromptAndKeepsPendingPrompt()
    {
        var prompt = new InAppDefaultAdapterConnectionPrompt();
        OBDConnectionEndpoint? requested = null;
        prompt.PromptRequested += (_, endpoint) => requested = endpoint;

        Assert.True(await prompt.Prepare(_ => { }));
        prompt.Present(Endpoint);

        Assert.Equal(Endpoint, requested);
        Assert.Equal(Endpoint, prompt.PendingPrompt);
    }

    /// <summary>了承を登録済みの通知先へ渡し、表示を閉じます。</summary>
    /// <remarks>責務: 「接続」の選択が1件の接続了承へ変換されることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task AcceptDeliversEndpointAndDismissesPrompt()
    {
        var prompt = new InAppDefaultAdapterConnectionPrompt();
        var accepted = new List<OBDConnectionEndpoint>();
        var dismissed = 0;
        prompt.PromptDismissed += (_, _) => dismissed += 1;
        await prompt.Prepare(accepted.Add);
        prompt.Present(Endpoint);

        prompt.Accept(Endpoint);

        Assert.Equal([Endpoint], accepted);
        Assert.Null(prompt.PendingPrompt);
        Assert.Equal(1, dismissed);
    }

    /// <summary>準備前に届いた了承を準備時に渡します。</summary>
    /// <remarks>責務: iOS の通知応答が準備より先に届いた場合と同じく了承を失わないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task AcceptBeforePrepareIsDeliveredOnPrepare()
    {
        var prompt = new InAppDefaultAdapterConnectionPrompt();
        var accepted = new List<OBDConnectionEndpoint>();

        prompt.Accept(Endpoint);
        await prompt.Prepare(accepted.Add);

        Assert.Equal([Endpoint], accepted);
    }

    /// <summary>辞退では了承を渡さず表示だけを閉じます。</summary>
    /// <remarks>責務: 「今はしない」が接続要求にならないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task DeclineDismissesWithoutAcceptance()
    {
        var prompt = new InAppDefaultAdapterConnectionPrompt();
        var accepted = new List<OBDConnectionEndpoint>();
        await prompt.Prepare(accepted.Add);
        prompt.Present(Endpoint);

        prompt.Decline();

        Assert.Empty(accepted);
        Assert.Null(prompt.PendingPrompt);
    }
}
