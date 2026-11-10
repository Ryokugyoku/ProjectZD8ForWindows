using ProjectZD8.Data.Devices;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices;

/// <summary>BLE一覧が既知OBD候補だけを公開することを検証します。</summary>
public sealed class OBDBluetoothCandidatePolicyTests
{
    /// <summary>公式に確認したOBDLink、VEEPEAK、VgateのBLE名称を許可することを検証します。</summary>
    /// <remarks>責務: 既知OBDメーカーの広告名称が選択候補として保持されることを確認します。</remarks>
    [Fact]
    public void AcceptsKnownOBDProductNames()
    {
        var policy = new OBDBluetoothCandidatePolicy();

        Assert.True(policy.Accepts(MakeAdapter("OBDLink CX")));
        Assert.True(policy.Accepts(MakeAdapter("VEEPEAK")));
        Assert.True(policy.Accepts(MakeAdapter("Vgate iCar Pro")));
        Assert.True(policy.Accepts(MakeAdapter("IOS-Vlink")));
    }

    /// <summary>名称が不明でも既知UART Service UUIDを持つ候補を許可することを検証します。</summary>
    /// <remarks>責務: 採用済みUART構成を広告するBLE候補が名称欠損だけで除外されないことを確認します。</remarks>
    [Fact]
    public void AcceptsKnownUARTServiceIdentifiers()
    {
        var policy = new OBDBluetoothCandidatePolicy();

        foreach (var serviceIdentifier in BluetoothUARTProfile.Supported.Select(profile => profile.ServiceUUID))
        {
            Assert.True(policy.Accepts(MakeAdapter(Guid.NewGuid().ToString().ToUpperInvariant(), [serviceIdentifier])), serviceIdentifier);
        }
    }

    /// <summary>OBD信号を持たない周辺BLE機器を除外することを検証します。</summary>
    /// <remarks>責務: 一般アクセサリー、UUID表示、Manufacturer Dataだけの候補をOBD一覧へ混入させないことを確認します。</remarks>
    [Fact]
    public void RejectsUnrelatedBluetoothPeripherals()
    {
        var policy = new OBDBluetoothCandidatePolicy();

        Assert.False(policy.Accepts(MakeAdapter("AirPods Pro")));
        Assert.False(policy.Accepts(MakeAdapter("Pioneer Navi")));
        Assert.False(policy.Accepts(MakeAdapter("11111111-2222-3333-4444-555555555555", hasManufacturerData: true)));
    }

    /// <summary>判定テスト用のBLE候補を生成します。</summary>
    /// <remarks>責務: 表示名と広告信号だけを変更可能なBLE候補を一意に構築します。</remarks>
    /// <param name="name">広告および一覧へ表示する名称。</param>
    /// <param name="serviceIdentifiers">広告に含めるService UUID一覧。</param>
    /// <param name="hasManufacturerData">Manufacturer Dataを持つかどうか。</param>
    /// <returns>未接続のBLE候補。</returns>
    private static DiscoveredAdapter MakeAdapter(
        string name,
        IReadOnlyList<string>? serviceIdentifiers = null,
        bool hasManufacturerData = false) =>
        new(
            id: $"bluetooth-low-energy:{name}",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: name,
            systemIdentifier: name,
            isConnected: false,
            productName: name,
            advertisementLocalName: name,
            hasManufacturerData: hasManufacturerData,
            bluetoothServiceIdentifiers: serviceIdentifiers ?? []);
}
