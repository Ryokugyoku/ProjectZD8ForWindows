namespace ProjectZD8.Diagnostics;

/// <summary>
/// 分類（<c>cat</c>）の名前です（docs/LOGGING_STRATEGY.md の 3 の表）。
/// </summary>
public static class DiagnosticCategories
{
    /// <summary>起動・終了・前回の異常終了・設定の読み込み・機能フラグ。</summary>
    public const string App = "app";

    /// <summary>画面の移動、ダイアログ、利用者の主な操作。</summary>
    public const string UI = "ui";

    /// <summary>機器の探索、到来の監視。</summary>
    public const string ObdDiscovery = "obd.discovery";

    /// <summary>接続・切断・初期化・プロトコルの決定。</summary>
    public const string ObdConnect = "obd.connect";

    /// <summary>ELM327 の送信と受信（Trace）。</summary>
    public const string ObdWire = "obd.wire";

    /// <summary>取得の開始・停止、バッチの保存、落ちたバッチ、周期の遅れ。</summary>
    public const string Acquisition = "acq";

    /// <summary>SQLite を開く・移行・失敗。</summary>
    public const string StoreSqlite = "store.sqlite";

    /// <summary>生ログストアの開き直し・末尾の切り詰め・fsync の失敗。</summary>
    public const string StoreLog = "store.log";

    /// <summary>サインイン・復元・失効。</summary>
    public const string Auth = "auth";

    /// <summary>CloudKit の要求と結果。</summary>
    public const string Sync = "sync";

    /// <summary>診断ログ自身（捨てた件数など）。</summary>
    public const string Diagnostics = "diag";
}
