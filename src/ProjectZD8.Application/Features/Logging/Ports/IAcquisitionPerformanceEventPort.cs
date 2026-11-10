namespace ProjectZD8.Application.Logging.Ports;

/// <summary>取得性能として区間計測する匿名操作です。</summary>
public enum AcquisitionPerformanceOperation
{
    /// <summary>1世代のmanifest作成を含む取得開始です。</summary>
    AcquisitionStart,

    /// <summary>manifestと取得開始境界の原子保存です。</summary>
    ManifestPersistence,

    /// <summary>open batch metadataの保存です。</summary>
    BatchOpenPersistence,

    /// <summary>request dispatch開始状態の保存です。</summary>
    RequestDispatchPersistence,

    /// <summary>physical requestからtyped observationまでの通信です。</summary>
    RequestTransport,

    /// <summary>responded Rawとterminal evidenceの原子保存です。</summary>
    RespondedPersistence,

    /// <summary>Rawを持たないterminal evidenceの保存です。</summary>
    NonRespondedPersistence,

    /// <summary>terminal batchのseal保存です。</summary>
    BatchSealPersistence,

    /// <summary>Production loopにおける1 batch全体の取得です。</summary>
    BatchAcquisition,

    /// <summary>stale世代から拒否したbatch取得です。</summary>
    StaleBatchRejection,

    /// <summary>指定世代からの新規取得を停止する取消処理です。</summary>
    GenerationCancellation,

    /// <summary>sessionと取得終了境界の原子保存です。</summary>
    SessionTerminationPersistence,

    /// <summary>process終了後に残った取得証拠の回復保存です。</summary>
    TerminationRecoveryPersistence,
}

/// <summary><see cref="AcquisitionPerformanceOperation"/> の raw value 変換です。</summary>
public static class AcquisitionPerformanceOperationRawValue
{
    /// <summary>Swift と同じ raw value 文字列を返します。</summary>
    /// <remarks>責務: 1件の操作をケース名そのままの文字列へ変換します。</remarks>
    /// <param name="value">変換する操作。</param>
    /// <returns>raw value 文字列。</returns>
    public static string ToRawValue(this AcquisitionPerformanceOperation value) => value switch
    {
        AcquisitionPerformanceOperation.AcquisitionStart => "acquisitionStart",
        AcquisitionPerformanceOperation.ManifestPersistence => "manifestPersistence",
        AcquisitionPerformanceOperation.BatchOpenPersistence => "batchOpenPersistence",
        AcquisitionPerformanceOperation.RequestDispatchPersistence => "requestDispatchPersistence",
        AcquisitionPerformanceOperation.RequestTransport => "requestTransport",
        AcquisitionPerformanceOperation.RespondedPersistence => "respondedPersistence",
        AcquisitionPerformanceOperation.NonRespondedPersistence => "nonRespondedPersistence",
        AcquisitionPerformanceOperation.BatchSealPersistence => "batchSealPersistence",
        AcquisitionPerformanceOperation.BatchAcquisition => "batchAcquisition",
        AcquisitionPerformanceOperation.StaleBatchRejection => "staleBatchRejection",
        AcquisitionPerformanceOperation.GenerationCancellation => "generationCancellation",
        AcquisitionPerformanceOperation.SessionTerminationPersistence => "sessionTerminationPersistence",
        AcquisitionPerformanceOperation.TerminationRecoveryPersistence => "terminationRecoveryPersistence",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value 文字列から操作を復元します。</summary>
    /// <remarks>責務: Swift と同じ raw value だけを操作へ変換します。</remarks>
    /// <param name="rawValue">raw value 文字列。</param>
    /// <param name="value">復元した操作。</param>
    /// <returns>復元できたら <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionPerformanceOperation value)
    {
        foreach (var candidate in AllCases)
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>宣言順の全ケースです。</summary>
    private static IReadOnlyList<AcquisitionPerformanceOperation> AllCases { get; } =
        Enum.GetValues<AcquisitionPerformanceOperation>();
}

/// <summary>性能区間へ付与する匿名の取得位置です。</summary>
public sealed record AcquisitionPerformanceContext
{
    /// <summary>polling workflowの世代です。</summary>
    public ulong? Generation { get; }

    /// <summary>session内のbatch ordinalです。</summary>
    public long? BatchOrdinal { get; }

    /// <summary>batch内のrequest ordinalです。</summary>
    public int? RequestOrdinal { get; }

    /// <summary>polling policyが評価したtickです。</summary>
    public ulong? PolicyTick { get; }

    /// <summary>予定したwake時刻からbatch開始までの非負遅延です。</summary>
    public ulong? ScheduleDelayNanoseconds { get; }

    /// <summary>個人・端末・車両識別子を含まない取得位置を生成します。</summary>
    /// <remarks>責務: performance eventへ必要な匿名ordinalとschedule遅延だけをまとめます。</remarks>
    /// <param name="generation">polling workflowの世代。</param>
    /// <param name="batchOrdinal">session内のbatch ordinal。</param>
    /// <param name="requestOrdinal">batch内のrequest ordinal。</param>
    /// <param name="policyTick">polling policyが評価したtick。</param>
    /// <param name="scheduleDelayNanoseconds">予定wake時刻から実開始までの非負遅延。</param>
    public AcquisitionPerformanceContext(
        ulong? generation = null,
        long? batchOrdinal = null,
        int? requestOrdinal = null,
        ulong? policyTick = null,
        ulong? scheduleDelayNanoseconds = null)
    {
        Generation = generation;
        BatchOrdinal = batchOrdinal;
        RequestOrdinal = requestOrdinal;
        PolicyTick = policyTick;
        ScheduleDelayNanoseconds = scheduleDelayNanoseconds;
    }
}

/// <summary>開始済みperformance区間を終了eventへ結び付けるtokenです。</summary>
public sealed record AcquisitionPerformanceInterval
{
    /// <summary>process内だけで使用する匿名interval番号です。</summary>
    public ulong Identifier { get; }

    /// <summary>区間が表す操作です。</summary>
    public AcquisitionPerformanceOperation Operation { get; }

    /// <summary>区間へ固定した匿名取得位置です。</summary>
    public AcquisitionPerformanceContext Context { get; }

    /// <summary>匿名番号、操作、取得位置を開始済み区間へ固定します。</summary>
    /// <remarks>責務: begin、queue entry、endを同じ匿名performance区間へ関連付けます。</remarks>
    /// <param name="identifier">process内だけで使用する匿名interval番号。</param>
    /// <param name="operation">区間が表す操作。</param>
    /// <param name="context">区間へ固定する匿名取得位置。</param>
    public AcquisitionPerformanceInterval(
        ulong identifier,
        AcquisitionPerformanceOperation operation,
        AcquisitionPerformanceContext context)
    {
        Identifier = identifier;
        Operation = operation;
        Context = context;
    }
}

/// <summary>performance区間が完了した結果です。</summary>
public enum AcquisitionPerformanceOutcome
{
    /// <summary>操作が成功しました。</summary>
    Succeeded,

    /// <summary>操作が失敗しました。</summary>
    Failed,

    /// <summary>操作がcancelされました。</summary>
    Cancelled,
}

/// <summary><see cref="AcquisitionPerformanceOutcome"/> の raw value 変換です。</summary>
public static class AcquisitionPerformanceOutcomeRawValue
{
    /// <summary>Swift と同じ raw value 文字列を返します。</summary>
    /// <remarks>責務: 1件の完了結果をケース名そのままの文字列へ変換します。</remarks>
    /// <param name="value">変換する完了結果。</param>
    /// <returns>raw value 文字列。</returns>
    public static string ToRawValue(this AcquisitionPerformanceOutcome value) => value switch
    {
        AcquisitionPerformanceOutcome.Succeeded => "succeeded",
        AcquisitionPerformanceOutcome.Failed => "failed",
        AcquisitionPerformanceOutcome.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value 文字列から完了結果を復元します。</summary>
    /// <remarks>責務: Swift と同じ raw value だけを完了結果へ変換します。</remarks>
    /// <param name="rawValue">raw value 文字列。</param>
    /// <param name="value">復元した完了結果。</param>
    /// <returns>復元できたら <c>true</c>。</returns>
    public static bool TryParse(string rawValue, out AcquisitionPerformanceOutcome value)
    {
        switch (rawValue)
        {
            case "succeeded": value = AcquisitionPerformanceOutcome.Succeeded; return true;
            case "failed": value = AcquisitionPerformanceOutcome.Failed; return true;
            case "cancelled": value = AcquisitionPerformanceOutcome.Cancelled; return true;
            default: value = default; return false;
        }
    }
}

/// <summary>Production取得を匿名performance eventへ通知する境界です。</summary>
public interface IAcquisitionPerformanceEventPort
{
    /// <summary>匿名取得位置に対するperformance区間を開始します。</summary>
    /// <remarks>責務: 1件の取得操作を後続eventと関連付けられる開始済み区間へ変換します。</remarks>
    /// <param name="operation">計測する取得操作。</param>
    /// <param name="context">個人・端末・車両識別子を含まない取得位置。</param>
    /// <returns>queue entryと終了通知に再利用する匿名区間。</returns>
    AcquisitionPerformanceInterval Begin(AcquisitionPerformanceOperation operation, AcquisitionPerformanceContext context);

    /// <summary>DB write closureへ入った時点を通知します。</summary>
    /// <remarks>責務: 1件の永続化区間についてcall開始後のQueue通過点を記録します。</remarks>
    /// <param name="interval"><c>Begin</c>が返した同じ永続化区間。</param>
    void QueueDidEnter(AcquisitionPerformanceInterval interval);

    /// <summary>performance区間を結果付きで終了します。</summary>
    /// <remarks>責務: 1件の開始済み取得操作を成功、失敗、cancelのいずれかで閉じます。</remarks>
    /// <param name="interval"><c>Begin</c>が返した同じ区間。</param>
    /// <param name="outcome">区間の完了結果。</param>
    void End(AcquisitionPerformanceInterval interval, AcquisitionPerformanceOutcome outcome);
}

/// <summary>通常buildでperformance eventを破棄する既定実装です。</summary>
public sealed class NoOpAcquisitionPerformanceEventPort : IAcquisitionPerformanceEventPort
{
    /// <summary>副作用のないperformance境界を生成します。</summary>
    /// <remarks>責務: 明示測定build以外の取得を計測副作用のない境界へ結び付けます。</remarks>
    public NoOpAcquisitionPerformanceEventPort()
    {
    }

    /// <summary>eventを出力しない開始済み区間を返します。</summary>
    /// <remarks>責務: 1件の取得操作を副作用のない固定tokenへ変換します。</remarks>
    /// <param name="operation">計測しない取得操作。</param>
    /// <param name="context">計測しない匿名取得位置。</param>
    /// <returns>後続no-op通知で再利用する固定token。</returns>
    public AcquisitionPerformanceInterval Begin(AcquisitionPerformanceOperation operation, AcquisitionPerformanceContext context) =>
        new(0, operation, context);

    /// <summary>Queue通過通知を破棄します。</summary>
    /// <remarks>責務: 通常buildのQueue通過を追加副作用なしで受理します。</remarks>
    /// <param name="interval">破棄する区間。</param>
    public void QueueDidEnter(AcquisitionPerformanceInterval interval)
    {
    }

    /// <summary>区間終了通知を破棄します。</summary>
    /// <remarks>責務: 通常buildの区間終了を追加副作用なしで受理します。</remarks>
    /// <param name="interval">破棄する区間。</param>
    /// <param name="outcome">破棄する完了結果。</param>
    public void End(AcquisitionPerformanceInterval interval, AcquisitionPerformanceOutcome outcome)
    {
    }
}
