namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>生ログと取得証跡のストア（LogStore）の設定です。</summary>
public sealed record LogStoreOptions
{
    /// <summary>既定の設定です。</summary>
    public static LogStoreOptions Default { get; } = new();

    /// <summary>1つのセグメントファイルの上限バイト数です。超えると次のセグメントへ切り替えます。</summary>
    public long SegmentMaxBytes { get; init; } = 16L * 1024 * 1024;

    /// <summary>書込み待ちキューの上限バイト数です。</summary>
    public long QueueCapacityBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>キューが満杯のときに書込みを待つ最長時間です。超えると保存先利用不能として失敗します。</summary>
    public TimeSpan EnqueueTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>ディスクへ確定する方針です。</summary>
    public LogStoreFlushPolicy FlushPolicy { get; init; } = LogStoreFlushPolicy.EveryGroup;

    /// <summary><see cref="LogStoreFlushPolicy.Periodic"/> のときの確定間隔です。</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>確定済み（切替後）のセグメントを圧縮するかどうかです。</summary>
    public bool CompressSealedSegments { get; init; } = true;

    /// <summary>疎な索引の1ブロックに含める生ログ件数です。</summary>
    public int IndexStride { get; init; } = 256;

    /// <summary>メモリ上に保持する、書込み中でないセッションログの最大数です。</summary>
    public int MaximumIdleSessionLogs { get; init; } = 4;
}
