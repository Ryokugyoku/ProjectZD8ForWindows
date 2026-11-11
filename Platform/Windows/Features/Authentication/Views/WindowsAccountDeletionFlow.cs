using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Application.Authentication.Actions;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Authentication.Views;

/// <summary>
/// アカウント削除の警告と確認のダイアログです（iOS 版の <c>MacOSAccountDeletionFlowView</c>）。
/// </summary>
/// <remarks>
/// 認証の状態の削除の段階に合わせて、警告（次へ・キャンセル）と、消えるものの一覧を見せる確認（削除・やり直し）を出します。
/// 削除中は閉じられません。
/// </remarks>
public sealed class WindowsAccountDeletionFlow
{
    /// <summary>警告のダイアログです。</summary>
    private readonly StateBoundDialog _warning = new();

    /// <summary>確認のダイアログです。</summary>
    private readonly StateBoundDialog _review = new();

    /// <summary>操作の送り先です。</summary>
    private readonly Action<AuthenticationAction> _send;

    /// <summary>今の削除の段階です。</summary>
    private AccountDeletionPhase _phase;

    /// <summary>操作の送り先を指定して作ります。</summary>
    /// <param name="send">認証の操作の送り先。</param>
    public WindowsAccountDeletionFlow(Action<AuthenticationAction> send)
    {
        _send = send;
    }

    /// <summary>
    /// 削除の段階に合わせてダイアログを出し入れします。
    /// </summary>
    /// <remarks>責務: 警告・確認・削除中・失敗の表示を、状態に合わせて切り替えます。</remarks>
    /// <param name="phase">削除の段階。</param>
    /// <param name="failure">削除の失敗。</param>
    /// <param name="xamlRoot">表示先。</param>
    public void Render(AccountDeletionPhase phase, AccountDeletionFailure? failure, XamlRoot? xamlRoot)
    {
        _phase = phase;
        _warning.Sync(
            phase == AccountDeletionPhase.Warning,
            "warning",
            xamlRoot,
            MakeWarningDialog,
            result => _send(result == ContentDialogResult.Primary
                ? AuthenticationAction.AccountDeletionWarningConfirmed
                : AuthenticationAction.AccountDeletionCancelled));
        var showsReview = phase is AccountDeletionPhase.Reviewing or AccountDeletionPhase.Deleting or AccountDeletionPhase.Failed;
        _review.Sync(
            showsReview,
            "review",
            xamlRoot,
            MakeReviewDialog,
            _ => _send(AuthenticationAction.AccountDeletionCancelled));
        if (_review.Current is { } dialog)
        {
            UpdateReviewDialog(dialog, phase, failure);
        }
    }

    /// <summary>警告のダイアログを作ります。</summary>
    /// <remarks>責務: 削除の前の警告と「次へ」「キャンセル」を持つダイアログを作ります。</remarks>
    /// <returns>ダイアログ。</returns>
    private static ContentDialog MakeWarningDialog() => new()
    {
        Title = LocalizedText.Get("account.delete.warning.title"),
        Content = Typography.Text(LocalizedText.Get("account.delete.warning.message"), "BodyTextBlockStyle"),
        PrimaryButtonText = LocalizedText.Get("account.delete.next"),
        CloseButtonText = LocalizedText.Get("account.delete.cancel"),
        DefaultButton = ContentDialogButton.Close,
    };

    /// <summary>確認のダイアログを作ります。</summary>
    /// <remarks>責務: 消えるものの一覧と削除ボタンを持つダイアログを作り、削除中は閉じられないようにします。</remarks>
    /// <returns>ダイアログ。</returns>
    private ContentDialog MakeReviewDialog()
    {
        var dialog = new ContentDialog
        {
            Title = LocalizedText.Get("account.delete.review.title"),
            CloseButtonText = LocalizedText.Get("account.delete.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            UiLog.AccountDeleteConfirmed(UiLog.Logger);
            _send(AuthenticationAction.AccountDeletionConfirmed);
        };
        dialog.Closing += (_, args) =>
        {
            if (_phase == AccountDeletionPhase.Deleting && args.Result != ContentDialogResult.None)
            {
                args.Cancel = true;
            }
        };
        return dialog;
    }

    /// <summary>確認のダイアログの中身を状態に合わせます。</summary>
    /// <remarks>責務: 削除の対象の一覧、失敗の表示、ボタンの文言と有効・無効を更新します。</remarks>
    /// <param name="dialog">確認のダイアログ。</param>
    /// <param name="phase">削除の段階。</param>
    /// <param name="failure">削除の失敗。</param>
    private static void UpdateReviewDialog(ContentDialog dialog, AccountDeletionPhase phase, AccountDeletionFailure? failure)
    {
        var body = new StackPanel { Spacing = 12, MaxWidth = 560 };
        body.Children.Add(Typography.Text(LocalizedText.Get("account.delete.review.message"), "SecondaryBodyTextStyle"));
        foreach (var (glyph, key) in new[]
        {
            (FluentSymbol.Car, "account.delete.item.driving_data"),
            (FluentSymbol.Car, "account.delete.item.vehicles"),
            (FluentSymbol.Cloud, "account.delete.item.shared_settings"),
            (FluentSymbol.Plug, "account.delete.item.adapter"),
            (FluentSymbol.Key, "account.delete.item.credential"),
        })
        {
            var row = new Grid { ColumnSpacing = 13 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(IconTile.Create(glyph, Tone.Danger, 34));
            var text = Typography.Text(LocalizedText.Get(key), "BodyTextBlockStyle");
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            body.Children.Add(new Border { Style = DesignTokens.Style("QuietSurfaceStyle"), Padding = new Thickness(Space.M), Child = row });
        }

        if (failure is not null)
        {
            body.Children.Add(ErrorReport.Bar(InfoBarSeverity.Error, LocalizedText.Get("account.delete.error")));
        }

        if (phase == AccountDeletionPhase.Deleting)
        {
            body.Children.Add(new ProgressBar { IsIndeterminate = true });
        }

        dialog.Content = new ScrollViewer { Content = body, MaxHeight = 480 };
        dialog.PrimaryButtonText = LocalizedText.Get(phase == AccountDeletionPhase.Failed ? "account.delete.retry" : "account.delete.confirm");
        dialog.IsPrimaryButtonEnabled = phase != AccountDeletionPhase.Deleting;
        dialog.PrimaryButtonStyle = DesignTokens.Style("DangerButtonStyle");
    }
}
