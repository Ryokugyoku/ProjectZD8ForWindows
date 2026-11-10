using System.Text;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Data.Network.CloudKit;
using ProjectZD8.Data.Persistence.Preferences;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// 端末間で同期する保存先で、アカウントごとのセッション失効の世代を同期します（iOS の <c>UbiquitousKeyValueStoreAccountSessionRevocationStore</c>）。
/// </summary>
/// <remarks>
/// <para>失効キーは iOS と同じ <c>authentication.revocation.v1.&lt;SHA-256&gt;</c>、端末内の受理済み基準キーは
/// <c>authentication.revocation.baseline.v1.&lt;SHA-256&gt;</c>。値は UUID 文字列の UTF-8 です。
/// Windows では同期先を CloudKit の AccountSessionRevocation レコード（marker）にします。</para>
/// <para>監視を始めたときに同期を要求し、届いた世代が受理済みの基準と違えば 1 回だけ通知します（iOS と同じ）。UI スレッドから呼ぶ前提です。</para>
/// </remarks>
public sealed class SynchronizedAccountSessionRevocationStore : IAccountSessionRevocationPort
{
    /// <summary>この端末が受理済みの失効世代を保持する端末内の保存先です。</summary>
    private readonly ILocalKeyValueStore _defaults;

    /// <summary>端末間で最新の失効世代を共有する保存先です。</summary>
    private readonly ISynchronizedKeyValueStore _ubiquitousStore;

    /// <summary>新しい世代の UUID を作る処理です。</summary>
    private readonly Func<Guid> _newMarker;

    /// <summary>現在監視中のアカウント固有の失効キーです。</summary>
    private string? _observedRevocationKey;

    /// <summary>現在監視中のアカウント固有の基準キーです。</summary>
    private string? _observedBaselineKey;

    /// <summary>現在の監視期間ですでに通知した失効世代です。</summary>
    private byte[]? _deliveredMarker;

    /// <summary>未受理の失効世代を Application へ返す処理です。</summary>
    private Action? _receiveRevocation;

    /// <summary>
    /// 端末内の基準の保存先と同期先を注入して作ります。
    /// </summary>
    /// <remarks>責務: セッション失効世代の端末内基準と端末間同期に使う境界を固定します。</remarks>
    /// <param name="defaults">受理済みの世代を保持する端末内保存先。</param>
    /// <param name="ubiquitousStore">最新の失効世代を共有する同期先。</param>
    /// <param name="newMarker">新しい UUID を作る処理。<c>null</c> なら <see cref="Guid.NewGuid"/>。</param>
    public SynchronizedAccountSessionRevocationStore(
        ILocalKeyValueStore defaults,
        ISynchronizedKeyValueStore ubiquitousStore,
        Func<Guid>? newMarker = null)
    {
        _defaults = defaults;
        _ubiquitousStore = ubiquitousStore;
        _newMarker = newMarker ?? Guid.NewGuid;
        _ubiquitousStore.ChangedExternally += ReceiveExternalChange;
    }

    /// <summary>
    /// 新しいログイン時点の失効世代を端末内の基準へ保存します。
    /// </summary>
    /// <remarks>責務: 1 件の新しいセッションへ、現在の失効世代を受理済みの基準として割り当てます。</remarks>
    /// <param name="userIdentifier">Apple がこのアプリへ割り当てたユーザー識別子。</param>
    public void RegisterCurrentSession(string userIdentifier)
    {
        _ubiquitousStore.Synchronize();
        var marker = _ubiquitousStore.Data(RevocationKey(userIdentifier));
        var key = BaselineKey(userIdentifier);
        if (marker is not null)
        {
            _defaults.SetData(key, marker);
        }
        else
        {
            _defaults.Remove(key);
        }
    }

    /// <summary>
    /// 推測できない新しい世代をアカウント固有のキーへ保存します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント失効要求を新しい UUID の世代として同期保存へ出します。</remarks>
    /// <param name="userIdentifier">Apple がこのアプリへ割り当てたユーザー識別子。</param>
    public void PublishRevocation(string userIdentifier)
    {
        var marker = Encoding.UTF8.GetBytes(_newMarker().ToString("D").ToUpperInvariant());
        _ubiquitousStore.SetData(RevocationKey(userIdentifier), marker);
        _ubiquitousStore.Synchronize();
    }

    /// <summary>
    /// 指定アカウントの未受理の失効世代を、監視開始時の確認と外部変更から監視します。
    /// </summary>
    /// <remarks>責務: 1 件のアカウント失効キーについて、端末内の基準と違う世代だけを通知します。</remarks>
    /// <param name="userIdentifier">Apple がこのアプリへ割り当てたユーザー識別子。</param>
    /// <param name="receive">未受理の失効世代を見つけたときに実行する処理。</param>
    public void StartObserving(string userIdentifier, Action receive)
    {
        StopObserving();
        _observedRevocationKey = RevocationKey(userIdentifier);
        _observedBaselineKey = BaselineKey(userIdentifier);
        _receiveRevocation = receive;
        _ubiquitousStore.Synchronize();
        DeliverRevocationIfNeeded();
    }

    /// <summary>
    /// 現在の失効の購読と監視情報を破棄します。
    /// </summary>
    /// <remarks>責務: 1 件のセッション失効監視を、再利用できる未購読の状態へ戻します。</remarks>
    public void StopObserving()
    {
        _observedRevocationKey = null;
        _observedBaselineKey = null;
        _deliveredMarker = null;
        _receiveRevocation = null;
    }

    /// <summary>
    /// 外部変更に監視対象の失効キーが含まれるときだけ、世代の差を確かめます。
    /// </summary>
    /// <remarks>責務: 1 件の外部変更を、現在監視中のアカウント失効キーへ絞り込みます。</remarks>
    /// <param name="changedKeys">変わったキー。</param>
    private void ReceiveExternalChange(IReadOnlyList<string> changedKeys)
    {
        if (_observedRevocationKey is not { } key || !changedKeys.Contains(key, StringComparer.Ordinal))
        {
            return;
        }
        DeliverRevocationIfNeeded();
    }

    /// <summary>
    /// 最新の世代が端末内の基準と違うときだけ 1 回通知します。
    /// </summary>
    /// <remarks>責務: 最新の失効世代と受理済みの基準の差を、重複しない失効通知へ変換します。</remarks>
    private void DeliverRevocationIfNeeded()
    {
        if (_observedRevocationKey is not { } revocationKey
            || _observedBaselineKey is not { } baselineKey
            || _ubiquitousStore.Data(revocationKey) is not { } marker
            || SameBytes(marker, _defaults.GetData(baselineKey))
            || SameBytes(marker, _deliveredMarker))
        {
            return;
        }
        _deliveredMarker = marker;
        _receiveRevocation?.Invoke();
    }

    /// <summary>
    /// 2 つのバイト列が同じかを返します。
    /// </summary>
    /// <remarks>責務: Swift の <c>Data</c> の等値比較を再現します。</remarks>
    /// <param name="left">左。</param>
    /// <param name="right">右。<c>null</c> なら常に違う。</param>
    /// <returns>同じなら <c>true</c>。</returns>
    private static bool SameBytes(byte[] left, byte[]? right) => right is not null && left.AsSpan().SequenceEqual(right);

    /// <summary>
    /// Apple のユーザー識別子を直接含まない失効キーへ変換します。
    /// </summary>
    /// <remarks>責務: 1 件のユーザー識別子を不可逆なセッション失効キーへ変換します（iOS と同じ）。</remarks>
    /// <param name="userIdentifier">Apple のユーザー識別子。</param>
    /// <returns>スキーマ世代と SHA-256 フィンガープリントを含む失効キー。</returns>
    public static string RevocationKey(string userIdentifier) =>
        $"authentication.revocation.v1.{CloudKitAccountFingerprint.Of(userIdentifier)}";

    /// <summary>
    /// Apple のユーザー識別子を直接含まない端末内の基準キーへ変換します。
    /// </summary>
    /// <remarks>責務: 1 件のユーザー識別子を不可逆な受理済み世代キーへ変換します（iOS と同じ）。</remarks>
    /// <param name="userIdentifier">Apple のユーザー識別子。</param>
    /// <returns>スキーマ世代と SHA-256 フィンガープリントを含む基準キー。</returns>
    private static string BaselineKey(string userIdentifier) =>
        $"authentication.revocation.baseline.v1.{CloudKitAccountFingerprint.Of(userIdentifier)}";
}
