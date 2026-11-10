using ProjectZD8.Data.Files;
using ProjectZD8.Data.Tests.TestSupport;

namespace ProjectZD8.Data.Tests.Files;

/// <summary>写真ファイルの読込を検証します。</summary>
/// <remarks>iOS版にテストはありません。Windows版の読込（ファイルURL、容量上限）を確かめます。</remarks>
public sealed class PhotoFileImporterTests
{
    /// <summary>車両写真が上限内なら読み、超えると拒否することを確かめます。</summary>
    [Fact]
    public async Task VehiclePhotoImporterEnforcesMaximumSize()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "photo.jpg");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await new VehiclePhotoFileImporter(maximumByteCount: 4).ImportPhoto(new Uri(path)));
        var error = await Assert.ThrowsAsync<VehiclePhotoTooLargeException>(() => new VehiclePhotoFileImporter(maximumByteCount: 3).ImportPhoto(new Uri(path)));
        Assert.Equal(3, error.MaximumByteCount);
    }

    /// <summary>整備写真を読み、ファイルURL以外を拒否することを確かめます。</summary>
    [Fact]
    public async Task MaintenancePhotoImporterReadsFileUrlsOnly()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "写真.png");
        await File.WriteAllBytesAsync(path, [9, 8]);

        Assert.Equal(new byte[] { 9, 8 }, await new MaintenancePhotoFileImporter().ImportPhoto(new Uri(path)));
        await Assert.ThrowsAsync<ArgumentException>(() => new MaintenancePhotoFileImporter().ImportPhoto(new Uri("https://example.com/photo.png")));
        await Assert.ThrowsAsync<ArgumentException>(() => new VehiclePhotoFileImporter().ImportPhoto(new Uri("https://example.com/photo.png")));
    }
}
