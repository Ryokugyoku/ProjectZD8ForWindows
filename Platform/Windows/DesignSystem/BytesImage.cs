using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// メモリ上の画像のバイト列（車両や整備の写真）を表示する Image を作ります。
/// </summary>
public static class BytesImage
{
    /// <summary>
    /// 画像を作ります（読み込みは後で行います）。
    /// </summary>
    /// <remarks>責務: 枠いっぱいに切り抜いて出す Image を作り、バイト列の読み込みを始めます。</remarks>
    /// <param name="data">画像のバイト列。</param>
    /// <param name="decodePixelWidth">読み込むときの幅（px）。0 なら元の大きさ。</param>
    /// <returns>Image。</returns>
    public static Image Create(byte[] data, int decodePixelWidth = 0)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        Load(image, data, decodePixelWidth);
        return image;
    }

    /// <summary>
    /// バイト列から画像を読み込みます。
    /// </summary>
    /// <remarks>責務: バイト列をメモリのストリームに書いて BitmapImage に読み込み、読めなければ何も出しません。</remarks>
    /// <param name="image">出す先。</param>
    /// <param name="data">画像のバイト列。</param>
    /// <param name="decodePixelWidth">読み込むときの幅。</param>
    private static async void Load(Image image, byte[] data, int decodePixelWidth)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(data);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }

            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception)
        {
            image.Source = null;
        }
    }
}
