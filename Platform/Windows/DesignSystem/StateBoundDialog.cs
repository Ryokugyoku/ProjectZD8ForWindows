using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 状態に「表示すべき」と書かれている間だけ ContentDialog を出しておく、表示の窓口です（SwiftUI の <c>.alert(isPresented:)</c>）。
/// </summary>
/// <remarks>
/// WinUI は 1 つの画面に ContentDialog を 1 つしか出せないので、アプリ全体で 1 つずつ順番に出します。
/// 利用者が閉じたときだけ <c>closed</c> を呼び、状態が変わって画面が閉じたときは呼びません。UI スレッドから使います。
/// </remarks>
public sealed class StateBoundDialog
{
    /// <summary>アプリ全体で同時に 1 つだけ出すための順番待ちです。</summary>
    private static readonly SemaphoreSlim Queue = new(1, 1);

    /// <summary>表示中（または表示待ち）のダイアログです。</summary>
    private ContentDialog? _dialog;

    /// <summary>表示中のダイアログが何を表しているかの鍵です。</summary>
    private object? _key;

    /// <summary>画面の都合（状態の変化）で閉じたかどうかです。</summary>
    private bool _closedByState;

    /// <summary>表示中（または表示待ち）のダイアログです。中身を状態に合わせて書き換えるときに使います。</summary>
    public ContentDialog? Current => _dialog;

    /// <summary>
    /// 状態に合わせてダイアログを出す・閉じる。
    /// </summary>
    /// <remarks>責務: 「表示すべきか」と鍵の変化を、ダイアログの表示・非表示へ反映します。</remarks>
    /// <param name="shouldShow">表示すべきなら true。</param>
    /// <param name="key">表示内容の鍵（プロンプトの値など）。変わったら出し直す。</param>
    /// <param name="xamlRoot">表示先。null なら何もしない。</param>
    /// <param name="make">ダイアログを作る処理。</param>
    /// <param name="closed">利用者が閉じたときの処理（押したボタンを受け取る）。</param>
    public void Sync(bool shouldShow, object? key, XamlRoot? xamlRoot, Func<ContentDialog> make, Action<ContentDialogResult> closed)
    {
        if (!shouldShow || !Equals(key, _key))
        {
            Dismiss();
        }

        if (!shouldShow || _dialog is not null || xamlRoot is null)
        {
            return;
        }

        var dialog = make();
        dialog.XamlRoot = xamlRoot;
        if (dialog.RequestedTheme == ElementTheme.Default)
        {
            // ダイアログは画面の外側（ポップアップ）に出るため、設定で上書きした外観を自分で引き継ぐ。
            dialog.RequestedTheme = DesignTokens.EffectiveTheme;
        }

        // 一覧や詳細を並べるダイアログのため、標準の最大幅（548px）を少し広げる。
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        _dialog = dialog;
        _key = key;
        _closedByState = false;
        _ = ShowAsync(dialog, closed);
    }

    /// <summary>
    /// 表示中のダイアログを、利用者の操作としてではなく閉じます。
    /// </summary>
    /// <remarks>責務: 状態がダイアログを求めなくなったときに、通知せずに閉じます。</remarks>
    public void Dismiss()
    {
        if (_dialog is not { } dialog)
        {
            return;
        }

        _closedByState = true;
        _dialog = null;
        _key = null;
        dialog.Hide();
    }

    /// <summary>
    /// 順番を待ってダイアログを出し、閉じた結果を通知します。
    /// </summary>
    /// <remarks>責務: 同時に 1 つだけ表示し、利用者が閉じたときだけ結果を通知します。</remarks>
    /// <param name="dialog">ダイアログ。</param>
    /// <param name="closed">利用者が閉じたときの処理。</param>
    /// <returns>完了を表すタスク。</returns>
    private async Task ShowAsync(ContentDialog dialog, Action<ContentDialogResult> closed)
    {
        await Queue.WaitAsync();
        try
        {
            if (!ReferenceEquals(_dialog, dialog))
            {
                return;
            }

            var result = await dialog.ShowAsync();
            if (ReferenceEquals(_dialog, dialog))
            {
                _dialog = null;
                _key = null;
                if (!_closedByState)
                {
                    closed(result);
                }
            }
        }
        catch (Exception)
        {
            if (ReferenceEquals(_dialog, dialog))
            {
                _dialog = null;
                _key = null;
            }
        }
        finally
        {
            Queue.Release();
        }
    }
}
