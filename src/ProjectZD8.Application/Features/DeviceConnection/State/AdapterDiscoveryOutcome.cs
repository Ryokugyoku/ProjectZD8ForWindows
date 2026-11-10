using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.State;

/// <summary>最新のアダプター探索がApplication境界へ返す結果です。</summary>
public abstract record AdapterDiscoveryOutcome
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: 結果の種類を下の3ケースに限定します。</remarks>
    private AdapterDiscoveryOutcome()
    {
    }

    /// <summary>候補一覧の取得に成功しました。</summary>
    /// <param name="Adapters">取得できた候補一覧。</param>
    public sealed record Discovered(IReadOnlyList<DiscoveredAdapter> Adapters) : AdapterDiscoveryOutcome
    {
        /// <summary>候補一覧を要素順に比較します。</summary>
        /// <remarks>責務: Swift の配列比較と同じく、要素と順番が同じときだけ等しいとします。</remarks>
        /// <param name="other">比較相手。</param>
        /// <returns>候補一覧が等しい場合は <c>true</c>。</returns>
        public bool Equals(Discovered? other) =>
            other is not null && Adapters.SequenceEqual(other.Adapters);

        /// <summary>候補一覧からハッシュ値を作ります。</summary>
        /// <remarks>責務: 要素順の比較と矛盾しないハッシュ値を返します。</remarks>
        /// <returns>ハッシュ値。</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var adapter in Adapters)
            {
                hash.Add(adapter);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>製品またはシステム状態により探索を利用できませんでした。</summary>
    /// <param name="Error">探索を利用できない理由。</param>
    public sealed record Unavailable(AdapterDiscoveryError Error) : AdapterDiscoveryOutcome;

    /// <summary>分類できないエラーにより探索を完了できませんでした。</summary>
    public sealed record Failed : AdapterDiscoveryOutcome;
}
