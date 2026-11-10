using ProjectZD8.Application.VehicleManagement.Ports;

namespace ProjectZD8.Data.Files;

/// <summary>ファイルURLから容量上限内の車両写真を読み込みます。</summary>
/// <remarks>
/// iOS版 <c>VehiclePhotoFileImporter</c> の移植です。iOS版のセキュリティスコープ付きURLの一時アクセスはWindowsにないため、
/// 画面がファイル選択で得たパスの <c>file:</c> URLをそのまま読みます。上限超過はiOS版の <c>CocoaError(.fileReadTooLarge)</c> の代わりに
/// <see cref="VehiclePhotoTooLargeException"/> を投げます。
/// </remarks>
public sealed class VehiclePhotoFileImporter : IVehiclePhotoImportPort
{
    /// <summary>CloudKit Assetへ渡す前に許容する最大画像容量です。</summary>
    private readonly long _maximumByteCount;

    /// <summary>最大容量を指定して画像読込境界を生成します。</summary>
    /// <remarks>責務: 1件の車両写真に許可する最大データ量を固定します。</remarks>
    /// <param name="maximumByteCount">許容する最大バイト数。既定は20MiB。</param>
    public VehiclePhotoFileImporter(long maximumByteCount = 20 * 1024 * 1024) => _maximumByteCount = maximumByteCount;

    /// <summary>指定URLのファイルを読み、容量内の画像データを返します。</summary>
    /// <remarks>責務: 1件の選択ファイルを読み込む前に容量を検証します。</remarks>
    /// <param name="url">ファイル選択で得た画像ファイルのURL。</param>
    /// <returns>最大容量以下の画像データ。</returns>
    /// <exception cref="ArgumentException">ファイルURLでない場合。</exception>
    /// <exception cref="VehiclePhotoTooLargeException">最大容量を超える場合。</exception>
    /// <exception cref="IOException">ファイル読込に失敗した場合。</exception>
    public async Task<byte[]> ImportPhoto(Uri url)
    {
        if (!url.IsFile)
        {
            throw new ArgumentException("Photo URL must be a file URL", nameof(url));
        }
        var path = url.LocalPath;
        if (new FileInfo(path).Length > _maximumByteCount)
        {
            throw new VehiclePhotoTooLargeException(_maximumByteCount);
        }
        var data = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        if (data.LongLength > _maximumByteCount)
        {
            throw new VehiclePhotoTooLargeException(_maximumByteCount);
        }
        return data;
    }
}
