using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.DeviceConnection.UseCases;

/// <summary>アダプター探索ユースケースの候補整形を検証します。</summary>
public sealed class DiscoverAdaptersUseCaseTests
{
    /// <summary>重複候補を除去して表示名順に整列することを検証します。</summary>
    /// <remarks>責務: 探索ポートの重複した結果が安定した候補一覧へ変換されることを確認します。</remarks>
    [Fact]
    public async Task ExecuteRemovesDuplicatesAndSortsByDisplayName()
    {
        var later = MakeAdapter(id: "later", name: "Zulu");
        var earlier = MakeAdapter(id: "earlier", name: "Alpha");
        var port = new AdapterDiscoveryPortFake([later, earlier, later]);
        var useCase = new DiscoverAdaptersUseCase(port);

        var result = await useCase.Execute(AdapterTransportMode.Usb);

        Assert.Equal(["earlier", "later"], result.Select(adapter => adapter.Id));
        Assert.Equal([AdapterTransportMode.Usb], port.RequestedModes);
    }

    /// <summary>同一Peripheral UUIDのBluetooth候補を1件へまとめることを検証します。</summary>
    /// <remarks>責務: 同じ安定識別子を持つ重複BLE通知が1件の探索結果になることを確認します。</remarks>
    [Fact]
    public async Task ExecuteMergesDuplicateBluetoothPeripheralIdentifiers()
    {
        const string identifier = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
        var first = new DiscoveredAdapter(
            id: $"bluetooth-low-energy:{identifier}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: "Adapter",
            systemIdentifier: identifier,
            isConnected: true);
        var duplicate = new DiscoveredAdapter(
            id: $"bluetooth-low-energy:{identifier}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: "Adapter Duplicate",
            systemIdentifier: identifier,
            isConnected: true);
        var port = new AdapterDiscoveryPortFake([first, duplicate]);
        var useCase = new DiscoverAdaptersUseCase(port);

        var result = await useCase.Execute(AdapterTransportMode.Bluetooth);

        Assert.Equal([first], result);
        Assert.Equal([AdapterTransportMode.Bluetooth], port.RequestedModes);
    }

    /// <summary>Bluetooth探索結果の未接続候補を選択前の候補として保持することを検証します。</summary>
    /// <remarks>責務: SwiftOBD2方式で検出した未接続Peripheralが選択一覧から失われないことを確認します。</remarks>
    [Fact]
    public async Task ExecuteIncludesDiscoveredBluetoothCandidatesBeforeConnection()
    {
        var connected = new DiscoveredAdapter(
            id: "connected",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: "Connected",
            systemIdentifier: "connected",
            isConnected: true);
        var disconnected = new DiscoveredAdapter(
            id: "disconnected",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: "Disconnected",
            systemIdentifier: "disconnected",
            isConnected: false);
        var port = new AdapterDiscoveryPortFake([disconnected, connected]);
        var useCase = new DiscoverAdaptersUseCase(port);

        var result = await useCase.Execute(AdapterTransportMode.Bluetooth);

        Assert.Equal([connected, disconnected], result);
        Assert.Equal([AdapterTransportMode.Bluetooth], port.RequestedModes);
    }

    /// <summary>テストで使用する最小のアダプター候補を生成します。</summary>
    /// <remarks>責務: 指定された識別子と表示名を持つUSB候補をテスト入力として構築します。</remarks>
    /// <param name="id">候補の安定識別子。</param>
    /// <param name="name">候補の表示名。</param>
    /// <returns>USB接続済みとして扱うテスト候補。</returns>
    private static DiscoveredAdapter MakeAdapter(string id, string name) =>
        new(
            id: id,
            transportMode: AdapterTransportMode.Usb,
            displayName: name,
            systemIdentifier: id,
            isConnected: true);
}

/// <summary>テストで決定的な候補一覧を返すアダプター探索ポートです。</summary>
file sealed class AdapterDiscoveryPortFake : IAdapterDiscoveryPort
{
    /// <summary>探索結果として返す候補です。</summary>
    private readonly IReadOnlyList<DiscoveredAdapter> adapters;

    /// <summary>ユースケースから要求された接続方式の保存先です。</summary>
    private readonly List<AdapterTransportMode> requestedModes = [];

    /// <summary>固定の探索結果を注入してFakeを生成します。</summary>
    /// <remarks>責務: 1件のテストシナリオで返す候補一覧を保持します。</remarks>
    /// <param name="adapters">探索要求に対して返す候補。</param>
    public AdapterDiscoveryPortFake(IReadOnlyList<DiscoveredAdapter> adapters)
    {
        this.adapters = adapters;
    }

    /// <summary>ユースケースから要求された接続方式です。</summary>
    public IReadOnlyList<AdapterTransportMode> RequestedModes => requestedModes;

    /// <summary>要求された接続方式を記録して固定候補を返します。</summary>
    /// <remarks>責務: 1件の探索要求を記録し、注入済みの候補一覧で応答します。</remarks>
    /// <param name="mode">探索対象として要求された接続方式。</param>
    /// <returns>初期化時に注入された候補。</returns>
    public Task<IReadOnlyList<DiscoveredAdapter>> DiscoverAdapters(AdapterTransportMode mode)
    {
        requestedModes.Add(mode);
        return Task.FromResult(adapters);
    }
}
