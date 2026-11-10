namespace ProjectZD8.Data.Devices.OBD;

/// <summary>UART構成の由来と互換範囲です。</summary>
public enum BluetoothUARTProfileKind
{
    /// <summary>提供資料に基づくOBDLink MX+試験構成です。</summary>
    ObdLinkMXPlusExperimental,

    /// <summary>OBDLink CX公式仕様およびSwiftOBD2が扱うFFF0構成です。</summary>
    Fff0,

    /// <summary>SwiftOBD2が扱うFFE0単一Characteristic構成です。</summary>
    Ffe0,

    /// <summary>SwiftOBD2がVGate iCar Pro例として扱う18F0構成です。</summary>
    VGate18F0,
}

/// <summary><see cref="BluetoothUARTProfileKind"/> の raw value を Swift と同じ文字列で扱います。</summary>
public static class BluetoothUARTProfileKindRawValue
{
    /// <summary>Swift と同じ raw value を返します。</summary>
    /// <remarks>責務: 1件の構成種別をログ表示用の安定文字列へ変換します。</remarks>
    /// <param name="value">変換する構成種別。</param>
    /// <returns>Swift のケース名と同じ文字列。</returns>
    public static string ToRawValue(this BluetoothUARTProfileKind value) => value switch
    {
        BluetoothUARTProfileKind.ObdLinkMXPlusExperimental => "obdLinkMXPlusExperimental",
        BluetoothUARTProfileKind.Fff0 => "fff0",
        BluetoothUARTProfileKind.Ffe0 => "ffe0",
        BluetoothUARTProfileKind.VGate18F0 => "vGate18F0",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}

/// <summary>試験する既知のBluetooth Low Energy UART構成を表します。</summary>
/// <remarks>iOS 版の <c>AppleBluetoothUARTProfile</c> に対応します。UUID の表記は CoreBluetooth の <c>CBUUID.uuidString</c> と同じ（16bit は4桁）です。</remarks>
public sealed record BluetoothUARTProfile
{
    /// <summary>UART構成の由来です。</summary>
    public BluetoothUARTProfileKind Kind { get; }

    /// <summary>UARTを公開するService UUIDです。</summary>
    public string ServiceUUID { get; }

    /// <summary>アダプターへ書き込むCharacteristic UUIDです。</summary>
    public string WriteCharacteristicUUID { get; }

    /// <summary>アダプターから通知を受けるCharacteristic UUIDです。</summary>
    public string NotifyCharacteristicUUID { get; }

    /// <summary>既知のUUIDを正規化してUART構成を生成します。</summary>
    /// <remarks>責務: 1件の既知UART定義を大文字UUIDによる比較可能な不変値へ固定します。</remarks>
    /// <param name="kind">UART構成の由来。</param>
    /// <param name="serviceUUID">UARTを公開するService UUID。</param>
    /// <param name="writeCharacteristicUUID">書込Characteristic UUID。</param>
    /// <param name="notifyCharacteristicUUID">通知Characteristic UUID。</param>
    public BluetoothUARTProfile(
        BluetoothUARTProfileKind kind,
        string serviceUUID,
        string writeCharacteristicUUID,
        string notifyCharacteristicUUID)
    {
        Kind = kind;
        ServiceUUID = serviceUUID.ToUpperInvariant();
        WriteCharacteristicUUID = writeCharacteristicUUID.ToUpperInvariant();
        NotifyCharacteristicUUID = notifyCharacteristicUUID.ToUpperInvariant();
    }

    /// <summary>実機試験で照合する既知UART構成を優先順で返します。</summary>
    public static IReadOnlyList<BluetoothUARTProfile> Supported { get; } =
    [
        new(
            BluetoothUARTProfileKind.ObdLinkMXPlusExperimental,
            "B3491406-44E4-4D83-97C5-CE3190130000",
            "B3491406-44E4-4D83-97C5-CE3190130001",
            "B3491406-44E4-4D83-97C5-CE3190130001"),
        new(BluetoothUARTProfileKind.Fff0, "FFF0", "FFF2", "FFF1"),
        new(BluetoothUARTProfileKind.Ffe0, "FFE0", "FFE1", "FFE1"),
        new(BluetoothUARTProfileKind.VGate18F0, "18F0", "2AF1", "2AF0"),
    ];
}

/// <summary>1件のBLE Characteristicが公開するUART関連能力です。</summary>
public sealed record BluetoothCharacteristicCapability
{
    /// <summary>Characteristic UUIDです。</summary>
    public string UUID { get; }

    /// <summary>Notifyを有効化できるかどうかです。</summary>
    public bool SupportsNotify { get; }

    /// <summary>応答あり書込を利用できるかどうかです。</summary>
    public bool SupportsWriteWithResponse { get; }

    /// <summary>応答なし書込を利用できるかどうかです。</summary>
    public bool SupportsWriteWithoutResponse { get; }

    /// <summary>OS非依存のCharacteristic能力を生成します。</summary>
    /// <remarks>責務: 1件のCharacteristic識別子とUART関連プロパティを比較可能な値へ固定します。</remarks>
    /// <param name="uuid">Characteristic UUID。</param>
    /// <param name="supportsNotify">Notify対応状態。</param>
    /// <param name="supportsWriteWithResponse">応答あり書込対応状態。</param>
    /// <param name="supportsWriteWithoutResponse">応答なし書込対応状態。</param>
    public BluetoothCharacteristicCapability(
        string uuid,
        bool supportsNotify,
        bool supportsWriteWithResponse,
        bool supportsWriteWithoutResponse)
    {
        UUID = uuid.ToUpperInvariant();
        SupportsNotify = supportsNotify;
        SupportsWriteWithResponse = supportsWriteWithResponse;
        SupportsWriteWithoutResponse = supportsWriteWithoutResponse;
    }
}

/// <summary>実機のService一覧から利用可能な既知UART構成を選択します。</summary>
public sealed class BluetoothUARTProfileResolver
{
    /// <summary>実機能力を満たす最初の既知UART構成を返します。</summary>
    /// <remarks>責務: Service別Characteristic能力をNotify可能かつ書込可能な既知UART構成へ解決します。</remarks>
    /// <param name="services">Service UUIDごとのCharacteristic能力。</param>
    /// <param name="profiles">優先順に照合する既知UART構成。<c>null</c> の場合は <see cref="BluetoothUARTProfile.Supported"/>。</param>
    /// <returns>必要なService、Notify、Write能力が揃う最初の構成。該当しない場合は <c>null</c>。</returns>
    /// <exception cref="ArgumentException">大文字化すると同じになるService UUIDが重複する場合（Swift の <c>uniqueKeysWithValues</c> と同じ）。</exception>
    public BluetoothUARTProfile? Resolve(
        IReadOnlyDictionary<string, IReadOnlyList<BluetoothCharacteristicCapability>> services,
        IReadOnlyList<BluetoothUARTProfile>? profiles = null)
    {
        var normalizedServices = services.ToDictionary(entry => entry.Key.ToUpperInvariant(), entry => entry.Value, StringComparer.Ordinal);
        return (profiles ?? BluetoothUARTProfile.Supported).FirstOrDefault(profile =>
        {
            if (!normalizedServices.TryGetValue(profile.ServiceUUID, out var characteristics))
            {
                return false;
            }

            var notify = characteristics.FirstOrDefault(characteristic =>
                string.Equals(characteristic.UUID, profile.NotifyCharacteristicUUID, StringComparison.Ordinal)
                && characteristic.SupportsNotify);
            var write = characteristics.FirstOrDefault(characteristic =>
                string.Equals(characteristic.UUID, profile.WriteCharacteristicUUID, StringComparison.Ordinal)
                && (characteristic.SupportsWriteWithResponse || characteristic.SupportsWriteWithoutResponse));
            return notify is not null && write is not null;
        });
    }
}
