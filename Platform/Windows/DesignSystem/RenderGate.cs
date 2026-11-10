using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 利用者が操作している間だけ、画面の作り直しを待たせます。
/// </summary>
/// <remarks>
/// 画面はモデルが変わるたびにボタンなどを作り直します。ライブ取得やログの読込みの間は 1 秒に何度も変わるので、
/// マウスのボタンを押してから離すまでの間にボタンが作り直されると、クリックが届きません（押したのに何も起きない）。
/// また、開いているドロップダウンや右クリックメニューも、作り直しで閉じてしまいます。
/// このクラスは、画面の中でポインターが押されている間と、メニューやドロップダウンなどの一時的なポップアップが開いている間は、
/// 作り直しを止めておき、操作が終わってから 1 回だけ作り直します。
/// </remarks>
public sealed class RenderGate
{
    /// <summary>押したままの状態を、取りこぼしとみなして解く時間です。</summary>
    private static readonly TimeSpan StuckPressTimeout = TimeSpan.FromSeconds(4);

    /// <summary>見張る画面の根です。</summary>
    private readonly FrameworkElement _root;

    /// <summary>一時的なポップアップが開いている間も待つかどうかです。</summary>
    private readonly bool _watchesPopups;

    /// <summary>押されているポインターの ID です。</summary>
    private readonly HashSet<uint> _pressedPointers = [];

    /// <summary>操作が終わったかを確かめるためのタイマーです。</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>待たせている作り直しです。</summary>
    private Action? _pending;

    /// <summary>最後にポインターが押された時刻です。</summary>
    private DateTimeOffset _lastPressedAt;

    /// <summary>見張る画面を指定して作ります。</summary>
    /// <param name="root">見張る画面の根。この中でのポインターの押下を見ます。</param>
    /// <param name="watchesPopups">メニューやドロップダウンが開いている間も待つなら true。</param>
    public RenderGate(FrameworkElement root, bool watchesPopups = true)
    {
        _root = root;
        _watchesPopups = watchesPopups;
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerEnded), true);
        root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerEnded), true);
        root.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerEnded), true);
        _timer.Tick += (_, _) => Flush();
        root.Unloaded += (_, _) =>
        {
            _timer.Stop();
            _pressedPointers.Clear();
            _pending = null;
        };
    }

    /// <summary>利用者が画面を操作している最中かどうかです。</summary>
    public bool IsInteracting
    {
        get
        {
            if (_pressedPointers.Count > 0 && DateTimeOffset.Now - _lastPressedAt > StuckPressTimeout)
            {
                // 離したことが届かなかった（ウインドウの外で離したなど）ときに、いつまでも止めたままにしない。
                _pressedPointers.Clear();
            }

            return _pressedPointers.Count > 0 || (_watchesPopups && HasTransientPopup());
        }
    }

    /// <summary>
    /// 操作中なら作り直しを後回しにします。
    /// </summary>
    /// <remarks>責務: 操作中なら <paramref name="retry"/> を覚えて true を返し、操作が終わったら 1 回だけ呼びます。操作中でなければ何もせず false を返します。</remarks>
    /// <param name="retry">操作が終わったあとに呼ぶ作り直し。呼ばれた時点の最新の状態を読み直すこと。</param>
    /// <returns>後回しにしたなら true。</returns>
    public bool TryDefer(Action retry)
    {
        if (!IsInteracting)
        {
            return false;
        }

        _pending = retry;
        _timer.Start();
        return true;
    }

    /// <summary>ポインターが押されたときの処理です。</summary>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _pressedPointers.Add(e.Pointer.PointerId);
        _lastPressedAt = DateTimeOffset.Now;
    }

    /// <summary>ポインターを離した（または取り消された）ときの処理です。</summary>
    /// <remarks>責務: ボタンの Click が済んだあとに、待たせている作り直しを流します。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="e">イベントの情報。</param>
    private void OnPointerEnded(object sender, PointerRoutedEventArgs e)
    {
        _pressedPointers.Remove(e.Pointer.PointerId);
        if (_pending is not null && _pressedPointers.Count == 0)
        {
            _root.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Flush);
        }
    }

    /// <summary>操作が終わっていれば、待たせている作り直しを呼びます。</summary>
    private void Flush()
    {
        if (_pending is null)
        {
            _timer.Stop();
            return;
        }

        if (IsInteracting)
        {
            return;
        }

        var pending = _pending;
        _pending = null;
        _timer.Stop();
        pending();
    }

    /// <summary>メニュー・ドロップダウン・カレンダーなどの一時的なポップアップが開いているかどうかを返します。</summary>
    /// <remarks>ツールチップと、ダイアログ（ContentDialog）は数えません。ダイアログの間も裏の画面は最新にしておくためです。</remarks>
    /// <returns>開いていれば true。</returns>
    private bool HasTransientPopup()
    {
        if (_root.XamlRoot is not { } xamlRoot)
        {
            return false;
        }

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            if (popup.Child is null or ToolTip or ContentDialog or Rectangle
                || popup.Child.GetType().Name.Contains("ContentDialog", StringComparison.Ordinal)
                || IsInsideContentDialog(popup))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>ポップアップがダイアログの中から開かれたものかどうかを返します。</summary>
    /// <param name="popup">ポップアップ。</param>
    /// <returns>ダイアログの中なら true。</returns>
    private static bool IsInsideContentDialog(Popup popup)
    {
        DependencyObject? current = popup;
        while (current is not null)
        {
            if (current is ContentDialog)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
