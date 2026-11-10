using System.Reflection;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Entities;

/// <summary>session取得manifestの生成時不変条件を検証します。</summary>
public sealed class ConnectionSessionAcquisitionManifestTests
{
    /// <summary>manifestのstored semantic fieldを取得証拠だけに制限します。</summary>
    /// <remarks>責務: VIN、account、VehicleID、端末名、adapter identity、Raw payload用fieldがmanifestに存在しないことを確認します。</remarks>
    [Fact]
    public void SemanticIdentityExcludesOperationalAndPersonalIdentifiers()
    {
        var request = new OBDPIDRequest(Service: 1, Pid: 12);
        var definition = new AcquisitionPIDDefinitionSnapshot(
            request: request,
            capabilitySupport: AcquisitionPIDCapabilitySupport.Indeterminate,
            isCollectionEnabled: false,
            definitionRevision: 1,
            requiredByteCount: 2,
            definitionIdentity: new AcquisitionPIDDefinitionIdentity(
                canonicalizationVersion: 1,
                expression: "(A * 256 + B) / 4"),
            unit: "rpm",
            validityRange: AcquisitionPIDValidityRange.Inclusive(minimum: 0, maximum: 16_383.75));
        var manifest = new ConnectionSessionAcquisitionManifest(
            manifestVersion: 1,
            applicationVersion: new AcquisitionApplicationVersion(
                marketingVersion: "fixture-version",
                buildVersion: "fixture-build"),
            schemaContractVersion: 1,
            pollingPolicyVersion: 1,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(requests: [request]),
            pidDefinitions: [definition],
            acquisitionPlatform: ConnectionSessionAcquisitionPlatform.MacOS,
            modelInputManifestVersion: 1);

        // Swift の Mirror に相当: public インスタンスプロパティを宣言順に列挙します。
        var names = manifest.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ManifestVersion",
                "ApplicationVersion",
                "SchemaContractVersion",
                "PollingPolicyVersion",
                "OrderedRequestedPIDs",
                "PidDefinitions",
                "AcquisitionPlatform",
                "ModelInputManifestVersion",
            },
            names);
    }

    /// <summary>要求PIDに対応するdefinitionがないmanifestを拒否します。</summary>
    /// <remarks>責務: 要求集合だけを保存して取得時definitionを欠落させられないことを確認します。</remarks>
    [Fact]
    public void RejectsMissingRequestedPIDDefinition()
    {
        var request = new OBDPIDRequest(Service: 1, Pid: 12);

        var error = Assert.Throws<ConnectionSessionAcquisitionManifestException>(() => new ConnectionSessionAcquisitionManifest(
            manifestVersion: 1,
            applicationVersion: new AcquisitionApplicationVersion(
                marketingVersion: "fixture-version",
                buildVersion: "fixture-build"),
            schemaContractVersion: 1,
            pollingPolicyVersion: 1,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(requests: [request]),
            pidDefinitions: [],
            acquisitionPlatform: ConnectionSessionAcquisitionPlatform.MacOS,
            modelInputManifestVersion: 1));
        Assert.Equal(ConnectionSessionAcquisitionManifestError.MissingRequestedPIDDefinition, error.Reason);
    }
}
