using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.LiveTelemetry.UseCases;

/// <summary>PIDテーブルの定義を使って未加工応答バイトを意味のある数値へ変換します。</summary>
public sealed class DecodeOBDPIDValueUseCase
{
    /// <summary>定義を取得する永続化境界です。</summary>
    private readonly IOBDPIDDefinitionRepository repository;

    /// <summary>制限付き数式の評価器です。</summary>
    private readonly OBDPIDFormulaEvaluator evaluator;

    /// <summary>PID定義リポジトリと式評価器を注入して生成します。</summary>
    /// <remarks>責務: PID定義取得と数値評価の交換可能な境界を固定します。</remarks>
    /// <param name="repository">Service/PID定義の取得先。</param>
    /// <param name="evaluator">保存式を応答バイトへ適用する評価器。<c>null</c> なら既定の評価器。</param>
    public DecodeOBDPIDValueUseCase(IOBDPIDDefinitionRepository repository, OBDPIDFormulaEvaluator? evaluator = null)
    {
        this.repository = repository;
        this.evaluator = evaluator ?? new OBDPIDFormulaEvaluator();
    }

    /// <summary>登録済みPID式を未加工応答バイトへ適用します。</summary>
    /// <remarks>責務: 1件のService/PID応答を登録定義に基づく数値と単位へ変換します。</remarks>
    /// <param name="service">OBD Service番号。</param>
    /// <param name="pid">Service内PID番号。</param>
    /// <param name="bytes">ServiceとPIDを除いた未加工応答バイト。</param>
    /// <returns>計算済み数値と定義単位。定義がなければ <c>null</c>。</returns>
    /// <exception cref="Exception">定義取得または数式評価に失敗した場合のエラー。</exception>
    public (double Value, string Unit)? Execute(byte service, byte pid, IReadOnlyList<byte> bytes)
    {
        var definition = repository.Definition(service, pid);
        if (definition is null)
        {
            return null;
        }

        return (evaluator.Evaluate(definition, bytes), definition.Unit);
    }
}
