using System.Text.Json;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Diagnostics;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Preferences;

/// <summary>JSON ファイルを使ってデフォルトアダプター設定を保持します。</summary>
/// <remarks>
/// iOS 版の <c>UserDefaultsDefaultAdapterPreferenceStore</c> に対応します。iOS が UserDefaults に保存する
/// 文字列だけの辞書（同じフィールド名）を、渡されたディレクトリーの JSON ファイル1件として保存します。
/// </remarks>
public sealed class JsonFileDefaultAdapterPreferenceStore : IDefaultAdapterPreferencePort
{
    /// <summary>既定の保存ファイル名です（iOS の UserDefaults キーと同じ名前）。</summary>
    public const string DefaultFileName = "deviceConnection.defaultAdapter.json";

    /// <summary>保存辞書内で使用するフィールド名です。</summary>
    private static class Field
    {
        /// <summary>安定識別子を保持するフィールド名です。</summary>
        public const string AdapterID = "adapterID";

        /// <summary>接続方式を保持するフィールド名です。</summary>
        public const string TransportMode = "transportMode";

        /// <summary>物理終端の種類を保持するフィールド名です。</summary>
        public const string ConnectionTransport = "connectionTransport";

        /// <summary>表示名を保持するフィールド名です。</summary>
        public const string DisplayName = "displayName";

        /// <summary>システム識別子を保持するフィールド名です。</summary>
        public const string SystemIdentifier = "systemIdentifier";
    }

    /// <summary>設定を保存するファイルの完全パスです。</summary>
    private readonly string filePath;

    /// <summary>保存先ディレクトリーとファイル名を注入してストアを生成します。</summary>
    /// <remarks>責務: デフォルトアダプター設定の保存先を1件の JSON ファイルへ固定します。</remarks>
    /// <param name="directoryPath">設定ファイルを置くディレクトリー（組み立てがアプリのローカルフォルダーなどを渡します）。</param>
    /// <param name="fileName">設定ファイルの名前。</param>
    public JsonFileDefaultAdapterPreferenceStore(string directoryPath, string fileName = DefaultFileName)
    {
        filePath = Path.Combine(directoryPath, fileName);
    }

    /// <summary>現在保存されているデフォルトアダプター設定を読み込みます。</summary>
    /// <remarks>責務: 1件の JSON 辞書を検証済みのデフォルト設定へ復元します。</remarks>
    /// <returns>必須フィールドをすべて復元できた設定。不完全な場合や読めない場合は <c>null</c>。</returns>
    public DefaultAdapterPreference? Load()
    {
        Dictionary<string, string>? values;
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filePath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            PreferencesLog.DefaultAdapterUnreadable(PreferencesLog.Logger, error, Redact.Path(filePath));
            return null;
        }

        if (values is null
            || !values.TryGetValue(Field.AdapterID, out var adapterID)
            || !values.TryGetValue(Field.TransportMode, out var rawTransportMode)
            || !AdapterTransportModeRawValue.TryParse(rawTransportMode, out var transportMode)
            || !values.TryGetValue(Field.SystemIdentifier, out var systemIdentifier)
            || RestoredConnectionTransport(
                values.GetValueOrDefault(Field.ConnectionTransport),
                transportMode,
                systemIdentifier) is not { } connectionTransport
            || !values.TryGetValue(Field.DisplayName, out var displayName))
        {
            return null;
        }

        return new DefaultAdapterPreference(
            adapterID: adapterID,
            transportMode: transportMode,
            displayName: displayName,
            systemIdentifier: systemIdentifier,
            connectionTransport: connectionTransport);
    }

    /// <summary>指定されたデフォルトアダプター設定を保存します。</summary>
    /// <remarks>責務: 1件のデフォルト設定を JSON 辞書として置き換えて保存します。</remarks>
    /// <param name="preference">保存するデフォルトアダプターの識別情報。</param>
    /// <exception cref="IOException">ファイルを書き込めない場合。</exception>
    public void Save(DefaultAdapterPreference preference)
    {
        var values = new Dictionary<string, string>
        {
            [Field.AdapterID] = preference.AdapterID,
            [Field.TransportMode] = preference.TransportMode.ToRawValue(),
            [Field.ConnectionTransport] = preference.ConnectionTransport.ToRawValue(),
            [Field.DisplayName] = preference.DisplayName,
            [Field.SystemIdentifier] = preference.SystemIdentifier,
        };
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(values));
        File.Move(temporaryPath, filePath, overwrite: true);
    }

    /// <summary>保存済みのデフォルトアダプター設定を削除します。</summary>
    /// <remarks>責務: 端末固有のデフォルトアダプター設定ファイルを存在しない状態へします。</remarks>
    public void Remove()
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>保存形式の物理終端を復元し、旧形式では探索方式と識別子から補完します。</summary>
    /// <remarks>責務: 永続化値または旧形式の接続方式を1件の物理終端へ変換します。</remarks>
    /// <param name="rawValue">保存されている物理終端の生値。</param>
    /// <param name="transportMode">旧形式の補完に使用する探索方式。</param>
    /// <param name="systemIdentifier">BLE UUIDとClassic識別子の判別に使用するシステム識別子。</param>
    /// <returns>復元できた物理終端。値が不正な場合は <c>null</c>。</returns>
    private static OBDConnectionEndpointTransport? RestoredConnectionTransport(
        string? rawValue,
        AdapterTransportMode transportMode,
        string systemIdentifier)
    {
        if (rawValue is not null)
        {
            return OBDConnectionEndpointTransportRawValue.TryParse(rawValue, out var transport) ? transport : null;
        }

        if (transportMode == AdapterTransportMode.Usb)
        {
            return OBDConnectionEndpointTransport.Serial;
        }

        // Swift の UUID(uuidString:) はハイフン付き36文字だけを受理するため、同じ形式で判定します。
        return Guid.TryParseExact(systemIdentifier, "D", out _)
            ? OBDConnectionEndpointTransport.BluetoothLowEnergy
            : OBDConnectionEndpointTransport.BluetoothClassic;
    }
}
