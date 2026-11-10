namespace ProjectZD8.Data.Persistence.LogStore;

/// <summary>ログの書込みをディスクへ確定（fsync）する方針です。</summary>
public enum LogStoreFlushPolicy
{
    /// <summary>書込みスレッドがまとめて書いたグループごとに確定します（group commit）。既定です。</summary>
    EveryGroup,

    /// <summary><see cref="LogStoreOptions.FlushInterval"/> ごとに確定します。間の書込みはOSのキャッシュに残ります。</summary>
    Periodic,

    /// <summary>明示的な確定（セッション終了など）以外はOSに任せます。</summary>
    OperatingSystem,
}
