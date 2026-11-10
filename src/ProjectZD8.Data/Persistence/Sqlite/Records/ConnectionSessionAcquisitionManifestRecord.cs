using Microsoft.Data.Sqlite;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Records;

/// <summary>canonical比較に用いる完全manifestと開始microsecondの組です。</summary>
/// <param name="Manifest">PID定義をService/PID順へ正規化した完全manifest。</param>
/// <param name="StartedAtMicroseconds">Unix epochからの開始microsecond。</param>
internal sealed record CanonicalAcquisitionStart(ConnectionSessionAcquisitionManifest Manifest, long StartedAtMicroseconds);

/// <summary>1件のappend-only Raw開始または終了境界の保存行です。</summary>
/// <param name="EventKind"><c>started</c> または <c>ended</c> のevent種別。</param>
/// <param name="OccurredAtMicroseconds">Unix epochからのmicrosecond時刻。</param>
/// <param name="EndReason">終了eventの直接原因。</param>
internal sealed record AcquisitionRawBoundaryRow(string EventKind, long OccurredAtMicroseconds, string? EndReason);

/// <summary>取得manifest、PID snapshot、要求順、Raw境界の4表とDomain値を相互変換します。</summary>
/// <remarks>
/// iOS版 <c>GRDBConnectionSessionAcquisitionRepository</c> のmanifest・境界部分と、
/// <c>ConnectionSessionAcquisitionManifestRecord</c> などの4つのRecordに相当します。
/// どのメソッドも呼出元のトランザクション内で動き、自分では確定しません。
/// </remarks>
internal static class ConnectionSessionAcquisitionManifestRecord
{
    /// <summary>入力manifestと開始時刻をDB比較用のcanonical値へ変換します。</summary>
    /// <remarks>責務: PID snapshot順を除外し要求順とmicrosecond時刻を保持する比較値を生成します。</remarks>
    /// <param name="manifest">canonical化する完全manifest。</param>
    /// <param name="startedAt">canonical化する開始時刻。</param>
    /// <returns>DB readbackと比較できるcanonical開始aggregate。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">semantic field欠落の場合は <c>Unavailable</c>。</exception>
    internal static CanonicalAcquisitionStart Canonicalize(ConnectionSessionAcquisitionManifest manifest, DateTimeOffset startedAt)
    {
        if (manifest.OrderedRequestedPIDs is not { } ordered)
        {
            throw Unavailable();
        }
        var sorted = manifest.PidDefinitions
            .OrderBy(definition => definition.Request.Service)
            .ThenBy(definition => definition.Request.Pid)
            .ToArray();
        var canonical = new ConnectionSessionAcquisitionManifest(
            manifestVersion: manifest.ManifestVersion,
            applicationVersion: manifest.ApplicationVersion,
            schemaContractVersion: manifest.SchemaContractVersion,
            pollingPolicyVersion: manifest.PollingPolicyVersion,
            orderedRequestedPIDs: ordered,
            pidDefinitions: sorted,
            acquisitionPlatform: manifest.AcquisitionPlatform,
            modelInputManifestVersion: manifest.ModelInputManifestVersion);
        ValidateComplete(canonical);
        return new CanonicalAcquisitionStart(canonical, AcquisitionMicroseconds.From(startedAt));
    }

    /// <summary>親セッションが存在するかを返します。</summary>
    /// <remarks>責務: 1件のセッションキーが接続セッション表にあるかを確認します。</remarks>
    /// <param name="connection">実行中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="key">セッションのDBキー。</param>
    /// <returns>存在する場合は <see langword="true"/>。</returns>
    internal static bool SessionExists(SqliteConnection connection, SqliteTransaction? transaction, string key) =>
        SqliteDatabase.Scalar(connection, transaction, "SELECT id FROM connection_sessions WHERE id = $1", key) is not null;

    /// <summary>sealed manifestがあるかを返します。</summary>
    /// <remarks>責務: batch保存の前提となる確定済みmanifestの有無を確認します。</remarks>
    /// <param name="connection">実行中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="key">セッションのDBキー。</param>
    /// <returns>確定済みmanifestがある場合は <see langword="true"/>。</returns>
    internal static bool IsSealed(SqliteConnection connection, SqliteTransaction? transaction, string key) =>
        SqliteDatabase.Scalar(
            connection,
            transaction,
            "SELECT sessionID FROM connection_session_acquisition_manifests WHERE sessionID = $1 AND isSealed = 1",
            key) is not null;

    /// <summary>manifestの要求順PIDを位置順で返します。</summary>
    /// <remarks>責務: 1件のセッションのordered PID行をService/PID要求の列へ変換します。</remarks>
    /// <param name="connection">実行中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="key">セッションのDBキー。</param>
    /// <returns>(位置, 要求) の列。</returns>
    internal static List<(int Ordinal, OBDPIDRequest Request)> OrderedPIDs(SqliteConnection connection, SqliteTransaction? transaction, string key) =>
        SqliteDatabase.Query(
            connection,
            transaction,
            "SELECT ordinal, service, pid FROM connection_session_acquisition_ordered_pids WHERE sessionID = $1 ORDER BY ordinal",
            reader => (reader.GetInt32(0), new OBDPIDRequest((byte)reader.GetInt64(1), (byte)reader.GetInt64(2))),
            key);

    /// <summary>新規開始aggregateを未確定から確定まで同じトランザクションへ挿入します。</summary>
    /// <remarks>責務: manifest、PID定義、要求順、開始境界を順に保存して最後にsealします。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="start">保存するcanonical開始aggregate。</param>
    /// <param name="key">親接続セッションのDBキー。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">seal件数が1件でない場合は <c>Unavailable</c>。</exception>
    internal static void InsertStart(SqliteConnection connection, SqliteTransaction transaction, CanonicalAcquisitionStart start, string key)
    {
        var manifest = start.Manifest;
        if (manifest.ManifestVersion is not { } manifestVersion
            || manifest.ApplicationVersion is not { } applicationVersion
            || manifest.SchemaContractVersion is not { } schemaContractVersion
            || manifest.PollingPolicyVersion is not { } pollingPolicyVersion
            || manifest.OrderedRequestedPIDs is not { } ordered
            || manifest.AcquisitionPlatform is not { } platform
            || manifest.ModelInputManifestVersion is not { } modelInputManifestVersion)
        {
            throw Unavailable();
        }
        SqliteDatabase.Execute(
            connection,
            transaction,
            "INSERT INTO connection_session_acquisition_manifests VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 0)",
            key,
            manifestVersion,
            applicationVersion.MarketingVersion,
            applicationVersion.BuildVersion,
            schemaContractVersion,
            pollingPolicyVersion,
            platform.ToRawValue(),
            modelInputManifestVersion);
        foreach (var definition in manifest.PidDefinitions)
        {
            var row = DefinitionRow(definition);
            SqliteDatabase.Execute(
                connection,
                transaction,
                "INSERT INTO connection_session_acquisition_pid_definitions VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)",
                key,
                (int)definition.Request.Service,
                (int)definition.Request.Pid,
                row.CapabilitySupport,
                row.IsCollectionEnabled ? 1 : 0,
                row.DefinitionRevision,
                row.RequiredByteCount,
                row.CanonicalizationVersion,
                row.Expression,
                row.Unit,
                row.RangeKind,
                row.Minimum,
                row.Maximum);
        }
        for (var ordinal = 0; ordinal < ordered.Requests.Count; ordinal++)
        {
            var request = ordered.Requests[ordinal];
            SqliteDatabase.Execute(
                connection,
                transaction,
                "INSERT INTO connection_session_acquisition_ordered_pids VALUES ($1, $2, $3, $4)",
                key,
                ordinal,
                (int)request.Service,
                (int)request.Pid);
        }
        SqliteDatabase.Execute(
            connection,
            transaction,
            "INSERT INTO connection_session_acquisition_raw_boundaries VALUES ($1, 'started', $2, NULL)",
            key,
            start.StartedAtMicroseconds);
        var sealedCount = SqliteDatabase.Execute(
            connection,
            transaction,
            "UPDATE connection_session_acquisition_manifests SET isSealed = 1 WHERE sessionID = $1 AND isSealed = 0",
            key);
        if (sealedCount != 1)
        {
            throw Unavailable();
        }
    }

    /// <summary>DBの4表から完全なcanonical開始aggregateを読み取ります。</summary>
    /// <remarks>責務: 1件のsessionに属するsealed manifest、PID集合、要求順、開始境界を破損検知付きで復元します。</remarks>
    /// <param name="connection">実行中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="key">読取対象のセッションキー。</param>
    /// <returns>全行が未保存なら <see langword="null"/>、完全保存ならcanonical aggregate。</returns>
    /// <exception cref="Exception">部分状態、decode不能、ordinal不連続の場合（呼出元で <c>Unavailable</c> へ変換）。</exception>
    internal static CanonicalAcquisitionStart? ReadCanonicalStart(SqliteConnection connection, SqliteTransaction? transaction, string key)
    {
        var manifestRows = SqliteDatabase.Query(
            connection,
            transaction,
            "SELECT manifestVersion, applicationMarketingVersion, applicationBuildVersion, schemaContractVersion, pollingPolicyVersion, acquisitionPlatform, modelInputManifestVersion, isSealed FROM connection_session_acquisition_manifests WHERE sessionID = $1",
            reader => (
                ManifestVersion: reader.GetInt32(0),
                Marketing: reader.GetString(1),
                Build: reader.GetString(2),
                SchemaContract: reader.GetInt32(3),
                PollingPolicy: reader.GetInt32(4),
                Platform: reader.GetString(5),
                ModelInput: reader.GetInt32(6),
                IsSealed: reader.GetInt64(7) != 0),
            key);
        var definitions = SqliteDatabase.Query(
            connection,
            transaction,
            "SELECT service, pid, capabilitySupport, isCollectionEnabled, definitionRevision, requiredByteCount, formulaCanonicalizationVersion, formulaExpression, unit, validityRangeKind, minimumValue, maximumValue FROM connection_session_acquisition_pid_definitions WHERE sessionID = $1 ORDER BY service, pid",
            MakeDomainPIDDefinition,
            key);
        var ordered = OrderedPIDs(connection, transaction, key);
        var boundaries = BoundaryRows(connection, transaction, key);
        ValidateBoundarySequence(boundaries);
        if (manifestRows.Count == 0)
        {
            if (definitions.Count != 0 || ordered.Count != 0 || boundaries.Count != 0)
            {
                throw Unavailable();
            }
            return null;
        }
        var manifestRow = manifestRows[0];
        var started = boundaries.FirstOrDefault(boundary => boundary.EventKind == "started");
        if (!manifestRow.IsSealed
            || started is null
            || boundaries.Count(boundary => boundary.EventKind == "started") != 1
            || started.EndReason is not null
            || ordered.Select((row, offset) => row.Ordinal == offset).Any(matches => !matches))
        {
            throw Unavailable();
        }
        if (!ConnectionSessionAcquisitionPlatformRawValue.TryParse(manifestRow.Platform, out var platform))
        {
            throw Unavailable();
        }
        var manifest = new ConnectionSessionAcquisitionManifest(
            manifestVersion: manifestRow.ManifestVersion,
            applicationVersion: new AcquisitionApplicationVersion(manifestRow.Marketing, manifestRow.Build),
            schemaContractVersion: manifestRow.SchemaContract,
            pollingPolicyVersion: manifestRow.PollingPolicy,
            orderedRequestedPIDs: new OrderedAcquisitionPIDSet(ordered.Select(row => row.Request).ToArray()),
            pidDefinitions: definitions,
            acquisitionPlatform: platform,
            modelInputManifestVersion: manifestRow.ModelInput);
        ValidateComplete(manifest);
        return new CanonicalAcquisitionStart(manifest, started.OccurredAtMicroseconds);
    }

    /// <summary>sessionのRaw境界行を開始、終了順で取得します。</summary>
    /// <remarks>責務: 1件のsessionに属する境界行を安定event順へ並べます。</remarks>
    /// <param name="connection">実行中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="key">読取対象のセッションキー。</param>
    /// <returns><c>started</c>、<c>ended</c> の順に並ぶ行。</returns>
    internal static List<AcquisitionRawBoundaryRow> BoundaryRows(SqliteConnection connection, SqliteTransaction? transaction, string key) =>
        SqliteDatabase.Query(
            connection,
            transaction,
            "SELECT eventKind, occurredAtMicroseconds, endReason FROM connection_session_acquisition_raw_boundaries WHERE sessionID = $1 ORDER BY CASE eventKind WHEN 'started' THEN 0 ELSE 1 END",
            reader => new AcquisitionRawBoundaryRow(reader.GetString(0), reader.GetInt64(1), SqliteValueConversion.NullableString(reader, 2)),
            key);

    /// <summary>DBから読んだRaw境界列のappend-only時系列を検証します。</summary>
    /// <remarks>責務: 空列または正しいstarted、任意の後続endedだけを有効な保存状態として許可します。</remarks>
    /// <param name="rows">event順へ整列済みのRaw境界行。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">event種別、reason、個数、時系列が不正な場合は <c>Unavailable</c>。</exception>
    internal static void ValidateBoundarySequence(IReadOnlyList<AcquisitionRawBoundaryRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }
        if (rows.Count > 2 || rows[0].EventKind != "started" || rows[0].EndReason is not null)
        {
            throw Unavailable();
        }
        if (rows.Count != 2)
        {
            return;
        }
        if (rows[1].EventKind != "ended"
            || rows[1].OccurredAtMicroseconds < rows[0].OccurredAtMicroseconds
            || rows[1].EndReason is not { } reason
            || !ConnectionSessionEndReasonRawValue.TryParse(reason, out _))
        {
            throw Unavailable();
        }
    }

    /// <summary>保存済み境界行をDomain eventへ復元します。</summary>
    /// <remarks>責務: 検証済みmicrosecond境界行を変更せずDomain event列へ変換します。</remarks>
    /// <param name="rows">検証済みの境界行。</param>
    /// <returns>開始から終了の順に並ぶ境界event。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">保存値破損の場合は <c>Unavailable</c>。</exception>
    internal static IReadOnlyList<AcquisitionRawBoundaryEvidence> MakeBoundaryEvidence(IReadOnlyList<AcquisitionRawBoundaryRow> rows) =>
        rows.Select<AcquisitionRawBoundaryRow, AcquisitionRawBoundaryEvidence>(row =>
        {
            var date = AcquisitionMicroseconds.ToDate(row.OccurredAtMicroseconds);
            if (row.EventKind == "started" && row.EndReason is null)
            {
                return new AcquisitionRawBoundaryEvidence.Started(date);
            }
            if (row.EventKind == "ended" && row.EndReason is { } raw && ConnectionSessionEndReasonRawValue.TryParse(raw, out var reason))
            {
                return new AcquisitionRawBoundaryEvidence.Ended(date, reason);
            }
            throw Unavailable();
        }).ToArray();

    /// <summary>終了境界を1件追記します。</summary>
    /// <remarks>責務: 検証済みの終了境界をappend-only表へ挿入します。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="key">セッションのDBキー。</param>
    /// <param name="endedMicroseconds">終了境界のmicrosecond。</param>
    /// <param name="reason">取得停止理由。</param>
    internal static void InsertEnd(SqliteConnection connection, SqliteTransaction transaction, string key, long endedMicroseconds, ConnectionSessionEndReason reason) =>
        SqliteDatabase.Execute(
            connection,
            transaction,
            "INSERT INTO connection_session_acquisition_raw_boundaries VALUES ($1, 'ended', $2, $3)",
            key,
            endedMicroseconds,
            reason.ToRawValue());

    /// <summary>manifest開始済みsessionへ終了境界を親トランザクション内で追記します。</summary>
    /// <remarks>責務: 取得開始の有無を保ったまま、開始済みの取得だけへ終了境界を追加します。</remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="key">終了対象のセッションキー。</param>
    /// <param name="endedAt">終了境界日時。</param>
    /// <param name="reason">取得停止理由。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">境界競合、時系列違反の場合。</exception>
    internal static void AppendEndIfAcquisitionStarted(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        DateTimeOffset endedAt,
        ConnectionSessionEndReason reason)
    {
        var boundaries = BoundaryRows(connection, transaction, key);
        var started = boundaries.FirstOrDefault(boundary => boundary.EventKind == "started");
        if (started is null)
        {
            if (boundaries.Count != 0)
            {
                throw Unavailable();
            }
            return;
        }
        var endedMicroseconds = AcquisitionMicroseconds.From(endedAt);
        if (boundaries.FirstOrDefault(boundary => boundary.EventKind == "ended") is { } ended)
        {
            if (ended.OccurredAtMicroseconds != endedMicroseconds || ended.EndReason != reason.ToRawValue())
            {
                throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.Conflict);
            }
            return;
        }
        if (endedMicroseconds < started.OccurredAtMicroseconds)
        {
            throw new ConnectionSessionAcquisitionRepositoryException(ConnectionSessionAcquisitionRepositoryError.EndBeforeStart);
        }
        InsertEnd(connection, transaction, key, endedMicroseconds, reason);
    }

    /// <summary>manifestが新規保存に必要な全semantic fieldを持つことを検証します。</summary>
    /// <remarks>責務: legacy欠落を含むmanifestを新規保存対象から拒否します。</remarks>
    /// <param name="manifest">検証するmanifest。</param>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">必須field欠落時は <c>Unavailable</c>。</exception>
    private static void ValidateComplete(ConnectionSessionAcquisitionManifest manifest)
    {
        if (manifest.ManifestVersion is null
            || manifest.ApplicationVersion is null
            || manifest.SchemaContractVersion is null
            || manifest.PollingPolicyVersion is null
            || manifest.OrderedRequestedPIDs is null
            || manifest.AcquisitionPlatform is null
            || manifest.ModelInputManifestVersion is null)
        {
            throw Unavailable();
        }
        foreach (var definition in manifest.PidDefinitions)
        {
            _ = DefinitionRow(definition);
        }
    }

    /// <summary>Domain PID snapshotを完全な永続化列へ変換します。</summary>
    /// <remarks>責務: 1件の完全PID snapshotを範囲kindを明示したDB列へ変換します。</remarks>
    /// <param name="definition">変換するPID snapshot。</param>
    /// <returns>挿入する列値。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">semantic fieldが欠落する場合は <c>Unavailable</c>。</exception>
    private static (string CapabilitySupport, bool IsCollectionEnabled, int DefinitionRevision, int RequiredByteCount, int CanonicalizationVersion, string Expression, string Unit, string RangeKind, double? Minimum, double? Maximum) DefinitionRow(
        AcquisitionPIDDefinitionSnapshot definition)
    {
        if (definition.CapabilitySupport is not { } support
            || definition.IsCollectionEnabled is not { } isEnabled
            || definition.DefinitionRevision is not { } revision
            || definition.RequiredByteCount is not { } byteCount
            || definition.DefinitionIdentity is not { } identity
            || definition.Unit is not { Length: > 0 } unit
            || definition.ValidityRange is not { } range)
        {
            throw Unavailable();
        }
        return (
            support.ToRawValue(),
            isEnabled,
            revision,
            byteCount,
            identity.CanonicalizationVersion,
            identity.Expression,
            unit,
            range.Minimum is null ? "notDeclared" : "inclusive",
            range.Minimum,
            range.Maximum);
    }

    /// <summary>永続化PID行をDomain snapshotへ復元します。</summary>
    /// <remarks>責務: 1件の検証済みPID定義行を完全なDomain snapshotへ変換します。</remarks>
    /// <param name="reader">読取中の行。</param>
    /// <returns>完全な取得時PID snapshot。</returns>
    /// <exception cref="ConnectionSessionAcquisitionRepositoryException">保存値が不正な場合は <c>Unavailable</c>。</exception>
    private static AcquisitionPIDDefinitionSnapshot MakeDomainPIDDefinition(SqliteDataReader reader)
    {
        var service = reader.GetInt64(0);
        var pid = reader.GetInt64(1);
        if (service is < 0 or > byte.MaxValue || pid is < 0 or > byte.MaxValue
            || !AcquisitionPIDCapabilitySupportRawValue.TryParse(reader.GetString(2), out var capability))
        {
            throw Unavailable();
        }
        var minimum = SqliteValueConversion.NullableDouble(reader, 10);
        var maximum = SqliteValueConversion.NullableDouble(reader, 11);
        var validityRange = reader.GetString(9) switch
        {
            "notDeclared" when minimum is null && maximum is null => AcquisitionPIDValidityRange.NotDeclared,
            "inclusive" when minimum is { } low && maximum is { } high => AcquisitionPIDValidityRange.Inclusive(low, high),
            _ => throw Unavailable(),
        };
        return new AcquisitionPIDDefinitionSnapshot(
            request: new OBDPIDRequest((byte)service, (byte)pid),
            capabilitySupport: capability,
            isCollectionEnabled: reader.GetInt64(3) != 0,
            definitionRevision: reader.GetInt32(4),
            requiredByteCount: reader.GetInt32(5),
            definitionIdentity: new AcquisitionPIDDefinitionIdentity(reader.GetInt32(6), reader.GetString(7)),
            unit: reader.GetString(8),
            validityRange: validityRange);
    }

    /// <summary>保存先利用不能のエラーを生成します。</summary>
    /// <remarks>責務: 破損や不完全状態を1種類のrepository errorへ変換します。</remarks>
    /// <returns><c>Unavailable</c> のrepository例外。</returns>
    private static ConnectionSessionAcquisitionRepositoryException Unavailable() =>
        new(ConnectionSessionAcquisitionRepositoryError.Unavailable);
}
