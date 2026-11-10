namespace ProjectZD8ForWindows.AppRoot.Composition;

/// <summary>
/// 本番の取得証拠へ適用する、明示的な契約の版の組です（iOS 版の同名の型）。
/// </summary>
/// <param name="ManifestVersion">マニフェスト構造の版。</param>
/// <param name="SchemaContractVersion">保存形式の契約の版。</param>
/// <param name="PollingPolicyVersion">ポーリング方針の契約の版。</param>
/// <param name="ModelInputManifestVersion">モデル入力変換の契約の版。</param>
/// <param name="FormulaCanonicalizationVersion">計算式の正規化の契約の版。</param>
public sealed record ProductionConnectionSessionAcquisitionConfiguration(
    int ManifestVersion,
    int SchemaContractVersion,
    int PollingPolicyVersion,
    int ModelInputManifestVersion,
    int FormulaCanonicalizationVersion)
{
    /// <summary>Phase 4G で承認された最初の本番契約です。iOS 版と同じ値です。</summary>
    public static ProductionConnectionSessionAcquisitionConfiguration Phase4G { get; } = new(
        ManifestVersion: 1,
        SchemaContractVersion: 1,
        PollingPolicyVersion: 1,
        ModelInputManifestVersion: 1,
        FormulaCanonicalizationVersion: 1);
}
