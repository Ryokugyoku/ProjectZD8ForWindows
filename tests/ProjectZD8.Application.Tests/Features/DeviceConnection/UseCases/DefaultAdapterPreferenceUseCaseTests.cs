using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.DeviceConnection.UseCases;

/// <summary>共通デフォルトアダプター設定ユースケースの保存と照合を検証します。</summary>
public sealed class DefaultAdapterPreferenceUseCaseTests
{
    /// <summary>保存境界から既存のデフォルト設定を復元できることを検証します。</summary>
    /// <remarks>責務: 共通ユースケースの読込が保存境界の値を変更せず返すことを確認します。</remarks>
    [Fact]
    public void LoadReturnsStoredPreference()
    {
        var adapter = MakeAdapter(id: "saved", name: "Saved Adapter");
        var expected = new DefaultAdapterPreference(adapter);
        var port = new DefaultAdapterPreferencePortFake(loadedPreference: expected);
        var useCase = new DefaultAdapterPreferenceUseCase(port);

        Assert.Equal(expected, useCase.Load());
    }

    /// <summary>検出済み候補をデフォルト設定へ変換して保存できることを検証します。</summary>
    /// <remarks>責務: 共通ユースケースが確定候補の識別情報を保存境界へ1件渡すことを確認します。</remarks>
    [Fact]
    public void SavePersistsDetectedAdapterPreference()
    {
        var adapter = MakeAdapter(id: "candidate", name: "Candidate");
        var port = new DefaultAdapterPreferencePortFake();
        var useCase = new DefaultAdapterPreferenceUseCase(port);

        var saved = useCase.Save(adapter);

        Assert.Equal(new DefaultAdapterPreference(adapter), saved);
        Assert.Equal(saved, port.SavedPreference);
    }

    /// <summary>保存済み識別情報と一致する最新候補だけを返すことを検証します。</summary>
    /// <remarks>責務: 共通ユースケースが異なる候補を除外して同一候補を選択することを確認します。</remarks>
    [Fact]
    public void DetectedAdapterReturnsOnlyMatchingCandidate()
    {
        var expected = MakeAdapter(id: "saved", name: "Detected Name");
        var different = MakeAdapter(id: "other", name: "Other");
        var preference = new DefaultAdapterPreference(expected);
        var useCase = new DefaultAdapterPreferenceUseCase(new DefaultAdapterPreferencePortFake());

        var result = useCase.DetectedAdapter(preference, [different, expected]);

        Assert.Equal(expected, result);
    }

    /// <summary>テストで使用するBluetoothアダプター候補を生成します。</summary>
    /// <remarks>責務: 指定された識別子と表示名を持つ候補を1件構築します。</remarks>
    /// <param name="id">候補を識別する安定識別子。</param>
    /// <param name="name">候補の表示名。</param>
    /// <returns>未接続状態のBluetooth候補。</returns>
    private static DiscoveredAdapter MakeAdapter(string id, string name) =>
        new(
            id: id,
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: name,
            systemIdentifier: id,
            isConnected: false);
}

/// <summary>共通ユースケーステストで保存内容を観測する保存境界です。</summary>
file sealed class DefaultAdapterPreferencePortFake : IDefaultAdapterPreferencePort
{
    /// <summary>読込時に返す既存設定です。</summary>
    private readonly DefaultAdapterPreference? loadedPreference;

    /// <summary>読込用の既存設定を注入してFakeを生成します。</summary>
    /// <remarks>責務: 1件のテストシナリオで返す既存設定を保持します。</remarks>
    /// <param name="loadedPreference">読込要求時に返す設定。</param>
    public DefaultAdapterPreferencePortFake(DefaultAdapterPreference? loadedPreference = null)
    {
        this.loadedPreference = loadedPreference;
    }

    /// <summary>最後に保存された設定です。</summary>
    public DefaultAdapterPreference? SavedPreference { get; private set; }

    /// <summary>注入済みの既存設定を返します。</summary>
    /// <remarks>責務: デフォルト設定の読込要求へ固定値で応答します。</remarks>
    /// <returns>初期化時に注入された設定。</returns>
    public DefaultAdapterPreference? Load() => loadedPreference;

    /// <summary>保存要求された設定を記録します。</summary>
    /// <remarks>責務: 1件のデフォルト設定をテストから観測可能な状態へ保持します。</remarks>
    /// <param name="preference">保存要求された設定。</param>
    public void Save(DefaultAdapterPreference preference)
    {
        SavedPreference = preference;
    }
}
