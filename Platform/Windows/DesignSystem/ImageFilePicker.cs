using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 画像ファイルを 1 つ選ぶ、Windows のファイルを開くダイアログです（SwiftUI の <c>.fileImporter(allowedContentTypes: [.image])</c>）。
/// </summary>
public static class ImageFilePicker
{
    /// <summary>
    /// 画像ファイルを選んでもらいます。
    /// </summary>
    /// <remarks>責務: 表示先のウインドウに結び付けたファイル選択を開き、選んだファイルの URI を返します。</remarks>
    /// <param name="xamlRoot">表示先。</param>
    /// <returns>選んだファイルの URI。選ばなければ null。</returns>
    public static async Task<Uri?> PickAsync(XamlRoot? xamlRoot)
    {
        if (xamlRoot?.ContentIslandEnvironment is not { } environment)
        {
            return null;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".heic", ".bmp", ".gif", ".webp" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, Win32Interop.GetWindowFromWindowId(environment.AppWindowId));
        var file = await picker.PickSingleFileAsync();
        return file is null ? null : new Uri(file.Path);
    }
}
