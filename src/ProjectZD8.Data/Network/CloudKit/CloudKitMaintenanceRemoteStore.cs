using ProjectZD8.Application.Maintenance.Ports;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit の private database へ、写真を含む整備カタログを資産として保存します（iOS の <c>CloudKitMaintenanceRemoteStore</c>）。
/// </summary>
/// <remarks>
/// レコード種類 MaintenanceCatalog、名前 <c>maintenance-catalog-&lt;SHA-256(ユーザー ID)&gt;</c>、フィールド catalogAsset と updatedAt。
/// 設定がない（ローカルモード）ときは、取得は空・保存は何もしません。設定はあるが CloudKit にサインインしていないときは
/// <see cref="MaintenanceRepositoryException"/>（Unavailable）を投げます。
/// </remarks>
public sealed class CloudKitMaintenanceRemoteStore : IMaintenanceRemoteStore
{
    /// <summary>整備カタログのレコード種類です。</summary>
    private const string RecordType = "MaintenanceCatalog";

    /// <summary>JSON の資産を入れるフィールド名です。</summary>
    private const string CatalogAssetKey = "catalogAsset";

    /// <summary>CloudKit のデータベースです。</summary>
    private readonly ICloudKitDatabase _database;

    /// <summary>更新日時に使う時計です。</summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// CloudKit のデータベースを注入して作ります。
    /// </summary>
    /// <remarks>責務: 整備カタログの遠隔の保存先を固定します。</remarks>
    /// <param name="database">CloudKit のデータベース。</param>
    /// <param name="time">時計。<c>null</c> なら <see cref="TimeProvider.System"/>。</param>
    public CloudKitMaintenanceRemoteStore(ICloudKitDatabase database, TimeProvider? time = null)
    {
        _database = database;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// CloudKit から写真と削除の印を含む整備カタログを取得します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント用の資産を整備記録の集合へ復号します。</remarks>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    /// <returns>遠隔に保存されている整備記録の集合。</returns>
    /// <exception cref="MaintenanceRepositoryException">CloudKit にサインインしていない場合。</exception>
    public Task<IReadOnlyList<MaintenanceRecord>> FetchRecords(string accountIdentifier)
    {
        if (_database.Availability == CloudKitAvailability.NotConfigured)
        {
            return Task.FromResult<IReadOnlyList<MaintenanceRecord>>([]);
        }
        return CloudKitUnavailabilityTranslator.Run<IReadOnlyList<MaintenanceRecord>>(
            async () =>
            {
                var record = await _database.LookupRecord(RecordName(accountIdentifier)).ConfigureAwait(false);
                if (record?.GetAsset(CatalogAssetKey) is not { } asset)
                {
                    return [];
                }
                var data = await _database.DownloadAsset(asset).ConfigureAwait(false);
                return CloudKitPayloadJsonCodec.DecodeMaintenanceCatalog(data);
            },
            Unavailable);
    }

    /// <summary>
    /// 写真と削除の印を含む整備カタログを CloudKit の資産へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント用の整備集合を、1 つの JSON 資産として更新します。</remarks>
    /// <param name="records">保存する整備記録の集合。</param>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    /// <exception cref="MaintenanceRepositoryException">CloudKit にサインインしていない場合。</exception>
    public Task SaveRecords(IReadOnlyList<MaintenanceRecord> records, string accountIdentifier)
    {
        if (_database.Availability == CloudKitAvailability.NotConfigured)
        {
            return Task.CompletedTask;
        }
        return CloudKitUnavailabilityTranslator.Run(
            async () =>
            {
                var data = CloudKitPayloadJsonCodec.EncodeMaintenanceCatalog(records);
                var name = RecordName(accountIdentifier);
                var asset = await _database.UploadAsset(RecordType, name, CatalogAssetKey, data).ConfigureAwait(false);
                await _database.UpsertRecord(
                        RecordType,
                        name,
                        new Dictionary<string, CloudKitFieldValue>
                        {
                            [CatalogAssetKey] = asset,
                            ["updatedAt"] = new CloudKitFieldValue.TimestampValue(_time.GetUtcNow()),
                        })
                    .ConfigureAwait(false);
            },
            Unavailable);
    }

    /// <summary>
    /// Apple の識別子を平文で保存しない整備カタログのレコード名へ変換します。
    /// </summary>
    /// <remarks>責務: 1 件の識別子を整備カタログ用の不可逆なレコード名へ変換します。</remarks>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    /// <returns>private database の中で安定するレコード名。</returns>
    public static string RecordName(string accountIdentifier) =>
        $"maintenance-catalog-{CloudKitAccountFingerprint.Of(accountIdentifier)}";

    /// <summary>
    /// 利用できないときのエラーを作ります。
    /// </summary>
    /// <remarks>責務: CloudKit を使えない状態を整備の保存先のエラーへ変換します。</remarks>
    /// <returns>エラー。</returns>
    private static Exception Unavailable() => new MaintenanceRepositoryException(MaintenanceRepositoryError.Unavailable);
}
