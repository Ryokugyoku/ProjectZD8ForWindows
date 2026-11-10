using ProjectZD8.Data.Devices.OBD;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>BLE UARTに対する既知UUIDと能力照合を検証します。</summary>
public sealed class BluetoothUARTProfileTests
{
    /// <summary>提供資料のMX+単一Characteristic構成を解決できることを検証します。</summary>
    /// <remarks>責務: b349 ServiceのNotify兼Write能力が実験プロファイルへ一致することを確認します。</remarks>
    [Fact]
    public void ResolvesExperimentalMXPlusSingleCharacteristicProfile()
    {
        var characteristic = new BluetoothCharacteristicCapability(
            "b3491406-44e4-4d83-97c5-ce3190130001",
            supportsNotify: true,
            supportsWriteWithResponse: false,
            supportsWriteWithoutResponse: true);

        var profile = new BluetoothUARTProfileResolver().Resolve(
            new Dictionary<string, IReadOnlyList<BluetoothCharacteristicCapability>>
            {
                ["b3491406-44e4-4d83-97c5-ce3190130000"] = [characteristic],
            });

        Assert.Equal(BluetoothUARTProfileKind.ObdLinkMXPlusExperimental, profile?.Kind);
    }

    /// <summary>OBDLink CX公式仕様のFFF0構成を解決できることを検証します。</summary>
    /// <remarks>責務: 分離したFFF1 NotifyとFFF2 Write能力がFFF0プロファイルへ一致することを確認します。</remarks>
    [Fact]
    public void ResolvesFFF0SplitCharacteristicProfile()
    {
        var profile = new BluetoothUARTProfileResolver().Resolve(
            new Dictionary<string, IReadOnlyList<BluetoothCharacteristicCapability>>
            {
                ["FFF0"] =
                [
                    new BluetoothCharacteristicCapability("FFF1", supportsNotify: true, supportsWriteWithResponse: false, supportsWriteWithoutResponse: false),
                    new BluetoothCharacteristicCapability("FFF2", supportsNotify: false, supportsWriteWithResponse: true, supportsWriteWithoutResponse: true),
                ],
            });

        Assert.Equal(BluetoothUARTProfileKind.Fff0, profile?.Kind);
    }

    /// <summary>UUIDだけ一致しても必要なプロパティがなければ採用しないことを検証します。</summary>
    /// <remarks>責務: NotifyまたはWrite能力を欠く類似ServiceがUARTとして誤採用されないことを確認します。</remarks>
    [Fact]
    public void RejectsProfileWithoutRequiredProperties()
    {
        var profile = new BluetoothUARTProfileResolver().Resolve(
            new Dictionary<string, IReadOnlyList<BluetoothCharacteristicCapability>>
            {
                ["FFE0"] =
                [
                    new BluetoothCharacteristicCapability("FFE1", supportsNotify: false, supportsWriteWithResponse: false, supportsWriteWithoutResponse: false),
                ],
            });

        Assert.Null(profile);
    }

    /// <summary>既知UART構成の優先順と raw value が iOS と同じことを検証します。</summary>
    /// <remarks>責務: 照合順序とログ表示用の識別子が変わらないことを確認します。</remarks>
    [Fact]
    public void SupportedProfilesKeepSwiftOrderAndRawValues()
    {
        Assert.Equal(
            ["obdLinkMXPlusExperimental", "fff0", "ffe0", "vGate18F0"],
            BluetoothUARTProfile.Supported.Select(profile => profile.Kind.ToRawValue()));
        Assert.Equal(
            ["B3491406-44E4-4D83-97C5-CE3190130000", "FFF0", "FFE0", "18F0"],
            BluetoothUARTProfile.Supported.Select(profile => profile.ServiceUUID));
    }
}
