using ProjectZD8.Application.Logging.Ports;
using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;
using ProjectZD8.Application.Tests.TestSupport;

namespace ProjectZD8.Application.Tests.Logging.UseCases;

/// <summary>session取得manifest作成とRaw開始許可のApplication orchestrationを検証します。</summary>
public sealed class CreateConnectionSessionAcquisitionManifestUseCaseTests
{
    /// <summary>Swift の <c>Date(timeIntervalSinceReferenceDate: 0)</c> に当たる基準時刻です。</summary>
    private static readonly DateTimeOffset ReferenceDate = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>完全な入力と開始境界を1回のrepository callで保存します。</summary>
    /// <remarks>責務: 完全manifestと開始eventの原子保存成功だけが同一世代のRaw開始許可を返すことを確認します。</remarks>
    [Fact]
    public async Task SavesCompleteManifestOnceBeforeAllowingRawAcquisition()
    {
        var fixture = MakeFixture();
        var permission = await fixture.Execute();

        Assert.Equal(fixture.Input.SessionID, permission.SessionID);
        Assert.Equal(fixture.Input.Generation, permission.Generation);
        Assert.Equal(1, fixture.AcquisitionRepository.SaveStartCallCount);
        Assert.Equal(permission.Manifest, fixture.AcquisitionRepository.StoredManifest);
        Assert.Equal(fixture.StartedAt, fixture.AcquisitionRepository.StoredStartedAt);
    }

    /// <summary>runtimeと明示versionをmanifestへそのまま伝播します。</summary>
    /// <remarks>責務: app/build/schema/manifest/polling/model-input versionを推測なしで保存値へ反映します。</remarks>
    [Fact]
    public async Task PropagatesAllVersionAuthorities()
    {
        var fixture = MakeFixture();
        var manifest = (await fixture.Execute()).Manifest;

        Assert.Equal("fixture-marketing", manifest.ApplicationVersion?.MarketingVersion);
        Assert.Equal("fixture-build", manifest.ApplicationVersion?.BuildVersion);
        Assert.Equal(31, manifest.SchemaContractVersion);
        Assert.Equal(11, manifest.ManifestVersion);
        Assert.Equal(21, manifest.PollingPolicyVersion);
        Assert.Equal(41, manifest.ModelInputManifestVersion);
    }

    /// <summary>端末名を含まないplatformだけをmanifestへ伝播します。</summary>
    /// <remarks>責務: runtime evidenceのacquisition platformをsemantic manifestへ固定します。</remarks>
    [Fact]
    public async Task PropagatesAcquisitionPlatform()
    {
        var fixture = MakeFixture(platform: ConnectionSessionAcquisitionPlatform.IPad);
        var platform = (await fixture.Execute()).Manifest.AcquisitionPlatform;

        Assert.Equal(ConnectionSessionAcquisitionPlatform.IPad, platform);
    }

    /// <summary>ordered PID setの入力順を保持します。</summary>
    /// <remarks>責務: policyが確定したPID要求順を並べ替えずmanifestへ保存します。</remarks>
    [Fact]
    public async Task PreservesOrderedRequestedPIDSet()
    {
        var fixture = MakeFixture(orderedRequests: [SecondRequest, FirstRequest]);
        var requests = (await fixture.Execute()).Manifest.OrderedRequestedPIDs?.Requests;

        Assert.Equal([SecondRequest, FirstRequest], requests);
    }

    /// <summary>capability supportとcollection enabledを別軸で保持します。</summary>
    /// <remarks>責務: 非対応証拠と収集選択を相互変換せずPID snapshotへ伝播します。</remarks>
    [Fact]
    public async Task SeparatesCapabilitySupportFromCollectionSelection()
    {
        var fixture = MakeFixture(
            capabilities:
            [
                Capability(FirstRequest, AcquisitionPIDCapabilitySupport.Unsupported, enabled: true),
                Capability(SecondRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: false),
            ]);
        var snapshots = (await fixture.Execute()).Manifest.PidDefinitions;

        Assert.Equal(AcquisitionPIDCapabilitySupport.Unsupported, snapshots[0].CapabilitySupport);
        Assert.True(snapshots[0].IsCollectionEnabled);
        Assert.Equal(AcquisitionPIDCapabilitySupport.Supported, snapshots[1].CapabilitySupport);
        Assert.False(snapshots[1].IsCollectionEnabled);
    }

    /// <summary>判定不能capabilityをunsupportedへ変換しません。</summary>
    /// <remarks>責務: 明示されたindeterminate証拠を同じ状態のままmanifestへ固定します。</remarks>
    [Fact]
    public async Task PreservesIndeterminateCapability()
    {
        var fixture = MakeFixture(
            capabilities:
            [
                Capability(FirstRequest, AcquisitionPIDCapabilitySupport.Indeterminate, enabled: true),
                Capability(SecondRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: true),
            ]);
        var support = (await fixture.Execute()).Manifest.PidDefinitions.FirstOrDefault()?.CapabilitySupport;

        Assert.Equal(AcquisitionPIDCapabilitySupport.Indeterminate, support);
    }

    /// <summary>PID definitionのsemantic fieldを完全に伝播します。</summary>
    /// <remarks>責務: revision、byte数、formula identity、unit、rangeを取得時snapshotへ固定します。</remarks>
    [Fact]
    public async Task PropagatesCompletePIDDefinitionSnapshot()
    {
        var fixture = MakeFixture();
        var firstSnapshot = (await fixture.Execute()).Manifest.PidDefinitions.FirstOrDefault();
        var snapshot = Assert.IsType<AcquisitionPIDDefinitionSnapshot>(firstSnapshot);

        Assert.Equal(7, snapshot.DefinitionRevision);
        Assert.Equal(2, snapshot.RequiredByteCount);
        Assert.Equal(51, snapshot.DefinitionIdentity?.CanonicalizationVersion);
        Assert.Equal("(A * 256 + B) / 4", snapshot.DefinitionIdentity?.Expression);
        Assert.Equal("rpm", snapshot.Unit);
        Assert.Equal(0, snapshot.ValidityRange?.Minimum);
        Assert.Equal(16_383.75, snapshot.ValidityRange?.Maximum);
    }

    /// <summary>requested PIDに対応するdefinition欠落を拒否します。</summary>
    /// <remarks>責務: definition snapshotを作れない要求集合からRaw開始許可を返さないことを確認します。</remarks>
    [Fact]
    public async Task RejectsMissingRequestedPIDDefinition()
    {
        var missing = new OBDPIDRequest(1, 14);
        var fixture = MakeFixture(orderedRequests: [FirstRequest, missing]);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.Manifest(ConnectionSessionAcquisitionManifestError.MissingRequestedPIDDefinition), error);
        Assert.Null(fixture.AcquisitionRepository.StoredStartedAt);
    }

    /// <summary>capability一覧にないPIDを拒否します。</summary>
    /// <remarks>責務: capability欠落をunsupportedへ変換せずRaw開始前の明示失敗にします。</remarks>
    [Fact]
    public async Task RejectsMissingCapabilityEvidence()
    {
        var fixture = MakeFixture(
            capabilities: [Capability(FirstRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: true)]);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.Application(CreateConnectionSessionAcquisitionManifestError.CapabilityEvidenceMissing), error);
        Assert.Equal(0, fixture.AcquisitionRepository.SaveStartCallCount);
    }

    /// <summary>ordered PID setの重複を拒否します。</summary>
    /// <remarks>責務: 同じService/PIDを複数要求するmanifestを保存しないことを確認します。</remarks>
    [Fact]
    public async Task RejectsDuplicateRequestedPID()
    {
        var fixture = MakeFixture(orderedRequests: [FirstRequest, FirstRequest]);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.OrderedSet(OrderedAcquisitionPIDSetError.DuplicateRequest), error);
        Assert.Equal(0, fixture.AcquisitionRepository.SaveStartCallCount);
    }

    /// <summary>PID definition一覧の重複を拒否します。</summary>
    /// <remarks>責務: 同じService/PIDのdefinition snapshotを複数保存できないことを確認します。</remarks>
    [Fact]
    public async Task RejectsDuplicatePIDDefinition()
    {
        var fixture = MakeFixture(
            definitions: [EngineSpeedDefinition, EngineSpeedDefinition],
            capabilities: [Capability(FirstRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: true)],
            orderedRequests: [FirstRequest]);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.Manifest(ConnectionSessionAcquisitionManifestError.DuplicatePIDDefinition), error);
        Assert.Equal(0, fixture.AcquisitionRepository.SaveStartCallCount);
    }

    /// <summary>capability一覧の重複を拒否します。</summary>
    /// <remarks>責務: 同じService/PIDの能力証拠を複数値から選ばず明示失敗にします。</remarks>
    [Fact]
    public async Task RejectsDuplicateCapabilityEvidence()
    {
        var duplicated = Capability(FirstRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: true);
        var fixture = MakeFixture(
            definitions: [EngineSpeedDefinition],
            capabilities: [duplicated, duplicated],
            orderedRequests: [FirstRequest]);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.Application(CreateConnectionSessionAcquisitionManifestError.DuplicateCapabilityEvidence), error);
        Assert.Equal(0, fixture.AcquisitionRepository.SaveStartCallCount);
    }

    /// <summary>repository duplicateをcanonical exact retryとして許可します。</summary>
    /// <remarks>責務: 同一sessionへの同一manifest再保存が保存済みmanifestのRaw開始許可を返すことを確認します。</remarks>
    [Fact]
    public async Task CanonicalRepositoryDuplicateReturnsPermission()
    {
        var fixture = MakeFixture();
        var original = await fixture.Execute();
        var retry = await fixture.Execute();

        Assert.Equal(original, retry);
        Assert.Equal(2, fixture.AcquisitionRepository.SaveStartCallCount);
        Assert.Equal(fixture.StartedAt, fixture.AcquisitionRepository.StoredStartedAt);
    }

    /// <summary>repository conflictを成功へ変換せず伝播します。</summary>
    /// <remarks>責務: 同一sessionの異内容manifest競合をRaw開始許可として扱わないことを確認します。</remarks>
    [Fact]
    public async Task PropagatesRepositoryConflict()
    {
        var fixture = MakeFixture(repositoryError: ConnectionSessionAcquisitionRepositoryError.Conflict);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.Repository(ConnectionSessionAcquisitionRepositoryError.Conflict), error);
        Assert.False(fixture.AcquisitionRepository.HasObservablePartialSuccess);
    }

    /// <summary>repository unavailableを成功へ変換せず伝播します。</summary>
    /// <remarks>責務: manifest保存先failureを空データまたは通常接続成功へ変換しないことを確認します。</remarks>
    [Fact]
    public async Task PropagatesRepositoryUnavailable()
    {
        var fixture = MakeFixture(repositoryError: ConnectionSessionAcquisitionRepositoryError.Unavailable);
        var error = await CapturedErrorFrom(fixture);

        Assert.Equal(new CapturedError.Repository(ConnectionSessionAcquisitionRepositoryError.Unavailable), error);
        Assert.False(fixture.AcquisitionRepository.HasObservablePartialSuccess);
    }

    /// <summary>原子保存失敗時に片方だけの成功もRaw許可も生成しません。</summary>
    /// <remarks>責務: repository失敗をmanifestまたは開始境界だけの観測可能な成功へ変換しないことを確認します。</remarks>
    [Fact]
    public async Task RepositoryFailureDoesNotExposePartialSuccessOrAllowRawAcquisition()
    {
        var fixture = MakeFixture(repositoryError: ConnectionSessionAcquisitionRepositoryError.Unavailable);

        _ = await CapturedErrorFrom(fixture);

        Assert.False(fixture.AcquisitionRepository.HasObservablePartialSuccess);
    }

    /// <summary>evidence取得中に世代が変わった結果を保存しません。</summary>
    /// <remarks>責務: 古いpolling世代のasync runtime結果を新しいsession取得へ混入させないことを確認します。</remarks>
    [Fact]
    public async Task DiscardsResultFromStaleGeneration()
    {
        var port = new SuspendedAcquisitionEvidencePort(RuntimeEvidence());
        var fixture = MakeFixture(evidencePort: port);
        fixture.UseCase.Activate(fixture.Input.Generation);
        var task = fixture.UseCase.Execute(fixture.Input);
        Assert.True(await AsyncWait.UntilAsync(() => port.IsWaiting));

        fixture.UseCase.Activate(fixture.Input.Generation + 1);
        port.Resume();

        await AssertInactiveGeneration(task);
        Assert.Equal(0, fixture.AcquisitionRepository.SaveStartCallCount);
        Assert.False(fixture.AcquisitionRepository.HasObservablePartialSuccess);
    }

    /// <summary>evidence取得中のcancel後に結果を保存しません。</summary>
    /// <remarks>責務: cancel済みpolling世代のasync runtime結果からmanifestまたはRaw開始許可を作らないことを確認します。</remarks>
    [Fact]
    public async Task DiscardsResultAfterCancellation()
    {
        var port = new SuspendedAcquisitionEvidencePort(RuntimeEvidence());
        var fixture = MakeFixture(evidencePort: port);
        fixture.UseCase.Activate(fixture.Input.Generation);
        var task = fixture.UseCase.Execute(fixture.Input);
        Assert.True(await AsyncWait.UntilAsync(() => port.IsWaiting));

        fixture.UseCase.Cancel(fixture.Input.Generation);
        port.Resume();

        await AssertInactiveGeneration(task);
        Assert.Equal(0, fixture.AcquisitionRepository.SaveStartCallCount);
    }

    /// <summary>retryが既存manifestを更新しません。</summary>
    /// <remarks>責務: 初回保存後の再実行が元manifestと開始境界を維持して同じ許可を返すことを確認します。</remarks>
    [Fact]
    public async Task RetryDoesNotUpdateExistingManifest()
    {
        var fixture = MakeFixture();
        var original = await fixture.Execute();
        var retry = await fixture.Execute();

        Assert.Equal(original, retry);
        Assert.Equal(original.Manifest, fixture.AcquisitionRepository.StoredManifest);
        Assert.Equal(1, fixture.Clock.CallCount);
        Assert.Equal(fixture.StartedAt, fixture.AcquisitionRepository.StoredStartedAt);
    }

    /// <summary>stale generation taskの結果を検証します。</summary>
    /// <remarks>責務: 非同期taskがinactive generationだけを返すことを確認します。</remarks>
    /// <param name="task">staleまたはcancel済みにしたmanifest作成task。</param>
    private static async Task AssertInactiveGeneration(Task<ConnectionSessionRawAcquisitionPermission> task)
    {
        var error = await Assert.ThrowsAsync<CreateConnectionSessionAcquisitionManifestException>(() => task);
        Assert.Equal(CreateConnectionSessionAcquisitionManifestError.InactiveGeneration, error.Reason);
    }

    /// <summary>fixture実行時の型付きerrorを取得します。</summary>
    /// <remarks>責務: Raw許可を期待しないtestで排他的な失敗種別を比較可能にします。</remarks>
    /// <param name="fixture">実行するmanifest作成fixture。</param>
    /// <returns>use caseが投げた分類済みtest error。</returns>
    private static async Task<CapturedError?> CapturedErrorFrom(UseCaseFixture fixture)
    {
        try
        {
            _ = await fixture.Execute();
            return null;
        }
        catch (CreateConnectionSessionAcquisitionManifestException error)
        {
            return new CapturedError.Application(error.Reason);
        }
        catch (ConnectionSessionAcquisitionManifestException error)
        {
            return new CapturedError.Manifest(error.Reason);
        }
        catch (OrderedAcquisitionPIDSetException error)
        {
            return new CapturedError.OrderedSet(error.Reason);
        }
        catch (ConnectionSessionAcquisitionRepositoryException error)
        {
            return new CapturedError.Repository(error.Reason);
        }
        catch (Exception)
        {
            return new CapturedError.Unexpected();
        }
    }

    /// <summary>manifest作成testの依存と入力を生成します。</summary>
    /// <remarks>責務: 実識別情報とRaw payloadを含まない決定的なApplication fixtureを返します。</remarks>
    /// <param name="platform">runtime evidenceへ設定するplatform。</param>
    /// <param name="definitions">取得開始時点のPID定義一覧。</param>
    /// <param name="capabilities">PID能力と収集選択。省略時は2件のsupported入力。</param>
    /// <param name="orderedRequests">manifestへ保持するPID要求順。</param>
    /// <param name="repositoryError">原子repositoryが返す明示失敗。</param>
    /// <param name="evidencePort">runtime evidence port。省略時は即時成功fixture。</param>
    /// <returns>初期世代を有効化して実行できるuse case fixture。</returns>
    private static UseCaseFixture MakeFixture(
        ConnectionSessionAcquisitionPlatform platform = ConnectionSessionAcquisitionPlatform.MacOS,
        IReadOnlyList<OBDPIDDefinition>? definitions = null,
        IReadOnlyList<ConnectionSessionAcquisitionPIDCapabilityInput>? capabilities = null,
        IReadOnlyList<OBDPIDRequest>? orderedRequests = null,
        ConnectionSessionAcquisitionRepositoryError? repositoryError = null,
        IConnectionSessionAcquisitionEvidencePort? evidencePort = null)
    {
        var acquisitionRepository = new AcquisitionRepositorySpy(repositoryError);
        var clock = new AdvancingClock(ReferenceDate.AddSeconds(500));
        var input = new CreateConnectionSessionAcquisitionManifestInput(
            sessionID: SyntheticSessionID,
            generation: 101,
            manifestVersion: 11,
            pollingPolicyVersion: 21,
            modelInputManifestVersion: 41,
            formulaCanonicalizationVersion: 51,
            pidDefinitions: definitions ?? [EngineSpeedDefinition, VehicleSpeedDefinition],
            pidCapabilities: capabilities ??
            [
                Capability(FirstRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: true),
                Capability(SecondRequest, AcquisitionPIDCapabilitySupport.Supported, enabled: true),
            ],
            orderedRequestedPIDs: orderedRequests ?? [FirstRequest, SecondRequest]);
        var port = evidencePort ?? new FixedAcquisitionEvidencePort(RuntimeEvidence(platform));
        var useCase = new CreateConnectionSessionAcquisitionManifestUseCase(acquisitionRepository, port, clock.Now);
        return new UseCaseFixture(useCase, input, acquisitionRepository, clock, ReferenceDate.AddSeconds(500));
    }

    /// <summary>runtime authorityのsynthetic evidenceを生成します。</summary>
    /// <remarks>責務: 実Production versionや端末名を含まないmanifest runtime fixtureを返します。</remarks>
    /// <param name="platform">fixtureへ設定するacquisition platform。</param>
    /// <returns>app/build/schema/platformのsynthetic evidence。</returns>
    private static ConnectionSessionAcquisitionRuntimeEvidence RuntimeEvidence(
        ConnectionSessionAcquisitionPlatform platform = ConnectionSessionAcquisitionPlatform.MacOS) =>
        new(new AcquisitionApplicationVersion("fixture-marketing", "fixture-build"), 31, platform);

    /// <summary>PID capability fixtureを生成します。</summary>
    /// <remarks>責務: 対応状態と収集選択を独立に指定したsynthetic入力を返します。</remarks>
    /// <param name="request">synthetic Service/PID。</param>
    /// <param name="support">明示capability状態。</param>
    /// <param name="enabled">収集選択。</param>
    /// <returns>manifest作成用capability入力。</returns>
    private static ConnectionSessionAcquisitionPIDCapabilityInput Capability(
        OBDPIDRequest request,
        AcquisitionPIDCapabilitySupport support,
        bool enabled) =>
        new(request, support, enabled);

    /// <summary>engine speed用synthetic PID定義です。</summary>
    private static OBDPIDDefinition EngineSpeedDefinition =>
        MakeDefinition(FirstRequest, 2, "(A * 256 + B) / 4", "rpm", 0, 16_383.75, 7);

    /// <summary>vehicle speed用synthetic PID定義です。</summary>
    private static OBDPIDDefinition VehicleSpeedDefinition =>
        MakeDefinition(SecondRequest, 1, "A", "km/h", 0, 255, 8);

    /// <summary>synthetic PID定義を生成します。</summary>
    /// <remarks>責務: manifest mapping testへ実車固有値を含まない完全なPID定義を返します。</remarks>
    /// <param name="request">synthetic Service/PID。</param>
    /// <param name="requiredByteCount">formulaが必要とするbyte数。</param>
    /// <param name="formula">synthetic制限付き式。</param>
    /// <param name="unit">synthetic semantic unit。</param>
    /// <param name="minimum">inclusive下限。</param>
    /// <param name="maximum">inclusive上限。</param>
    /// <param name="revision">synthetic definition revision。</param>
    /// <returns>manifest snapshotへ変換するPID定義。</returns>
    private static OBDPIDDefinition MakeDefinition(
        OBDPIDRequest request,
        int requiredByteCount,
        string formula,
        string unit,
        double minimum,
        double maximum,
        int revision) =>
        new(
            service: request.Service,
            pid: request.Pid,
            nameKey: "fixture.pid.name",
            requiredByteCount: requiredByteCount,
            formula: formula,
            unit: unit,
            minimumValue: minimum,
            maximumValue: maximum,
            sourceURI: "https://fixture.invalid/pid",
            revision: revision);

    /// <summary>最初のsynthetic PID要求です。</summary>
    private static OBDPIDRequest FirstRequest => new(1, 12);

    /// <summary>2番目のsynthetic PID要求です。</summary>
    private static OBDPIDRequest SecondRequest => new(1, 13);

    /// <summary>実sessionと無関係な固定session IDです。</summary>
    private static ConnectionSessionID SyntheticSessionID =>
        new(new Guid([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 90]));

    /// <summary>use case実行に必要なtest依存を保持します。</summary>
    /// <param name="UseCase">検証対象use caseです。</param>
    /// <param name="Input">実行する完全入力です。</param>
    /// <param name="AcquisitionRepository">manifestと開始境界の原子保存spyです。</param>
    /// <param name="Clock">retry間で異なる時刻を返し得るclock spyです。</param>
    /// <param name="StartedAt">開始eventへ記録する固定時刻です。</param>
    private sealed record UseCaseFixture(
        CreateConnectionSessionAcquisitionManifestUseCase UseCase,
        CreateConnectionSessionAcquisitionManifestInput Input,
        AcquisitionRepositorySpy AcquisitionRepository,
        AdvancingClock Clock,
        DateTimeOffset StartedAt)
    {
        /// <summary>初期世代を有効化してuse caseを実行します。</summary>
        /// <remarks>責務: fixture入力と同じpolling世代でmanifest作成を開始します。</remarks>
        /// <returns>保存成功後のRaw開始許可。</returns>
        public Task<ConnectionSessionRawAcquisitionPermission> Execute()
        {
            UseCase.Activate(Input.Generation);
            return UseCase.Execute(Input);
        }
    }

    /// <summary>test比較用に複数error型を排他的に包みます。</summary>
    private abstract record CapturedError
    {
        /// <summary>Application入力または世代errorです。</summary>
        public sealed record Application(CreateConnectionSessionAcquisitionManifestError Error) : CapturedError;

        /// <summary>Domain manifest生成errorです。</summary>
        public sealed record Manifest(ConnectionSessionAcquisitionManifestError Error) : CapturedError;

        /// <summary>ordered set生成errorです。</summary>
        public sealed record OrderedSet(OrderedAcquisitionPIDSetError Error) : CapturedError;

        /// <summary>manifest repository errorです。</summary>
        public sealed record Repository(ConnectionSessionAcquisitionRepositoryError Error) : CapturedError;

        /// <summary>testが分類していないerrorです。</summary>
        public sealed record Unexpected : CapturedError;
    }

    /// <summary>呼出しごとに異なる時刻を返すthread-safeなtest clockです。</summary>
    /// <param name="firstDate">最初の呼出しで返す時刻。</param>
    private sealed class AdvancingClock(DateTimeOffset firstDate)
    {
        /// <summary>clock状態を保護するlockです。</summary>
        private readonly Lock _lock = new();

        /// <summary>現在までの呼出し回数です。</summary>
        private int _storedCallCount;

        /// <summary>現在までの時刻要求回数です。</summary>
        public int CallCount
        {
            get
            {
                lock (_lock)
                {
                    return _storedCallCount;
                }
            }
        }

        /// <summary>呼出し回数に応じたsynthetic時刻を返します。</summary>
        /// <remarks>責務: retryがclockを再採番した場合に異なる値となる決定的時刻を供給します。</remarks>
        /// <returns>最初の時刻へ呼出し回数分の秒を加えた値。</returns>
        public DateTimeOffset Now()
        {
            lock (_lock)
            {
                var value = firstDate.AddSeconds(_storedCallCount);
                _storedCallCount += 1;
                return value;
            }
        }
    }

    /// <summary>runtime evidenceを即時返すport fixtureです。</summary>
    /// <param name="storedEvidence">返却するsynthetic runtime evidence。</param>
    private sealed class FixedAcquisitionEvidencePort(ConnectionSessionAcquisitionRuntimeEvidence storedEvidence)
        : IConnectionSessionAcquisitionEvidencePort
    {
        /// <summary>保存済みruntime evidenceを返します。</summary>
        /// <remarks>責務: manifest作成testへ決定的なruntime evidenceを供給します。</remarks>
        /// <returns>初期化時に保持したruntime evidence。</returns>
        public Task<ConnectionSessionAcquisitionRuntimeEvidence> Evidence() => Task.FromResult(storedEvidence);
    }

    /// <summary>runtime取得をtestが再開するまで保留するport fixtureです。</summary>
    /// <param name="storedEvidence">再開時に返すsynthetic runtime evidence。</param>
    private sealed class SuspendedAcquisitionEvidencePort(ConnectionSessionAcquisitionRuntimeEvidence storedEvidence)
        : IConnectionSessionAcquisitionEvidencePort
    {
        /// <summary>状態を保護するlockです。</summary>
        private readonly Lock _lock = new();

        /// <summary>保留中のruntime要求です。</summary>
        private TaskCompletionSource<ConnectionSessionAcquisitionRuntimeEvidence>? _continuation;

        /// <summary>runtime要求が保留状態に入ったかを示します。</summary>
        public bool IsWaiting
        {
            get
            {
                lock (_lock)
                {
                    return _continuation is not null;
                }
            }
        }

        /// <summary>testが再開するまでruntime要求を保留します。</summary>
        /// <remarks>責務: polling世代変更を挟めるasync evidence取得境界を再現します。</remarks>
        /// <returns><c>Resume()</c> 後のsynthetic runtime evidence。</returns>
        public Task<ConnectionSessionAcquisitionRuntimeEvidence> Evidence()
        {
            lock (_lock)
            {
                _continuation = new TaskCompletionSource<ConnectionSessionAcquisitionRuntimeEvidence>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return _continuation.Task;
            }
        }

        /// <summary>保留中のruntime要求を成功で再開します。</summary>
        /// <remarks>責務: 1件の保留要求へ保持済みruntime evidenceを返します。</remarks>
        public void Resume()
        {
            lock (_lock)
            {
                _continuation?.SetResult(storedEvidence);
                _continuation = null;
            }
        }
    }

    /// <summary>manifestとRaw境界の原子保存呼出しと保存値を記録します。</summary>
    /// <param name="saveError">初回保存でも返すrepository error。</param>
    private sealed class AcquisitionRepositorySpy(ConnectionSessionAcquisitionRepositoryError? saveError)
        : IConnectionSessionAcquisitionRepository
    {
        /// <summary>保存済みmanifestです。</summary>
        public ConnectionSessionAcquisitionManifest? StoredManifest { get; private set; }

        /// <summary>保存済みRaw開始境界時刻です。</summary>
        public DateTimeOffset? StoredStartedAt { get; private set; }

        /// <summary>保存済みRaw終了境界です。</summary>
        public AcquisitionRawBoundaryEvidence? StoredEnd { get; private set; }

        /// <summary>原子保存呼出し回数です。</summary>
        public int SaveStartCallCount { get; private set; }

        /// <summary>manifestと開始境界の片方だけが観測可能かを示します。</summary>
        public bool HasObservablePartialSuccess => (StoredManifest is null) != (StoredStartedAt is null);

        /// <summary>manifestとRaw開始境界を一度だけ同時に記録します。</summary>
        /// <remarks>責務: 部分状態を公開せずduplicate、conflict、unavailableをfixtureとして再現します。</remarks>
        /// <param name="manifest">保存対象manifest。</param>
        /// <param name="startedAt">保存対象の開始境界時刻。</param>
        /// <param name="sessionID">synthetic session識別子。</param>
        public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID)
        {
            SaveStartCallCount += 1;
            if (saveError is { } error)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(error);
            }
            if (StoredManifest is { } storedManifest && StoredStartedAt is { } storedStartedAt)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(
                    storedManifest.Equals(manifest) && storedStartedAt == startedAt
                        ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                        : ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            StoredManifest = manifest;
            StoredStartedAt = startedAt;
        }

        /// <summary>Raw終了境界を一度だけ追記します。</summary>
        /// <remarks>責務: 開始済みfixtureへ終了eventを既存値の更新なしで追記します。</remarks>
        /// <param name="endedAt">終了境界時刻。</param>
        /// <param name="reason">終了理由。</param>
        /// <param name="sessionID">synthetic session識別子。</param>
        public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID)
        {
            if (StoredStartedAt is not { } storedStartedAt)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
            }
            var evidence = new AcquisitionRawBoundaryEvidence.Ended(endedAt, reason);
            if (StoredEnd is { } storedEnd)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(
                    storedEnd.Equals(evidence)
                        ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                        : ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            if (endedAt < storedStartedAt)
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.EndBeforeStart);
            }
            StoredEnd = evidence;
        }

        /// <summary>保存済みmanifestを返します。</summary>
        /// <remarks>責務: spyが保持するmanifestを変更せず読み取ります。</remarks>
        /// <param name="sessionID">synthetic session識別子。</param>
        /// <returns>保存済みmanifest。</returns>
        public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID) =>
            StoredManifest ?? throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);

        /// <summary>保存済み境界eventを返します。</summary>
        /// <remarks>責務: spyが記録した開始と終了eventを追記順のまま読み取ります。</remarks>
        /// <param name="sessionID">synthetic session識別子。</param>
        /// <returns>保存済み境界event列。</returns>
        public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID)
        {
            var evidence = new List<AcquisitionRawBoundaryEvidence>();
            if (StoredStartedAt is { } storedStartedAt)
            {
                evidence.Add(new AcquisitionRawBoundaryEvidence.Started(storedStartedAt));
            }
            if (StoredEnd is { } storedEnd)
            {
                evidence.Add(storedEnd);
            }
            return evidence;
        }
    }
}
