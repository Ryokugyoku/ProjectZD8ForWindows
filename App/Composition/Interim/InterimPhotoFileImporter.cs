using System;
using System.IO;
using System.Threading.Tasks;
using ProjectZD8.Application.Maintenance.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】画面で選んだ画像ファイルをそのまま読み込む写真の取り込みです（縮小・形式変換はしません）。
/// </summary>
/// <remarks>iOS 版の <c>VehiclePhotoFileImporter</c> と <c>MaintenancePhotoFileImporter</c> の代わり。永続化担当の Data/Files 実装ができたら差し替えます。</remarks>
public sealed class InterimPhotoFileImporter : IVehiclePhotoImportPort, IMaintenancePhotoImportPort
{
    /// <summary>取り込める最大のファイルサイズ（バイト）です。</summary>
    private const long MaximumByteCount = 12 * 1024 * 1024;

    /// <summary>画像ファイルを読み込みます。</summary>
    /// <remarks>責務: ローカルファイルの URL から画像のバイト列を読みます。大きすぎるファイルは拒みます。</remarks>
    /// <param name="url">ファイルの URL。</param>
    /// <returns>画像のバイト列。</returns>
    public async Task<byte[]> ImportPhoto(Uri url)
    {
        if (!url.IsFile)
        {
            throw new IOException("Only local files can be imported.");
        }

        var info = new FileInfo(url.LocalPath);
        if (!info.Exists || info.Length > MaximumByteCount)
        {
            throw new IOException("The photo file is missing or too large.");
        }

        return await File.ReadAllBytesAsync(url.LocalPath);
    }
}
