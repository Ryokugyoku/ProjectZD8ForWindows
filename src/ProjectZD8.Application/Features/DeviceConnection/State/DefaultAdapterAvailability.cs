using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.State;

/// <summary>保存済みデフォルトアダプターの現在の接続可否を表します。</summary>
public abstract record DefaultAdapterAvailability
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: 接続可否の種類を下の3ケースに限定します。</remarks>
    private DefaultAdapterAvailability()
    {
    }

    /// <summary>デフォルトアダプターが設定されていません。</summary>
    public sealed record NotConfigured : DefaultAdapterAvailability;

    /// <summary>保存済みデフォルトアダプターが最新探索で検出されていません。</summary>
    /// <param name="DisplayName">HOMEに表示するデフォルトアダプター名称。</param>
    public sealed record NotDetected(string DisplayName) : DefaultAdapterAvailability
    {
        /// <summary>HOMEに表示するデフォルトアダプター名称です。</summary>
        public new string DisplayName { get; init; } = DisplayName;
    }

    /// <summary>保存済みデフォルトアダプターが最新探索で検出され、接続終端を利用できます。</summary>
    /// <param name="DisplayName">HOMEに表示するデフォルトアダプター名称。</param>
    /// <param name="Endpoint">最新探索で検出できた接続終端。</param>
    public sealed record Detected(string DisplayName, OBDConnectionEndpoint Endpoint) : DefaultAdapterAvailability
    {
        /// <summary>HOMEに表示するデフォルトアダプター名称です。</summary>
        public new string DisplayName { get; init; } = DisplayName;
    }

    /// <summary>デフォルトアダプターが保存済みかどうかです。</summary>
    public bool HasDefaultAdapter => this is not NotConfigured;

    /// <summary>HOMEに表示するデフォルトアダプター名称です。</summary>
    public string? DisplayName => this switch
    {
        NotDetected notDetected => notDetected.DisplayName,
        Detected detected => detected.DisplayName,
        _ => null,
    };

    /// <summary>保存済みデフォルトアダプターを最新探索で検出できたかどうかです。</summary>
    public bool IsDetected => this is Detected;

    /// <summary>最新探索で検出できたデフォルトアダプターの接続終端です。</summary>
    public OBDConnectionEndpoint? ConnectionEndpoint => (this as Detected)?.Endpoint;

    /// <summary>保存設定と最新探索候補から現在の接続可否を生成します。</summary>
    /// <remarks>責務: 保存済みデフォルトと一致する最新候補だけを接続可能状態へ変換します。</remarks>
    /// <param name="preference">保存済みデフォルトアダプター設定。</param>
    /// <param name="detectedAdapter">最新探索でプライマリーとして照合された候補。</param>
    /// <returns>現在の接続可否。</returns>
    public static DefaultAdapterAvailability Create(
        DefaultAdapterPreference? preference,
        DiscoveredAdapter? detectedAdapter)
    {
        if (preference is null)
        {
            return new NotConfigured();
        }

        if (detectedAdapter is null || !preference.Matches(detectedAdapter))
        {
            return new NotDetected(preference.DisplayName);
        }

        return new Detected(preference.DisplayName, new OBDConnectionEndpoint(detectedAdapter));
    }
}
