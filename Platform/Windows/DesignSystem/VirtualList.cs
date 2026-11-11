using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// コードで行を組む、仮想化した一覧です（ListView の上の小さな補助）。
/// </summary>
/// <remarks>
/// 見えている行の分だけ要素を作り、スクロールで外れた行の要素は使い回します（<see cref="ListViewBase.ContainerContentChanging"/>）。
/// 行の要素は <c>create</c> で 1 回だけ作り、項目が替わるたびに <c>update</c> で値だけを差し替えます。
/// 項目は <see cref="Sync"/> で差分だけ反映するので、選択・スクロール位置・押している最中の行が保たれます。
/// 何百件にもなる一覧（セッション・整備記録・PID）はこれを使います。UI スレッドからだけ使います。
/// </remarks>
/// <typeparam name="TItem">項目。</typeparam>
public sealed class VirtualList<TItem>
    where TItem : class
{
    /// <summary>行の器のテンプレートです（空の Grid。中身はコードで入れる）。</summary>
    private const string RowTemplate =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid /></DataTemplate>";

    /// <summary>行の要素を作る処理です。</summary>
    private readonly Func<FrameworkElement> _create;

    /// <summary>行の要素の値を差し替える処理です。</summary>
    private readonly Action<FrameworkElement, TItem> _update;

    /// <summary>表示する項目です。</summary>
    private readonly ObservableCollection<TItem> _items = [];

    /// <summary>行の作成と更新の処理を指定して作ります。</summary>
    /// <param name="create">行の要素を作る処理（項目なしで作る。値は <paramref name="update"/> で入れる）。</param>
    /// <param name="update">行の要素へ項目の値を入れる処理。</param>
    public VirtualList(Func<FrameworkElement> create, Action<FrameworkElement, TItem> update)
    {
        _create = create;
        _update = update;
        View = new ListView
        {
            ItemsSource = _items,
            ItemTemplate = (DataTemplate)XamlReader.Load(RowTemplate),
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
        };
        View.ContainerContentChanging += OnContainerContentChanging;
    }

    /// <summary>一覧のコントロールです（選択・クリック・余白などはこれに設定する）。</summary>
    public ListView View { get; }

    /// <summary>表示している項目です。</summary>
    public IReadOnlyList<TItem> Items => _items;

    /// <summary>
    /// 項目の並びを、差分だけ反映します。
    /// </summary>
    /// <remarks>責務: 消えた項目を外し、新しい項目を足し、順番を合わせ、同じ鍵で中身が替わった項目だけを差し替えます。</remarks>
    /// <typeparam name="TKey">鍵。</typeparam>
    /// <param name="items">新しい並び。</param>
    /// <param name="key">項目から鍵を取り出す処理。</param>
    public void Sync<TKey>(IReadOnlyList<TItem> items, Func<TItem, TKey> key)
        where TKey : notnull
    {
        var wanted = new HashSet<TKey>();
        foreach (var item in items)
        {
            wanted.Add(key(item));
        }

        for (var index = _items.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(key(_items[index])))
            {
                _items.RemoveAt(index);
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var itemKey = key(item);
            var current = -1;
            for (var probe = index; probe < _items.Count; probe++)
            {
                if (EqualityComparer<TKey>.Default.Equals(key(_items[probe]), itemKey))
                {
                    current = probe;
                    break;
                }
            }

            if (current < 0)
            {
                _items.Insert(index, item);
            }
            else
            {
                if (current != index)
                {
                    _items.Move(current, index);
                }

                if (!ReferenceEquals(_items[index], item) && !Equals(_items[index], item))
                {
                    _items[index] = item;
                }
            }
        }

        while (_items.Count > items.Count)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    /// <summary>行の器に項目が入ったときの処理です。</summary>
    /// <remarks>責務: 器に行の要素がなければ 1 回だけ作り、項目の値を入れます。</remarks>
    /// <param name="sender">送り元。</param>
    /// <param name="args">イベントの情報。</param>
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer.ContentTemplateRoot is not Grid host || args.Item is not TItem item)
        {
            return;
        }

        if (host.Children.Count == 0)
        {
            host.Children.Add(_create());
        }

        var row = (FrameworkElement)host.Children[0];
        _update(row, item);

        // 器の読み上げ名は、そのままだと項目の ToString（レコードの中身全部）になる。行が付けた名前を器へ写す。
        var name = AutomationProperties.GetName(row);
        if (!string.IsNullOrEmpty(name))
        {
            AutomationProperties.SetName(args.ItemContainer, name);
        }

        args.Handled = true;
    }
}
