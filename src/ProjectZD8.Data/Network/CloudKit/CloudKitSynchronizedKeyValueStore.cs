using System.Text;
using ProjectZD8.Data.Persistence.Preferences;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// iCloud Key-Value Store の代わりに、CloudKit のレコードで設定とログアウト通知を同期します。
/// </summary>
/// <remarks>
/// <para>キーとレコードの対応（PROGRESS.md の「iOS 版の CloudKit 化の設計案」）:</para>
/// <list type="bullet">
/// <item><c>settings.account.v1.&lt;fp&gt;</c> → レコード種類 AccountSettings、名前 <c>account-settings-&lt;fp&gt;</c>、
/// フィールド settingsData（Bytes。キーを並べ替えた JSON）と updatedAt（Timestamp）。</item>
/// <item><c>authentication.revocation.v1.&lt;fp&gt;</c> → レコード種類 AccountSessionRevocation、名前 <c>session-revocation-&lt;fp&gt;</c>、
/// フィールド marker（String。UUID）と revokedAt（Timestamp）。</item>
/// </list>
/// <para>ほかのキーは同期せずメモリにだけ置きます。値はメモリにも持ち、読み書きはすぐに返ります。CloudKit との通信は裏で 1 件ずつ行い、
/// 取得した値が変わっていれば <see cref="ChangedExternally"/> を、作ったときの <see cref="SynchronizationContext"/>（UI スレッド）で発生させます。
/// 送信に失敗した変更は保留にして、次の <see cref="Synchronize"/> で送り直します。CloudKit を使えないときは、メモリだけで動きます。</para>
/// </remarks>
public sealed class CloudKitSynchronizedKeyValueStore : ISynchronizedKeyValueStore
{
    /// <summary>設定のキーの接頭辞です。</summary>
    private const string SettingsKeyPrefix = "settings.account.v1.";

    /// <summary>失効のキーの接頭辞です。</summary>
    private const string RevocationKeyPrefix = "authentication.revocation.v1.";

    /// <summary>CloudKit のデータベースです。</summary>
    private readonly ICloudKitDatabase _database;

    /// <summary>更新日時に使う時計です。</summary>
    private readonly TimeProvider _time;

    /// <summary>変更の通知を送るスレッドの文脈です。</summary>
    private readonly SynchronizationContext? _context;

    /// <summary>状態を守る鍵です。</summary>
    private readonly Lock _lock = new();

    /// <summary>通信を 1 件ずつにする鍵です。</summary>
    private readonly SemaphoreSlim _worker = new(1, 1);

    /// <summary>今わかっている値です。</summary>
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

    /// <summary>取得の対象にしているキーです。</summary>
    private readonly HashSet<string> _tracked = new(StringComparer.Ordinal);

    /// <summary>まだ送っていない変更（値。<c>null</c> は削除）と、その版です。</summary>
    private readonly Dictionary<string, (byte[]? Value, long Version)> _pending = new(StringComparer.Ordinal);

    /// <summary>変更の版を数える番号です。</summary>
    private long _version;

    /// <summary>
    /// CloudKit のデータベースを注入して作ります。変更の通知は、作ったスレッドの文脈（UI スレッド）で発生します。
    /// </summary>
    /// <remarks>責務: 同期先と時計を固定し、通知のスレッドを現在の文脈に決めます。</remarks>
    /// <param name="database">CloudKit のデータベース。</param>
    /// <param name="time">時計。<c>null</c> なら <see cref="TimeProvider.System"/>。</param>
    public CloudKitSynchronizedKeyValueStore(ICloudKitDatabase database, TimeProvider? time = null)
        : this(database, time, SynchronizationContext.Current)
    {
    }

    /// <summary>
    /// CloudKit のデータベースと、通知を送る文脈を注入して作ります。
    /// </summary>
    /// <remarks>責務: 同期先と時計と通知のスレッドを固定します。</remarks>
    /// <param name="database">CloudKit のデータベース。</param>
    /// <param name="time">時計。<c>null</c> なら <see cref="TimeProvider.System"/>。</param>
    /// <param name="context">通知を送る文脈。<c>null</c> なら通信を終えたスレッドで直接通知します。</param>
    public CloudKitSynchronizedKeyValueStore(ICloudKitDatabase database, TimeProvider? time, SynchronizationContext? context)
    {
        _database = database;
        _time = time ?? TimeProvider.System;
        _context = context;
    }

    /// <inheritdoc />
    public event Action<IReadOnlyList<string>>? ChangedExternally;

    /// <inheritdoc />
    public byte[]? Data(string key)
    {
        bool newlyTracked;
        byte[]? value;
        lock (_lock)
        {
            newlyTracked = IsSynchronizedKey(key) && _tracked.Add(key);
            value = _cache.TryGetValue(key, out var cached) ? cached.ToArray() : null;
        }
        if (newlyTracked)
        {
            _ = RunInBackground([key]);
        }
        return value;
    }

    /// <inheritdoc />
    public void SetData(string key, byte[] value)
    {
        lock (_lock)
        {
            _cache[key] = value.ToArray();
            if (IsSynchronizedKey(key))
            {
                _tracked.Add(key);
                _pending[key] = (value.ToArray(), ++_version);
            }
        }
    }

    /// <inheritdoc />
    public void RemoveObject(string key)
    {
        lock (_lock)
        {
            _cache.Remove(key);
            if (IsSynchronizedKey(key))
            {
                _tracked.Add(key);
                _pending[key] = (null, ++_version);
            }
        }
    }

    /// <inheritdoc />
    public bool Synchronize()
    {
        _ = RunInBackground(null);
        return _database.Availability == CloudKitAvailability.Available;
    }

    /// <summary>
    /// 保留中の変更を送り、対象のキーの最新の値を取得し終えるまで待ちます。
    /// </summary>
    /// <remarks>
    /// 責務: 1 回の同期をやり切ります。アプリが前面に来たとき・終わる前に組み立てから呼べます。
    /// 通信の失敗は外に出さず、保留のまま残します。
    /// </remarks>
    /// <returns>同期の処理。</returns>
    public Task SynchronizeAsync() => RunInBackground(null);

    /// <summary>
    /// 送信と取得を、ほかの通信と重ならないように行います。
    /// </summary>
    /// <remarks>責務: 1 回分の送信と取得を順番に実行し、変わったキーを通知します。</remarks>
    /// <param name="keys">取得するキー。<c>null</c> なら対象のキーすべて。</param>
    /// <returns>処理。</returns>
    private async Task RunInBackground(IReadOnlyList<string>? keys)
    {
        if (_database.Availability != CloudKitAvailability.Available)
        {
            return;
        }
        await _worker.WaitAsync().ConfigureAwait(false);
        try
        {
            await Flush().ConfigureAwait(false);
            var changed = await Fetch(keys).ConfigureAwait(false);
            if (changed.Count > 0)
            {
                Raise(changed);
            }
        }
        catch (Exception exception) when (exception is CloudKitException or CloudKitUnavailableException
            or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // 通信の失敗は保留のまま次回に送り直します（iCloud KV と同じく、呼び出し側には知らせません）。
        }
        finally
        {
            _worker.Release();
        }
    }

    /// <summary>
    /// 保留中の変更を CloudKit へ送ります。
    /// </summary>
    /// <remarks>責務: 保留中の保存と削除をレコードの保存と削除へ変換して送ります。</remarks>
    /// <returns>処理。</returns>
    private async Task Flush()
    {
        List<KeyValuePair<string, (byte[]? Value, long Version)>> pending;
        lock (_lock)
        {
            pending = _pending.ToList();
        }
        foreach (var (key, (value, version)) in pending)
        {
            var (recordType, recordName) = RecordFor(key);
            if (value is null)
            {
                await _database.DeleteRecordIfPresent(recordName).ConfigureAwait(false);
            }
            else
            {
                await _database.SaveRecord(
                        new CloudKitRecord(recordName, recordType, FieldsFor(key, value)),
                        CloudKitSaveOperation.ForceReplace)
                    .ConfigureAwait(false);
            }
            lock (_lock)
            {
                if (_pending.TryGetValue(key, out var current) && current.Version == version)
                {
                    _pending.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// 対象のキーの最新の値を CloudKit から取得します。
    /// </summary>
    /// <remarks>責務: レコードを読んでメモリ上の値と比べ、変わったキーを返します。保留中のキーは手元の値を優先します。</remarks>
    /// <param name="keys">取得するキー。<c>null</c> なら対象のキーすべて。</param>
    /// <returns>変わったキー。</returns>
    private async Task<List<string>> Fetch(IReadOnlyList<string>? keys)
    {
        List<string> targets;
        lock (_lock)
        {
            targets = (keys ?? _tracked.ToList()).Where(IsSynchronizedKey).Distinct(StringComparer.Ordinal).ToList();
        }
        var changed = new List<string>();
        foreach (var key in targets)
        {
            var (_, recordName) = RecordFor(key);
            var record = await _database.LookupRecord(recordName).ConfigureAwait(false);
            var remote = record is null ? null : ValueFrom(key, record);
            lock (_lock)
            {
                if (_pending.ContainsKey(key))
                {
                    continue;
                }
                var hasLocal = _cache.TryGetValue(key, out var local);
                if (remote is null)
                {
                    if (record is null && hasLocal)
                    {
                        _cache.Remove(key);
                        changed.Add(key);
                    }
                    continue;
                }
                if (!hasLocal || !local!.AsSpan().SequenceEqual(remote))
                {
                    _cache[key] = remote;
                    changed.Add(key);
                }
            }
        }
        return changed;
    }

    /// <summary>
    /// 変わったキーを通知します。
    /// </summary>
    /// <remarks>責務: 作ったときの文脈（UI スレッド）で変更の通知を発生させます。</remarks>
    /// <param name="changed">変わったキー。</param>
    private void Raise(IReadOnlyList<string> changed)
    {
        if (_context is { } context)
        {
            context.Post(_ => ChangedExternally?.Invoke(changed), null);
        }
        else
        {
            ChangedExternally?.Invoke(changed);
        }
    }

    /// <summary>
    /// CloudKit と同期するキーかどうかを返します。
    /// </summary>
    /// <remarks>責務: 設定と失効のキーだけを同期の対象にします。</remarks>
    /// <param name="key">キー。</param>
    /// <returns>同期するなら <c>true</c>。</returns>
    private static bool IsSynchronizedKey(string key) =>
        key.StartsWith(SettingsKeyPrefix, StringComparison.Ordinal)
        || key.StartsWith(RevocationKeyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// キーに対応するレコードの種類と名前を返します。
    /// </summary>
    /// <remarks>責務: 1 件のキーを、フィンガープリントを含むレコード名へ変換します。</remarks>
    /// <param name="key">同期するキー。</param>
    /// <returns>レコードの種類と名前。</returns>
    public static (string RecordType, string RecordName) RecordFor(string key)
    {
        if (key.StartsWith(SettingsKeyPrefix, StringComparison.Ordinal))
        {
            return ("AccountSettings", $"account-settings-{key[SettingsKeyPrefix.Length..]}");
        }
        if (key.StartsWith(RevocationKeyPrefix, StringComparison.Ordinal))
        {
            return ("AccountSessionRevocation", $"session-revocation-{key[RevocationKeyPrefix.Length..]}");
        }
        throw new ArgumentException("同期しないキーです。", nameof(key));
    }

    /// <summary>
    /// 値をレコードのフィールドにします。
    /// </summary>
    /// <remarks>責務: 設定は settingsData と updatedAt、失効は marker と revokedAt にします。</remarks>
    /// <param name="key">キー。</param>
    /// <param name="value">値。</param>
    /// <returns>フィールド。</returns>
    private Dictionary<string, CloudKitFieldValue> FieldsFor(string key, byte[] value)
    {
        var now = new CloudKitFieldValue.TimestampValue(_time.GetUtcNow());
        return key.StartsWith(SettingsKeyPrefix, StringComparison.Ordinal)
            ? new Dictionary<string, CloudKitFieldValue>
            {
                ["settingsData"] = new CloudKitFieldValue.BytesValue(value),
                ["updatedAt"] = now,
            }
            : new Dictionary<string, CloudKitFieldValue>
            {
                ["marker"] = new CloudKitFieldValue.StringValue(Encoding.UTF8.GetString(value)),
                ["revokedAt"] = now,
            };
    }

    /// <summary>
    /// レコードから値を取り出します。
    /// </summary>
    /// <remarks>責務: settingsData のバイト列、または marker の UTF-8 を値にします。</remarks>
    /// <param name="key">キー。</param>
    /// <param name="record">レコード。</param>
    /// <returns>値。フィールドがなければ <c>null</c>。</returns>
    private static byte[]? ValueFrom(string key, CloudKitRecord record) =>
        key.StartsWith(SettingsKeyPrefix, StringComparison.Ordinal)
            ? record.GetBytes("settingsData")
            : record.GetString("marker") is { } marker ? Encoding.UTF8.GetBytes(marker) : null;
}
