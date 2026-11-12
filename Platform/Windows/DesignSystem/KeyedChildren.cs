using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// パネルの子を、項目の鍵ごとに保持して差分だけ直します（「消して全部作り直す」の代わり）。
/// </summary>
/// <remarks>
/// 新しい鍵の項目だけ要素を作り、残る鍵の要素は値だけを差し替え、消えた鍵の要素だけを外します。並び順も合わせます。
/// 要素が作り直されないので、押している最中のボタンが消えず、開いているメニューも閉じません。
/// 数十個までの手組みの並び（カードの格子・設定の行など）に使います。数百を超える一覧は <see cref="VirtualList{TItem}"/> を使います。
/// </remarks>
/// <typeparam name="TKey">項目の鍵（ID など）。</typeparam>
/// <typeparam name="TItem">項目。</typeparam>
/// <typeparam name="TElement">項目を出す要素。</typeparam>
public sealed class KeyedChildren<TKey, TItem, TElement>
    where TKey : notnull
    where TElement : UIElement
{
    /// <summary>子を持つパネルです。</summary>
    private readonly Panel _panel;

    /// <summary>項目から鍵を取り出す処理です。</summary>
    private readonly Func<TItem, TKey> _key;

    /// <summary>要素を作る処理です。</summary>
    private readonly Func<TItem, TElement> _create;

    /// <summary>要素の値を差し替える処理です。</summary>
    private readonly Action<TElement, TItem> _update;

    /// <summary>鍵ごとの要素です。</summary>
    private readonly Dictionary<TKey, TElement> _elements = [];

    /// <summary>パネルと、鍵・作成・更新の処理を指定して作ります。</summary>
    /// <param name="panel">子を持つパネル。ほかの子を混ぜないこと。</param>
    /// <param name="key">項目から鍵を取り出す処理。</param>
    /// <param name="create">要素を作る処理（作った直後に <paramref name="update"/> も呼ぶ）。</param>
    /// <param name="update">要素の値だけを差し替える処理。同じ値なら何もしないのが望ましい。</param>
    public KeyedChildren(Panel panel, Func<TItem, TKey> key, Func<TItem, TElement> create, Action<TElement, TItem> update)
    {
        _panel = panel;
        _key = key;
        _create = create;
        _update = update;
    }

    /// <summary>今ある要素の数です。</summary>
    public int Count => _elements.Count;

    /// <summary>
    /// 鍵の要素を探します。
    /// </summary>
    /// <remarks>責務: 保持している要素を鍵で返します。</remarks>
    /// <param name="key">鍵。</param>
    /// <param name="element">要素。</param>
    /// <returns>あれば true。</returns>
    public bool TryGet(TKey key, out TElement element) => _elements.TryGetValue(key, out element!);

    /// <summary>
    /// 項目の並びに合わせて、子を差分で直します。
    /// </summary>
    /// <remarks>責務: 足りない要素を作り、残る要素を更新し、消えた要素を外し、順番をそろえます。</remarks>
    /// <param name="items">表示する項目の並び。</param>
    public void Sync(IEnumerable<TItem> items)
    {
        var seen = new HashSet<TKey>();
        var index = 0;
        foreach (var item in items)
        {
            var key = _key(item);
            if (!seen.Add(key))
            {
                continue;
            }

            if (!_elements.TryGetValue(key, out var element))
            {
                element = _create(item);
                _elements[key] = element;
            }

            _update(element, item);
            var current = _panel.Children.IndexOf(element);
            if (current < 0)
            {
                _panel.Children.Insert(Math.Min(index, _panel.Children.Count), element);
            }
            else if (current != index)
            {
                _panel.Children.Move((uint)current, (uint)index);
            }

            index++;
        }

        if (_elements.Count == seen.Count)
        {
            return;
        }

        var removed = new List<TKey>();
        foreach (var (key, element) in _elements)
        {
            if (!seen.Contains(key))
            {
                _panel.Children.Remove(element);
                removed.Add(key);
            }
        }

        foreach (var key in removed)
        {
            _elements.Remove(key);
        }
    }
}
