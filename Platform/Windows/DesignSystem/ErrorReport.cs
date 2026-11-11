using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8.Diagnostics;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;
using Windows.ApplicationModel.DataTransfer;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 利用者に見えるエラー表示に付ける、報告用の番号（<c>7F3A-1842</c>）の小さな表示です。
/// </summary>
/// <remarks>
/// 実行 ID の先頭 4 桁と、直近の Error の <c>seq</c> を並べた番号を出し、コピーできるようにします。
/// 利用者がこの番号を伝えれば、診断ログの該当行がすぐ見つかります。この実行でまだ Error を記録していなければ、何も出しません。
/// </remarks>
public static class ErrorReport
{
    /// <summary>今の報告用の番号です。この実行でまだ Error がなければ null。</summary>
    public static string? CurrentNumber => DiagnosticLog.Session?.ReportNumber;

    /// <summary>
    /// エラーの InfoBar を作ります。
    /// </summary>
    /// <remarks>責務: 閉じられない InfoBar を開いた状態で作り、報告用の番号の表示を中に付けます。</remarks>
    /// <param name="severity">重さ。</param>
    /// <param name="message">メッセージ。</param>
    /// <returns>InfoBar。</returns>
    public static InfoBar Bar(InfoBarSeverity severity, string message)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = false, Severity = severity, Message = message };
        AttachTo(bar);
        return bar;
    }

    /// <summary>
    /// InfoBar に報告用の番号の表示を付けます。
    /// </summary>
    /// <remarks>責務: 番号があるときだけ、InfoBar の <c>Content</c> に番号とコピーのボタンを置きます（すでに別の中身があれば何もしない）。</remarks>
    /// <param name="bar">InfoBar。</param>
    public static void AttachTo(InfoBar bar)
    {
        if (bar.Content is null && ReferenceLine() is { } line)
        {
            bar.Content = line;
        }
    }

    /// <summary>
    /// 報告用の番号の行を作ります。
    /// </summary>
    /// <remarks>責務: 「報告番号 7F3A-1842」と、クリップボードへコピーするボタンを横に並べます。</remarks>
    /// <returns>行。番号がなければ null。</returns>
    public static StackPanel? ReferenceLine()
    {
        if (CurrentNumber is not { } number)
        {
            return null;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var label = Typography.Text(LocalizedText.Format("windows.error.report_number", number), "SecondaryCaptionTextStyle");
        label.IsTextSelectionEnabled = true;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var icon = new FontIcon { Glyph = FluentSymbol.Copy, FontSize = 12 };
        var copy = new HyperlinkButton
        {
            Content = icon,
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 0,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(copy, LocalizedText.Get("windows.error.report_number.copy"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, LocalizedText.Get("windows.error.report_number.copy"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(copy, "windows-error-report-number-copy");
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(number);
            Clipboard.SetContent(package);
            icon.Glyph = FluentSymbol.Accept;
            UiLog.ReportNumberCopied(UiLog.Logger);
        };
        row.Children.Add(copy);
        return row;
    }
}
