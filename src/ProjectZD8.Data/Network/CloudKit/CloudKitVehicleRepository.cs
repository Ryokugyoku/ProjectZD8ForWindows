using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Data.Mapping;
using ProjectZD8.Data.Persistence.Preferences;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// 端末内の車両リポジトリ（SQLite）を正として、CloudKit の VehicleCatalog と同期します（iOS の <c>CloudKitVehicleRepository</c>）。
/// </summary>
/// <remarks>
/// <para><b>iOS との違い</b>：iOS は CloudKit だけに保存し、UserDefaults はオフライン用のキャッシュです。Windows は端末内のリポジトリが正で、
/// CloudKit のカタログ（<c>vehicle-catalog-&lt;fp&gt;</c> の catalogAsset。iOS と同じ JSON）と読み込みのたびに突き合わせます。</para>
/// <para><b>突き合わせの規則</b>（VehicleCatalog には削除の印がないため、前回同期したときの車両 ID の一覧を端末内に残し、3 者で比べます）:</para>
/// <list type="bullet">
/// <item>両方にある：更新日時（updatedAt）の新しい方を採ります。同じならこの端末を採ります（整備記録の同期と同じ）。</item>
/// <item>この端末にだけある：前回の一覧にあれば、ほかの端末で消されたので、この端末からも消します。なければ新しく追加したので送ります。</item>
/// <item>CloudKit にだけある：前回の一覧にあれば、この端末で消したので CloudKit からも消します。なければほかの端末で追加されたので取り込みます。</item>
/// <item>結果が CloudKit と違えば、カタログ全体を更新日時の新しい順で送り直し、前回の一覧を更新します。</item>
/// </list>
/// <para>CloudKit を使えない・通信に失敗した・カタログが読めない（新しい形式など）ときは、端末内の一覧だけを返し、CloudKit は書き換えません
/// （iOS はキャッシュが空なら例外を投げますが、Windows は端末内が正なので返します）。保存と削除は端末内に書いた後で同期を試み、
/// 同期の失敗は次の読み込みに回します。</para>
/// </remarks>
public sealed class CloudKitVehicleRepository : IVehicleRepository, IAccountVehicleDataErasurePort
{
    /// <summary>車両カタログのレコード種類です。</summary>
    private const string RecordType = "VehicleCatalog";

    /// <summary>カタログの資産のフィールド名です。</summary>
    private const string CatalogAssetKey = "catalogAsset";

    /// <summary>端末内の車両リポジトリ（正）です。</summary>
    private readonly IVehicleRepository _localRepository;

    /// <summary>CloudKit のデータベースです。</summary>
    private readonly ICloudKitDatabase _database;

    /// <summary>前回同期した車両 ID の一覧の保存先です。</summary>
    private readonly ILocalKeyValueStore _syncState;

    /// <summary>更新日時に使う時計です。</summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// 端末内のリポジトリ、CloudKit、同期状態の保存先を注入して作ります。
    /// </summary>
    /// <remarks>責務: 車両の正の保存先と同期先を固定します。</remarks>
    /// <param name="localRepository">端末内の車両リポジトリ（SQLite）。</param>
    /// <param name="database">CloudKit のデータベース。</param>
    /// <param name="syncState">前回同期した車両 ID の一覧を残す端末内の保存先。</param>
    /// <param name="time">時計。<c>null</c> なら <see cref="TimeProvider.System"/>。</param>
    public CloudKitVehicleRepository(
        IVehicleRepository localRepository,
        ICloudKitDatabase database,
        ILocalKeyValueStore syncState,
        TimeProvider? time = null)
    {
        _localRepository = localRepository;
        _database = database;
        _syncState = syncState;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// 端末内の車両を読み、使えるなら CloudKit のカタログと突き合わせます。
    /// </summary>
    /// <remarks>責務: 1 件のアカウントの車両一覧を、同期した結果として返します。</remarks>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    /// <returns>更新日時の新しい順に並ぶ登録車両。</returns>
    public async Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier)
    {
        var local = await _localRepository.LoadVehicles(accountIdentifier).ConfigureAwait(false);
        if (_database.Availability != CloudKitAvailability.Available)
        {
            return Sorted(local);
        }
        try
        {
            return await Synchronize(local, accountIdentifier).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsSynchronizationFailure(exception))
        {
            return Sorted(local);
        }
    }

    /// <summary>
    /// 車両 1 件を端末内に保存し、CloudKit と同期します。
    /// </summary>
    /// <remarks>責務: 1 件のプロフィールの更新を、端末内とアカウント別のカタログへ反映します。</remarks>
    /// <param name="vehicle">保存する車両プロフィール。</param>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    public async Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier)
    {
        await _localRepository.SaveVehicle(vehicle, accountIdentifier).ConfigureAwait(false);
        await LoadVehicles(accountIdentifier).ConfigureAwait(false);
    }

    /// <summary>
    /// 車両 1 件を端末内から消し、CloudKit と同期します。
    /// </summary>
    /// <remarks>責務: 指定 ID のプロフィールだけを、端末内とアカウント別のカタログから除きます。</remarks>
    /// <param name="id">削除する車両 ID。</param>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    public async Task DeleteVehicle(VehicleID id, string accountIdentifier)
    {
        await _localRepository.DeleteVehicle(id, accountIdentifier).ConfigureAwait(false);
        await LoadVehicles(accountIdentifier).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定アカウントの CloudKit の車両カタログを消します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウントに属する車両プロフィールと写真を CloudKit から消します。ローカルモードでは何もしません。</remarks>
    /// <param name="accountIdentifier">削除対象の Apple アカウント識別子。</param>
    /// <exception cref="CloudKitUnavailableException">CloudKit にサインインしていない場合。</exception>
    public async Task DeleteAllVehicleData(string accountIdentifier)
    {
        if (_database.Availability == CloudKitAvailability.NotConfigured)
        {
            return;
        }
        await _database.DeleteRecordIfPresent(RecordName(accountIdentifier)).ConfigureAwait(false);
        _syncState.Remove(SyncStateKey(accountIdentifier));
    }

    /// <summary>
    /// 指定アカウントの端末内の登録車両 ID を返します。
    /// </summary>
    /// <remarks>
    /// 責務: 車両別の PID 設定の削除に使う ID を、端末内のリポジトリから集めます。ポートが同期的なので、
    /// UI スレッドを止めないよう別スレッドで読み、終わるまで待ちます。
    /// </remarks>
    /// <param name="accountIdentifier">照会する Apple アカウント識別子。</param>
    /// <returns>登録車両 ID。</returns>
    public IReadOnlyList<VehicleID> LocalVehicleIDs(string accountIdentifier) =>
        Task.Run(() => _localRepository.LoadVehicles(accountIdentifier)).GetAwaiter().GetResult()
            .Select(vehicle => vehicle.Id)
            .ToArray();

    /// <summary>
    /// 指定アカウントの端末内の車両をすべて消します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウントに属する車両プロフィールと写真を、この端末から除きます（iOS のキャッシュ削除にあたります）。</remarks>
    /// <param name="accountIdentifier">削除対象の Apple アカウント識別子。</param>
    public void RemoveLocalVehicleCache(string accountIdentifier)
    {
        Task.Run(async () =>
        {
            foreach (var vehicle in await _localRepository.LoadVehicles(accountIdentifier).ConfigureAwait(false))
            {
                await _localRepository.DeleteVehicle(vehicle.Id, accountIdentifier).ConfigureAwait(false);
            }
        }).GetAwaiter().GetResult();
        _syncState.Remove(SyncStateKey(accountIdentifier));
    }

    /// <summary>
    /// 端末内と CloudKit のカタログを突き合わせます。
    /// </summary>
    /// <remarks>責務: 3 者の比較で結果を決め、端末内と CloudKit の両方へ反映します。</remarks>
    /// <param name="local">端末内の車両。</param>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>結果の車両一覧。</returns>
    private async Task<IReadOnlyList<VehicleProfile>> Synchronize(IReadOnlyList<VehicleProfile> local, string accountIdentifier)
    {
        var name = RecordName(accountIdentifier);
        var record = await _database.LookupRecord(name).ConfigureAwait(false);
        IReadOnlyList<VehicleProfile> remote = [];
        if (record is not null)
        {
            if (record.GetAsset(CatalogAssetKey) is not { } asset)
            {
                return Sorted(local);
            }
            var data = await _database.DownloadAsset(asset).ConfigureAwait(false);
            try
            {
                remote = CloudKitPayloadJsonCodec.DecodeVehicleCatalog(data);
            }
            catch (JsonException)
            {
                // iOS と同じく、読めないカタログは使わず、上書きもしません。
                return Sorted(local);
            }
        }

        var previous = LoadSyncState(accountIdentifier);
        var localByID = local.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var remoteByID = remote.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var merged = new List<VehicleProfile>();
        foreach (var id in localByID.Keys.Union(remoteByID.Keys))
        {
            var inLocal = localByID.TryGetValue(id, out var l);
            var inRemote = remoteByID.TryGetValue(id, out var r);
            if (inLocal && inRemote)
            {
                var winner = l!.UpdatedAt >= r!.UpdatedAt ? l : r;
                if (!ReferenceEquals(winner, l) && winner != l)
                {
                    await _localRepository.SaveVehicle(winner, accountIdentifier).ConfigureAwait(false);
                }
                merged.Add(winner);
            }
            else if (inLocal)
            {
                if (previous.Contains(id))
                {
                    await _localRepository.DeleteVehicle(id, accountIdentifier).ConfigureAwait(false);
                }
                else
                {
                    merged.Add(l!);
                }
            }
            else if (!previous.Contains(id))
            {
                await _localRepository.SaveVehicle(r!, accountIdentifier).ConfigureAwait(false);
                merged.Add(r!);
            }
        }

        var sorted = Sorted(merged);
        if (record is null && sorted.Count == 0)
        {
            SaveSyncState(accountIdentifier, sorted);
            return sorted;
        }
        if (record is null || !sorted.SequenceEqual(Sorted(remote)))
        {
            var catalog = CloudKitPayloadJsonCodec.EncodeVehicleCatalog(sorted);
            var uploaded = await _database.UploadAsset(RecordType, name, CatalogAssetKey, catalog).ConfigureAwait(false);
            await _database.UpsertRecord(
                    RecordType,
                    name,
                    new Dictionary<string, CloudKitFieldValue>
                    {
                        [CatalogAssetKey] = uploaded,
                        ["updatedAt"] = new CloudKitFieldValue.TimestampValue(_time.GetUtcNow()),
                    })
                .ConfigureAwait(false);
        }
        SaveSyncState(accountIdentifier, sorted);
        return sorted;
    }

    /// <summary>
    /// 前回同期した車両 ID の一覧を読みます。
    /// </summary>
    /// <remarks>責務: 端末内に残した ID の一覧を集合へ変換します。壊れていれば空にします。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>前回の ID の集合。</returns>
    private HashSet<VehicleID> LoadSyncState(string accountIdentifier)
    {
        var result = new HashSet<VehicleID>();
        if (_syncState.GetString(SyncStateKey(accountIdentifier)) is not { } text)
        {
            return result;
        }
        try
        {
            if (JsonNode.Parse(text) is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item?.GetValueKind() == JsonValueKind.String && Guid.TryParse(item.GetValue<string>(), out var id))
                    {
                        result.Add(new VehicleID(id));
                    }
                }
            }
        }
        catch (JsonException)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>
    /// 同期した車両 ID の一覧を残します。
    /// </summary>
    /// <remarks>責務: 次の突き合わせの基準になる ID の一覧を保存します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <param name="vehicles">同期した車両。</param>
    private void SaveSyncState(string accountIdentifier, IEnumerable<VehicleProfile> vehicles)
    {
        var array = new JsonArray();
        foreach (var vehicle in vehicles)
        {
            array.Add(JsonValue.Create(SwiftJsonValues.UuidString(vehicle.Id.RawValue)));
        }
        _syncState.SetString(SyncStateKey(accountIdentifier), array.ToJsonString());
    }

    /// <summary>
    /// 同期の失敗として扱う例外かどうかを返します。
    /// </summary>
    /// <remarks>責務: 通信と CloudKit の失敗だけを、端末内の一覧へのフォールバックの対象にします。</remarks>
    /// <param name="exception">例外。</param>
    /// <returns>同期の失敗なら <c>true</c>。</returns>
    private static bool IsSynchronizationFailure(Exception exception) =>
        exception is CloudKitException or CloudKitUnavailableException or HttpRequestException or TaskCanceledException;

    /// <summary>
    /// 更新日時の新しい順に並べます。
    /// </summary>
    /// <remarks>責務: iOS と同じ並び（updatedAt の降順）にします。同じ日時は元の順を保ちます。</remarks>
    /// <param name="vehicles">車両。</param>
    /// <returns>並べた車両。</returns>
    private static IReadOnlyList<VehicleProfile> Sorted(IEnumerable<VehicleProfile> vehicles) =>
        vehicles.OrderByDescending(vehicle => vehicle.UpdatedAt).ToArray();

    /// <summary>
    /// Apple の識別子を平文で保存しない車両カタログのレコード名へ変換します。
    /// </summary>
    /// <remarks>責務: 1 件の識別子を車両カタログ用の不可逆なレコード名へ変換します（iOS と同じ）。</remarks>
    /// <param name="accountIdentifier">Apple アカウントのアプリ固有の識別子。</param>
    /// <returns>private database の中で安定するレコード名。</returns>
    public static string RecordName(string accountIdentifier) =>
        $"vehicle-catalog-{CloudKitAccountFingerprint.Of(accountIdentifier)}";

    /// <summary>
    /// 前回同期した ID の一覧の保存キーを返します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウントを、識別子を含まない保存キーへ変換します。</remarks>
    /// <param name="accountIdentifier">アカウント識別子。</param>
    /// <returns>保存キー。</returns>
    private static string SyncStateKey(string accountIdentifier) =>
        $"vehicle.catalog.sync.v1.{CloudKitAccountFingerprint.Of(accountIdentifier)}";
}
