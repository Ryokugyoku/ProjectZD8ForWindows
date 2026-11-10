using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Domain.Repositories;

/// <summary>PID変換定義を永続化してService/PID単位で取得する能力です。</summary>
public interface IOBDPIDDefinitionRepository
{
    /// <summary>登録済みの現在PID定義を識別子順で取得します。</summary>
    /// <remarks>責務: 永続化されているPID定義一覧をService/PIDの昇順で返します。</remarks>
    /// <returns>登録済みPID定義の一覧。</returns>
    /// <exception cref="Exception">永続読込に失敗した場合のリポジトリエラー。</exception>
    IReadOnlyList<OBDPIDDefinition> Definitions();

    /// <summary>定義の改訂番号が新しい場合だけ登録または更新します。</summary>
    /// <remarks>責務: 1件のPID定義をService/PID複合識別子へ非破壊で保存します。</remarks>
    /// <param name="definition">保存する検証済みPID定義。</param>
    /// <exception cref="Exception">スキーマ制約または永続化に失敗した場合のリポジトリエラー。</exception>
    void Upsert(OBDPIDDefinition definition);

    /// <summary>指定Service/PIDの現在定義を取得します。</summary>
    /// <remarks>責務: 1件のService/PID複合識別子に対応する現在定義を返します。</remarks>
    /// <param name="service">OBD Service番号。</param>
    /// <param name="pid">Service内PID番号。</param>
    /// <returns>登録済み定義。未登録の場合は <see langword="null"/>。</returns>
    /// <exception cref="Exception">永続読込に失敗した場合のリポジトリエラー。</exception>
    OBDPIDDefinition? Definition(byte service, byte pid);
}