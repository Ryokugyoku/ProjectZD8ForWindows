using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.Repositories;

/// <summary>session取得repositoryの原子開始保存とappend-only終了契約を検証します。</summary>
public sealed class ConnectionSessionAcquisitionRepositoryTests
{
    /// <summary>Swift の <c>Date(timeIntervalSinceReferenceDate: 0)</c> に当たる基準時刻です。</summary>
    private static readonly DateTimeOffset ReferenceDate = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>manifestと開始境界を1回の操作で保存して読み取ります。</summary>
    /// <remarks>責務: repository contractが開始に必要な両証拠を部分状態なしで公開することを確認します。</remarks>
    [Fact]
    public void AtomicallySavesManifestAndStartEvidence()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();
        var sessionID = SyntheticSessionID(1);
        var manifest = MakeManifest(buildVersion: "fixture-build-a");
        var startedAt = Timestamp(100);

        repository.SaveStartOnce(manifest, startedAt, sessionID);

        Assert.Equal(manifest, repository.Manifest(sessionID));
        Assert.Equal(
            new AcquisitionRawBoundaryEvidence[] { new AcquisitionRawBoundaryEvidence.Started(startedAt) },
            repository.BoundaryEvidence(sessionID));
        Assert.False(repository.HasPartialEvidence(sessionID));
    }

    /// <summary>同じsession、manifest、開始eventの再実行を重複として区別します。</summary>
    /// <remarks>責務: exact retryを成功へ既定化せず既存値を更新しないことを確認します。</remarks>
    [Fact]
    public void DistinguishesExactStartRetryAsDuplicateWithoutMutation()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();
        var sessionID = SyntheticSessionID(2);
        var manifest = MakeManifest(buildVersion: "fixture-build-a");
        var startedAt = Timestamp(100);
        repository.SaveStartOnce(manifest, startedAt, sessionID);

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(
            () => repository.SaveStartOnce(manifest, startedAt, sessionID));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.Duplicate, error.Reason);
        Assert.Equal(manifest, repository.Manifest(sessionID));
        Assert.Equal(
            new AcquisitionRawBoundaryEvidence[] { new AcquisitionRawBoundaryEvidence.Started(startedAt) },
            repository.BoundaryEvidence(sessionID));
    }

    /// <summary>同じsessionの異なるmanifestを競合として区別します。</summary>
    /// <remarks>責務: manifest競合が既存manifestまたは開始境界をin-place更新しないことを確認します。</remarks>
    [Fact]
    public void DistinguishesManifestConflictWithoutMutation()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();
        var sessionID = SyntheticSessionID(3);
        var original = MakeManifest(buildVersion: "fixture-build-a");
        var conflicting = MakeManifest(buildVersion: "fixture-build-b");
        var startedAt = Timestamp(100);
        repository.SaveStartOnce(original, startedAt, sessionID);

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(
            () => repository.SaveStartOnce(conflicting, startedAt, sessionID));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.Conflict, error.Reason);
        Assert.Equal(original, repository.Manifest(sessionID));
        Assert.Equal(
            new AcquisitionRawBoundaryEvidence[] { new AcquisitionRawBoundaryEvidence.Started(startedAt) },
            repository.BoundaryEvidence(sessionID));
    }

    /// <summary>同じsessionの異なる開始eventを競合として区別します。</summary>
    /// <remarks>責務: 開始境界競合が既存manifestまたは開始境界をin-place更新しないことを確認します。</remarks>
    [Fact]
    public void DistinguishesStartBoundaryConflictWithoutMutation()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();
        var sessionID = SyntheticSessionID(4);
        var manifest = MakeManifest(buildVersion: "fixture-build-a");
        repository.SaveStartOnce(manifest, Timestamp(100), sessionID);

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(
            () => repository.SaveStartOnce(manifest, Timestamp(101), sessionID));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.Conflict, error.Reason);
        Assert.Equal(
            new AcquisitionRawBoundaryEvidence[] { new AcquisitionRawBoundaryEvidence.Started(Timestamp(100)) },
            repository.BoundaryEvidence(sessionID));
    }

    /// <summary>原子保存失敗時に片方だけの証拠を公開しません。</summary>
    /// <remarks>責務: unavailable failureをmanifestまたは開始境界だけの部分成功へ変換しないことを確認します。</remarks>
    [Fact]
    public void UnavailableStartSaveDoesNotExposePartialEvidence()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository(saveError: ConnectionSessionAcquisitionRepositoryError.Unavailable);
        var sessionID = SyntheticSessionID(5);

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(
            () => repository.SaveStartOnce(MakeManifest(buildVersion: "fixture-build-a"), Timestamp(100), sessionID));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.Unavailable, error.Reason);
        Assert.False(repository.HasManifest(sessionID));
        Assert.Empty(repository.BoundaryEvidence(sessionID));
        Assert.False(repository.HasPartialEvidence(sessionID));
    }

    /// <summary>開始後に終了をappend-onlyで追記します。</summary>
    /// <remarks>責務: 終了境界の追記が保存済みmanifestと開始境界を変更しないことを確認します。</remarks>
    [Fact]
    public void AppendsEndWithoutMutatingStartEvidence()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();
        var sessionID = SyntheticSessionID(6);
        var manifest = MakeManifest(buildVersion: "fixture-build-a");
        repository.SaveStartOnce(manifest, Timestamp(100), sessionID);

        repository.AppendEnd(Timestamp(200), ConnectionSessionEndReason.UserDisconnected, sessionID);

        Assert.Equal(manifest, repository.Manifest(sessionID));
        Assert.Equal(
            new AcquisitionRawBoundaryEvidence[]
            {
                new AcquisitionRawBoundaryEvidence.Started(Timestamp(100)),
                new AcquisitionRawBoundaryEvidence.Ended(Timestamp(200), ConnectionSessionEndReason.UserDisconnected),
            },
            repository.BoundaryEvidence(sessionID));
    }

    /// <summary>開始前の終了追記を拒否します。</summary>
    /// <remarks>責務: 終了eventを開始証拠のないsessionへ関連付けられないことを確認します。</remarks>
    [Fact]
    public void RejectsEndWithoutStart()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(
            () => repository.AppendEnd(Timestamp(200), ConnectionSessionEndReason.ConnectionLost, SyntheticSessionID(7)));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing, error.Reason);
    }

    /// <summary>開始より前の終了追記を拒否します。</summary>
    /// <remarks>責務: Raw取得境界を逆転した時系列として保存できないことを確認します。</remarks>
    [Fact]
    public void RejectsEndBeforeStart()
    {
        var repository = new FakeConnectionSessionAcquisitionRepository();
        var sessionID = SyntheticSessionID(8);
        repository.SaveStartOnce(MakeManifest(buildVersion: "fixture-build-a"), Timestamp(200), sessionID);

        var error = Assert.Throws<ConnectionSessionAcquisitionRepositoryException>(
            () => repository.AppendEnd(Timestamp(100), ConnectionSessionEndReason.AcquisitionFailed, sessionID));
        Assert.Equal(ConnectionSessionAcquisitionRepositoryError.EndBeforeStart, error.Reason);
    }

    /// <summary>識別情報を持たないsynthetic manifest fixtureを生成します。</summary>
    /// <remarks>責務: repository testへbuild versionだけを差し替えた決定的manifestを提供します。</remarks>
    /// <param name="buildVersion">fixture内で競合を作る非実在build文字列。</param>
    /// <returns>repository保存用の完全manifest。</returns>
    private static ConnectionSessionAcquisitionManifest MakeManifest(string buildVersion)
    {
        var request = new OBDPIDRequest(Service: 1, Pid: 12);
        var definition = new AcquisitionPIDDefinitionSnapshot(
            request: request,
            capabilitySupport: AcquisitionPIDCapabilitySupport.Supported,
            isCollectionEnabled: true,
            definitionRevision: 1,
            requiredByteCount: 2,
            definitionIdentity: new AcquisitionPIDDefinitionIdentity(
                canonicalizationVersion: 1,
                expression: "(A * 256 + B) / 4"),
            unit: "rpm",
            validityRange: AcquisitionPIDValidityRange.Inclusive(minimum: 0, maximum: 16_383.75));
        return new ConnectionSessionAcquisitionManifest(
            manifestVersion: 1,
            applicationVersion: new AcquisitionApplicationVersion(
                marketingVersion: "fixture-version",
                buildVersion: buildVersion),
            schemaContractVersion: 1,
            pollingPolicyVersion: 1,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(requests: [request]),
            pidDefinitions: [definition],
            acquisitionPlatform: ConnectionSessionAcquisitionPlatform.MacOS,
            modelInputManifestVersion: 1);
    }

    /// <summary>test用の固定時刻を生成します。</summary>
    /// <remarks>責務: Raw境界testへ実走行と無関係な決定的時刻を返します。</remarks>
    /// <param name="seconds">reference date（2001-01-01 UTC）からの秒数。</param>
    /// <returns>指定秒数のsynthetic時刻。</returns>
    private static DateTimeOffset Timestamp(double seconds) => ReferenceDate.AddSeconds(seconds);

    /// <summary>実sessionと無関係な固定UUIDからtest用session IDを生成します。</summary>
    /// <remarks>責務: repository testの関連付けにだけ使うsynthetic session IDを決定的に返します。</remarks>
    /// <param name="suffix">fixtureごとの末尾byte。</param>
    /// <returns>実データと無関係なsynthetic session ID。</returns>
    private static ConnectionSessionID SyntheticSessionID(byte suffix) =>
        new(new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, suffix));
}

/// <summary>repositoryの原子開始保存とappend-only終了規則をメモリ上で再現します。</summary>
file sealed class FakeConnectionSessionAcquisitionRepository : IConnectionSessionAcquisitionRepository
{
    /// <summary>sessionごとの保存済み取得証拠です。</summary>
    private readonly Dictionary<ConnectionSessionID, StoredAcquisitionEvidence> _storage = [];
    /// <summary>testから注入された開始保存失敗です。</summary>
    private readonly ConnectionSessionAcquisitionRepositoryError? _saveError;

    /// <summary>repository fakeを生成します。</summary>
    /// <remarks>責務: 開始保存時に返す任意errorを固定します。</remarks>
    /// <param name="saveError">初回の原子保存でも返すrepository error。</param>
    public FakeConnectionSessionAcquisitionRepository(ConnectionSessionAcquisitionRepositoryError? saveError = null)
    {
        _saveError = saveError;
    }

    /// <summary>manifestと開始境界を一度だけ同時に保存します。</summary>
    /// <remarks>責務: 部分状態を作らず同じ組の重複と異なる組の競合を区別します。</remarks>
    /// <param name="manifest">保存するimmutable manifest。</param>
    /// <param name="startedAt">保存する開始境界時刻。</param>
    /// <param name="sessionID">synthetic session識別子。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">注入error、同じ組なら <c>Duplicate</c>、異なる組なら <c>Conflict</c>。</exception>
    public void SaveStartOnce(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt, ConnectionSessionID sessionID)
    {
        if (_saveError is { } saveError)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(saveError);
        }
        if (_storage.TryGetValue(sessionID, out var existing))
        {
            throw new ConnectionSessionAcquisitionRepositoryException(
                existing.Manifest == manifest && existing.StartedAt == startedAt
                    ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                    : ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        _storage[sessionID] = new StoredAcquisitionEvidence(manifest, startedAt, End: null);
    }

    /// <summary>Raw終了境界を一度だけ追記します。</summary>
    /// <remarks>責務: 保存済み開始境界を検証して終了eventだけを追記します。</remarks>
    /// <param name="endedAt">終了境界時刻。</param>
    /// <param name="reason">終了理由。</param>
    /// <param name="sessionID">synthetic session識別子。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">開始欠落、時系列違反、重複、または競合を区別するrepository error。</exception>
    public void AppendEnd(DateTimeOffset endedAt, ConnectionSessionEndReason reason, ConnectionSessionID sessionID)
    {
        if (!_storage.TryGetValue(sessionID, out var existing))
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.StartEvidenceMissing);
        }
        var evidence = new AcquisitionRawBoundaryEvidence.Ended(endedAt, reason);
        if (existing.End is { } storedEnd)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(
                storedEnd == evidence
                    ? ConnectionSessionAcquisitionRepositoryError.Duplicate
                    : ConnectionSessionAcquisitionRepositoryError.Conflict);
        }
        if (endedAt < existing.StartedAt)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.EndBeforeStart);
        }
        _storage[sessionID] = existing with { End = evidence };
    }

    /// <summary>保存済みmanifestを読み取ります。</summary>
    /// <remarks>責務: 保存済みmanifestを変更せず返し未登録を明示失敗にします。</remarks>
    /// <param name="sessionID">synthetic session識別子。</param>
    /// <returns>保存済みmanifest。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">未登録の場合は <c>NotFound</c>。</exception>
    public ConnectionSessionAcquisitionManifest Manifest(ConnectionSessionID sessionID)
    {
        if (!_storage.TryGetValue(sessionID, out var existing))
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.NotFound);
        }
        return existing.Manifest;
    }

    /// <summary>保存済み境界eventを読み取ります。</summary>
    /// <remarks>責務: 開始と任意の終了を追記順のまま返します。</remarks>
    /// <param name="sessionID">synthetic session識別子。</param>
    /// <returns>保存済み境界event列。</returns>
    public IReadOnlyList<AcquisitionRawBoundaryEvidence> BoundaryEvidence(ConnectionSessionID sessionID)
    {
        if (!_storage.TryGetValue(sessionID, out var existing))
        {
            return [];
        }
        var evidence = new List<AcquisitionRawBoundaryEvidence> { new AcquisitionRawBoundaryEvidence.Started(existing.StartedAt) };
        if (existing.End is { } end)
        {
            evidence.Add(end);
        }
        return evidence;
    }

    /// <summary>sessionにmanifestが保存済みかを示します。</summary>
    /// <remarks>責務: 原子保存失敗後のmanifest可視性をtestへ返します。</remarks>
    /// <param name="sessionID">synthetic session識別子。</param>
    /// <returns>manifestが保存済みなら <see langword="true"/>。</returns>
    public bool HasManifest(ConnectionSessionID sessionID) => _storage.ContainsKey(sessionID);

    /// <summary>sessionに片方だけの開始証拠が存在するかを示します。</summary>
    /// <remarks>責務: fake内部にもmanifestまたは開始境界だけの部分状態がないことをtestへ返します。</remarks>
    /// <param name="sessionID">synthetic session識別子。</param>
    /// <returns>原子契約に反する部分状態があれば <see langword="true"/>。</returns>
    public bool HasPartialEvidence(ConnectionSessionID sessionID) => false;
}

/// <summary>fake repositoryがsession単位で原子的に保持する取得証拠です。</summary>
/// <param name="Manifest">更新しない取得manifestです。</param>
/// <param name="StartedAt">更新しないRaw開始境界時刻です。</param>
/// <param name="End">後から一度だけ追記するRaw終了境界です。</param>
file sealed record StoredAcquisitionEvidence(
    ConnectionSessionAcquisitionManifest Manifest,
    DateTimeOffset StartedAt,
    AcquisitionRawBoundaryEvidence.Ended? End);
