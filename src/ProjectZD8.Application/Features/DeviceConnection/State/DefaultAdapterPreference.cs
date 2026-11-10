using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.State;

/// <summary>次回以降に優先するアダプターの永続化可能な識別情報です。</summary>
public sealed record DefaultAdapterPreference
{
    /// <summary>探索結果間で同じ候補を識別する安定識別子です。</summary>
    public string AdapterID { get; }

    /// <summary>デフォルト候補を探索する物理接続方式です。</summary>
    public AdapterTransportMode TransportMode { get; }

    /// <summary>接続開始時に使用する物理終端の種類です。</summary>
    public OBDConnectionEndpointTransport ConnectionTransport { get; }

    /// <summary>設定済み候補として表示する名称です。</summary>
    public string DisplayName { get; }

    /// <summary>システムが候補へ割り当てた識別子です。</summary>
    public string SystemIdentifier { get; }

    /// <summary>検出済み候補からデフォルト設定を生成します。</summary>
    /// <remarks>責務: 1件の検出結果を次回探索で照合できるデフォルト設定へ変換します。</remarks>
    /// <param name="adapter">ユーザーがデフォルトとして確定した検出済み候補。</param>
    public DefaultAdapterPreference(DiscoveredAdapter adapter)
    {
        AdapterID = adapter.Id;
        TransportMode = adapter.TransportMode;
        ConnectionTransport = adapter.ConnectionTransport;
        DisplayName = adapter.DisplayName;
        SystemIdentifier = adapter.SystemIdentifier;
    }

    /// <summary>永続化済みの各値からデフォルト設定を復元します。</summary>
    /// <remarks>責務: 保存境界から読み込んだ識別情報をデフォルト設定へまとめます。</remarks>
    /// <param name="adapterID">探索結果間で同じ候補を識別する安定識別子。</param>
    /// <param name="transportMode">デフォルト候補を探索する物理接続方式。</param>
    /// <param name="displayName">設定済み候補として表示する名称。</param>
    /// <param name="systemIdentifier">システムが候補へ割り当てた識別子。</param>
    /// <param name="connectionTransport">接続開始時に使用する物理終端の種類。</param>
    public DefaultAdapterPreference(
        string adapterID,
        AdapterTransportMode transportMode,
        string displayName,
        string systemIdentifier,
        OBDConnectionEndpointTransport? connectionTransport = null)
    {
        AdapterID = adapterID;
        TransportMode = transportMode;
        ConnectionTransport = connectionTransport
            ?? (transportMode == AdapterTransportMode.Usb
                ? OBDConnectionEndpointTransport.Serial
                : OBDConnectionEndpointTransport.BluetoothLowEnergy);
        DisplayName = displayName;
        SystemIdentifier = systemIdentifier;
    }

    /// <summary>検出済み候補がこのデフォルト設定と一致するかを返します。</summary>
    /// <remarks>責務: 1件の検出結果を保存済みの安定識別情報と照合します。</remarks>
    /// <param name="adapter">照合対象の検出済み候補。</param>
    /// <returns>同じ接続方式かつ同じ安定識別子を持つ場合は <c>true</c>。</returns>
    public bool Matches(DiscoveredAdapter adapter) =>
        TransportMode == adapter.TransportMode
        && string.Equals(AdapterID, adapter.Id, StringComparison.Ordinal)
        && string.Equals(SystemIdentifier, adapter.SystemIdentifier, StringComparison.Ordinal);
}
