using ProjectZD8.Application.Logging.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.Ports;

/// <summary>Production取得loopが永続取得証拠を開始するための確定済み入力です。</summary>
public sealed record LiveTelemetryAcquisitionStartInput
{
    /// <summary>取得loopが所有する世代です。</summary>
    public ulong Generation { get; }

    /// <summary>manifestへ固定する取得時PID定義です。</summary>
    public IReadOnlyList<OBDPIDDefinition> Definitions { get; }

    /// <summary>PID定義ごとの確認済みcapabilityと収集選択です。</summary>
    public IReadOnlyList<ConnectionSessionAcquisitionPIDCapabilityInput> Capabilities { get; }

    /// <summary>polling policyが基準tickで確定した要求順です。</summary>
    public IReadOnlyList<OBDPIDRequest> OrderedRequests { get; }

    /// <summary>取得開始に必要な証拠入力を固定します。</summary>
    /// <remarks>責務: 1世代の定義、capability、要求順をmanifest開始入力へまとめます。</remarks>
    /// <param name="generation">取得loopが所有する世代。</param>
    /// <param name="definitions">manifestへ固定する取得時PID定義。</param>
    /// <param name="capabilities">PID定義ごとの確認済みcapabilityと収集選択。</param>
    /// <param name="orderedRequests">polling policyが確定した基準要求順。</param>
    public LiveTelemetryAcquisitionStartInput(
        ulong generation,
        IReadOnlyList<OBDPIDDefinition> definitions,
        IReadOnlyList<ConnectionSessionAcquisitionPIDCapabilityInput> capabilities,
        IReadOnlyList<OBDPIDRequest> orderedRequests)
    {
        Generation = generation;
        Definitions = definitions.ToArray();
        Capabilities = capabilities.ToArray();
        OrderedRequests = orderedRequests.ToArray();
    }
}

/// <summary>Production取得loopが1 batchを方式Bで保存するための確定済み入力です。</summary>
public sealed record LiveTelemetryAcquisitionBatchInput
{
    /// <summary>取得loopが所有する世代です。</summary>
    public ulong Generation { get; }

    /// <summary>session内で安定したbatch番号です。</summary>
    public long BatchOrdinal { get; }

    /// <summary>polling policyが評価したtickです。</summary>
    public ulong PolicyTick { get; }

    /// <summary>batch開始を観測した実時間です。</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>250 ms sleepの予定wake時刻から実開始までの非負遅延です。</summary>
    public ulong? ScheduleDelayNanoseconds { get; }

    /// <summary>今回選択された順序付きPID定義です。</summary>
    public IReadOnlyList<OBDPIDDefinition> Definitions { get; }

    /// <summary>物理要求に使用する終端です。</summary>
    public OBDConnectionEndpoint Endpoint { get; }

    /// <summary>1回のpolling tickを永続batch入力へ固定します。</summary>
    /// <remarks>責務: batch identity、policy tick、schedule遅延、選択定義、終端を1件の取得要求へまとめます。</remarks>
    /// <param name="generation">取得loopが所有する世代。</param>
    /// <param name="batchOrdinal">session内で安定したbatch番号。</param>
    /// <param name="policyTick">polling policyが評価したtick。</param>
    /// <param name="startedAt">batch開始を観測した実時間。</param>
    /// <param name="definitions">今回選択された順序付きPID定義。</param>
    /// <param name="endpoint">物理要求に使用する終端。</param>
    /// <param name="scheduleDelayNanoseconds">tick 0では <c>null</c>、後続tickでは予定wakeからの非負遅延。</param>
    public LiveTelemetryAcquisitionBatchInput(
        ulong generation,
        long batchOrdinal,
        ulong policyTick,
        DateTimeOffset startedAt,
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint,
        ulong? scheduleDelayNanoseconds = null)
    {
        Generation = generation;
        BatchOrdinal = batchOrdinal;
        PolicyTick = policyTick;
        StartedAt = startedAt;
        ScheduleDelayNanoseconds = scheduleDelayNanoseconds;
        Definitions = definitions.ToArray();
        Endpoint = endpoint;
    }
}

/// <summary>Production取得loopを方式BのApplication orchestrationへ接続する能力です。</summary>
public interface ILiveTelemetryAcquisitionEvidencePort
{
    /// <summary>manifestと開始境界を保存し、成功した世代だけを取得可能にします。</summary>
    /// <remarks>責務: 1世代の確定済み取得入力を物理要求許可へ変換します。</remarks>
    /// <param name="input">定義、capability、policy順を含む取得開始入力。</param>
    /// <returns>開始処理の完了。</returns>
    /// <exception cref="Exception">session不在、入力不正、stale世代、または保存失敗。</exception>
    Task Start(LiveTelemetryAcquisitionStartInput input);

    /// <summary>1回のpolling tickを方式Bで取得・保存して表示用sampleを返します。</summary>
    /// <remarks>責務: 1 batchの物理観測を原子保存済みRaw証拠と表示用sampleへ変換します。</remarks>
    /// <param name="input">stable ordinalとpolicy tickを持つbatch入力。</param>
    /// <returns>同じ物理応答から生成した表示用sample。</returns>
    /// <exception cref="Exception">stale世代、入力不正、通信後の永続化失敗、またはbatch確定失敗。</exception>
    Task<IReadOnlyList<OBDPIDSample>> Acquire(LiveTelemetryAcquisitionBatchInput input);

    /// <summary>指定世代からの新しい証拠作成を停止します。</summary>
    /// <remarks>責務: 1世代のmanifestとbatch orchestrationをstale状態へ遷移させます。</remarks>
    /// <param name="generation">停止する取得世代。</param>
    /// <returns>停止処理の完了。</returns>
    Task Cancel(ulong generation);
}
