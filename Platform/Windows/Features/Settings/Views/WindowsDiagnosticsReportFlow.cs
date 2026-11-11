using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;
using ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using ProjectZD8ForWindows.Platform.Windows.Logging;
using Windows.Storage.Pickers;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Views;

/// <summary>
/// 不具合レポートの書き出しの流れです（設定画面の「不具合レポートを書き出す」と、シェルの「前回は正常に終了しませんでした」のボタンが使う）。
/// </summary>
/// <remarks>責務: 保存先を選んでもらい、zip を作り、エクスプローラーでそのファイルを選んだ状態で開きます。失敗したら理由を見せます。</remarks>
public static class WindowsDiagnosticsReportFlow
{
    /// <summary>いま書き出している最中か（二重に始めない）。</summary>
    private static bool s_isExporting;

    /// <summary>
    /// 不具合レポートを書き出します。
    /// </summary>
    /// <param name="model">診断の表示モデル。</param>
    /// <param name="xamlRoot">表示先。</param>
    /// <returns>書き出して開いたら true。選ばなかった・失敗したら false。</returns>
    public static async Task<bool> ExportAsync(WindowsDiagnosticsModel model, XamlRoot? xamlRoot)
    {
        if (!model.IsAvailable || s_isExporting)
        {
            return false;
        }

        s_isExporting = true;
        try
        {
            var path = await PickSavePathAsync(xamlRoot, model.SuggestedReportFileName());
            if (path is null)
            {
                UiLog.ReportExportCancelled(UiLog.Logger);
                return false;
            }

            try
            {
                var result = await Task.Run(() => model.ExportReport(path));
                if (result is null)
                {
                    return false;
                }

                UiLog.ReportExported(UiLog.Logger, result.LogFileCount, result.ErrorCount, new FileInfo(path).Length);
                Reveal(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                UiLog.ReportExportFailed(UiLog.Logger, exception);
                await ShowFailureAsync(xamlRoot);
                return false;
            }
        }
        finally
        {
            s_isExporting = false;
        }
    }

    /// <summary>保存先を選んでもらいます。</summary>
    /// <param name="xamlRoot">表示先。</param>
    /// <param name="suggestedFileName">既定のファイル名（拡張子つき）。</param>
    /// <returns>選んだ保存先のパス。選ばなければ null。</returns>
    private static async Task<string?> PickSavePathAsync(XamlRoot? xamlRoot, string suggestedFileName)
    {
        if (xamlRoot?.ContentIslandEnvironment is not { } environment)
        {
            return null;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
        };
        picker.FileTypeChoices.Add(LocalizedText.Get("windows.diagnostics.report.file_type"), [".zip"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(environment.AppWindowId));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>エクスプローラーで、そのファイルを選んだ状態で開きます。</summary>
    /// <param name="path">ファイルのパス。</param>
    private static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくても、書き出し自体は終わっている。
        }
    }

    /// <summary>書き出せなかったことを見せます。</summary>
    /// <param name="xamlRoot">表示先。</param>
    /// <returns>完了を表すタスク。</returns>
    private static async Task ShowFailureAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null)
        {
            return;
        }

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Typography.Text(LocalizedText.Get("windows.diagnostics.report.failed.message"), "BodyTextBlockStyle"));
        if (ErrorReport.ReferenceLine() is { } reference)
        {
            body.Children.Add(reference);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            RequestedTheme = DesignTokens.EffectiveTheme,
            Title = LocalizedText.Get("windows.diagnostics.report.failed.title"),
            Content = body,
            CloseButtonText = LocalizedText.Get("windows.diagnostics.close"),
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // ほかのダイアログが開いている間は出せない。ログには残してある。
        }
    }
}
