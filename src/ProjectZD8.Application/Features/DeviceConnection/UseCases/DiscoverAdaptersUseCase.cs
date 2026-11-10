using System.Globalization;
using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.UseCases;

/// <summary>選択された接続方式で利用可能なアダプター候補を読み込むユースケースです。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class DiscoverAdaptersUseCase
{
    /// <summary>システム固有の探索処理を隠蔽するポートです。</summary>
    private readonly IAdapterDiscoveryPort discoveryPort;

    /// <summary>アダプター探索ポートを注入してユースケースを生成します。</summary>
    /// <remarks>責務: アダプター探索ユースケースへ単一の探索境界を結び付けます。</remarks>
    /// <param name="discoveryPort">システム固有の探索処理を提供するポート。</param>
    public DiscoverAdaptersUseCase(IAdapterDiscoveryPort discoveryPort)
    {
        this.discoveryPort = discoveryPort;
    }

    /// <summary>指定された接続方式で現在選択できる候補を取得します。</summary>
    /// <remarks>
    /// 責務: 1件の接続方式指定を重複のない選択前候補一覧へ変換します。
    /// 表示名の比較は Swift の <c>localizedStandardCompare</c> に合わせ、現在のカルチャで大文字小文字を区別せず数字を数値として比べます。
    /// </remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <returns>表示名順に整列した重複のないアダプター候補。</returns>
    /// <exception cref="Exception">システムのデバイス情報へアクセスできない場合の探索エラー。</exception>
    public async Task<IReadOnlyList<DiscoveredAdapter>> Execute(AdapterTransportMode mode)
    {
        var adapters = await discoveryPort.DiscoverAdapters(mode);
        var compareInfo = CultureInfo.CurrentCulture.CompareInfo;
        return adapters
            .DistinctBy(adapter => adapter.Id, StringComparer.Ordinal)
            .OrderBy(
                adapter => adapter.DisplayName,
                Comparer<string>.Create((left, right) => compareInfo.Compare(
                    left,
                    right,
                    CompareOptions.IgnoreCase | CompareOptions.NumericOrdering)))
            .ToArray();
    }
}
