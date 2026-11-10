using ProjectZD8.Application.DeviceConnection.Ports;
using ProjectZD8.Application.DeviceConnection.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.DeviceConnection.UseCases;

/// <summary>最新要求だけを有効にするアダプター探索ライフサイクルを調整します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class LatestAdapterDiscoveryUseCase
{
    /// <summary>1回の候補取得を実行する探索ユースケースです。</summary>
    private readonly DiscoverAdaptersUseCase discoverAdapters;

    /// <summary>現在実行中の探索タスクのキャンセル元です。</summary>
    private CancellationTokenSource? discoveryCancellation;

    /// <summary>現在実行中の探索タスクです。</summary>
    private Task? discoveryTask;

    /// <summary>キャンセルへ即応しない古い完了も拒否する探索世代です。</summary>
    private ulong discoveryGeneration;

    /// <summary>単発探索ユースケースを注入して最新探索ユースケースを生成します。</summary>
    /// <remarks>責務: 最新探索ライフサイクルを1件の候補取得ユースケースへ結び付けます。</remarks>
    /// <param name="discoverAdapters">接続方式ごとの候補を取得する単発ユースケース。</param>
    public LatestAdapterDiscoveryUseCase(DiscoverAdaptersUseCase discoverAdapters)
    {
        this.discoverAdapters = discoverAdapters;
    }

    /// <summary>以前の要求を無効化して指定方式の探索を開始します。</summary>
    /// <remarks>責務: 1件の最新探索要求だけを完了通知へ変換します。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <param name="receive">最新要求が完了した場合だけ呼び出す結果通知。</param>
    public void Start(AdapterTransportMode mode, Action<AdapterDiscoveryOutcome> receive)
    {
        InvalidateCurrentDiscovery();
        var requestedGeneration = discoveryGeneration;
        var cancellation = new CancellationTokenSource();
        discoveryCancellation = cancellation;
        var task = Discover(mode, receive, requestedGeneration, cancellation);
        if (!task.IsCompleted)
        {
            discoveryTask = task;
        }
    }

    /// <summary>現在の探索を終了して以後の完了通知を無効化します。</summary>
    /// <remarks>責務: 進行中の探索タスクとその世代に属する結果を無効化します。</remarks>
    public void Cancel()
    {
        InvalidateCurrentDiscovery();
    }

    /// <summary>1回の探索を実行し、最新要求のときだけ結果を通知します。</summary>
    /// <remarks>責務: Swift の探索タスク本体として、探索結果を完了通知へ変換します。</remarks>
    /// <param name="mode">探索対象の物理接続方式。</param>
    /// <param name="receive">最新要求が完了した場合だけ呼び出す結果通知。</param>
    /// <param name="requestedGeneration">探索開始時に記録した世代。</param>
    /// <param name="cancellation">この探索のキャンセル元。</param>
    /// <returns>探索の完了を表すタスク。</returns>
    private async Task Discover(
        AdapterTransportMode mode,
        Action<AdapterDiscoveryOutcome> receive,
        ulong requestedGeneration,
        CancellationTokenSource cancellation)
    {
        AdapterDiscoveryOutcome outcome;
        try
        {
            outcome = new AdapterDiscoveryOutcome.Discovered(await discoverAdapters.Execute(mode));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (AdapterDiscoveryException error)
        {
            outcome = new AdapterDiscoveryOutcome.Unavailable(error.Reason);
        }
        catch (Exception)
        {
            outcome = new AdapterDiscoveryOutcome.Failed();
        }

        if (!AcceptsResult(requestedGeneration, cancellation.Token))
        {
            return;
        }

        discoveryTask = null;
        discoveryCancellation = null;
        receive(outcome);
    }

    /// <summary>完了した探索が現在の最新要求かを判定します。</summary>
    /// <remarks>責務: 1件の完了結果を現在の探索世代と照合します。</remarks>
    /// <param name="generation">探索開始時に記録した世代。</param>
    /// <param name="cancellationToken">探索タスクのキャンセル状態。</param>
    /// <returns>現在の完了通知へ反映してよい場合は <c>true</c>。</returns>
    private bool AcceptsResult(ulong generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && discoveryGeneration == generation;

    /// <summary>現在の探索タスクと世代を無効化します。</summary>
    /// <remarks>責務: 次の探索または終了後に古い結果が通知されない境界を確立します。</remarks>
    private void InvalidateCurrentDiscovery()
    {
        discoveryCancellation?.Cancel();
        discoveryCancellation = null;
        discoveryTask = null;
        unchecked
        {
            discoveryGeneration += 1;
        }
    }
}
