using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// モデルの変更の知らせを束ね、UI スレッドで 1 フレームに 1 回まで（指定すればそれより少なく）だけ画面を更新します。
/// </summary>
/// <remarks>
/// ライブ取得やログの読込みはモデルを 1 秒に何度も変えます。知らせのたびに画面を更新せず、<see cref="Request"/> で「汚れた」印だけを付け、
/// 次の描画の直前（<see cref="CompositionTarget.Rendering"/>）に 1 回だけ <c>flush</c> を呼びます。
/// 待っている更新がないときは Rendering を購読しないので、アイドル時の負荷はありません。
/// <see cref="MinInterval"/> を決めると、それより短い間隔では更新しません（人が読む数値は 100 ms 程度で十分）。
/// 持ち主の要素が外されると止まります。UI スレッド以外から <see cref="Request"/> を呼んでもよい。
/// </remarks>
public sealed class UiUpdateThrottle
{
    /// <summary>持ち主の要素です。</summary>
    private readonly FrameworkElement _owner;

    /// <summary>更新の処理です。</summary>
    private readonly Action _flush;

    /// <summary>最小の間隔を待つためのタイマーです。</summary>
    private readonly DispatcherQueueTimer _timer;

    /// <summary>更新を待っているかどうかです。</summary>
    private bool _dirty;

    /// <summary>Rendering を購読しているかどうかです。</summary>
    private bool _hooked;

    /// <summary>最後に更新した時刻です。</summary>
    private long _lastFlushTicks;

    /// <summary>持ち主と更新の処理を指定して作ります。</summary>
    /// <param name="owner">持ち主の要素（外されたら止まる）。</param>
    /// <param name="flush">更新の処理。呼ばれた時点の最新の状態を読むこと。</param>
    /// <param name="minInterval">更新の最小の間隔。省略時は 1 フレーム。</param>
    public UiUpdateThrottle(FrameworkElement owner, Action flush, TimeSpan? minInterval = null)
    {
        _owner = owner;
        _flush = flush;
        MinInterval = minInterval ?? TimeSpan.Zero;
        _timer = owner.DispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Hook();
        owner.Unloaded += (_, _) => Cancel();
    }

    /// <summary>更新の最小の間隔です。</summary>
    public TimeSpan MinInterval { get; set; }

    /// <summary>
    /// 更新を頼みます。
    /// </summary>
    /// <remarks>責務: 印を付け、次のフレームで 1 回だけ更新されるようにします。何回呼んでも 1 回にまとまります。</remarks>
    public void Request()
    {
        if (!_owner.DispatcherQueue.HasThreadAccess)
        {
            _owner.DispatcherQueue.TryEnqueue(Request);
            return;
        }

        if (_dirty)
        {
            return;
        }

        _dirty = true;
        var wait = MinInterval - TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond - _lastFlushTicks);
        if (wait > TimeSpan.Zero)
        {
            _timer.Interval = wait;
            _timer.Start();
        }
        else
        {
            Hook();
        }
    }

    /// <summary>
    /// 待っている更新を、今すぐ行います。
    /// </summary>
    /// <remarks>責務: 印が付いていれば、次のフレームを待たずに更新します（表示された直後の最初の描画など）。</remarks>
    public void FlushNow()
    {
        Unhook();
        _timer.Stop();
        _dirty = false;
        _lastFlushTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        _flush();
    }

    /// <summary>待っている更新を取り消します。</summary>
    public void Cancel()
    {
        Unhook();
        _timer.Stop();
        _dirty = false;
    }

    /// <summary>Rendering を購読します。</summary>
    private void Hook()
    {
        if (!_hooked && _dirty)
        {
            _hooked = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>Rendering の購読をやめます。</summary>
    private void Unhook()
    {
        if (_hooked)
        {
            _hooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    /// <summary>描画の直前の処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnRendering(object? sender, object e)
    {
        if (_dirty)
        {
            FlushNow();
        }
        else
        {
            Unhook();
        }
    }
}
