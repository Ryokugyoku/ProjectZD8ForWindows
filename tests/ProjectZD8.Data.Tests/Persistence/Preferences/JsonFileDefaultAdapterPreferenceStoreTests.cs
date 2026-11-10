using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Persistence.Preferences;

/// <summary>JSON ファイルによるデフォルトアダプター設定の保存と復元を検証します。</summary>
/// <remarks>iOS 版の <c>UserDefaultsDefaultAdapterPreferenceStoreTests</c> に対応します。</remarks>
public sealed class JsonFileDefaultAdapterPreferenceStoreTests : IDisposable
{
    /// <summary>テストごとの一時ディレクトリーです。</summary>
    private readonly string directory = Path.Combine(Path.GetTempPath(), "JsonFileDefaultAdapterPreferenceStoreTests", Guid.NewGuid().ToString("N"));

    /// <summary>保存ファイルのパスです。</summary>
    private string FilePath => Path.Combine(directory, JsonFileDefaultAdapterPreferenceStore.DefaultFileName);

    /// <summary>一時ディレクトリーを削除します。</summary>
    /// <remarks>責務: テストで作ったファイルを残さないようにします。</remarks>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>保存した設定を同じ識別情報で復元できることを検証します。</summary>
    /// <remarks>責務: デフォルト設定の JSON 往復が値を維持することを確認します。</remarks>
    [Fact]
    public void SaveAndLoadPreserveDefaultAdapterPreference()
    {
        var store = new JsonFileDefaultAdapterPreferenceStore(directory);
        var preference = new DefaultAdapterPreference(
            adapterID: "usb:adapter",
            transportMode: AdapterTransportMode.Usb,
            displayName: "ZD8 Adapter",
            systemIdentifier: "COM3");

        store.Save(preference);

        Assert.Equal(preference, store.Load());
    }

    /// <summary>保存形式が iOS の UserDefaults 辞書と同じフィールド名と値であることを検証します。</summary>
    /// <remarks>責務: 文字列だけの辞書として raw value で保存されることを確認します。</remarks>
    [Fact]
    public void SaveWritesSwiftCompatibleFields()
    {
        new JsonFileDefaultAdapterPreferenceStore(directory).Save(new DefaultAdapterPreference(
            adapterID: "bluetooth-classic:00-04-3E-12-34-56",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: "OBDLink MX+",
            systemIdentifier: "00-04-3E-12-34-56",
            connectionTransport: OBDConnectionEndpointTransport.BluetoothClassic));

        var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))!;

        Assert.Equal("bluetooth", values["transportMode"]);
        Assert.Equal("bluetoothClassic", values["connectionTransport"]);
        Assert.Equal(["adapterID", "connectionTransport", "displayName", "systemIdentifier", "transportMode"], values.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>不完全な保存辞書をデフォルト設定として復元しないことを検証します。</summary>
    /// <remarks>責務: 必須識別情報が欠けた保存値を未設定として扱うことを確認します。</remarks>
    [Fact]
    public void LoadReturnsNullForIncompleteStoredValue()
    {
        Write("""{"adapterID":"partial"}""");

        Assert.Null(new JsonFileDefaultAdapterPreferenceStore(directory).Load());
    }

    /// <summary>壊れたファイルとファイルなしを未設定として扱うことを検証します。</summary>
    /// <remarks>責務: 読めない保存値で例外を出さないことを確認します。</remarks>
    [Fact]
    public void LoadReturnsNullForMissingOrCorruptFile()
    {
        var store = new JsonFileDefaultAdapterPreferenceStore(directory);
        Assert.Null(store.Load());

        Write("{not json");
        Assert.Null(store.Load());

        Write("""{"adapterID":1}""");
        Assert.Null(store.Load());
    }

    /// <summary>物理終端を持たない旧保存形式を探索方式から安全に補完できることを検証します。</summary>
    /// <remarks>責務: 旧形式のUSB設定をシリアル終端として後方互換復元することを確認します。</remarks>
    [Fact]
    public void LoadRestoresLegacyUSBPreferenceAsSerialTransport()
    {
        Write("""{"adapterID":"usb:legacy","transportMode":"usb","displayName":"Legacy USB","systemIdentifier":"COM9"}""");

        Assert.Equal(OBDConnectionEndpointTransport.Serial, new JsonFileDefaultAdapterPreferenceStore(directory).Load()?.ConnectionTransport);
    }

    /// <summary>物理終端を持たない旧Bluetooth形式を識別子からClassicとして補完できることを検証します。</summary>
    /// <remarks>責務: UUIDではない旧Bluetooth設定をClassic終端として復元することを確認します。</remarks>
    [Fact]
    public void LoadRestoresLegacyNonUUIDBluetoothPreferenceAsClassicTransport()
    {
        Write("""{"adapterID":"bluetooth:legacy-classic","transportMode":"bluetooth","displayName":"Legacy Classic","systemIdentifier":"42"}""");

        Assert.Equal(OBDConnectionEndpointTransport.BluetoothClassic, new JsonFileDefaultAdapterPreferenceStore(directory).Load()?.ConnectionTransport);
    }

    /// <summary>物理終端を持たない旧Bluetooth形式でUUID識別子ならBLEとして補完できることを検証します。</summary>
    /// <remarks>責務: UUID形式の旧Bluetooth設定をBLE終端として復元することを確認します。</remarks>
    [Fact]
    public void LoadRestoresLegacyUUIDBluetoothPreferenceAsLowEnergyTransport()
    {
        Write("""{"adapterID":"bluetooth:legacy-ble","transportMode":"bluetooth","displayName":"Legacy BLE","systemIdentifier":"AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"}""");

        Assert.Equal(OBDConnectionEndpointTransport.BluetoothLowEnergy, new JsonFileDefaultAdapterPreferenceStore(directory).Load()?.ConnectionTransport);
    }

    /// <summary>アカウント削除時に保存済みデフォルトアダプターを除去できることを検証します。</summary>
    /// <remarks>責務: 端末固有設定の削除後にデフォルトアダプターが復元されないことを確認します。</remarks>
    [Fact]
    public void RemoveDeletesSavedDefaultAdapterPreference()
    {
        var store = new JsonFileDefaultAdapterPreferenceStore(directory);
        store.Save(new DefaultAdapterPreference(
            adapterID: "bluetooth:adapter",
            transportMode: AdapterTransportMode.Bluetooth,
            displayName: "ZD8 Adapter",
            systemIdentifier: "adapter-id"));

        store.Remove();

        Assert.Null(store.Load());
        Assert.False(File.Exists(FilePath));
    }

    /// <summary>保存ファイルへ文字列を書きます。</summary>
    /// <remarks>責務: 旧形式や壊れた保存値をテスト用に用意します。</remarks>
    /// <param name="json">書き込む内容。</param>
    private void Write(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, json);
    }
}
