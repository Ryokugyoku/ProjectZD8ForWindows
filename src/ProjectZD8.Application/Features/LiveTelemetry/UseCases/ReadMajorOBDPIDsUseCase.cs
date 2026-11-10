using System.Diagnostics;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.UseCases;

/// <summary>PID定義DBの現在一覧を実車応答へ適用します。</summary>
public sealed class ReadMajorOBDPIDsUseCase
{
    /// <summary>読取対象を取得するPID定義永続化境界です。</summary>
    private readonly IOBDPIDDefinitionRepository definitionRepository;

    /// <summary>実車から未加工PIDバイトを取得する境界です。</summary>
    private readonly IOBDPIDTelemetryPort telemetry;

    /// <summary>制限付き数式を適用する評価器です。</summary>
    private readonly OBDPIDFormulaEvaluator evaluator;

    /// <summary>観測完了日時を供給するクロックです。</summary>
    private readonly Func<DateTimeOffset> now;

    /// <summary>要求バッチの単調経過時間を計測するクロックです。</summary>
    private readonly Func<ulong> monotonicNanoseconds;

    /// <summary>数値化前の応答をLoggingへ通知する処理です。</summary>
    private readonly Func<OBDRawResponseObservation, Task> rawResponseDidReceive;

    /// <summary>PID定義Repository、実車読取境界、評価器を固定します。</summary>
    /// <remarks>責務: PID定義永続化境界を1回の実車数値化処理へ結び付けます。</remarks>
    /// <param name="definitionRepository">読取時点のPID定義一覧を返す永続化境界。</param>
    /// <param name="telemetry">未加工PIDバイトの取得境界。</param>
    /// <param name="evaluator">定義式の評価器。<c>null</c> なら既定の評価器。</param>
    /// <param name="now">観測完了日時の供給元。<c>null</c> なら現在時刻。</param>
    /// <param name="monotonicNanoseconds">要求バッチ経過時間を計測する単調クロック。<c>null</c> なら <see cref="Stopwatch"/>。</param>
    /// <param name="rawResponseDidReceive">数値化前の応答をLoggingへ通知する処理。<c>null</c> なら何もしない。</param>
    public ReadMajorOBDPIDsUseCase(
        IOBDPIDDefinitionRepository definitionRepository,
        IOBDPIDTelemetryPort telemetry,
        OBDPIDFormulaEvaluator? evaluator = null,
        Func<DateTimeOffset>? now = null,
        Func<ulong>? monotonicNanoseconds = null,
        Func<OBDRawResponseObservation, Task>? rawResponseDidReceive = null)
    {
        this.definitionRepository = definitionRepository;
        this.telemetry = telemetry;
        this.evaluator = evaluator ?? new OBDPIDFormulaEvaluator();
        this.now = now ?? (() => DateTimeOffset.Now);
        this.monotonicNanoseconds = monotonicNanoseconds ?? SystemMonotonicNanoseconds;
        this.rawResponseDidReceive = rawResponseDidReceive ?? (_ => Task.CompletedTask);
    }

    /// <summary>DB登録済みの全PID定義を読み込みます。</summary>
    /// <remarks>責務: 現在のPID定義永続化状態を空でない読取対象一覧へ変換します。</remarks>
    /// <returns>Service/PID順のPID定義一覧。</returns>
    /// <exception cref="OBDPIDTelemetryException">PID定義読込に失敗した場合または一覧が空の場合は <see cref="OBDPIDTelemetryError.DefinitionCatalogUnavailable"/>。</exception>
    public IReadOnlyList<OBDPIDDefinition> LoadDefinitions()
    {
        IReadOnlyList<OBDPIDDefinition> definitions;
        try
        {
            definitions = definitionRepository.Definitions();
        }
        catch (Exception)
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.DefinitionCatalogUnavailable);
        }

        var decodable = definitions.Where(definition => definition.IsDecodable).ToList();
        if (decodable.Count == 0)
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.DefinitionCatalogUnavailable);
        }

        return decodable;
    }

    /// <summary>車両設定で収集有効な対応PIDに一致する数値化可能定義を読み込みます。</summary>
    /// <remarks>責務: 車両別収集選択を安全に数値化できるPID定義一覧へ変換します。</remarks>
    /// <param name="capabilities">車両で確認済みの対応PID設定。</param>
    /// <returns>収集有効かつ数式確認済みの定義一覧。</returns>
    /// <exception cref="OBDPIDTelemetryException">PIDカタログ読込に失敗した場合または対象が空の場合のエラー。</exception>
    public IReadOnlyList<OBDPIDDefinition> LoadDefinitions(IReadOnlyList<VehiclePIDCapability> capabilities)
    {
        var enabled = capabilities
            .Where(capability => capability.IsCollectionEnabled)
            .Select(capability => capability.Id.Request)
            .ToHashSet();
        var definitions = LoadDefinitions()
            .Where(definition => enabled.Contains(new OBDPIDRequest(definition.Service, definition.Pid)))
            .ToList();
        if (definitions.Count == 0)
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.DefinitionCatalogUnavailable);
        }

        return definitions;
    }

    /// <summary>指定PID定義を1回読み取り、応答済み項目だけを数値化します。</summary>
    /// <remarks>責務: 1回のOBD接続から応答があったPIDだけの数値観測を生成します。</remarks>
    /// <param name="definitions">今回照会するPID定義。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <returns>入力定義順で並ぶ数値化済みPID観測。</returns>
    /// <exception cref="Exception">実車読取または応答済み値の数式評価に失敗した場合のエラー。</exception>
    public async Task<IReadOnlyList<OBDPIDSample>> Execute(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint)
    {
        var standardDefinitions = definitions.Where(definition => !definition.IsVehicleSpecific).ToList();
        var vehicleSpecificDefinitions = definitions.Where(definition => definition.IsVehicleSpecific).ToList();
        var requests = standardDefinitions
            .Select(definition => new OBDPIDRequest(definition.Service, definition.Pid))
            .ToList();
        var startedAt = monotonicNanoseconds();
        var responses = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        if (requests.Count != 0)
        {
            foreach (var (request, payload) in await telemetry.Read(requests, endpoint))
            {
                responses[request] = payload;
            }
        }

        if (vehicleSpecificDefinitions.Count != 0)
        {
            var vehicleResponses = await telemetry.ReadVehicleSpecific(vehicleSpecificDefinitions, endpoint);
            foreach (var (request, payload) in vehicleResponses)
            {
                responses[request] = payload;
            }
        }

        var observedAt = now();
        var elapsed = unchecked(monotonicNanoseconds() - startedAt);
        await RecordRawResponses(responses, observedAt, elapsed);
        var samples = new List<OBDPIDSample>();
        foreach (var definition in definitions)
        {
            var request = new OBDPIDRequest(definition.Service, definition.Pid);
            if (!responses.TryGetValue(request, out var bytes))
            {
                continue;
            }

            samples.Add(new OBDPIDSample(
                request: request,
                nameKey: definition.NameKey,
                value: evaluator.Evaluate(definition, bytes),
                unit: definition.Unit,
                observedAt: observedAt,
                vehicleModelCode: definition.VehicleModelCode,
                summaryKey: definition.SummaryKey,
                highValueKey: definition.HighValueKey,
                lowValueKey: definition.LowValueKey,
                correlationKey: definition.CorrelationKey));
        }

        return samples;
    }

    /// <summary>DB登録済みの全PIDを1回読み取り、定義式で数値化します。</summary>
    /// <remarks>責務: 1回のOBD接続からDB登録済みPIDの応答済み数値観測を生成します。</remarks>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <returns>DBのService/PID順で並ぶ数値化済みPID観測。</returns>
    /// <exception cref="Exception">PID定義読込、実車読取、または数式評価に失敗した場合のエラー。</exception>
    public Task<IReadOnlyList<OBDPIDSample>> Execute(OBDConnectionEndpoint endpoint)
    {
        IReadOnlyList<OBDPIDDefinition> definitions;
        try
        {
            definitions = LoadDefinitions();
        }
        catch (Exception error)
        {
            return Task.FromException<IReadOnlyList<OBDPIDSample>>(error);
        }

        return Execute(definitions, endpoint);
    }

    /// <summary>現在のPID通信セッションを終了します。</summary>
    /// <remarks>責務: 注入済みPID取得境界へ接続資源の終了を通知します。</remarks>
    /// <returns>終了通知の完了。</returns>
    public Task EndSession() => telemetry.EndSession();

    /// <summary>応答辞書を数値化前のLogging観測として通知します。</summary>
    /// <remarks>責務: 1回の要求バッチ応答を安定したService/PID順のRaw観測へ変換します。</remarks>
    /// <param name="responses">取得境界が返した未デコード応答辞書。</param>
    /// <param name="observedAt">応答群を受け取った実時間。</param>
    /// <param name="elapsedNanoseconds">要求バッチ開始から応答群受信までの単調時間。</param>
    /// <returns>通知の完了。</returns>
    /// <exception cref="Exception">Logging保存境界がRaw応答を受理できない場合のエラー。</exception>
    private async Task RecordRawResponses(
        IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>> responses,
        DateTimeOffset observedAt,
        ulong elapsedNanoseconds)
    {
        var orderedRequests = responses.Keys
            .OrderBy(request => request.Service)
            .ThenBy(request => request.Pid)
            .ToList();
        foreach (var request in orderedRequests)
        {
            await rawResponseDidReceive(new OBDRawResponseObservation(
                observedAt: observedAt,
                batchElapsedNanoseconds: elapsedNanoseconds,
                request: request,
                payload: responses[request]));
        }
    }

    /// <summary>OSの単調クロックをナノ秒で返します。</summary>
    /// <remarks>責務: Swift の <c>DispatchTime.now().uptimeNanoseconds</c> に相当する既定の単調時刻を供給します。</remarks>
    /// <returns>単調クロックのナノ秒値。</returns>
    private static ulong SystemMonotonicNanoseconds() =>
        (ulong)Stopwatch.GetElapsedTime(0).Ticks * 100UL;
}
