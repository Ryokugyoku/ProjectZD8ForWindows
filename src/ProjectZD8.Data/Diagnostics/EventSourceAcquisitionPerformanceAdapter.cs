using ProjectZD8.Application.Logging.Ports;

namespace ProjectZD8.Data.Diagnostics;

/// <summary>匿名の取得性能区間をETWイベントへ変換します。</summary>
/// <remarks>
/// iOS版 <c>OSSignpostAcquisitionPerformanceAdapter</c> の移植です。Apple signpostの代わりに
/// <see cref="AcquisitionPerformanceEventSource"/>（プロバイダー名 <c>ProjectZD8-AcquisitionPerformance</c>）へ出します。
/// </remarks>
public sealed class EventSourceAcquisitionPerformanceAdapter : IAcquisitionPerformanceEventPort
{
    /// <summary>repository外artifactと照合する匿名run識別子です。</summary>
    private readonly string _runIdentifier;
    /// <summary>出力先のイベントソースです。</summary>
    private readonly AcquisitionPerformanceEventSource _log;
    /// <summary>interval番号の採番を直列化するロックです。</summary>
    private readonly object _identifierLock = new();
    /// <summary>次に割り当てるprocess内interval番号です。</summary>
    private ulong _nextIdentifier = 1;

    /// <summary>明示された匿名run識別子のadapterを生成します。</summary>
    /// <remarks>責務: 1件の承認済み測定runを専用イベントソースへ結び付けます。</remarks>
    /// <param name="runIdentifier">1〜64バイトの英数字、<c>.</c>、<c>_</c>、<c>-</c>だけを含む匿名run識別子。</param>
    /// <exception cref="ArgumentException">識別子が条件を満たさない場合。</exception>
    public EventSourceAcquisitionPerformanceAdapter(string runIdentifier)
    {
        if (!IsValid(runIdentifier))
        {
            throw new ArgumentException("Run identifier must be 1-64 ASCII letters, digits, '.', '_' or '-'", nameof(runIdentifier));
        }
        _runIdentifier = runIdentifier;
        _log = AcquisitionPerformanceEventSource.Log;
    }

    /// <summary>有効な匿名run識別子がある場合だけadapterを生成します。</summary>
    /// <remarks>責務: 任意の外部入力をprivacy制約を満たす測定境界または明示的な無効状態へ変換します。</remarks>
    /// <param name="runIdentifier">測定buildへ外部から渡された任意文字列。</param>
    /// <returns>1〜64バイトの許可ASCIIだけならadapter、それ以外は <see langword="null"/>。</returns>
    public static EventSourceAcquisitionPerformanceAdapter? MakeIfValid(string? runIdentifier) =>
        runIdentifier is not null && IsValid(runIdentifier) ? new EventSourceAcquisitionPerformanceAdapter(runIdentifier) : null;

    /// <summary>匿名取得位置に対する区間を開始します。</summary>
    /// <remarks>責務: 1件の取得操作を匿名ordinalだけを含む開始イベントへ変換します。</remarks>
    /// <param name="operation">計測する取得操作。</param>
    /// <param name="context">個人・端末・車両識別子を含まない取得位置。</param>
    /// <returns>待機終了と終了イベントに再利用する匿名区間。</returns>
    public AcquisitionPerformanceInterval Begin(AcquisitionPerformanceOperation operation, AcquisitionPerformanceContext context)
    {
        var interval = new AcquisitionPerformanceInterval(MakeIdentifier(), operation, context);
        if (_log.IsEnabled())
        {
            _log.Begin(
                _runIdentifier,
                Signed(interval.Identifier),
                operation.ToRawValue(),
                Signed(context.Generation),
                context.BatchOrdinal ?? -1,
                context.RequestOrdinal ?? -1,
                Signed(context.PolicyTick),
                Signed(context.ScheduleDelayNanoseconds));
        }
        return interval;
    }

    /// <summary>保存先の待機を終えた時点を同じ区間番号で記録します。</summary>
    /// <remarks>責務: 1件の永続化区間を待機終了イベントへ関連付けます。</remarks>
    /// <param name="interval"><see cref="Begin"/> が返した同じ区間。</param>
    public void QueueDidEnter(AcquisitionPerformanceInterval interval)
    {
        if (!_log.IsEnabled())
        {
            return;
        }
        var context = interval.Context;
        _log.QueueEntry(
            _runIdentifier,
            Signed(interval.Identifier),
            interval.Operation.ToRawValue(),
            Signed(context.Generation),
            context.BatchOrdinal ?? -1,
            context.RequestOrdinal ?? -1,
            Signed(context.PolicyTick),
            Signed(context.ScheduleDelayNanoseconds));
    }

    /// <summary>区間を結果付きで終了します。</summary>
    /// <remarks>責務: 1件の開始イベントを同じ匿名番号と完了結果で閉じます。</remarks>
    /// <param name="interval"><see cref="Begin"/> が返した同じ区間。</param>
    /// <param name="outcome">区間の完了結果。</param>
    public void End(AcquisitionPerformanceInterval interval, AcquisitionPerformanceOutcome outcome)
    {
        if (_log.IsEnabled())
        {
            _log.End(_runIdentifier, Signed(interval.Identifier), interval.Operation.ToRawValue(), outcome.ToRawValue());
        }
    }

    /// <summary>process内で重複しない次のinterval番号を返します。</summary>
    /// <remarks>責務: 並行する取得eventへロックで直列化した匿名番号を1件割り当てます。</remarks>
    /// <returns>0を使用せず、上限で1へ戻るprocess内番号。</returns>
    private ulong MakeIdentifier()
    {
        lock (_identifierLock)
        {
            var identifier = _nextIdentifier;
            _nextIdentifier = _nextIdentifier == ulong.MaxValue ? 1 : _nextIdentifier + 1;
            return identifier;
        }
    }

    /// <summary>任意の非負値を欠落sentinelと区別できる符号付き値へ変換します。</summary>
    /// <remarks>責務: 値をlongの範囲へ丸め、欠落を -1 へ変換します。</remarks>
    /// <param name="value">変換する任意値。</param>
    /// <returns><see langword="null"/>なら -1、それ以外はlong範囲へ丸めた値。</returns>
    private static long Signed(ulong? value) => value is { } present ? (long)Math.Min(present, long.MaxValue) : -1;

    /// <summary>run識別子が匿名ASCII契約を満たすかを返します。</summary>
    /// <remarks>責務: 1件の外部run識別子を長さと許可文字だけで検証します。</remarks>
    /// <param name="runIdentifier">検証する外部入力。</param>
    /// <returns>1〜64バイトかつ英数字、<c>.</c>、<c>_</c>、<c>-</c>だけなら <see langword="true"/>。</returns>
    private static bool IsValid(string runIdentifier)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(runIdentifier);
        return bytes.Length is >= 1 and <= 64
            && bytes.All(value => value is >= (byte)'0' and <= (byte)'9'
                or >= (byte)'A' and <= (byte)'Z'
                or >= (byte)'a' and <= (byte)'z'
                or (byte)'-' or (byte)'.' or (byte)'_');
    }
}
