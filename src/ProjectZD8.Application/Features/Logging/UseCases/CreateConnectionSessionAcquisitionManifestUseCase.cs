using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging.UseCases;

/// <summary>1件のPIDについてmanifestへ固定する能力と収集選択のApplication入力です。</summary>
public sealed record ConnectionSessionAcquisitionPIDCapabilityInput
{
    /// <summary>能力証拠を結び付けるService/PIDです。</summary>
    public OBDPIDRequest Request { get; }

    /// <summary>明示証拠に基づく対応状態です。</summary>
    public AcquisitionPIDCapabilitySupport Support { get; }

    /// <summary>対応状態とは独立した今回の収集選択です。</summary>
    public bool IsCollectionEnabled { get; }

    /// <summary>PID能力と収集選択を明示入力として固定します。</summary>
    /// <remarks>責務: 1件のService/PIDについて対応状態と収集選択を別軸で保持します。</remarks>
    /// <param name="request">能力証拠を結び付けるService/PID。</param>
    /// <param name="support"><c>supported</c>、<c>unsupported</c>、<c>indeterminate</c>の明示状態。</param>
    /// <param name="isCollectionEnabled">今回の収集選択。</param>
    public ConnectionSessionAcquisitionPIDCapabilityInput(
        OBDPIDRequest request,
        AcquisitionPIDCapabilitySupport support,
        bool isCollectionEnabled)
    {
        Request = request;
        Support = support;
        IsCollectionEnabled = isCollectionEnabled;
    }
}

/// <summary>session取得manifestを作成する決定済みApplication入力です。</summary>
public sealed class CreateConnectionSessionAcquisitionManifestInput
{
    /// <summary>manifestを所有するsession識別子です。</summary>
    public ConnectionSessionID SessionID { get; }

    /// <summary>取得workflowが割り当てたpolling世代です。</summary>
    public ulong Generation { get; }

    /// <summary>manifest構造の明示versionです。</summary>
    public int ManifestVersion { get; }

    /// <summary>polling方針の明示versionです。</summary>
    public int PollingPolicyVersion { get; }

    /// <summary>model入力変換契約の明示versionです。</summary>
    public int ModelInputManifestVersion { get; }

    /// <summary>formula文字列の比較規則を示す明示versionです。</summary>
    public int FormulaCanonicalizationVersion { get; }

    /// <summary>取得開始時点のPID定義一覧です。</summary>
    public IReadOnlyList<OBDPIDDefinition> PidDefinitions { get; }

    /// <summary>取得開始時点のPID能力と収集選択一覧です。</summary>
    public IReadOnlyList<ConnectionSessionAcquisitionPIDCapabilityInput> PidCapabilities { get; }

    /// <summary>policyが確定した要求順のService/PID一覧です。</summary>
    public IReadOnlyList<OBDPIDRequest> OrderedRequestedPIDs { get; }

    /// <summary>manifest作成へ渡す確定済み入力を保持します。</summary>
    /// <remarks>責務: session、世代、version、PID定義、能力、要求順を推測のない作成要求へまとめます。</remarks>
    /// <param name="sessionID">manifestを所有するsession識別子。</param>
    /// <param name="generation">取得workflowが割り当てたpolling世代。</param>
    /// <param name="manifestVersion">manifest構造の明示version。</param>
    /// <param name="pollingPolicyVersion">polling方針の明示version。</param>
    /// <param name="modelInputManifestVersion">model入力変換契約の明示version。</param>
    /// <param name="formulaCanonicalizationVersion">formula比較規則の明示version。</param>
    /// <param name="pidDefinitions">取得開始時点のPID定義一覧。</param>
    /// <param name="pidCapabilities">取得開始時点のPID能力と収集選択一覧。</param>
    /// <param name="orderedRequestedPIDs">policyが確定した要求順のService/PID一覧。</param>
    public CreateConnectionSessionAcquisitionManifestInput(
        ConnectionSessionID sessionID,
        ulong generation,
        int manifestVersion,
        int pollingPolicyVersion,
        int modelInputManifestVersion,
        int formulaCanonicalizationVersion,
        IReadOnlyList<OBDPIDDefinition> pidDefinitions,
        IReadOnlyList<ConnectionSessionAcquisitionPIDCapabilityInput> pidCapabilities,
        IReadOnlyList<OBDPIDRequest> orderedRequestedPIDs)
    {
        SessionID = sessionID;
        Generation = generation;
        ManifestVersion = manifestVersion;
        PollingPolicyVersion = pollingPolicyVersion;
        ModelInputManifestVersion = modelInputManifestVersion;
        FormulaCanonicalizationVersion = formulaCanonicalizationVersion;
        PidDefinitions = pidDefinitions.ToArray();
        PidCapabilities = pidCapabilities.ToArray();
        OrderedRequestedPIDs = orderedRequestedPIDs.ToArray();
    }
}

/// <summary>manifest保存と開始境界保存が完了したRaw取得許可です。</summary>
public sealed record ConnectionSessionRawAcquisitionPermission
{
    /// <summary>許可対象のsession識別子です。</summary>
    public ConnectionSessionID SessionID { get; }

    /// <summary>許可対象のpolling世代です。</summary>
    public ulong Generation { get; }

    /// <summary>保存済みのimmutable manifestです。</summary>
    public ConnectionSessionAcquisitionManifest Manifest { get; }

    /// <summary>保存完了済みのRaw取得許可を生成します。</summary>
    /// <remarks>責務: 保存済みmanifestを同じsessionとpolling世代のRaw開始許可へ結び付けます。</remarks>
    /// <param name="sessionID">許可対象のsession識別子。</param>
    /// <param name="generation">許可対象のpolling世代。</param>
    /// <param name="manifest">保存済みのimmutable manifest。</param>
    public ConnectionSessionRawAcquisitionPermission(
        ConnectionSessionID sessionID,
        ulong generation,
        ConnectionSessionAcquisitionManifest manifest)
    {
        SessionID = sessionID;
        Generation = generation;
        Manifest = manifest;
    }
}

/// <summary>manifest作成をRaw開始前に停止するApplication上の理由です。</summary>
public enum CreateConnectionSessionAcquisitionManifestError
{
    /// <summary>要求が現在のpolling世代に属していません。</summary>
    InactiveGeneration,

    /// <summary>同じService/PIDのcapability入力が重複しています。</summary>
    DuplicateCapabilityEvidence,

    /// <summary>PID定義に対応するcapability入力がありません。</summary>
    CapabilityEvidenceMissing,

    /// <summary>capability入力に対応するPID定義がありません。</summary>
    PidDefinitionEvidenceMissing,

    /// <summary>revision、byte数、formula、unitのいずれかが完全ではありません。</summary>
    IncompletePIDDefinition,

    /// <summary>validity rangeの片側だけが存在します。</summary>
    IncompleteValidityRange,
}

/// <summary><see cref="CreateConnectionSessionAcquisitionManifestError"/> を理由として持つ例外です。</summary>
public sealed class CreateConnectionSessionAcquisitionManifestException : Exception
{
    /// <summary>manifest作成を停止した理由です。</summary>
    public CreateConnectionSessionAcquisitionManifestError Reason { get; }

    /// <summary>理由を指定して例外を生成します。</summary>
    /// <remarks>責務: manifest作成の停止理由を1件の例外へ包みます。</remarks>
    /// <param name="reason">manifest作成を停止した理由。</param>
    public CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError reason)
        : base($"{nameof(CreateConnectionSessionAcquisitionManifestError)}.{reason}")
    {
        Reason = reason;
    }
}

/// <summary>確定済み取得入力からsessionの取得開始証拠を原子的に保存します。</summary>
/// <remarks>Swift では actor でした。共有状態は lock で守り、await をまたいでロックしません。</remarks>
public sealed class CreateConnectionSessionAcquisitionManifestUseCase
{
    /// <summary>manifestとRaw開始境界の原子的な保存先です。</summary>
    private readonly IConnectionSessionAcquisitionRepository _acquisitionRepository;

    /// <summary>app/build、schema契約、platformのauthorityです。</summary>
    private readonly IConnectionSessionAcquisitionEvidencePort _evidencePort;

    /// <summary>Raw取得開始許可直前の時刻を供給します。</summary>
    private readonly Func<DateTimeOffset> _now;

    /// <summary>actor の直列化を表す同期区間のロックです。</summary>
    private readonly Lock _gate = new();

    /// <summary>現在の取得workflowが有効としたpolling世代です。</summary>
    private ulong? _activeGeneration;

    /// <summary>同一sessionと世代のretryで再利用するRaw開始境界です。</summary>
    private RetainedConnectionSessionAcquisitionStartBoundary? _retainedStartBoundary;

    /// <summary>原子repository、runtime evidence、clockを固定します。</summary>
    /// <remarks>責務: manifest作成workflowの原子的な永続化境界とruntime authorityを注入します。</remarks>
    /// <param name="acquisitionRepository">manifestとRaw開始境界の原子的な保存先。</param>
    /// <param name="evidencePort">app/build、schema契約、platformのauthority。</param>
    /// <param name="now">Raw取得開始許可直前の時刻供給元。<c>null</c> なら現在時刻。</param>
    public CreateConnectionSessionAcquisitionManifestUseCase(
        IConnectionSessionAcquisitionRepository acquisitionRepository,
        IConnectionSessionAcquisitionEvidencePort evidencePort,
        Func<DateTimeOffset>? now = null)
    {
        _acquisitionRepository = acquisitionRepository;
        _evidencePort = evidencePort;
        _now = now ?? (() => DateTimeOffset.Now);
        _activeGeneration = null;
        _retainedStartBoundary = null;
    }

    /// <summary>新しいpolling世代だけをmanifest作成対象として有効化します。</summary>
    /// <remarks>責務: 後続のmanifest作成結果を照合する現在世代を置き換えます。</remarks>
    /// <param name="generation">取得workflowが開始する新しいpolling世代。</param>
    public void Activate(ulong generation)
    {
        lock (_gate)
        {
            if (_activeGeneration != generation)
            {
                _retainedStartBoundary = null;
            }
            _activeGeneration = generation;
        }
    }

    /// <summary>指定polling世代の未完了manifest作成を無効化します。</summary>
    /// <remarks>責務: 現在世代と一致する取得開始だけをcancel済み状態へ遷移させます。</remarks>
    /// <param name="generation">cancelするpolling世代。</param>
    public void Cancel(ulong generation)
    {
        lock (_gate)
        {
            if (_activeGeneration != generation)
            {
                return;
            }
            _activeGeneration = null;
            _retainedStartBoundary = null;
        }
    }

    /// <summary>確定済み入力をmanifestへ変換し、開始証拠の原子保存後だけRaw開始を許可します。</summary>
    /// <remarks>責務: 現在世代のmanifestと開始境界を部分成功なしで保存してRaw開始許可を返します。</remarks>
    /// <param name="input">session、世代、version、PID定義、能力、要求順を含む確定済み入力。</param>
    /// <returns>manifestと開始境界の保存に成功した同一session・世代のRaw開始許可。</returns>
    /// <exception cref="Exception">世代不一致、入力欠落、Domain不変条件、runtime取得、repository保存のいずれかが失敗した場合のエラー。</exception>
    public async Task<ConnectionSessionRawAcquisitionPermission> Execute(
        CreateConnectionSessionAcquisitionManifestInput input)
    {
        lock (_gate)
        {
            EnsureCurrent(input.Generation);
        }
        var runtimeEvidence = await _evidencePort.Evidence();
        lock (_gate)
        {
            EnsureCurrent(input.Generation);
            var manifest = MakeManifest(input, runtimeEvidence);
            EnsureCurrent(input.Generation);
            var startedAt = StartBoundaryDate(input.SessionID, input.Generation);
            try
            {
                _acquisitionRepository.SaveStartOnce(manifest, startedAt, input.SessionID);
            }
            catch (ConnectionSessionAcquisitionRepositoryException error)
                when (error.Reason == ConnectionSessionAcquisitionRepositoryError.Duplicate)
            {
                if (!Equals(_acquisitionRepository.Manifest(input.SessionID), manifest))
                {
                    throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Conflict);
                }
            }
            return new ConnectionSessionRawAcquisitionPermission(input.SessionID, input.Generation, manifest);
        }
    }

    /// <summary>指定世代が現在の取得workflowに属することを検証します。</summary>
    /// <remarks>責務: staleまたはcancel済み世代を永続化処理より前に拒否します。</remarks>
    /// <param name="generation">検証するpolling世代。</param>
    /// <exception cref="CreateConnectionSessionAcquisitionManifestException">現在世代と一致しない場合は <c>InactiveGeneration</c>。</exception>
    private void EnsureCurrent(ulong generation)
    {
        if (_activeGeneration != generation)
        {
            throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.InactiveGeneration);
        }
    }

    /// <summary>同じsessionと世代のretryへ同一の開始境界時刻を返します。</summary>
    /// <remarks>責務: 原子保存結果が曖昧でもretryが既存開始eventを書き換えない安定入力を供給します。</remarks>
    /// <param name="sessionID">開始境界を所有するsession識別子。</param>
    /// <param name="generation">開始境界を所有するpolling世代。</param>
    /// <returns>初回に採番し同じsessionと世代で再利用する開始境界時刻。</returns>
    private DateTimeOffset StartBoundaryDate(ConnectionSessionID sessionID, ulong generation)
    {
        if (_retainedStartBoundary is { } retained
            && retained.SessionID == sessionID
            && retained.Generation == generation)
        {
            return retained.StartedAt;
        }
        var startedAt = _now();
        _retainedStartBoundary = new RetainedConnectionSessionAcquisitionStartBoundary(sessionID, generation, startedAt);
        return startedAt;
    }

    /// <summary>Application入力をDomain manifestへ変換します。</summary>
    /// <remarks>責務: runtime evidenceとPID取得入力を欠落補完なしでimmutable manifestへ変換します。</remarks>
    /// <param name="input">version、PID定義、能力、要求順を含むApplication入力。</param>
    /// <param name="runtimeEvidence">portが返したapp/build、schema契約、platform。</param>
    /// <returns>session識別情報をsemantic identityへ含めないDomain manifest。</returns>
    /// <exception cref="Exception">入力欠落またはDomain不変条件に違反した場合のエラー。</exception>
    private static ConnectionSessionAcquisitionManifest MakeManifest(
        CreateConnectionSessionAcquisitionManifestInput input,
        ConnectionSessionAcquisitionRuntimeEvidence runtimeEvidence)
    {
        var capabilityRequests = input.PidCapabilities.Select(capability => capability.Request).ToArray();
        var capabilitySet = new HashSet<OBDPIDRequest>(capabilityRequests);
        if (capabilitySet.Count != capabilityRequests.Length)
        {
            throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.DuplicateCapabilityEvidence);
        }
        var definitionSet = new HashSet<OBDPIDRequest>(
            input.PidDefinitions.Select(definition => new OBDPIDRequest(definition.Service, definition.Pid)));
        var capabilityByRequest = input.PidCapabilities.ToDictionary(capability => capability.Request);
        if (!definitionSet.IsSubsetOf(capabilitySet))
        {
            throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.CapabilityEvidenceMissing);
        }
        if (!capabilitySet.IsSubsetOf(definitionSet))
        {
            throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.PidDefinitionEvidenceMissing);
        }
        var snapshots = input.PidDefinitions.Select(definition =>
        {
            var request = new OBDPIDRequest(definition.Service, definition.Pid);
            if (!capabilityByRequest.TryGetValue(request, out var capability))
            {
                throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.CapabilityEvidenceMissing);
            }
            return MakeSnapshot(definition, capability, input.FormulaCanonicalizationVersion);
        }).ToArray();
        return new ConnectionSessionAcquisitionManifest(
            manifestVersion: input.ManifestVersion,
            applicationVersion: runtimeEvidence.ApplicationVersion,
            schemaContractVersion: runtimeEvidence.SchemaContractVersion,
            pollingPolicyVersion: input.PollingPolicyVersion,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(input.OrderedRequestedPIDs),
            pidDefinitions: snapshots,
            acquisitionPlatform: runtimeEvidence.AcquisitionPlatform,
            modelInputManifestVersion: input.ModelInputManifestVersion);
    }

    /// <summary>既存PID定義と明示capabilityをDomain snapshotへ変換します。</summary>
    /// <remarks>責務: 1件のPIDについてrevision、bytes、formula identity、unit、range、能力、選択を欠落なしで固定します。</remarks>
    /// <param name="definition">取得開始時点のPID定義。</param>
    /// <param name="capability">同じService/PIDの明示能力と収集選択。</param>
    /// <param name="canonicalizationVersion">formula比較規則の明示version。</param>
    /// <returns>完全な取得時PID definition snapshot。</returns>
    /// <exception cref="CreateConnectionSessionAcquisitionManifestException">必須definition fieldまたはvalidity rangeが不完全な場合のエラー。</exception>
    private static AcquisitionPIDDefinitionSnapshot MakeSnapshot(
        OBDPIDDefinition definition,
        ConnectionSessionAcquisitionPIDCapabilityInput capability,
        int canonicalizationVersion)
    {
        if (definition.Revision <= 0
            || definition.RequiredByteCount is not { } requiredByteCount
            || requiredByteCount <= 0
            || definition.Formula is not { } formula
            || formula.Length == 0
            || definition.Unit.Length == 0)
        {
            throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.IncompletePIDDefinition);
        }
        AcquisitionPIDValidityRange validityRange = (definition.MinimumValue, definition.MaximumValue) switch
        {
            ({ } minimum, { } maximum) => AcquisitionPIDValidityRange.Inclusive(minimum, maximum),
            (null, null) => AcquisitionPIDValidityRange.NotDeclared,
            _ => throw new CreateConnectionSessionAcquisitionManifestException(CreateConnectionSessionAcquisitionManifestError.IncompleteValidityRange),
        };
        return new AcquisitionPIDDefinitionSnapshot(
            request: capability.Request,
            capabilitySupport: capability.Support,
            isCollectionEnabled: capability.IsCollectionEnabled,
            definitionRevision: definition.Revision,
            requiredByteCount: requiredByteCount,
            definitionIdentity: new AcquisitionPIDDefinitionIdentity(canonicalizationVersion, formula),
            unit: definition.Unit,
            validityRange: validityRange);
    }

    /// <summary>原子保存retryまで保持するsession単位のRaw開始境界です。</summary>
    /// <param name="SessionID">開始境界を所有するsession識別子。</param>
    /// <param name="Generation">開始境界を所有するpolling世代。</param>
    /// <param name="StartedAt">最初の保存試行で採番した開始境界時刻。</param>
    private sealed record RetainedConnectionSessionAcquisitionStartBoundary(
        ConnectionSessionID SessionID,
        ulong Generation,
        DateTimeOffset StartedAt);
}
