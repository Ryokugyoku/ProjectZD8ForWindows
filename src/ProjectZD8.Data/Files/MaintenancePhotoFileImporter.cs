using ProjectZD8.Application.Maintenance.Ports;

namespace ProjectZD8.Data.Files;

/// <summary>ファイルURLから整備写真を読み込みます。</summary>
/// <remarks>
/// iOS版 <c>MaintenancePhotoFileImporter</c> の移植です。iOS版のセキュリティスコープ付きURLの一時アクセスはWindowsにないため、
/// 画面がファイル選択で得たパスの <c>file:</c> URLをそのまま読みます。
/// </remarks>
public sealed class MaintenancePhotoFileImporter : IMaintenancePhotoImportPort
{
    /// <summary>選択URLから写真データを取得します。</summary>
    /// <remarks>責務: 1件のファイルURLを整備写真データへ変換します。</remarks>
    /// <param name="url">画面が選択した画像ファイルのURL。</param>
    /// <returns>画像ファイルのバイト列。</returns>
    /// <exception cref="ArgumentException">ファイルURLでない場合。</exception>
    /// <exception cref="IOException">ファイル読込に失敗した場合。</exception>
    public async Task<byte[]> ImportPhoto(Uri url)
    {
        if (!url.IsFile)
        {
            throw new ArgumentException("Photo URL must be a file URL", nameof(url));
        }
        return await File.ReadAllBytesAsync(url.LocalPath).ConfigureAwait(false);
    }
}
