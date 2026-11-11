using System.Text;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit の private database で、接続セッションの概要・生ログの資産・Mac 取込受領証・削除の印を交換します
/// （iOS の <c>CloudKitConnectionSessionTransferRepository</c>）。
/// </summary>
/// <remarks>
/// <para>レコード名（fp = SHA-256(ユーザー ID) の小文字 16 進、uuid = セッション ID の小文字）:
/// ConnectionSessionMetadata <c>session-metadata-fp-uuid</c>、ConnectionSessionRawLog <c>raw-session-fp-uuid</c>、
/// ConnectionSessionMacReceipt <c>mac-receipt-fp-uuid-SHA256(deviceID)</c>、ConnectionSessionDeletion <c>deleted-session-fp-uuid</c>。
/// 生ログと概要の JSON は形式バージョン 2（キーを並べ替え、日付は 1970 年からのミリ秒）。</para>
/// <para>使えないとき：設定がない（ローカルモード）なら、取得は空・削除は何もしない・保存は
/// <see cref="ConnectionSessionRepositoryException"/>（Unavailable）。サインインしていないなら、すべて Unavailable を投げます。</para>
/// <para>iOS との違い：レコードがないときの <c>downloadTransfer</c> は、iOS の <c>CKError.unknownItem</c> の代わりに InvalidState を投げます
/// （Domain の既定の実装と同じ）。資産の送受信の進み具合は、送受信のバイト数で知らせます。</para>
/// </remarks>
public sealed class CloudKitConnectionSessionTransferRepository : IConnectionSessionTransferRepository
{
    /// <summary>生ログの資産のレコード種類です。</summary>
    private const string TransferRecordType = "ConnectionSessionRawLog";

    /// <summary>生ログを含まないセッション概要のレコード種類です。</summary>
    private const string MetadataRecordType = "ConnectionSessionMetadata";

    /// <summary>Mac 取込受領証のレコード種類です。</summary>
    private const string ReceiptRecordType = "ConnectionSessionMacReceipt";

    /// <summary>全端末へセッションの削除を伝えるレコード種類です。</summary>
    private const string DeletionRecordType = "ConnectionSessionDeletion";

    /// <summary>生ログの資産のフィールド名です。</summary>
    private const string PayloadAssetKey = "payloadAsset";

    /// <summary>符号化したセッション概要のフィールド名です。</summary>
    private const string MetadataDataKey = "metadataData";

    /// <summary>アカウントの不可逆な識別値のフィールド名です。</summary>
    private const string AccountFingerprintKey = "accountFingerprint";

    /// <summary>セッション UUID のフィールド名です。</summary>
    private const string SessionIDKey = "sessionID";

    /// <summary>形式バージョンのフィールド名です。</summary>
    private const string PayloadFormatVersionKey = "payloadFormatVersion";

    /// <summary>セッション ID と生ログだけを持つ現在の形式です。</summary>
    private const long CurrentPayloadFormatVersion = 2;

    /// <summary>資産のバイト列の SHA-256 のフィールド名です。</summary>
    private const string ManifestDigestKey = "manifestDigest";

    /// <summary>CloudKit のデータベースです。</summary>
    private readonly ICloudKitDatabase _database;

    /// <summary>
    /// CloudKit のデータベースを注入して作ります。
    /// </summary>
    /// <remarks>責務: セッションの転送先を 1 件の CloudKit の実装へ結び付けます。</remarks>
    /// <param name="database">CloudKit のデータベース。</param>
    public CloudKitConnectionSessionTransferRepository(ICloudKitDatabase database)
    {
        _database = database;
    }

    /// <summary>
    /// 生ログを含まないセッション概要を CloudKit へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件のセッション概要を、決まった形の JSON 付きのレコードへ変換します。</remarks>
    /// <param name="metadata">一覧の共有に必要なセッション概要と生ログのマニフェスト。</param>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <exception cref="ConnectionSessionRepositoryException">状態の検証に失敗したか、使えない場合。</exception>
    public async Task PublishMetadata(ConnectionSessionCloudMetadata metadata, string accountIdentifier)
    {
        if (metadata.Session.EndedAt is not { } endedAt
            || !string.Equals(metadata.Session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal)
            || metadata.ManifestDigest.Length == 0)
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        await Write(async () =>
        {
            await _database.UpsertRecord(
                    MetadataRecordType,
                    MetadataRecordName(metadata.Session.Id, accountIdentifier),
                    new Dictionary<string, CloudKitFieldValue>
                    {
                        [AccountFingerprintKey] = new CloudKitFieldValue.StringValue(Fingerprint(accountIdentifier)),
                        [SessionIDKey] = new CloudKitFieldValue.StringValue(LowercasedUuid(metadata.Session.Id)),
                        [ManifestDigestKey] = new CloudKitFieldValue.StringValue(metadata.ManifestDigest),
                        [PayloadFormatVersionKey] = new CloudKitFieldValue.Int64Value(CurrentPayloadFormatVersion),
                        [MetadataDataKey] = new CloudKitFieldValue.BytesValue(CloudKitPayloadJsonCodec.EncodeMetadata(metadata)),
                        ["endedAt"] = new CloudKitFieldValue.TimestampValue(endedAt),
                    })
                .ConfigureAwait(false);
        });
    }

    /// <summary>
    /// 指定アカウントの、生ログを含まないセッション概要を取得します。
    /// </summary>
    /// <remarks>責務: 概要のレコードを、生ログを含まないセッション一覧へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>生ログを含まないセッション概要。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">整合性の検証に失敗したか、使えない場合。</exception>
    public Task<IReadOnlyList<ConnectionSessionCloudMetadata>> DownloadMetadata(string accountIdentifier) =>
        Read<IReadOnlyList<ConnectionSessionCloudMetadata>>([], async () =>
        {
            var records = await Records(
                    MetadataRecordType,
                    accountIdentifier,
                    [SessionIDKey, ManifestDigestKey, PayloadFormatVersionKey, MetadataDataKey])
                .ConfigureAwait(false);
            var byID = new Dictionary<ConnectionSessionID, ConnectionSessionCloudMetadata>();
            foreach (var record in records)
            {
                if (record.GetInt64(PayloadFormatVersionKey) != CurrentPayloadFormatVersion)
                {
                    continue;
                }
                var data = record.GetBytes(MetadataDataKey) ?? throw IntegrityConflict();
                ConnectionSessionCloudMetadata metadata;
                try
                {
                    metadata = CloudKitPayloadJsonCodec.DecodeMetadata(data);
                }
                catch (System.Text.Json.JsonException exception)
                {
                    SyncLog.TransferPayloadInvalid(SyncLog.Logger, exception, "metadata");
                    throw IntegrityConflict();
                }
                if (!string.Equals(metadata.Session.AccountIdentifier, accountIdentifier, StringComparison.Ordinal)
                    || !string.Equals(metadata.ManifestDigest, record.GetString(ManifestDigestKey), StringComparison.Ordinal)
                    || !SameUuid(metadata.Session.Id, record.GetString(SessionIDKey)))
                {
                    throw IntegrityConflict();
                }
                if (byID.TryGetValue(metadata.Session.Id, out var existing) && existing != metadata)
                {
                    throw IntegrityConflict();
                }
                byID[metadata.Session.Id] = metadata;
            }
            return byID.Values.ToArray();
        });

    /// <summary>
    /// 生ログの資産を取らずに、CloudKit のセッション別マニフェストを復元します。
    /// </summary>
    /// <remarks>責務: 指定アカウントの生ログのレコードを、セッション別マニフェストの辞書へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>セッション ID をキーとするマニフェスト。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">必須のフィールドがないか、重複が食い違うか、使えない場合。</exception>
    public Task<IReadOnlyDictionary<ConnectionSessionID, string>> DownloadTransferManifests(string accountIdentifier) =>
        Read<IReadOnlyDictionary<ConnectionSessionID, string>>(new Dictionary<ConnectionSessionID, string>(), async () =>
        {
            var records = await Records(
                    TransferRecordType,
                    accountIdentifier,
                    [SessionIDKey, ManifestDigestKey, PayloadFormatVersionKey])
                .ConfigureAwait(false);
            var manifests = new Dictionary<ConnectionSessionID, string>();
            foreach (var record in records)
            {
                if (record.GetInt64(PayloadFormatVersionKey) != CurrentPayloadFormatVersion)
                {
                    continue;
                }
                if (ParseUuid(record.GetString(SessionIDKey)) is not { } uuid
                    || record.GetString(ManifestDigestKey) is not { } digest)
                {
                    throw IntegrityConflict();
                }
                var sessionID = new ConnectionSessionID(uuid);
                if (manifests.TryGetValue(sessionID, out var existing) && !string.Equals(existing, digest, StringComparison.Ordinal))
                {
                    throw IntegrityConflict();
                }
                manifests[sessionID] = digest;
            }
            return manifests;
        });

    /// <summary>
    /// 指定セッションの生ログの資産を取得して要約値を検証します。
    /// </summary>
    /// <remarks>責務: 1 件の生ログのレコードを、検証済みの転送パッケージへ変換します。</remarks>
    /// <param name="sessionID">生ログを取得するセッション ID。</param>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>資産の SHA-256 が保存済みのマニフェストと一致した転送パッケージ。</returns>
    public Task<VerifiedConnectionSessionTransfer> DownloadTransfer(ConnectionSessionID sessionID, string accountIdentifier) =>
        DownloadTransfer(sessionID, accountIdentifier, _ => { });

    /// <summary>
    /// 指定セッションの生ログの資産を、進み具合を知らせながら取得して要約値を検証します。
    /// </summary>
    /// <remarks>責務: 1 件の生ログのレコードを、取得率と検証済みの転送パッケージへ変換します。</remarks>
    /// <param name="sessionID">生ログを取得するセッション ID。</param>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <param name="progress">0〜1 で知らせる取得の進み具合。</param>
    /// <returns>検証済みの転送パッケージ。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">レコードがない・検証に失敗した・使えない場合。</exception>
    public Task<VerifiedConnectionSessionTransfer> DownloadTransfer(
        ConnectionSessionID sessionID,
        string accountIdentifier,
        Action<double> progress) =>
        Write(async () =>
        {
            progress(0);
            var record = await _database.LookupRecord(TransferRecordName(sessionID, accountIdentifier)).ConfigureAwait(false)
                ?? throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
            var transfer = await VerifiedTransfer(record, accountIdentifier, progress).ConfigureAwait(false);
            progress(1);
            return transfer;
        });

    /// <summary>
    /// セッション ID に紐づく生ログを CloudKit の資産へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件の生ログを SHA-256 付きのレコードへ変換します。</remarks>
    /// <param name="package">保存するセッション識別情報と未デコードの生ログ。</param>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>保存した資産のバイト列の SHA-256。</returns>
    public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) =>
        Upload(package, accountIdentifier, _ => { });

    /// <summary>
    /// セッション ID に紐づく生ログを、進み具合を知らせながら CloudKit の資産へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件の生ログを、保存率と SHA-256 付きのレコードへ変換します。</remarks>
    /// <param name="package">保存するセッション識別情報と未デコードの生ログ。</param>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <param name="progress">0〜1 で知らせる保存の進み具合。</param>
    /// <returns>保存した資産のバイト列の SHA-256。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">アカウントが違うか、使えない場合。</exception>
    public async Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier, Action<double> progress)
    {
        if (!string.Equals(package.AccountIdentifier, accountIdentifier, StringComparison.Ordinal))
        {
            throw new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.InvalidState);
        }
        return await Write(async () =>
        {
            var data = CloudKitPayloadJsonCodec.EncodeTransferPackage(package);
            var digest = CloudKitAccountFingerprint.Sha256Hex(data);
            var name = TransferRecordName(package.SessionID, accountIdentifier);
            progress(0);
            var asset = await _database.UploadAsset(TransferRecordType, name, PayloadAssetKey, data, value => progress(value * 0.95))
                .ConfigureAwait(false);
            await _database.UpsertRecord(
                    TransferRecordType,
                    name,
                    new Dictionary<string, CloudKitFieldValue>
                    {
                        [AccountFingerprintKey] = new CloudKitFieldValue.StringValue(Fingerprint(accountIdentifier)),
                        [SessionIDKey] = new CloudKitFieldValue.StringValue(LowercasedUuid(package.SessionID)),
                        [ManifestDigestKey] = new CloudKitFieldValue.StringValue(digest),
                        [PayloadFormatVersionKey] = new CloudKitFieldValue.Int64Value(CurrentPayloadFormatVersion),
                        [PayloadAssetKey] = asset,
                    })
                .ConfigureAwait(false);
            progress(1);
            return digest;
        });
    }

    /// <summary>
    /// 指定アカウントのすべての生ログの資産を検証して復元します。
    /// </summary>
    /// <remarks>責務: 生ログのレコード群を、要約値を検証済みの転送パッケージへ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>検証済みの転送パッケージ。</returns>
    public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
        Read<IReadOnlyList<VerifiedConnectionSessionTransfer>>([], async () =>
        {
            var records = await Records(TransferRecordType, accountIdentifier).ConfigureAwait(false);
            var output = new List<VerifiedConnectionSessionTransfer>(records.Count);
            foreach (var record in records)
            {
                output.Add(await VerifiedTransfer(record, accountIdentifier, null).ConfigureAwait(false));
            }
            return output;
        });

    /// <summary>
    /// Mac の取込受領証を CloudKit へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件の取込結果を、セッションとインストール別の受領証レコードへ変換します。</remarks>
    /// <param name="receipt">読み戻して検証したマニフェストの受領証。</param>
    /// <param name="sessionID">取り込んだ接続セッション ID。</param>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
        Write(async () =>
        {
            await _database.UpsertRecord(
                    ReceiptRecordType,
                    ReceiptRecordName(sessionID, receipt.DeviceID, accountIdentifier),
                    new Dictionary<string, CloudKitFieldValue>
                    {
                        [AccountFingerprintKey] = new CloudKitFieldValue.StringValue(Fingerprint(accountIdentifier)),
                        [SessionIDKey] = new CloudKitFieldValue.StringValue(LowercasedUuid(sessionID)),
                        [ManifestDigestKey] = new CloudKitFieldValue.StringValue(receipt.ManifestDigest),
                        ["deviceID"] = new CloudKitFieldValue.StringValue(receipt.DeviceID),
                        ["deviceName"] = new CloudKitFieldValue.StringValue(receipt.DeviceName),
                        ["importedAt"] = new CloudKitFieldValue.TimestampValue(receipt.ImportedAt),
                    })
                .ConfigureAwait(false);
        });

    /// <summary>
    /// 指定アカウントの Mac 取込受領証を復元します。
    /// </summary>
    /// <remarks>責務: 受領証レコード群を、セッション ID と取込結果の組へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>取込日時の新しい順のセッション ID と受領証。</returns>
    public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
        string accountIdentifier) =>
        Read<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([], async () =>
        {
            var records = await Records(ReceiptRecordType, accountIdentifier).ConfigureAwait(false);
            return records
                .Select(record =>
                {
                    if (ParseUuid(record.GetString(SessionIDKey)) is not { } uuid
                        || record.GetString("deviceID") is not { } deviceID
                        || record.GetString("deviceName") is not { } deviceName
                        || record.GetTimestamp("importedAt") is not { } importedAt
                        || record.GetString(ManifestDigestKey) is not { } digest)
                    {
                        throw IntegrityConflict();
                    }
                    return (new ConnectionSessionID(uuid), new ConnectionSessionMacImportReceipt(deviceID, deviceName, importedAt, digest));
                })
                .OrderByDescending(pair => pair.Item2.ImportedAt)
                .ToArray();
        });

    /// <summary>
    /// 指定アカウントのセッション削除の印を復元します。
    /// </summary>
    /// <remarks>責務: 削除の印のレコード群を、重複のない接続セッション ID の集合へ変換します。</remarks>
    /// <param name="accountIdentifier">同期対象の Apple アカウント識別子。</param>
    /// <returns>全端末から削除すべき接続セッション ID の集合。</returns>
    public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
        Read<IReadOnlySet<ConnectionSessionID>>(new HashSet<ConnectionSessionID>(), async () =>
        {
            var records = await Records(DeletionRecordType, accountIdentifier).ConfigureAwait(false);
            var output = new HashSet<ConnectionSessionID>();
            foreach (var record in records)
            {
                output.Add(new ConnectionSessionID(ParseUuid(record.GetString(SessionIDKey)) ?? throw IntegrityConflict()));
            }
            return output;
        });

    /// <summary>
    /// 削除の印を保存し、対応する生ログ・概要・受領証のレコードを消します。
    /// </summary>
    /// <remarks>責務: 1 件の接続セッションを全端末の削除対象として残し、運転データのレコードを消します。</remarks>
    /// <param name="sessionID">削除する接続セッション ID。</param>
    /// <param name="accountIdentifier">削除対象を所有する Apple アカウント識別子。</param>
    public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) =>
        Delete(async () =>
        {
            await _database.UpsertRecord(
                    DeletionRecordType,
                    DeletionRecordName(sessionID, accountIdentifier),
                    new Dictionary<string, CloudKitFieldValue>
                    {
                        [AccountFingerprintKey] = new CloudKitFieldValue.StringValue(Fingerprint(accountIdentifier)),
                        [SessionIDKey] = new CloudKitFieldValue.StringValue(LowercasedUuid(sessionID)),
                    })
                .ConfigureAwait(false);
            await _database.DeleteRecordIfPresent(TransferRecordName(sessionID, accountIdentifier)).ConfigureAwait(false);
            await _database.DeleteRecordIfPresent(MetadataRecordName(sessionID, accountIdentifier)).ConfigureAwait(false);
            var receipts = await Records(ReceiptRecordType, accountIdentifier).ConfigureAwait(false);
            foreach (var record in receipts.Where(record => SameUuid(sessionID, record.GetString(SessionIDKey))))
            {
                await _database.DeleteRecordIfPresent(record.RecordName).ConfigureAwait(false);
            }
        });

    /// <summary>
    /// 指定アカウントのすべての概要・生ログ・受領証・削除の印を CloudKit から消します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウントに属するすべての運転データのレコードを、やり直せる削除へ変換します。</remarks>
    /// <param name="accountIdentifier">削除対象の Apple アカウント識別子。</param>
    public Task DeleteAll(string accountIdentifier) =>
        Delete(async () =>
        {
            var transfers = await Records(TransferRecordType, accountIdentifier, [SessionIDKey]).ConfigureAwait(false);
            var receipts = await Records(ReceiptRecordType, accountIdentifier, [SessionIDKey]).ConfigureAwait(false);
            var deletions = await Records(DeletionRecordType, accountIdentifier, [SessionIDKey]).ConfigureAwait(false);
            var metadata = await Records(MetadataRecordType, accountIdentifier, [SessionIDKey]).ConfigureAwait(false);
            foreach (var record in transfers.Concat(metadata).Concat(receipts).Concat(deletions))
            {
                await _database.DeleteRecordIfPresent(record.RecordName).ConfigureAwait(false);
            }
        });

    /// <summary>
    /// 生ログのレコードの資産・要約値・所有関係を検証して転送パッケージを返します。
    /// </summary>
    /// <remarks>責務: 1 件の生ログのレコードを、整合性を検証した転送パッケージへ変換します。</remarks>
    /// <param name="record">生ログのレコード。</param>
    /// <param name="accountIdentifier">期待するアカウント識別子。</param>
    /// <param name="progress">取得の進み具合。</param>
    /// <returns>検証済みの転送パッケージ。</returns>
    /// <exception cref="ConnectionSessionRepositoryException">資産がない・要約値が違う・読めない・所有者が違う場合。</exception>
    private async Task<VerifiedConnectionSessionTransfer> VerifiedTransfer(
        CloudKitRecord record,
        string accountIdentifier,
        Action<double>? progress)
    {
        if (record.GetAsset(PayloadAssetKey) is not { } asset
            || record.GetString(ManifestDigestKey) is not { } expectedDigest
            || record.GetInt64(PayloadFormatVersionKey) != CurrentPayloadFormatVersion)
        {
            throw IntegrityConflict();
        }
        var data = await _database.DownloadAsset(asset, progress).ConfigureAwait(false);
        if (!string.Equals(CloudKitAccountFingerprint.Sha256Hex(data), expectedDigest, StringComparison.Ordinal))
        {
            throw IntegrityConflict();
        }
        ConnectionSessionTransferPackage package;
        try
        {
            package = CloudKitPayloadJsonCodec.DecodeTransferPackage(data);
        }
        catch (System.Text.Json.JsonException exception)
        {
            SyncLog.TransferPayloadInvalid(SyncLog.Logger, exception, "package");
            throw IntegrityConflict();
        }
        if (!string.Equals(package.AccountIdentifier, accountIdentifier, StringComparison.Ordinal)
            || !SameUuid(package.SessionID, record.GetString(SessionIDKey)))
        {
            throw IntegrityConflict();
        }
        return new VerifiedConnectionSessionTransfer(package, expectedDigest);
    }

    /// <summary>
    /// アカウントのフィンガープリントが一致するレコードを、すべてのページから取得します。
    /// </summary>
    /// <remarks>責務: 1 件のレコード種類とアカウントを、全ページのレコードへ変換します。</remarks>
    /// <param name="recordType">レコード種類。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <param name="desiredKeys">資産の取得を避けるときに限るフィールド。</param>
    /// <returns>レコード。</returns>
    private Task<IReadOnlyList<CloudKitRecord>> Records(
        string recordType,
        string accountIdentifier,
        IReadOnlyList<string>? desiredKeys = null) =>
        _database.QueryRecords(recordType, AccountFingerprintKey, Fingerprint(accountIdentifier), desiredKeys);

    /// <summary>
    /// 読み取りの処理を、使えない状態に合わせて実行します。
    /// </summary>
    /// <remarks>責務: ローカルモードなら空の結果、サインインしていなければ Unavailable にします。</remarks>
    /// <typeparam name="T">結果の型。</typeparam>
    /// <param name="empty">ローカルモードの結果。</param>
    /// <param name="action">読み取りの処理。</param>
    /// <returns>結果。</returns>
    private Task<T> Read<T>(T empty, Func<Task<T>> action) =>
        _database.Availability == CloudKitAvailability.NotConfigured
            ? Task.FromResult(empty)
            : CloudKitUnavailabilityTranslator.Run(action, Unavailable);

    /// <summary>
    /// 書き込みの処理を、使えない状態に合わせて実行します。
    /// </summary>
    /// <remarks>責務: 使えなければ Unavailable にします。</remarks>
    /// <typeparam name="T">結果の型。</typeparam>
    /// <param name="action">書き込みの処理。</param>
    /// <returns>結果。</returns>
    private Task<T> Write<T>(Func<Task<T>> action) => CloudKitUnavailabilityTranslator.Run(action, Unavailable);

    /// <summary>
    /// 結果のない書き込みの処理を、使えない状態に合わせて実行します。
    /// </summary>
    /// <remarks>責務: 使えなければ Unavailable にします。</remarks>
    /// <param name="action">書き込みの処理。</param>
    /// <returns>処理。</returns>
    private Task Write(Func<Task> action) => CloudKitUnavailabilityTranslator.Run(action, Unavailable);

    /// <summary>
    /// 削除の処理を、使えない状態に合わせて実行します。
    /// </summary>
    /// <remarks>責務: ローカルモードなら何もせず、サインインしていなければ Unavailable にします。</remarks>
    /// <param name="action">削除の処理。</param>
    /// <returns>処理。</returns>
    private Task Delete(Func<Task> action) =>
        _database.Availability == CloudKitAvailability.NotConfigured
            ? Task.CompletedTask
            : CloudKitUnavailabilityTranslator.Run(action, Unavailable);

    /// <summary>
    /// 生ログのレコード名を作ります。
    /// </summary>
    /// <remarks>責務: アカウントとセッション ID を衝突しないレコード名へ変換します。</remarks>
    /// <param name="sessionID">セッション ID。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns><c>raw-session-fp-uuid</c>。</returns>
    public static string TransferRecordName(ConnectionSessionID sessionID, string accountIdentifier) =>
        $"raw-session-{Fingerprint(accountIdentifier)}-{LowercasedUuid(sessionID)}";

    /// <summary>
    /// 概要のレコード名を作ります。
    /// </summary>
    /// <remarks>責務: アカウントとセッション ID を衝突しないレコード名へ変換します。</remarks>
    /// <param name="sessionID">セッション ID。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns><c>session-metadata-fp-uuid</c>。</returns>
    public static string MetadataRecordName(ConnectionSessionID sessionID, string accountIdentifier) =>
        $"session-metadata-{Fingerprint(accountIdentifier)}-{LowercasedUuid(sessionID)}";

    /// <summary>
    /// 受領証のレコード名を作ります。
    /// </summary>
    /// <remarks>責務: アカウント・セッション・インストール識別子を衝突しないレコード名へ変換します。</remarks>
    /// <param name="sessionID">セッション ID。</param>
    /// <param name="deviceID">取り込んだインストールの安定した識別子。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns><c>mac-receipt-fp-uuid-SHA256(deviceID)</c>。</returns>
    public static string ReceiptRecordName(ConnectionSessionID sessionID, string deviceID, string accountIdentifier) =>
        $"mac-receipt-{Fingerprint(accountIdentifier)}-{LowercasedUuid(sessionID)}-{CloudKitAccountFingerprint.Sha256Hex(Encoding.UTF8.GetBytes(deviceID))}";

    /// <summary>
    /// 削除の印のレコード名を作ります。
    /// </summary>
    /// <remarks>責務: アカウントとセッション ID を衝突しないレコード名へ変換します。</remarks>
    /// <param name="sessionID">セッション ID。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns><c>deleted-session-fp-uuid</c>。</returns>
    public static string DeletionRecordName(ConnectionSessionID sessionID, string accountIdentifier) =>
        $"deleted-session-{Fingerprint(accountIdentifier)}-{LowercasedUuid(sessionID)}";

    /// <summary>
    /// アカウントのフィンガープリントを返します。
    /// </summary>
    /// <remarks>責務: 1 件の識別子を小文字の SHA-256 へ変換します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>小文字 16 進の SHA-256。</returns>
    private static string Fingerprint(string accountIdentifier) => CloudKitAccountFingerprint.Of(accountIdentifier);

    /// <summary>
    /// セッション ID を小文字の UUID 文字列にします。
    /// </summary>
    /// <remarks>責務: iOS の <c>uuidString.lowercased()</c> と同じ文字列を返します。</remarks>
    /// <param name="sessionID">セッション ID。</param>
    /// <returns>小文字の UUID。</returns>
    private static string LowercasedUuid(ConnectionSessionID sessionID) => sessionID.RawValue.ToString("D");

    /// <summary>
    /// UUID 文字列を読みます。
    /// </summary>
    /// <remarks>責務: iOS の <c>UUID(uuidString:)</c> と同じく、36 文字の UUID だけを受け付けます。</remarks>
    /// <param name="value">文字列。</param>
    /// <returns>UUID。読めなければ <c>null</c>。</returns>
    private static Guid? ParseUuid(string? value) =>
        value is not null && Guid.TryParseExact(value, "D", out var uuid) ? uuid : null;

    /// <summary>
    /// セッション ID と文字列が、大文字小文字を問わず同じかを返します。
    /// </summary>
    /// <remarks>責務: iOS の <c>caseInsensitiveCompare == .orderedSame</c> を再現します。</remarks>
    /// <param name="sessionID">セッション ID。</param>
    /// <param name="value">比べる文字列。</param>
    /// <returns>同じなら <c>true</c>。</returns>
    private static bool SameUuid(ConnectionSessionID sessionID, string? value) =>
        string.Equals(sessionID.RawValue.ToString("D"), value ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 整合性のエラーを作ります。
    /// </summary>
    /// <remarks>責務: 共通の IntegrityConflict を返します。</remarks>
    /// <returns>エラー。</returns>
    private static ConnectionSessionRepositoryException IntegrityConflict() =>
        new(ConnectionSessionRepositoryError.IntegrityConflict);

    /// <summary>
    /// 利用できないときのエラーを作ります。
    /// </summary>
    /// <remarks>責務: CloudKit を使えない状態を接続セッションの保存先のエラーへ変換します。</remarks>
    /// <returns>エラー。</returns>
    private static Exception Unavailable() => new ConnectionSessionRepositoryException(ConnectionSessionRepositoryError.Unavailable);
}
