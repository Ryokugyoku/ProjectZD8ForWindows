using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.DeviceConnection.State;

/// <summary>デフォルトアダプターの共通接続可否判定を検証します。</summary>
public sealed class DefaultAdapterAvailabilityTests
{
    /// <summary>保存設定がない場合は未設定状態になることを検証します。</summary>
    /// <remarks>責務: デフォルト設定がない入力を接続不能な未設定状態へ変換することを確認します。</remarks>
    [Fact]
    public void MissingPreferenceIsNotConfigured()
    {
        var availability = DefaultAdapterAvailability.Create(
            preference: null,
            detectedAdapter: MakeAdapter(id: "detected"));

        Assert.Equal(new DefaultAdapterAvailability.NotConfigured(), availability);
        Assert.False(availability.HasDefaultAdapter);
        Assert.Null(availability.ConnectionEndpoint);
    }

    /// <summary>保存済み候補が最新探索に存在しない場合は未検出状態になることを検証します。</summary>
    /// <remarks>責務: 保存設定だけが残る入力から接続終端を公開しないことを確認します。</remarks>
    [Fact]
    public void SavedButMissingAdapterIsNotDetected()
    {
        var savedAdapter = MakeAdapter(id: "saved", name: "Saved Adapter");

        var availability = DefaultAdapterAvailability.Create(
            preference: new DefaultAdapterPreference(savedAdapter),
            detectedAdapter: null);

        Assert.Equal(new DefaultAdapterAvailability.NotDetected("Saved Adapter"), availability);
        Assert.True(availability.HasDefaultAdapter);
        Assert.False(availability.IsDetected);
        Assert.Null(availability.ConnectionEndpoint);
    }

    /// <summary>保存設定と一致する最新候補だけが接続終端を公開することを検証します。</summary>
    /// <remarks>責務: 一致する検出候補を接続可能状態と現在の物理終端へ変換することを確認します。</remarks>
    [Fact]
    public void MatchingDetectedAdapterExposesConnectionEndpoint()
    {
        var adapter = MakeAdapter(id: "saved", name: "Saved Adapter");

        var availability = DefaultAdapterAvailability.Create(
            preference: new DefaultAdapterPreference(adapter),
            detectedAdapter: adapter);

        Assert.True(availability.IsDetected);
        Assert.Equal(new OBDConnectionEndpoint(adapter), availability.ConnectionEndpoint);
    }

    /// <summary>MX+候補がBluetooth Classic終端をHOMEへ公開することを検証します。</summary>
    /// <remarks>責務: 探索時に確定したClassic方式をBLEへ変換せず接続終端へ保持することを確認します。</remarks>
    [Fact]
    public void MatchingMXPlusAdapterPreservesBluetoothClassicEndpoint()
    {
        var adapter = new DiscoveredAdapter(
            id: "bluetooth-classic:00-04-3E-12-34-56",
            transportMode: AdapterTransportMode.Bluetooth,
            connectionTransport: OBDConnectionEndpointTransport.BluetoothClassic,
            displayName: "OBDLink MX+ 48318",
            systemIdentifier: "00-04-3E-12-34-56",
            isConnected: false);

        var availability = DefaultAdapterAvailability.Create(
            preference: new DefaultAdapterPreference(adapter),
            detectedAdapter: adapter);

        Assert.Equal(OBDConnectionEndpointTransport.BluetoothClassic, availability.ConnectionEndpoint?.Transport);
        Assert.Equal("00-04-3E-12-34-56", availability.ConnectionEndpoint?.SystemIdentifier);
    }

    /// <summary>テスト用Bluetoothアダプター候補を生成します。</summary>
    /// <remarks>責務: 指定した識別子と名称を持つ未接続候補を1件構築します。</remarks>
    /// <param name="id">候補を識別する安定識別子。</param>
    /// <param name="name">候補の表示名称。</param>
    /// <returns>共通接続可否テストで使用するBluetooth候補。</returns>
    private static DiscoveredAdapter MakeAdapter(string id, string name = "Adapter") =>
        new(
            id: id,
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: name,
            systemIdentifier: id,
            isConnected: false);
}
