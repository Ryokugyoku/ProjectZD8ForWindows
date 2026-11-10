using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Policies;

/// <summary>
/// 取得manifestのexact、再decode承認、非互換、unknown判定を検証します。
/// </summary>
public sealed class AcquisitionManifestCompatibilityPolicyTests
{
    /// <summary>
    /// 同一の完全manifestをexact compatibleと判定します。
    /// </summary>
    [Fact]
    public void TestExactlyCompatible()
    {
        var manifest = MakeManifest();

        Assert.Equal<AcquisitionManifestCompatibility>(
            new AcquisitionManifestCompatibility.ExactlyCompatible(),
            Policy.Compatibility(acquired: manifest, candidate: manifest));
    }

    /// <summary>
    /// PID順序の差を非互換にします。
    /// </summary>
    [Fact]
    public void TestPIDOrderDifferenceIsIncompatible()
    {
        var acquired = MakeManifest(requests: new[] { FirstRequest, SecondRequest });
        var candidate = MakeManifest(requests: new[] { SecondRequest, FirstRequest });

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(AcquisitionManifestIncompatibilityReason.OrderedPIDSetMismatch),
            Policy.Compatibility(acquired: acquired, candidate: candidate));
    }

    /// <summary>
    /// PID definition revision差を非互換にします。
    /// </summary>
    [Fact]
    public void TestRevisionDifferenceIsIncompatible()
    {
        AssertDefinitionDifference(
            AcquisitionManifestIncompatibilityReason.DefinitionRevisionMismatch,
            MakeDefinition(revision: 2));
    }

    /// <summary>
    /// 必要byte数差を非互換にします。
    /// </summary>
    [Fact]
    public void TestRequiredByteCountDifferenceIsIncompatible()
    {
        AssertDefinitionDifference(
            AcquisitionManifestIncompatibilityReason.RequiredByteCountMismatch,
            MakeDefinition(requiredByteCount: 1));
    }

    /// <summary>
    /// formula identity差を非互換にします。
    /// </summary>
    [Fact]
    public void TestDefinitionIdentityDifferenceIsIncompatible()
    {
        var identity = new AcquisitionPIDDefinitionIdentity(canonicalizationVersion: 1, expression: "A * 256 + B");
        AssertDefinitionDifference(
            AcquisitionManifestIncompatibilityReason.DefinitionIdentityMismatch,
            MakeDefinition(identity: identity));
    }

    /// <summary>
    /// 単位差を非互換にします。
    /// </summary>
    [Fact]
    public void TestUnitDifferenceIsIncompatible()
    {
        AssertDefinitionDifference(
            AcquisitionManifestIncompatibilityReason.UnitMismatch,
            MakeDefinition(unit: "rps"));
    }

    /// <summary>
    /// 有効範囲差を非互換にします。
    /// </summary>
    [Fact]
    public void TestValidityRangeDifferenceIsIncompatible()
    {
        var range = AcquisitionPIDValidityRange.Inclusive(minimum: 0, maximum: 8_000);
        AssertDefinitionDifference(
            AcquisitionManifestIncompatibilityReason.ValidityRangeMismatch,
            MakeDefinition(validityRange: range));
    }

    /// <summary>
    /// manifest version差を非互換にします。
    /// </summary>
    [Fact]
    public void TestManifestVersionDifferenceIsIncompatible()
    {
        var versionPolicy = MakePolicy(manifestVersions: new long[] { 1, 2 });
        var result = versionPolicy.Compatibility(acquired: MakeManifest(), candidate: MakeManifest(manifestVersion: 2));

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(AcquisitionManifestIncompatibilityReason.ManifestVersionMismatch),
            result);
    }

    /// <summary>
    /// schema version差を非互換にします。
    /// </summary>
    [Fact]
    public void TestSchemaVersionDifferenceIsIncompatible()
    {
        var versionPolicy = MakePolicy(schemaVersions: new long[] { 1, 2 });
        var result = versionPolicy.Compatibility(acquired: MakeManifest(), candidate: MakeManifest(schemaVersion: 2));

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(AcquisitionManifestIncompatibilityReason.SchemaContractVersionMismatch),
            result);
    }

    /// <summary>
    /// polling policy version差を非互換にします。
    /// </summary>
    [Fact]
    public void TestPollingPolicyVersionDifferenceIsIncompatible()
    {
        var versionPolicy = MakePolicy(pollingVersions: new long[] { 1, 2 });
        var result = versionPolicy.Compatibility(acquired: MakeManifest(), candidate: MakeManifest(pollingVersion: 2));

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(AcquisitionManifestIncompatibilityReason.PollingPolicyVersionMismatch),
            result);
    }

    /// <summary>
    /// model入力version差を非互換にします。
    /// </summary>
    [Fact]
    public void TestModelInputVersionDifferenceIsIncompatible()
    {
        var versionPolicy = MakePolicy(modelVersions: new long[] { 1, 2 });
        var result = versionPolicy.Compatibility(acquired: MakeManifest(), candidate: MakeManifest(modelVersion: 2));

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(AcquisitionManifestIncompatibilityReason.ModelInputManifestVersionMismatch),
            result);
    }

    /// <summary>
    /// 取得時revision欠落をunknownにします。
    /// </summary>
    [Fact]
    public void TestLegacyDefinitionMetadataIsUnknown()
    {
        var legacy = MakeManifest(definitions: new[] { MakeDefinition(revision: null), MakeDefinition(request: SecondRequest) });

        Assert.Equal<AcquisitionManifestCompatibility>(
            Unknown(AcquisitionManifestUnknownReason.AcquisitionDefinitionEvidenceMissing),
            Policy.Compatibility(acquired: legacy, candidate: MakeManifest()));
    }

    /// <summary>
    /// 取得manifestがないlegacy sessionをunknownにします。
    /// </summary>
    [Fact]
    public void TestMissingAcquisitionManifestIsUnknown()
    {
        Assert.Equal<AcquisitionManifestCompatibility>(
            Unknown(AcquisitionManifestUnknownReason.ManifestMissing),
            Policy.Compatibility(acquired: null, candidate: MakeManifest()));
    }

    /// <summary>
    /// snapshot一覧にないPIDをunsupportedへ変換しません。
    /// </summary>
    [Fact]
    public void TestMissingPIDSnapshotIsUnknownRatherThanUnsupported()
    {
        var acquired = MakeManifest();
        var extraRequest = new OBDPIDRequest(Service: 1, Pid: 14);
        var candidate = MakeManifest(
            definitions: new[]
            {
                MakeDefinition(),
                MakeDefinition(request: SecondRequest),
                MakeDefinition(request: extraRequest),
            });

        Assert.Equal<AcquisitionManifestCompatibility>(
            Unknown(AcquisitionManifestUnknownReason.AcquisitionDefinitionEvidenceMissing),
            Policy.Compatibility(acquired: acquired, candidate: candidate));
    }

    /// <summary>
    /// reader未対応versionをunknownにします。
    /// </summary>
    [Fact]
    public void TestUnknownVersionIsUnknown()
    {
        var result = Policy.Compatibility(acquired: MakeManifest(), candidate: MakeManifest(manifestVersion: 99));

        Assert.Equal<AcquisitionManifestCompatibility>(
            Unknown(AcquisitionManifestUnknownReason.UnknownManifestVersion),
            result);
    }

    /// <summary>
    /// 明示承認なしのrevision差をapprovedへ昇格しません。
    /// </summary>
    [Fact]
    public void TestRedecodeRequiresExplicitApproval()
    {
        var acquired = MakeManifest();
        var candidateDefinition = MakeDefinition(revision: 2);
        var candidate = MakeManifest(definitions: new[] { candidateDefinition, MakeDefinition(request: SecondRequest) });

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(AcquisitionManifestIncompatibilityReason.DefinitionRevisionMismatch),
            Policy.Compatibility(acquired: acquired, candidate: candidate));
    }

    /// <summary>
    /// 明示承認済み再decodeでも元manifestを変更しません。
    /// </summary>
    [Fact]
    public void TestApprovedRedecodeDoesNotMutateAcquiredManifest()
    {
        var acquiredDefinition = MakeDefinition();
        var candidateDefinition = MakeDefinition(revision: 2);
        var acquired = MakeManifest(definitions: new[] { acquiredDefinition, MakeDefinition(request: SecondRequest) });
        var original = MakeManifest(definitions: new[] { acquiredDefinition, MakeDefinition(request: SecondRequest) });
        var candidate = MakeManifest(definitions: new[] { candidateDefinition, MakeDefinition(request: SecondRequest) });
        var approval = new ApprovedAcquisitionPIDRedecode(
            AcquiredDefinition: acquiredDefinition,
            ApprovedDefinition: candidateDefinition);

        Assert.Equal<AcquisitionManifestCompatibility>(
            new AcquisitionManifestCompatibility.ApprovedRedecodeCompatible(),
            Policy.Compatibility(acquired: acquired, candidate: candidate, approvals: new[] { approval }));
        Assert.Equal(original, acquired);
    }

    /// <summary>
    /// 比較に使う既知version policyです。
    /// </summary>
    private static AcquisitionManifestCompatibilityPolicy Policy => MakePolicy();

    /// <summary>
    /// 最初のsynthetic Service/PIDです。
    /// </summary>
    private static OBDPIDRequest FirstRequest => new(Service: 1, Pid: 12);

    /// <summary>
    /// 2番目のsynthetic Service/PIDです。
    /// </summary>
    private static OBDPIDRequest SecondRequest => new(Service: 1, Pid: 13);

    /// <summary>
    /// 単独reasonの非互換判定を生成します。
    /// </summary>
    /// <param name="reason">非互換reason。</param>
    /// <returns>reason 1件の非互換判定。</returns>
    private static AcquisitionManifestCompatibility Incompatible(AcquisitionManifestIncompatibilityReason reason) =>
        new AcquisitionManifestCompatibility.Incompatible(new[] { reason });

    /// <summary>
    /// 単独reasonのunknown判定を生成します。
    /// </summary>
    /// <param name="reason">unknown reason。</param>
    /// <returns>reason 1件のunknown判定。</returns>
    private static AcquisitionManifestCompatibility Unknown(AcquisitionManifestUnknownReason reason) =>
        new AcquisitionManifestCompatibility.Unknown(new[] { reason });

    /// <summary>
    /// 指定PID semantic差の単独reasonを検証します。
    /// </summary>
    /// <param name="reason">期待する単独の非互換reason。</param>
    /// <param name="candidate">比較側で差し替えるPID定義。</param>
    private static void AssertDefinitionDifference(
        AcquisitionManifestIncompatibilityReason reason,
        AcquisitionPIDDefinitionSnapshot candidate)
    {
        var acquired = MakeManifest();
        var compared = MakeManifest(definitions: new[] { candidate, MakeDefinition(request: SecondRequest) });

        Assert.Equal<AcquisitionManifestCompatibility>(
            Incompatible(reason),
            Policy.Compatibility(acquired: acquired, candidate: compared));
    }

    /// <summary>
    /// version authorityを差し替えたpolicyを生成します。
    /// </summary>
    /// <param name="manifestVersions">既知manifest version集合。</param>
    /// <param name="schemaVersions">既知schema contract version集合。</param>
    /// <param name="pollingVersions">既知polling policy version集合。</param>
    /// <param name="modelVersions">既知model input manifest version集合。</param>
    /// <returns>指定versionだけを既知とするpolicy。</returns>
    private static AcquisitionManifestCompatibilityPolicy MakePolicy(
        IEnumerable<long>? manifestVersions = null,
        IEnumerable<long>? schemaVersions = null,
        IEnumerable<long>? pollingVersions = null,
        IEnumerable<long>? modelVersions = null) =>
        new(
            new SupportedAcquisitionContractVersions(
                manifestVersions: manifestVersions ?? new long[] { 1 },
                schemaContractVersions: schemaVersions ?? new long[] { 1 },
                pollingPolicyVersions: pollingVersions ?? new long[] { 1 },
                modelInputManifestVersions: modelVersions ?? new long[] { 1 }));

    /// <summary>
    /// 完全または指定差分を持つmanifest fixtureを生成します。
    /// </summary>
    /// <param name="manifestVersion">manifest構造version。</param>
    /// <param name="schemaVersion">schema契約version。</param>
    /// <param name="pollingVersion">polling方針version。</param>
    /// <param name="modelVersion">model入力契約version。</param>
    /// <param name="requests">ordered PID集合。</param>
    /// <param name="definitions">PID定義snapshot一覧。省略時はordered setと同じ2件。</param>
    /// <returns>policy比較用のmanifest。</returns>
    private static ConnectionSessionAcquisitionManifest MakeManifest(
        int? manifestVersion = 1,
        int? schemaVersion = 1,
        int? pollingVersion = 1,
        int? modelVersion = 1,
        IReadOnlyList<OBDPIDRequest>? requests = null,
        IReadOnlyList<AcquisitionPIDDefinitionSnapshot>? definitions = null)
    {
        var orderedRequests = requests ?? new[] { FirstRequest, SecondRequest };
        var snapshots = definitions ?? new[] { MakeDefinition(), MakeDefinition(request: SecondRequest) };
        return new ConnectionSessionAcquisitionManifest(
            manifestVersion: manifestVersion,
            applicationVersion: new AcquisitionApplicationVersion(marketingVersion: "test-version", buildVersion: "test-build"),
            schemaContractVersion: schemaVersion,
            pollingPolicyVersion: pollingVersion,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(orderedRequests),
            pidDefinitions: snapshots,
            acquisitionPlatform: ConnectionSessionAcquisitionPlatform.IPhone,
            modelInputManifestVersion: modelVersion);
    }

    /// <summary>
    /// 完全または指定差分を持つPID定義fixtureを生成します。
    /// </summary>
    /// <param name="request">Service/PID。</param>
    /// <param name="revision">取得時revision。</param>
    /// <param name="requiredByteCount">必要byte数。</param>
    /// <param name="identity">完全な式identity。</param>
    /// <param name="unit">semantic unit。</param>
    /// <param name="validityRange">inclusive validity range。</param>
    /// <returns>policy比較用のPID定義snapshot。</returns>
    private static AcquisitionPIDDefinitionSnapshot MakeDefinition(
        OBDPIDRequest? request = null,
        int? revision = 1,
        int? requiredByteCount = 2,
        AcquisitionPIDDefinitionIdentity? identity = null,
        string? unit = "rpm",
        AcquisitionPIDValidityRange? validityRange = null) =>
        new(
            request: request ?? FirstRequest,
            capabilitySupport: AcquisitionPIDCapabilitySupport.Supported,
            isCollectionEnabled: true,
            definitionRevision: revision,
            requiredByteCount: requiredByteCount,
            definitionIdentity: identity ?? new AcquisitionPIDDefinitionIdentity(
                canonicalizationVersion: 1,
                expression: "(A * 256 + B) / 4"),
            unit: unit,
            validityRange: validityRange ?? AcquisitionPIDValidityRange.Inclusive(minimum: 0, maximum: 16_383.75));
}
