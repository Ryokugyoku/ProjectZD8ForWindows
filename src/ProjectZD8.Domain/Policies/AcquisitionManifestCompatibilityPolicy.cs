using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Policies;

/// <summary>互換性判定でreaderが理解できる契約version集合です。</summary>
public sealed record SupportedAcquisitionContractVersions
{
    /// <summary>対応するmanifest構造versionです。</summary>
    public IReadOnlySet<long> ManifestVersions { get; }

    /// <summary>対応する保存schema契約versionです。</summary>
    public IReadOnlySet<long> SchemaContractVersions { get; }

    /// <summary>対応するpolling方針versionです。</summary>
    public IReadOnlySet<long> PollingPolicyVersions { get; }

    /// <summary>対応するmodel入力契約versionです。</summary>
    public IReadOnlySet<long> ModelInputManifestVersions { get; }

    /// <summary>readerが明示対応するversion集合を生成します。</summary>
    /// <remarks>責務: 互換性policyが既知として扱える4種類の契約versionを固定します。</remarks>
    /// <param name="manifestVersions">対応するmanifest構造version。</param>
    /// <param name="schemaContractVersions">対応する保存schema契約version。</param>
    /// <param name="pollingPolicyVersions">対応するpolling方針version。</param>
    /// <param name="modelInputManifestVersions">対応するmodel入力契約version。</param>
    public SupportedAcquisitionContractVersions(
        IEnumerable<long> manifestVersions,
        IEnumerable<long> schemaContractVersions,
        IEnumerable<long> pollingPolicyVersions,
        IEnumerable<long> modelInputManifestVersions)
    {
        ManifestVersions = new HashSet<long>(manifestVersions);
        SchemaContractVersions = new HashSet<long>(schemaContractVersions);
        PollingPolicyVersions = new HashSet<long>(pollingPolicyVersions);
        ModelInputManifestVersions = new HashSet<long>(modelInputManifestVersions);
    }

    /// <summary>4種類のversion集合を集合として比較します。</summary>
    /// <remarks>責務: Swift の <c>Set</c> の等価性と同じく要素集合だけで値を比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>4種類すべての集合が一致する場合は <see langword="true"/>。</returns>
    public bool Equals(SupportedAcquisitionContractVersions? other) =>
        other is not null
        && ManifestVersions.SetEquals(other.ManifestVersions)
        && SchemaContractVersions.SetEquals(other.SchemaContractVersions)
        && PollingPolicyVersions.SetEquals(other.PollingPolicyVersions)
        && ModelInputManifestVersions.SetEquals(other.ModelInputManifestVersions);

    /// <summary>集合の要素順に依存しないハッシュ値を返します。</summary>
    /// <remarks>責務: <see cref="Equals(SupportedAcquisitionContractVersions?)"/> と整合するハッシュ値を計算します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(
            SetHash(ManifestVersions),
            SetHash(SchemaContractVersions),
            SetHash(PollingPolicyVersions),
            SetHash(ModelInputManifestVersions));

    /// <summary>集合の順序に依存しないハッシュ値を計算します。</summary>
    /// <remarks>責務: 要素ハッシュの排他的論理和で順序非依存の値を作ります。</remarks>
    /// <param name="values">対象集合。</param>
    /// <returns>順序非依存のハッシュ値。</returns>
    private static int SetHash(IReadOnlySet<long> values)
    {
        var hash = values.Count;
        foreach (var value in values)
        {
            hash ^= value.GetHashCode();
        }
        return hash;
    }
}

/// <summary>人間が明示承認した取得時定義から別定義への再decode契約です。</summary>
/// <remarks>責務: 1件のPIDについて方向付きの再decode承認範囲を完全な値の対へ固定します。</remarks>
/// <param name="AcquiredDefinition">変更しない取得時PID snapshot。</param>
/// <param name="ApprovedDefinition">再decodeで使用を承認されたPID snapshot。</param>
public sealed record ApprovedAcquisitionPIDRedecode(
    AcquisitionPIDDefinitionSnapshot AcquiredDefinition,
    AcquisitionPIDDefinitionSnapshot ApprovedDefinition);

/// <summary>manifestが互換ではない機械判定理由です。</summary>
public enum AcquisitionManifestIncompatibilityReason
{
    /// <summary>manifest構造versionが異なります。</summary>
    ManifestVersionMismatch,
    /// <summary>アプリまたはbuild versionが異なります。</summary>
    ApplicationVersionMismatch,
    /// <summary>保存schema契約versionが異なります。</summary>
    SchemaContractVersionMismatch,
    /// <summary>polling方針versionが異なります。</summary>
    PollingPolicyVersionMismatch,
    /// <summary>model入力契約versionが異なります。</summary>
    ModelInputManifestVersionMismatch,
    /// <summary>取得platformが異なります。</summary>
    AcquisitionPlatformMismatch,
    /// <summary>ordered PID集合の要素または順序が異なります。</summary>
    OrderedPIDSetMismatch,
    /// <summary>PID対応状態または収集選択が異なります。</summary>
    PidCapabilityMismatch,
    /// <summary>PID定義revisionが異なります。</summary>
    DefinitionRevisionMismatch,
    /// <summary>必要byte数が異なります。</summary>
    RequiredByteCountMismatch,
    /// <summary>式またはcanonicalization契約が異なります。</summary>
    DefinitionIdentityMismatch,
    /// <summary>単位が異なります。</summary>
    UnitMismatch,
    /// <summary>有効範囲が異なります。</summary>
    ValidityRangeMismatch,
}

/// <summary><see cref="AcquisitionManifestIncompatibilityReason"/> と Swift の raw value の相互変換です。</summary>
public static class AcquisitionManifestIncompatibilityReasonRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件の理由を永続化・比較用の安定文字列へ写像します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift のケース名と同じ raw value。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this AcquisitionManifestIncompatibilityReason value) => value switch
    {
        AcquisitionManifestIncompatibilityReason.ManifestVersionMismatch => "manifestVersionMismatch",
        AcquisitionManifestIncompatibilityReason.ApplicationVersionMismatch => "applicationVersionMismatch",
        AcquisitionManifestIncompatibilityReason.SchemaContractVersionMismatch => "schemaContractVersionMismatch",
        AcquisitionManifestIncompatibilityReason.PollingPolicyVersionMismatch => "pollingPolicyVersionMismatch",
        AcquisitionManifestIncompatibilityReason.ModelInputManifestVersionMismatch => "modelInputManifestVersionMismatch",
        AcquisitionManifestIncompatibilityReason.AcquisitionPlatformMismatch => "acquisitionPlatformMismatch",
        AcquisitionManifestIncompatibilityReason.OrderedPIDSetMismatch => "orderedPIDSetMismatch",
        AcquisitionManifestIncompatibilityReason.PidCapabilityMismatch => "pidCapabilityMismatch",
        AcquisitionManifestIncompatibilityReason.DefinitionRevisionMismatch => "definitionRevisionMismatch",
        AcquisitionManifestIncompatibilityReason.RequiredByteCountMismatch => "requiredByteCountMismatch",
        AcquisitionManifestIncompatibilityReason.DefinitionIdentityMismatch => "definitionIdentityMismatch",
        AcquisitionManifestIncompatibilityReason.UnitMismatch => "unitMismatch",
        AcquisitionManifestIncompatibilityReason.ValidityRangeMismatch => "validityRangeMismatch",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 完全一致（Ordinal）する raw value だけをケースへ変換します。</remarks>
    /// <param name="rawValue">復元する raw value。</param>
    /// <param name="value">復元したケース。失敗時は既定値。</param>
    /// <returns>一致するケースがあった場合は <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionManifestIncompatibilityReason value)
    {
        foreach (var candidate in Enum.GetValues<AcquisitionManifestIncompatibilityReason>())
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }
}

/// <summary>manifest互換性を証拠不足で決められない機械判定理由です。</summary>
public enum AcquisitionManifestUnknownReason
{
    /// <summary>取得manifest自体がありません。</summary>
    ManifestMissing,
    /// <summary>manifest構造versionが欠落しています。</summary>
    LegacyManifestVersionMissing,
    /// <summary>アプリまたはbuild versionが欠落しています。</summary>
    LegacyApplicationVersionMissing,
    /// <summary>保存schema契約versionが欠落しています。</summary>
    LegacySchemaContractVersionMissing,
    /// <summary>polling方針versionが欠落しています。</summary>
    LegacyPollingPolicyVersionMissing,
    /// <summary>ordered PID集合が欠落しています。</summary>
    LegacyRequestedPIDSetMissing,
    /// <summary>取得platformが欠落しています。</summary>
    LegacyAcquisitionPlatformMissing,
    /// <summary>model入力契約versionが欠落しています。</summary>
    LegacyModelInputManifestVersionMissing,
    /// <summary>PID能力または収集選択の証拠が欠落しています。</summary>
    LegacyCapabilityEvidenceMissing,
    /// <summary>取得時PID定義のsemantic metadataが欠落しています。</summary>
    AcquisitionDefinitionEvidenceMissing,
    /// <summary>manifest構造versionをreaderが理解できません。</summary>
    UnknownManifestVersion,
    /// <summary>保存schema契約versionをreaderが理解できません。</summary>
    UnknownSchemaContractVersion,
    /// <summary>polling方針versionをreaderが理解できません。</summary>
    UnknownPollingPolicyVersion,
    /// <summary>model入力契約versionをreaderが理解できません。</summary>
    UnknownModelInputManifestVersion,
}

/// <summary><see cref="AcquisitionManifestUnknownReason"/> と Swift の raw value の相互変換です。</summary>
public static class AcquisitionManifestUnknownReasonRawValue
{
    /// <summary>ケースを Swift と同じ raw value 文字列へ変換します。</summary>
    /// <remarks>責務: 1件の理由を永続化・比較用の安定文字列へ写像します。</remarks>
    /// <param name="value">変換するケース。</param>
    /// <returns>Swift のケース名と同じ raw value。</returns>
    /// <exception cref="ArgumentOutOfRangeException">未定義の値の場合。</exception>
    public static string ToRawValue(this AcquisitionManifestUnknownReason value) => value switch
    {
        AcquisitionManifestUnknownReason.ManifestMissing => "manifestMissing",
        AcquisitionManifestUnknownReason.LegacyManifestVersionMissing => "legacyManifestVersionMissing",
        AcquisitionManifestUnknownReason.LegacyApplicationVersionMissing => "legacyApplicationVersionMissing",
        AcquisitionManifestUnknownReason.LegacySchemaContractVersionMissing => "legacySchemaContractVersionMissing",
        AcquisitionManifestUnknownReason.LegacyPollingPolicyVersionMissing => "legacyPollingPolicyVersionMissing",
        AcquisitionManifestUnknownReason.LegacyRequestedPIDSetMissing => "legacyRequestedPIDSetMissing",
        AcquisitionManifestUnknownReason.LegacyAcquisitionPlatformMissing => "legacyAcquisitionPlatformMissing",
        AcquisitionManifestUnknownReason.LegacyModelInputManifestVersionMissing => "legacyModelInputManifestVersionMissing",
        AcquisitionManifestUnknownReason.LegacyCapabilityEvidenceMissing => "legacyCapabilityEvidenceMissing",
        AcquisitionManifestUnknownReason.AcquisitionDefinitionEvidenceMissing => "acquisitionDefinitionEvidenceMissing",
        AcquisitionManifestUnknownReason.UnknownManifestVersion => "unknownManifestVersion",
        AcquisitionManifestUnknownReason.UnknownSchemaContractVersion => "unknownSchemaContractVersion",
        AcquisitionManifestUnknownReason.UnknownPollingPolicyVersion => "unknownPollingPolicyVersion",
        AcquisitionManifestUnknownReason.UnknownModelInputManifestVersion => "unknownModelInputManifestVersion",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Swift の raw value 文字列からケースを復元します。</summary>
    /// <remarks>責務: 完全一致（Ordinal）する raw value だけをケースへ変換します。</remarks>
    /// <param name="rawValue">復元する raw value。</param>
    /// <param name="value">復元したケース。失敗時は既定値。</param>
    /// <returns>一致するケースがあった場合は <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionManifestUnknownReason value)
    {
        foreach (var candidate in Enum.GetValues<AcquisitionManifestUnknownReason>())
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }
}

/// <summary>2件の取得manifestに対する排他的互換性結果です。</summary>
public abstract record AcquisitionManifestCompatibility
{
    /// <summary>外部からケースを追加できないよう派生を同じ型の中だけに限定します。</summary>
    /// <remarks>責務: Swift の閉じた enum と同じくケース集合を固定します。</remarks>
    private AcquisitionManifestCompatibility()
    {
    }

    /// <summary>全ての比較対象契約が一致します。</summary>
    public sealed record ExactlyCompatible : AcquisitionManifestCompatibility;

    /// <summary>差のある全PID定義が明示承認済み再decode契約に一致します。</summary>
    public sealed record ApprovedRedecodeCompatible : AcquisitionManifestCompatibility;

    /// <summary>証拠は揃っていますが互換でない理由があります。</summary>
    /// <param name="Reasons">非互換の理由一覧。</param>
    public sealed record Incompatible(IReadOnlyList<AcquisitionManifestIncompatibilityReason> Reasons)
        : AcquisitionManifestCompatibility
    {
        /// <summary>理由一覧を要素と順序で比較します。</summary>
        /// <remarks>責務: Swift の配列等価性と同じく要素列で値を比較します。</remarks>
        /// <param name="other">比較対象。</param>
        /// <returns>理由一覧が順序どおり一致する場合は <see langword="true"/>。</returns>
        public bool Equals(Incompatible? other) =>
            other is not null && Reasons.SequenceEqual(other.Reasons);

        /// <summary>理由一覧の要素列からハッシュ値を返します。</summary>
        /// <remarks>責務: <see cref="Equals(Incompatible?)"/> と整合するハッシュ値を計算します。</remarks>
        /// <returns>ハッシュ値。</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var reason in Reasons)
            {
                hash.Add(reason);
            }
            return hash.ToHashCode();
        }
    }

    /// <summary>legacy欠落、未知version、または証拠不足により判定できません。</summary>
    /// <param name="Reasons">判定できない理由一覧。</param>
    public sealed record Unknown(IReadOnlyList<AcquisitionManifestUnknownReason> Reasons)
        : AcquisitionManifestCompatibility
    {
        /// <summary>理由一覧を要素と順序で比較します。</summary>
        /// <remarks>責務: Swift の配列等価性と同じく要素列で値を比較します。</remarks>
        /// <param name="other">比較対象。</param>
        /// <returns>理由一覧が順序どおり一致する場合は <see langword="true"/>。</returns>
        public bool Equals(Unknown? other) =>
            other is not null && Reasons.SequenceEqual(other.Reasons);

        /// <summary>理由一覧の要素列からハッシュ値を返します。</summary>
        /// <remarks>責務: <see cref="Equals(Unknown?)"/> と整合するハッシュ値を計算します。</remarks>
        /// <returns>ハッシュ値。</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var reason in Reasons)
            {
                hash.Add(reason);
            }
            return hash.ToHashCode();
        }
    }
}

/// <summary>取得manifest同士を明示versionとsemantic evidenceで比較します。</summary>
public sealed class AcquisitionManifestCompatibilityPolicy
{
    /// <summary>readerが明示対応するversion集合です。</summary>
    private readonly SupportedAcquisitionContractVersions _supportedVersions;

    /// <summary>対応version集合を注入してpolicyを生成します。</summary>
    /// <remarks>責務: manifest互換性判定で既知とする契約versionのauthorityを固定します。</remarks>
    /// <param name="supportedVersions">readerが明示対応する4種類のversion集合。</param>
    public AcquisitionManifestCompatibilityPolicy(SupportedAcquisitionContractVersions supportedVersions)
    {
        _supportedVersions = supportedVersions;
    }

    /// <summary>取得時manifestと比較対象manifestの互換性を判定します。</summary>
    /// <remarks>責務: 欠落、未知version、完全一致、明示承認済み再decode、非互換を既定化せず排他的に返します。</remarks>
    /// <param name="acquired">変更してはならない取得時manifest。存在しないlegacy sessionは <see langword="null"/>。</param>
    /// <param name="candidate">比較するreaderまたは再decode側のmanifest。存在しない場合は <see langword="null"/>。</param>
    /// <param name="approvals">方向付きの明示承認済みPID再decode契約。省略時は空。</param>
    /// <returns>識別情報を含まないreasonを伴う排他的互換性結果。</returns>
    public AcquisitionManifestCompatibility Compatibility(
        ConnectionSessionAcquisitionManifest? acquired,
        ConnectionSessionAcquisitionManifest? candidate,
        IReadOnlyList<ApprovedAcquisitionPIDRedecode>? approvals = null)
    {
        approvals ??= Array.Empty<ApprovedAcquisitionPIDRedecode>();
        if (acquired is null || candidate is null)
        {
            return new AcquisitionManifestCompatibility.Unknown(
                new[] { AcquisitionManifestUnknownReason.ManifestMissing });
        }

        var unknownReasons = UnknownReasons(new[] { acquired, candidate });
        if (unknownReasons.Count != 0)
        {
            return new AcquisitionManifestCompatibility.Unknown(unknownReasons);
        }

        var incompatibilities = ManifestIncompatibilities(acquired, candidate);
        if (incompatibilities.Count != 0)
        {
            return new AcquisitionManifestCompatibility.Incompatible(incompatibilities);
        }

        // Swift の Dictionary(uniqueKeysWithValues:) と同じく重複キーは不正（manifest の生成時に排除済み）。
        var acquiredDefinitions = acquired.PidDefinitions.ToDictionary(d => d.Request);
        var candidateDefinitions = candidate.PidDefinitions.ToDictionary(d => d.Request);
        if (!new HashSet<OBDPIDRequest>(acquiredDefinitions.Keys).SetEquals(candidateDefinitions.Keys))
        {
            return new AcquisitionManifestCompatibility.Unknown(
                new[] { AcquisitionManifestUnknownReason.AcquisitionDefinitionEvidenceMissing });
        }
        var requiresApprovedRedecode = false;

        foreach (var request in acquired.PidDefinitions.Select(d => d.Request))
        {
            if (!acquiredDefinitions.TryGetValue(request, out var acquiredDefinition)
                || !candidateDefinitions.TryGetValue(request, out var candidateDefinition))
            {
                return new AcquisitionManifestCompatibility.Unknown(
                    new[] { AcquisitionManifestUnknownReason.AcquisitionDefinitionEvidenceMissing });
            }
            if (!Equals(acquiredDefinition.CapabilitySupport, candidateDefinition.CapabilitySupport)
                || !Equals(acquiredDefinition.IsCollectionEnabled, candidateDefinition.IsCollectionEnabled))
            {
                incompatibilities.Add(AcquisitionManifestIncompatibilityReason.PidCapabilityMismatch);
            }

            var semanticReasons = DefinitionIncompatibilities(acquiredDefinition, candidateDefinition);
            if (semanticReasons.Count != 0)
            {
                if (approvals.Any(approval =>
                        Equals(approval.AcquiredDefinition, acquiredDefinition)
                        && Equals(approval.ApprovedDefinition, candidateDefinition)))
                {
                    requiresApprovedRedecode = true;
                }
                else
                {
                    incompatibilities.AddRange(semanticReasons);
                }
            }
        }

        var reasons = Unique(incompatibilities);
        if (reasons.Count != 0)
        {
            return new AcquisitionManifestCompatibility.Incompatible(reasons);
        }
        return requiresApprovedRedecode
            ? new AcquisitionManifestCompatibility.ApprovedRedecodeCompatible()
            : new AcquisitionManifestCompatibility.ExactlyCompatible();
    }

    /// <summary>legacy欠落とreader未対応versionを列挙します。</summary>
    /// <remarks>責務: 互換性比較より前に判定をunknownへ止める証拠不足を重複なく抽出します。</remarks>
    /// <param name="manifests">取得側と比較側のmanifest。</param>
    /// <returns>識別情報を含まないunknown reason一覧。</returns>
    private List<AcquisitionManifestUnknownReason> UnknownReasons(
        IReadOnlyList<ConnectionSessionAcquisitionManifest> manifests)
    {
        var reasons = new List<AcquisitionManifestUnknownReason>();
        foreach (var manifest in manifests)
        {
            if (manifest.ManifestVersion is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacyManifestVersionMissing); }
            if (manifest.ApplicationVersion is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacyApplicationVersionMissing); }
            if (manifest.SchemaContractVersion is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacySchemaContractVersionMissing); }
            if (manifest.PollingPolicyVersion is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacyPollingPolicyVersionMissing); }
            if (manifest.OrderedRequestedPIDs is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacyRequestedPIDSetMissing); }
            if (manifest.AcquisitionPlatform is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacyAcquisitionPlatformMissing); }
            if (manifest.ModelInputManifestVersion is null) { reasons.Add(AcquisitionManifestUnknownReason.LegacyModelInputManifestVersionMissing); }
            if (manifest.PidDefinitions.Any(d => d.CapabilitySupport is null || d.IsCollectionEnabled is null))
            {
                reasons.Add(AcquisitionManifestUnknownReason.LegacyCapabilityEvidenceMissing);
            }
            if (manifest.PidDefinitions.Any(d =>
                    d.DefinitionRevision is null || d.RequiredByteCount is null || d.DefinitionIdentity is null
                    || d.Unit is null || d.ValidityRange is null))
            {
                reasons.Add(AcquisitionManifestUnknownReason.AcquisitionDefinitionEvidenceMissing);
            }
            if (manifest.ManifestVersion is { } manifestVersion
                && !_supportedVersions.ManifestVersions.Contains(manifestVersion))
            {
                reasons.Add(AcquisitionManifestUnknownReason.UnknownManifestVersion);
            }
            if (manifest.SchemaContractVersion is { } schemaContractVersion
                && !_supportedVersions.SchemaContractVersions.Contains(schemaContractVersion))
            {
                reasons.Add(AcquisitionManifestUnknownReason.UnknownSchemaContractVersion);
            }
            if (manifest.PollingPolicyVersion is { } pollingPolicyVersion
                && !_supportedVersions.PollingPolicyVersions.Contains(pollingPolicyVersion))
            {
                reasons.Add(AcquisitionManifestUnknownReason.UnknownPollingPolicyVersion);
            }
            if (manifest.ModelInputManifestVersion is { } modelInputManifestVersion
                && !_supportedVersions.ModelInputManifestVersions.Contains(modelInputManifestVersion))
            {
                reasons.Add(AcquisitionManifestUnknownReason.UnknownModelInputManifestVersion);
            }
        }
        return Unique(reasons);
    }

    /// <summary>session単位の既知metadata差を列挙します。</summary>
    /// <remarks>責務: PID定義以外のmanifest契約差を機械判定reasonへ変換します。</remarks>
    /// <param name="acquired">取得時manifest。</param>
    /// <param name="candidate">比較対象manifest。</param>
    /// <returns>session単位の非互換reason一覧。</returns>
    private static List<AcquisitionManifestIncompatibilityReason> ManifestIncompatibilities(
        ConnectionSessionAcquisitionManifest acquired,
        ConnectionSessionAcquisitionManifest candidate)
    {
        var reasons = new List<AcquisitionManifestIncompatibilityReason>();
        if (!Equals(acquired.ManifestVersion, candidate.ManifestVersion)) { reasons.Add(AcquisitionManifestIncompatibilityReason.ManifestVersionMismatch); }
        if (!Equals(acquired.ApplicationVersion, candidate.ApplicationVersion)) { reasons.Add(AcquisitionManifestIncompatibilityReason.ApplicationVersionMismatch); }
        if (!Equals(acquired.SchemaContractVersion, candidate.SchemaContractVersion)) { reasons.Add(AcquisitionManifestIncompatibilityReason.SchemaContractVersionMismatch); }
        if (!Equals(acquired.PollingPolicyVersion, candidate.PollingPolicyVersion)) { reasons.Add(AcquisitionManifestIncompatibilityReason.PollingPolicyVersionMismatch); }
        if (!Equals(acquired.ModelInputManifestVersion, candidate.ModelInputManifestVersion))
        {
            reasons.Add(AcquisitionManifestIncompatibilityReason.ModelInputManifestVersionMismatch);
        }
        if (!Equals(acquired.AcquisitionPlatform, candidate.AcquisitionPlatform)) { reasons.Add(AcquisitionManifestIncompatibilityReason.AcquisitionPlatformMismatch); }
        if (!Equals(acquired.OrderedRequestedPIDs, candidate.OrderedRequestedPIDs)) { reasons.Add(AcquisitionManifestIncompatibilityReason.OrderedPIDSetMismatch); }
        return reasons;
    }

    /// <summary>1件のPID定義のsemantic差を列挙します。</summary>
    /// <remarks>責務: revisionだけに依存せずbyte数、式identity、単位、有効範囲の差を個別reasonへ変換します。</remarks>
    /// <param name="acquired">取得時PID定義snapshot。</param>
    /// <param name="candidate">比較対象PID定義snapshot。</param>
    /// <returns>PID定義の非互換reason一覧。</returns>
    private static List<AcquisitionManifestIncompatibilityReason> DefinitionIncompatibilities(
        AcquisitionPIDDefinitionSnapshot acquired,
        AcquisitionPIDDefinitionSnapshot candidate)
    {
        var reasons = new List<AcquisitionManifestIncompatibilityReason>();
        if (!Equals(acquired.DefinitionRevision, candidate.DefinitionRevision)) { reasons.Add(AcquisitionManifestIncompatibilityReason.DefinitionRevisionMismatch); }
        if (!Equals(acquired.RequiredByteCount, candidate.RequiredByteCount)) { reasons.Add(AcquisitionManifestIncompatibilityReason.RequiredByteCountMismatch); }
        if (!Equals(acquired.DefinitionIdentity, candidate.DefinitionIdentity)) { reasons.Add(AcquisitionManifestIncompatibilityReason.DefinitionIdentityMismatch); }
        if (!CanonicallyEquals(acquired.Unit, candidate.Unit)) { reasons.Add(AcquisitionManifestIncompatibilityReason.UnitMismatch); }
        if (!Equals(acquired.ValidityRange, candidate.ValidityRange)) { reasons.Add(AcquisitionManifestIncompatibilityReason.ValidityRangeMismatch); }
        return reasons;
    }

    /// <summary>2件の任意文字列をSwiftの <c>String</c> 等価性（Unicode正準等価）で比較します。</summary>
    /// <remarks>責務: 合成済み文字と結合文字列の表記差を同一単位として扱うSwiftの比較結果を再現します。</remarks>
    /// <param name="left">比較する文字列。</param>
    /// <param name="right">比較する文字列。</param>
    /// <returns>両方 <see langword="null"/>、または正準等価な場合は <see langword="true"/>。</returns>
    private static bool CanonicallyEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }
        try
        {
            return string.Equals(
                left.Normalize(System.Text.NormalizationForm.FormD),
                right.Normalize(System.Text.NormalizationForm.FormD),
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // 不正なサロゲートを含む文字列は Swift の String に存在しないため、Ordinal 比較の結果を採用する。
            return false;
        }
    }

    /// <summary>reasonの最初の出現順を保持して重複を除きます。</summary>
    /// <remarks>責務: 同じ機械判定reasonを結果へ一度だけ保持します。</remarks>
    /// <typeparam name="T">reasonの型。</typeparam>
    /// <param name="values">重複を含み得るreason一覧。</param>
    /// <returns>最初の出現順を保持したreason一覧。</returns>
    private static List<T> Unique<T>(IEnumerable<T> values)
    {
        var seen = new HashSet<T>();
        var result = new List<T>();
        foreach (var value in values)
        {
            if (seen.Add(value))
            {
                result.Add(value);
            }
        }
        return result;
    }
}
