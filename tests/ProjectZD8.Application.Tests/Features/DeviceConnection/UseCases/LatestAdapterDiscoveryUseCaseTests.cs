using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Application.Tests.TestSupport;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.DeviceConnection.UseCases;

/// <summary>最新要求だけを通知するアダプター探索ライフサイクルを検証します。</summary>
public sealed class LatestAdapterDiscoveryUseCaseTests
{
    /// <summary>キャンセルを無視する古い探索完了が通知されないことを検証します。</summary>
    /// <remarks>責務: 再探索後は最新世代の候補だけが完了通知へ到達することを確認します。</remarks>
    [Fact]
    public async Task StartRejectsResultFromPreviousGeneration()
    {
        var oldAdapter = MakeAdapter(id: "old");
        var currentAdapter = MakeAdapter(id: "current");
        var port = new ControlledLatestDiscoveryPortFake();
        var useCase = MakeUseCase(port);
        var outcomes = new List<AdapterDiscoveryOutcome>();

        useCase.Start(AdapterTransportMode.Bluetooth, outcomes.Add);
        await WaitForRequestCount(1, port);
        useCase.Start(AdapterTransportMode.Bluetooth, outcomes.Add);
        await WaitForRequestCount(2, port);

        port.CompleteRequest(1, [currentAdapter]);
        await AsyncWait.UntilAsync(() => outcomes.Count > 0);
        port.CompleteRequest(0, [oldAdapter]);
        await Task.Yield();

        Assert.Equal([new AdapterDiscoveryOutcome.Discovered([currentAdapter])], outcomes);
    }

    /// <summary>明示的な終了後に保留中の探索結果が通知されないことを検証します。</summary>
    /// <remarks>責務: 画面終了に相当するキャンセルが遅延完了の通知を拒否することを確認します。</remarks>
    [Fact]
    public async Task CancelRejectsPendingResult()
    {
        var port = new ControlledLatestDiscoveryPortFake();
        var useCase = MakeUseCase(port);
        var outcomes = new List<AdapterDiscoveryOutcome>();

        useCase.Start(AdapterTransportMode.Bluetooth, outcomes.Add);
        await WaitForRequestCount(1, port);
        useCase.Cancel();
        port.CompleteRequest(0, [MakeAdapter(id: "late")]);
        await Task.Yield();

        Assert.Empty(outcomes);
    }

    /// <summary>指定ポートを利用する最新探索ユースケースを生成します。</summary>
    /// <remarks>責務: テスト用探索ポートを共通探索ライフサイクルへ結び付けます。</remarks>
    /// <param name="port">完了順を制御する探索ポート。</param>
    /// <returns>指定ポートを利用する最新探索ユースケース。</returns>
    private static LatestAdapterDiscoveryUseCase MakeUseCase(IAdapterDiscoveryPort port) =>
        new(new DiscoverAdaptersUseCase(port));

    /// <summary>指定識別子を持つテスト用Bluetooth候補を生成します。</summary>
    /// <remarks>責務: 最新探索テストへ渡す最小のBluetooth候補を構築します。</remarks>
    /// <param name="id">候補の安定識別子。</param>
    /// <returns>接続済みのBluetooth候補。</returns>
    private static DiscoveredAdapter MakeAdapter(string id) =>
        new(
            id: id,
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: id,
            systemIdentifier: id,
            isConnected: true);

    /// <summary>Fakeが指定件数の探索要求を受け取るまで待機します。</summary>
    /// <remarks>責務: テストで完了させる探索継続が登録されるまで限定時間待機します。</remarks>
    /// <param name="expectedCount">待機する探索要求件数。</param>
    /// <param name="port">要求数を監視する制御可能Fake。</param>
    /// <returns>待機の完了を表すタスク。</returns>
    private static async Task WaitForRequestCount(int expectedCount, ControlledLatestDiscoveryPortFake port)
    {
        await AsyncWait.UntilAsync(() => port.RequestCount >= expectedCount);
        Assert.Equal(expectedCount, port.RequestCount);
    }

    /// <summary>完了順をテストから制御できる最新探索用Fakeです。</summary>
    private sealed class ControlledLatestDiscoveryPortFake : IAdapterDiscoveryPort
    {
        /// <summary>未完了の探索継続を要求順に保持します。</summary>
        private readonly List<TaskCompletionSource<IReadOnlyList<DiscoveredAdapter>>> continuations = [];

        /// <summary>受け取った探索要求件数です。</summary>
        public int RequestCount => continuations.Count;

        /// <summary>テストから完了されるまで探索結果を保留します。</summary>
        /// <remarks>責務: 1件の探索要求を順序付き継続として記録します。</remarks>
        /// <param name="mode">探索対象として要求された接続方式。</param>
        /// <returns>テストから指定された時点の候補一覧。</returns>
        public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode)
        {
            var continuation = new TaskCompletionSource<IReadOnlyList<DiscoveredAdapter>>();
            continuations.Add(continuation);
            return continuation.Task;
        }

        /// <summary>指定順の探索要求を候補一覧で完了します。</summary>
        /// <remarks>責務: 1件の保留中探索へテスト指定の候補一覧を返します。</remarks>
        /// <param name="index">完了する探索要求の順序。</param>
        /// <param name="adapters">探索結果として返す候補一覧。</param>
        public void CompleteRequest(int index, IReadOnlyList<DiscoveredAdapter> adapters)
        {
            continuations[index].SetResult(adapters);
        }
    }
}
