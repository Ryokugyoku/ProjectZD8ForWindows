using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>取得を生成したアプリのversionを表します。</summary>
public sealed record AcquisitionApplicationVersion
{
    /// <summary>ユーザー向けmarketing versionです。</summary>
    public string MarketingVersion { get; }
    /// <summary>配布buildを識別するbuild versionです。</summary>
    public string BuildVersion { get; }

    /// <summary>アプリとbuildのversionを生成します。</summary>
    /// <remarks>責務: 取得runtimeのアプリversionをmarketing値とbuild値の組へ固定します。</remarks>
    /// <param name="marketingVersion">空でないユーザー向けversion。</param>
    /// <param name="buildVersion">空でない配布build version。</param>
    /// <exception cref="ConnectionSessionAcquisitionManifestException">いずれかが空の場合（<see cref="ConnectionSessionAcquisitionManifestError.InvalidApplicationVersion"/>）。</exception>
    public AcquisitionApplicationVersion(string marketingVersion, string buildVersion)
    {
        if (marketingVersion.Length == 0 || buildVersion.Length == 0)
        {
            throw new ConnectionSessionAcquisitionManifestException(ConnectionSessionAcquisitionManifestError.InvalidApplicationVersion);
        }
        MarketingVersion = marketingVersion;
        BuildVersion = buildVersion;
    }
}

/// <summary>1件のsessionでRawを生成した取得条件の不変証拠です。</summary>
public sealed record ConnectionSessionAcquisitionManifest
{
    /// <summary>manifest構造のversionです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public int? ManifestVersion { get; }
    /// <summary>取得runtimeのアプリversionです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public AcquisitionApplicationVersion? ApplicationVersion { get; }
    /// <summary>保存schema契約のversionです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public int? SchemaContractVersion { get; }
    /// <summary>polling方針のversionです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public int? PollingPolicyVersion { get; }
    /// <summary>順序付き要求PID集合です。<see langword="null"/> はlegacy欠落を表します。</summary>
    public OrderedAcquisitionPIDSet? OrderedRequestedPIDs { get; }
    /// <summary>取得時点のPID定義と能力選択snapshotです。</summary>
    public IReadOnlyList<AcquisitionPIDDefinitionSnapshot> PidDefinitions { get; }
    /// <summary>端末名を含まない取得platformです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public ConnectionSessionAcquisitionPlatform? AcquisitionPlatform { get; }
    /// <summary>model入力変換契約のversionです。<see langword="null"/> はlegacy欠落を表します。</summary>
    public int? ModelInputManifestVersion { get; }

    /// <summary>session単位の取得manifestを生成します。</summary>
    /// <remarks>責務: session識別情報を含めずに取得条件とPID証拠を不変manifestへ固定します。</remarks>
    /// <param name="manifestVersion">正のmanifest構造version、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="applicationVersion">取得runtime version、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="schemaContractVersion">正のschema契約version、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="pollingPolicyVersion">正のpolling方針version、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="orderedRequestedPIDs">重複のない要求PID順、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="pidDefinitions">取得時点のPID定義と能力選択snapshot。</param>
    /// <param name="acquisitionPlatform">端末名を含まないplatform、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <param name="modelInputManifestVersion">正のmodel入力契約version、またはlegacy欠落を示す <see langword="null"/>。</param>
    /// <exception cref="ConnectionSessionAcquisitionManifestException">versionが正でない、PID snapshotが重複する、または要求PIDのsnapshotがない場合。</exception>
    public ConnectionSessionAcquisitionManifest(
        int? manifestVersion,
        AcquisitionApplicationVersion? applicationVersion,
        int? schemaContractVersion,
        int? pollingPolicyVersion,
        OrderedAcquisitionPIDSet? orderedRequestedPIDs,
        IReadOnlyList<AcquisitionPIDDefinitionSnapshot> pidDefinitions,
        ConnectionSessionAcquisitionPlatform? acquisitionPlatform,
        int? modelInputManifestVersion)
    {
        int?[] versions = [manifestVersion, schemaContractVersion, pollingPolicyVersion, modelInputManifestVersion];
        if (!versions.Where(version => version is not null).All(version => version > 0))
        {
            throw new ConnectionSessionAcquisitionManifestException(ConnectionSessionAcquisitionManifestError.InvalidContractVersion);
        }
        var copied = pidDefinitions.ToArray();
        var definitionRequests = new HashSet<OBDPIDRequest>(copied.Select(definition => definition.Request));
        if (definitionRequests.Count != copied.Length)
        {
            throw new ConnectionSessionAcquisitionManifestException(ConnectionSessionAcquisitionManifestError.DuplicatePIDDefinition);
        }
        if (orderedRequestedPIDs is { } ordered && !ordered.Requests.All(definitionRequests.Contains))
        {
            throw new ConnectionSessionAcquisitionManifestException(ConnectionSessionAcquisitionManifestError.MissingRequestedPIDDefinition);
        }
        ManifestVersion = manifestVersion;
        ApplicationVersion = applicationVersion;
        SchemaContractVersion = schemaContractVersion;
        PollingPolicyVersion = pollingPolicyVersion;
        OrderedRequestedPIDs = orderedRequestedPIDs;
        PidDefinitions = copied;
        AcquisitionPlatform = acquisitionPlatform;
        ModelInputManifestVersion = modelInputManifestVersion;
    }

    /// <summary>PID snapshot列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じくPID snapshot列を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(ConnectionSessionAcquisitionManifest? other) =>
        other is not null
        && ManifestVersion == other.ManifestVersion
        && EqualityComparer<AcquisitionApplicationVersion?>.Default.Equals(ApplicationVersion, other.ApplicationVersion)
        && SchemaContractVersion == other.SchemaContractVersion
        && PollingPolicyVersion == other.PollingPolicyVersion
        && EqualityComparer<OrderedAcquisitionPIDSet?>.Default.Equals(OrderedRequestedPIDs, other.OrderedRequestedPIDs)
        && PidDefinitions.SequenceEqual(other.PidDefinitions)
        && AcquisitionPlatform == other.AcquisitionPlatform
        && ModelInputManifestVersion == other.ModelInputManifestVersion;

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: PID snapshot列の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ManifestVersion);
        hash.Add(ApplicationVersion);
        hash.Add(SchemaContractVersion);
        hash.Add(PollingPolicyVersion);
        hash.Add(OrderedRequestedPIDs);
        foreach (var definition in PidDefinitions)
        {
            hash.Add(definition);
        }
        hash.Add(AcquisitionPlatform);
        hash.Add(ModelInputManifestVersion);
        return hash.ToHashCode();
    }
}

/// <summary>session取得manifestを確定できない理由です。</summary>
public enum ConnectionSessionAcquisitionManifestError
{
    /// <summary>アプリversionの一部が空です。</summary>
    InvalidApplicationVersion,
    /// <summary>version契約値が正ではありません。</summary>
    InvalidContractVersion,
    /// <summary>同じService/PIDのsnapshotが複数あります。</summary>
    DuplicatePIDDefinition,
    /// <summary>ordered要求集合に対応するPID snapshotがありません。</summary>
    MissingRequestedPIDDefinition,
}

/// <summary>session取得manifestを確定できないことを表す例外です。</summary>
public sealed class ConnectionSessionAcquisitionManifestException : Exception
{
    /// <summary>確定できない理由です。</summary>
    public ConnectionSessionAcquisitionManifestError Reason { get; }

    /// <summary>理由を指定して例外を生成します。</summary>
    /// <remarks>責務: Swift の <c>ConnectionSessionAcquisitionManifestError</c> のケースを例外として運びます。</remarks>
    /// <param name="reason">確定できない理由。</param>
    public ConnectionSessionAcquisitionManifestException(ConnectionSessionAcquisitionManifestError reason)
        : base(reason.ToString())
    {
        Reason = reason;
    }
}
