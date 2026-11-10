using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Data.Persistence.LogStore.Format;

/// <summary>セッションログ（追記専用ジャーナル）の1レコードです。</summary>
/// <remarks>
/// 1レコードは1回の原子的な変更です。iOS版でSQLiteの1トランザクションだった変更（正応答Rawとrequest終端など）は
/// 1レコードにまとめ、長さとCRC32で枠付けして書くことで、途中で切れたレコードを丸ごと捨てられるようにします。
/// </remarks>
internal abstract record SessionLogRecord
{
    /// <summary>派生型だけが生成できるようにします。</summary>
    /// <remarks>責務: レコード種別を下の派生型に限定します。</remarks>
    private SessionLogRecord()
    {
    }

    /// <summary>取得証拠を伴わない生ログの追記です。</summary>
    /// <param name="Entry">採番済みの生ログ。</param>
    internal sealed record Raw(ConnectionSessionRawLogEntry Entry) : SessionLogRecord;

    /// <summary>open batchと選択済みrequest列の開始です。</summary>
    /// <param name="Ordinal">batch番号。</param>
    /// <param name="Generation">取得世代。</param>
    /// <param name="PolicyTick">polling方針のtick。</param>
    /// <param name="IsSelectionEvaluationComplete">選択評価が完了しているか。</param>
    /// <param name="StartedTicks">batch開始日時（UTCのTicks）。</param>
    /// <param name="ManifestOrdinals">request順のmanifest PID位置。</param>
    internal sealed record BatchBegun(
        long Ordinal,
        ulong Generation,
        ulong PolicyTick,
        bool IsSelectionEvaluationComplete,
        long StartedTicks,
        int[] ManifestOrdinals) : SessionLogRecord;

    /// <summary>selected-only requestのdispatch開始です。</summary>
    /// <param name="Ordinal">batch番号。</param>
    /// <param name="RequestOrdinal">batch内要求順。</param>
    internal sealed record DispatchBegun(long Ordinal, int RequestOrdinal) : SessionLogRecord;

    /// <summary>Rawを伴わないrequest終端です。</summary>
    /// <param name="Ordinal">batch番号。</param>
    /// <param name="Request">終端request証拠。</param>
    internal sealed record RequestTerminal(long Ordinal, PIDRequestEvidence Request) : SessionLogRecord;

    /// <summary>正応答Rawとresponded終端の原子的な組です。</summary>
    /// <param name="Entry">採番済みの生ログ。</param>
    /// <param name="Ordinal">batch番号。</param>
    /// <param name="Request">Raw sequenceを参照する終端request証拠。</param>
    internal sealed record Responded(ConnectionSessionRawLogEntry Entry, long Ordinal, PIDRequestEvidence Request) : SessionLogRecord;

    /// <summary>batchの終端（request終端の更新を含む）です。</summary>
    /// <param name="Ordinal">batch番号。</param>
    /// <param name="CompletionState">終端状態。</param>
    /// <param name="CompletedTicks">終端確定日時（UTCのTicks）。</param>
    /// <param name="Failure">失敗理由。</param>
    /// <param name="Updates">同時に終端へ更新するrequest証拠。</param>
    internal sealed record BatchSealed(
        long Ordinal,
        AcquisitionBatchCompletionState CompletionState,
        long CompletedTicks,
        AcquisitionBatchFailure? Failure,
        PIDRequestEvidence[] Updates) : SessionLogRecord;

    /// <summary>batch全体の写しです。世代の書き直し（生ログの除去・復元）で使います。</summary>
    /// <param name="Batch">batch証拠の全体。</param>
    internal sealed record BatchSnapshot(AcquisitionBatchEvidence Batch) : SessionLogRecord;
}
