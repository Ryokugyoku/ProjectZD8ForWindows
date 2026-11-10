namespace ProjectZD8.Data.Files;

/// <summary>車両写真が許容容量を超えていることを表します。</summary>
/// <remarks>iOS版の <c>CocoaError(.fileReadTooLarge)</c> に相当します。</remarks>
public sealed class VehiclePhotoTooLargeException : IOException
{
    /// <summary>許容する最大バイト数を指定して生成します。</summary>
    /// <remarks>責務: 容量超過の理由を例外へ包みます。</remarks>
    /// <param name="maximumByteCount">許容する最大バイト数。</param>
    public VehiclePhotoTooLargeException(long maximumByteCount)
        : base($"Vehicle photo exceeds {maximumByteCount} bytes")
    {
        MaximumByteCount = maximumByteCount;
    }

    /// <summary>許容する最大バイト数です。</summary>
    public long MaximumByteCount { get; }
}
