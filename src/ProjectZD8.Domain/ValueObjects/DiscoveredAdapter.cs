namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// システムのデバイス情報から検出したアダプター候補を表します。
/// </summary>
public sealed record DiscoveredAdapter
{
    /// <summary>
    /// 同一候補を探索結果間で識別する安定識別子です。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 候補を検出した物理接続方式です。
    /// </summary>
    public AdapterTransportMode TransportMode { get; }

    /// <summary>
    /// OBDコマンド通信で使用する物理終端の種類です。
    /// </summary>
    public OBDConnectionEndpointTransport ConnectionTransport { get; }

    /// <summary>
    /// 一覧で優先表示する名称または代替識別子です。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// システムから取得できたメーカー名称です。
    /// </summary>
    public string? ManufacturerName { get; }

    /// <summary>
    /// システムから取得できた製品名称です。
    /// </summary>
    public string? ProductName { get; }

    /// <summary>
    /// Bluetoothアドバタイズメントから取得できたローカル名称です。
    /// </summary>
    public string? AdvertisementLocalName { get; }

    /// <summary>
    /// BluetoothアドバタイズメントにManufacturer Dataが含まれていたかどうかです。
    /// </summary>
    public bool? HasManufacturerData { get; }

    /// <summary>
    /// Bluetoothアドバタイズメントから取得できたService UUID一覧です。
    /// </summary>
    public IReadOnlyList<string> BluetoothServiceIdentifiers { get; }

    /// <summary>
    /// システムが候補へ割り当てた識別子です。
    /// </summary>
    public string SystemIdentifier { get; }

    /// <summary>
    /// システムから取得できたシリアル番号です。
    /// </summary>
    public string? SerialNumber { get; }

    /// <summary>
    /// システムから取得できたベンダー識別値です。
    /// </summary>
    public string? VendorIdentifier { get; }

    /// <summary>
    /// システムから取得できた製品識別値です。
    /// </summary>
    public string? ProductIdentifier { get; }

    /// <summary>
    /// 検出時点で接続済みと確認できたかどうかです。
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// 検出時点でシステムから取得できた接続状態です。
    /// </summary>
    public DiscoveredAdapterConnectionState ConnectionState { get; }

    /// <summary>
    /// システム由来の各項目を保持するアダプター候補を生成します。
    /// </summary>
    /// <remarks>
    /// 責務: 1件の検出結果を表示と後続設定に利用できる不変値へまとめます。
    /// C# では省略可能引数を末尾に置く必要があるため、Swift 版と引数の順序が異なります。名前付き引数で呼び出してください。
    /// </remarks>
    /// <param name="id">同一候補を識別する安定識別子。</param>
    /// <param name="transportMode">候補を検出した物理接続方式。</param>
    /// <param name="displayName">一覧で優先表示する名称または代替識別子。</param>
    /// <param name="systemIdentifier">システムが候補へ割り当てた識別子。</param>
    /// <param name="isConnected">検出時点で接続済みと確認できたかどうか。</param>
    /// <param name="connectionTransport">OBDコマンド通信で使用する物理終端。省略時は探索方式から解決します。</param>
    /// <param name="manufacturerName">システムから取得できたメーカー名称。</param>
    /// <param name="productName">システムから取得できた製品名称。</param>
    /// <param name="advertisementLocalName">Bluetoothアドバタイズメントから取得できたローカル名称。</param>
    /// <param name="hasManufacturerData">BluetoothアドバタイズメントにManufacturer Dataが含まれていたかどうか。</param>
    /// <param name="bluetoothServiceIdentifiers">Bluetooth広告から取得できたService UUID一覧。省略時は空です。</param>
    /// <param name="serialNumber">システムから取得できたシリアル番号。</param>
    /// <param name="vendorIdentifier">システムから取得できたベンダー識別値。</param>
    /// <param name="productIdentifier">システムから取得できた製品識別値。</param>
    /// <param name="connectionState">検出時点でシステムから取得できた接続状態。省略時は <paramref name="isConnected"/> から解決します。</param>
    public DiscoveredAdapter(
        string id,
        AdapterTransportMode transportMode,
        string displayName,
        string systemIdentifier,
        bool isConnected,
        OBDConnectionEndpointTransport? connectionTransport = null,
        string? manufacturerName = null,
        string? productName = null,
        string? advertisementLocalName = null,
        bool? hasManufacturerData = null,
        IReadOnlyList<string>? bluetoothServiceIdentifiers = null,
        string? serialNumber = null,
        string? vendorIdentifier = null,
        string? productIdentifier = null,
        DiscoveredAdapterConnectionState? connectionState = null)
    {
        Id = id;
        TransportMode = transportMode;
        ConnectionTransport = connectionTransport
            ?? (transportMode == AdapterTransportMode.Usb
                ? OBDConnectionEndpointTransport.Serial
                : OBDConnectionEndpointTransport.BluetoothLowEnergy);
        DisplayName = displayName;
        ManufacturerName = manufacturerName;
        ProductName = productName;
        AdvertisementLocalName = advertisementLocalName;
        HasManufacturerData = hasManufacturerData;
        BluetoothServiceIdentifiers = bluetoothServiceIdentifiers?.ToArray() ?? [];
        SystemIdentifier = systemIdentifier;
        SerialNumber = serialNumber;
        VendorIdentifier = vendorIdentifier;
        ProductIdentifier = productIdentifier;
        IsConnected = isConnected;
        ConnectionState = connectionState
            ?? (isConnected ? DiscoveredAdapterConnectionState.Connected : DiscoveredAdapterConnectionState.Disconnected);
    }

    /// <summary>
    /// 全項目が一致するかを返します。
    /// </summary>
    /// <remarks>責務: Service UUID一覧を要素順で比較し、Swift 版の <c>Equatable</c> と同じ等価性を提供します。</remarks>
    /// <param name="other">比較対象のアダプター候補。</param>
    /// <returns>全項目が一致する場合は <c>true</c>。</returns>
    public bool Equals(DiscoveredAdapter? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && string.Equals(Id, other.Id, StringComparison.Ordinal)
            && TransportMode == other.TransportMode
            && ConnectionTransport == other.ConnectionTransport
            && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
            && string.Equals(ManufacturerName, other.ManufacturerName, StringComparison.Ordinal)
            && string.Equals(ProductName, other.ProductName, StringComparison.Ordinal)
            && string.Equals(AdvertisementLocalName, other.AdvertisementLocalName, StringComparison.Ordinal)
            && HasManufacturerData == other.HasManufacturerData
            && BluetoothServiceIdentifiers.SequenceEqual(other.BluetoothServiceIdentifiers, StringComparer.Ordinal)
            && string.Equals(SystemIdentifier, other.SystemIdentifier, StringComparison.Ordinal)
            && string.Equals(SerialNumber, other.SerialNumber, StringComparison.Ordinal)
            && string.Equals(VendorIdentifier, other.VendorIdentifier, StringComparison.Ordinal)
            && string.Equals(ProductIdentifier, other.ProductIdentifier, StringComparison.Ordinal)
            && IsConnected == other.IsConnected
            && ConnectionState == other.ConnectionState;
    }

    /// <summary>
    /// 等価性と整合するハッシュ値を返します。
    /// </summary>
    /// <remarks>責務: Service UUID一覧の要素を含めて <see cref="Equals(DiscoveredAdapter?)"/> と整合するハッシュ値を計算します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(TransportMode);
        hash.Add(ConnectionTransport);
        hash.Add(DisplayName, StringComparer.Ordinal);
        hash.Add(ManufacturerName, StringComparer.Ordinal);
        hash.Add(ProductName, StringComparer.Ordinal);
        hash.Add(AdvertisementLocalName, StringComparer.Ordinal);
        hash.Add(HasManufacturerData);
        foreach (var identifier in BluetoothServiceIdentifiers)
        {
            hash.Add(identifier, StringComparer.Ordinal);
        }

        hash.Add(SystemIdentifier, StringComparer.Ordinal);
        hash.Add(SerialNumber, StringComparer.Ordinal);
        hash.Add(VendorIdentifier, StringComparer.Ordinal);
        hash.Add(ProductIdentifier, StringComparer.Ordinal);
        hash.Add(IsConnected);
        hash.Add(ConnectionState);
        return hash.ToHashCode();
    }
}
