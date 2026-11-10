using ProjectZD8.Application.Analysis.State;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Analysis.UseCases;

/// <summary>保存済みRawログへPID定義の数式を適用し時系列表示値を作成します。</summary>
public sealed class DecodeSessionLogTimelineUseCase
{
    /// <summary>Rawログを復元する永続化境界です。</summary>
    private readonly IConnectionSessionRawLogRepository _rawLogRepository;
    /// <summary>PID定義を復元する永続化境界です。</summary>
    private readonly IOBDPIDDefinitionRepository _definitionRepository;
    /// <summary>保存済み数式を評価するDomainポリシーです。</summary>
    private readonly OBDPIDFormulaEvaluator _evaluator;
    /// <summary>1回の画面反映で変換する最大Rawログ件数です。</summary>
    private readonly int _batchSize;

    /// <summary>永続化境界と評価器を注入して生成します。</summary>
    /// <remarks>責務: セッション時系列変換に必要な読取境界と式評価器を固定します。</remarks>
    /// <param name="rawLogRepository">保存済みRawログの取得先。</param>
    /// <param name="definitionRepository">PID変換定義の取得先。</param>
    /// <param name="evaluator">定義数式を評価するポリシー。<see langword="null"/> なら新しい評価器を使います。</param>
    /// <param name="batchSize">1回の進捗通知へ含める最大件数。1未満は1として扱います。</param>
    public DecodeSessionLogTimelineUseCase(
        IConnectionSessionRawLogRepository rawLogRepository,
        IOBDPIDDefinitionRepository definitionRepository,
        OBDPIDFormulaEvaluator? evaluator = null,
        int batchSize = 128)
    {
        _rawLogRepository = rawLogRepository;
        _definitionRepository = definitionRepository;
        _evaluator = evaluator ?? new OBDPIDFormulaEvaluator();
        _batchSize = Math.Max(1, batchSize);
    }

    /// <summary>指定セッションの全Raw PIDを小分けに変換して通知します。</summary>
    /// <remarks>
    /// 責務: 1件のセッションRawログを時系列順の変換済みバッチとして順次公開します。
    /// Swift の <c>Task.checkCancellation()</c> は <paramref name="cancellationToken"/> で表します。
    /// </remarks>
    /// <param name="sessionID">解析する保存済み接続セッションID。</param>
    /// <param name="prepared">Rawログ総件数の読込完了通知。</param>
    /// <param name="batchDecoded">数式変換を完了した時系列バッチの通知。</param>
    /// <param name="cancellationToken">解析の取り消しを伝えるトークン。</param>
    /// <returns>全バッチの通知完了を表すタスク。</returns>
    /// <exception cref="OperationCanceledException">取り消された場合。</exception>
    /// <exception cref="Exception">RawログまたはPID定義を読み込めない場合の永続化エラー。</exception>
    public async Task Execute(
        ConnectionSessionID sessionID,
        Action<long> prepared,
        Action<IReadOnlyList<SessionLogAnalysisState.TimelineSample>> batchDecoded,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = _definitionRepository.Definitions();
        var lookup = definitions.ToDictionary(definition => new OBDPIDRequest(definition.Service, definition.Pid));
        prepared(_rawLogRepository.EntryCount(sessionID));
        long? lastSequence = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = _rawLogRepository.Entries(sessionID, lastSequence, _batchSize);
            if (entries.Count == 0)
            {
                break;
            }
            batchDecoded(entries.Select(entry => Decode(entry, lookup)).ToList());
            lastSequence = entries[^1].Sequence;
            await Task.Yield();
        }
    }

    /// <summary>1件のRaw応答へ対応する定義式を適用します。</summary>
    /// <remarks>責務: 1件のRaw PID応答を定義有無と評価結果に対応する表示値へ変換します。</remarks>
    /// <param name="entry">変換するRaw応答。</param>
    /// <param name="definitions">Service/PIDで索引化したPID定義。</param>
    /// <returns>数式変換結果または変換不能理由を保持する表示値。</returns>
    private SessionLogAnalysisState.TimelineSample Decode(
        ConnectionSessionRawLogEntry entry,
        IReadOnlyDictionary<OBDPIDRequest, OBDPIDDefinition> definitions)
    {
        if (!definitions.TryGetValue(new OBDPIDRequest(entry.Service, entry.Pid), out var definition))
        {
            return Sample(entry, null, null, SessionLogAnalysisState.TimelineSampleDecodingFailure.MissingDefinition);
        }
        if (!definition.IsDecodable)
        {
            return Sample(entry, definition, null, SessionLogAnalysisState.TimelineSampleDecodingFailure.UnavailableFormula);
        }
        try
        {
            return Sample(entry, definition, _evaluator.Evaluate(definition, entry.Payload), null);
        }
        catch (Exception)
        {
            return Sample(entry, definition, null, SessionLogAnalysisState.TimelineSampleDecodingFailure.InvalidPayload);
        }
    }

    /// <summary>1件のRaw応答と定義を表示用サンプルへ変換します。</summary>
    /// <remarks>責務: 1件のRaw応答と任意のPID定義を時系列表示値へ固定します。</remarks>
    /// <param name="entry">変換元のRaw応答。</param>
    /// <param name="definition">対応するPID定義。未登録時は <see langword="null"/>。</param>
    /// <param name="value">数式変換後の値。</param>
    /// <param name="failure">数値化できなかった理由。</param>
    /// <returns>原データ来歴を保持する表示用PIDサンプル。</returns>
    private static SessionLogAnalysisState.TimelineSample Sample(
        ConnectionSessionRawLogEntry entry,
        OBDPIDDefinition? definition,
        double? value,
        SessionLogAnalysisState.TimelineSampleDecodingFailure? failure) =>
        new(
            sequence: entry.Sequence,
            observedAt: entry.ObservedAt,
            service: entry.Service,
            pid: entry.Pid,
            nameKey: definition?.NameKey,
            value: value,
            unit: definition?.Unit,
            vehicleModelCode: definition?.VehicleModelCode,
            payload: entry.Payload,
            decodingFailure: failure);
}
