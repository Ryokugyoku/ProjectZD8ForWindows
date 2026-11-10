using Microsoft.Data.Sqlite;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Persistence.Sqlite.Records;

/// <summary><c>connection_sessions</c> 行とDomain接続セッションを相互変換します。</summary>
/// <remarks>iOS版 <c>ConnectionSessionRecord</c> に相当します。日時列はUTCのTicksです。</remarks>
internal static class ConnectionSessionRecord
{
    /// <summary>接続セッション表の全列です（読取順）。</summary>
    internal const string Columns =
        "id, accountIdentifier, startedAt, endedAt, endReason, stopReviewDecision, vehicleID, vehicleName, " +
        "vehicleDisplayIdentifier, acquisitionPlatform, acquisitionDeviceName, startingOdometerKilometers, " +
        "endingOdometerKilometers, distanceSourceModelCode, rawRecordCount, rawByteCount, localRawState, cloudSyncState, " +
        "manifestDigest, macImportedDeviceID, macImportedDeviceName, macImportedAt, macImportedManifestDigest, rawLastAccessedAt";

    /// <summary>セッションを新規保存または既存行を更新します。</summary>
    /// <remarks>
    /// 責務: 1件のDomain接続セッションを列指向の永続化表現へ変換して保存します。
    /// 子の取得証拠を消さないよう、<c>INSERT OR REPLACE</c> ではなく <c>ON CONFLICT DO UPDATE</c> を使います。
    /// </remarks>
    /// <param name="connection">書込み中の接続。</param>
    /// <param name="transaction">書込み中のトランザクション。</param>
    /// <param name="session">保存するセッション。</param>
    internal static void Save(SqliteConnection connection, SqliteTransaction transaction, ConnectionSession session)
    {
        var summary = session.RawLogSummary;
        var receipt = summary.MacImportReceipt;
        SqliteDatabase.Execute(
            connection,
            transaction,
            $"""
            INSERT INTO connection_sessions ({Columns})
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24)
            ON CONFLICT (id) DO UPDATE SET
                accountIdentifier = excluded.accountIdentifier, startedAt = excluded.startedAt, endedAt = excluded.endedAt,
                endReason = excluded.endReason, stopReviewDecision = excluded.stopReviewDecision, vehicleID = excluded.vehicleID,
                vehicleName = excluded.vehicleName, vehicleDisplayIdentifier = excluded.vehicleDisplayIdentifier,
                acquisitionPlatform = excluded.acquisitionPlatform, acquisitionDeviceName = excluded.acquisitionDeviceName,
                startingOdometerKilometers = excluded.startingOdometerKilometers,
                endingOdometerKilometers = excluded.endingOdometerKilometers,
                distanceSourceModelCode = excluded.distanceSourceModelCode, rawRecordCount = excluded.rawRecordCount,
                rawByteCount = excluded.rawByteCount, localRawState = excluded.localRawState,
                cloudSyncState = excluded.cloudSyncState, manifestDigest = excluded.manifestDigest,
                macImportedDeviceID = excluded.macImportedDeviceID, macImportedDeviceName = excluded.macImportedDeviceName,
                macImportedAt = excluded.macImportedAt, macImportedManifestDigest = excluded.macImportedManifestDigest,
                rawLastAccessedAt = excluded.rawLastAccessedAt
            """,
            SqliteValueConversion.Key(session.Id.RawValue),
            session.AccountIdentifier,
            SqliteValueConversion.Ticks(session.StartedAt),
            SqliteValueConversion.Ticks(session.EndedAt),
            session.EndReason?.ToRawValue(),
            session.StopReviewDecision?.ToRawValue(),
            session.Vehicle is { } vehicle ? SqliteValueConversion.Key(vehicle.Id.RawValue) : null,
            session.Vehicle?.Name,
            session.Vehicle?.DisplayIdentifier,
            session.AcquisitionDevice?.Platform.ToRawValue(),
            session.AcquisitionDevice?.Name,
            session.StartingOdometerKilometers,
            session.EndingOdometerKilometers,
            session.DistanceSourceModelCode,
            summary.RecordCount,
            summary.ByteCount,
            summary.LocalState.ToRawValue(),
            summary.CloudState.ToRawValue(),
            summary.ManifestDigest,
            receipt?.DeviceID,
            receipt?.DeviceName,
            receipt is null ? null : SqliteValueConversion.Ticks(receipt.ImportedAt),
            receipt?.ManifestDigest,
            SqliteValueConversion.Ticks(summary.LastAccessedAt));
    }

    /// <summary>1件のセッションIDの保存行を読み取ります。</summary>
    /// <remarks>責務: 主キーで1行を読み、検証済みDomainセッションへ復元します。</remarks>
    /// <param name="connection">読取中の接続。</param>
    /// <param name="transaction">実行中のトランザクション。</param>
    /// <param name="sessionID">読み取るセッションID。</param>
    /// <returns>保存済みセッション。未保存なら <see langword="null"/>。不正値の行は例外です。</returns>
    /// <exception cref="InvalidDataException">識別子や終了原因が不正な場合。</exception>
    internal static ConnectionSession? Fetch(SqliteConnection connection, SqliteTransaction? transaction, ConnectionSessionID sessionID)
    {
        var sessions = SqliteDatabase.Query(
            connection,
            transaction,
            $"SELECT {Columns} FROM connection_sessions WHERE id = $1",
            reader => MakeDomainSession(reader),
            SqliteValueConversion.Key(sessionID.RawValue));
        return sessions.Count == 0 ? null : sessions[0] ?? throw new InvalidDataException("Connection session contains an invalid identifier or end reason");
    }

    /// <summary>1行をDomainセッションへ復元します。</summary>
    /// <remarks>責務: 1件の検証済みレコードをDomain接続セッションへ復元します（iOS版 <c>makeDomainSession</c>）。</remarks>
    /// <param name="reader">読取中の行（<see cref="Columns"/> の順）。</param>
    /// <returns>復元できた接続セッション。不正な識別値または終了原因の場合は <see langword="null"/>。</returns>
    internal static ConnectionSession? MakeDomainSession(SqliteDataReader reader)
    {
        if (!Guid.TryParse(reader.GetString(0), out var sessionUUID)
            || !ConnectionSessionLocalRawStateRawValue.TryParse(reader.GetString(16), out var localState)
            || !ConnectionSessionCloudSyncStateRawValue.TryParse(reader.GetString(17), out var cloudState))
        {
            return null;
        }
        ConnectionSessionEndReason? reason = null;
        if (SqliteValueConversion.NullableString(reader, 4) is { } endReason)
        {
            if (!ConnectionSessionEndReasonRawValue.TryParse(endReason, out var decoded))
            {
                return null;
            }
            reason = decoded;
        }
        ConnectionSessionVehicle? vehicle = null;
        if (SqliteValueConversion.NullableString(reader, 6) is { } vehicleID
            && SqliteValueConversion.NullableString(reader, 7) is { } vehicleName
            && SqliteValueConversion.NullableString(reader, 8) is { } displayIdentifier
            && Guid.TryParse(vehicleID, out var vehicleUUID))
        {
            vehicle = new ConnectionSessionVehicle(new VehicleID(vehicleUUID), vehicleName, displayIdentifier);
        }
        ConnectionSessionAcquisitionDevice? device = null;
        if (SqliteValueConversion.NullableString(reader, 9) is { } platformRaw
            && ConnectionSessionAcquisitionPlatformRawValue.TryParse(platformRaw, out var platform)
            && SqliteValueConversion.NullableString(reader, 10) is { Length: > 0 } deviceName)
        {
            device = new ConnectionSessionAcquisitionDevice(platform, deviceName);
        }
        ConnectionSessionStopReviewDecision? decision = null;
        if (SqliteValueConversion.NullableString(reader, 5) is { } decisionRaw)
        {
            if (!ConnectionSessionStopReviewDecisionRawValue.TryParse(decisionRaw, out var decoded))
            {
                return null;
            }
            decision = decoded;
        }
        ConnectionSessionMacImportReceipt? receipt = null;
        if (SqliteValueConversion.NullableString(reader, 19) is { } receiptDevice
            && SqliteValueConversion.NullableString(reader, 20) is { } receiptName
            && SqliteValueConversion.NullableInt64(reader, 21) is { } importedAt
            && SqliteValueConversion.NullableString(reader, 22) is { } receiptDigest)
        {
            receipt = new ConnectionSessionMacImportReceipt(receiptDevice, receiptName, SqliteValueConversion.Date(importedAt), receiptDigest);
        }
        var endedAt = SqliteValueConversion.NullableInt64(reader, 3);
        var lastAccessed = SqliteValueConversion.NullableInt64(reader, 23);
        return new ConnectionSession(
            accountIdentifier: reader.GetString(1),
            id: new ConnectionSessionID(sessionUUID),
            startedAt: SqliteValueConversion.Date(reader.GetInt64(2)),
            vehicle: vehicle,
            acquisitionDevice: device)
        {
            EndedAt = endedAt is { } ended ? SqliteValueConversion.Date(ended) : null,
            EndReason = reason,
            StopReviewDecision = decision,
            StartingOdometerKilometers = SqliteValueConversion.NullableDouble(reader, 11),
            EndingOdometerKilometers = SqliteValueConversion.NullableDouble(reader, 12),
            DistanceSourceModelCode = SqliteValueConversion.NullableString(reader, 13),
            RawLogSummary = new ConnectionSessionRawLogSummary(
                recordCount: reader.GetInt64(14),
                byteCount: reader.GetInt64(15),
                localState: localState,
                cloudState: cloudState,
                manifestDigest: SqliteValueConversion.NullableString(reader, 18),
                macImportReceipt: receipt,
                lastAccessedAt: lastAccessed is { } accessed ? SqliteValueConversion.Date(accessed) : null),
        };
    }
}
