using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence.Sqlite.Repositories;

/// <summary>PID定義DBを準備できない場合に明示的な失敗を返します。</summary>
/// <remarks>iOS版 <c>UnavailableOBDPIDDefinitionRepository</c> の移植です（iOS版の非公開エラー型は <see cref="InvalidOperationException"/> にしました）。</remarks>
public sealed class UnavailableOBDPIDDefinitionRepository : IOBDPIDDefinitionRepository
{
    /// <summary>定義一覧の読込を失敗させます。</summary>
    /// <remarks>責務: 照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public IReadOnlyList<OBDPIDDefinition> Definitions() => throw Unavailable();

    /// <summary>定義の保存を失敗させます。</summary>
    /// <remarks>責務: 保存要求を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="definition">保存できない定義。</param>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public void Upsert(OBDPIDDefinition definition) => throw Unavailable();

    /// <summary>定義の読込を失敗させます。</summary>
    /// <remarks>責務: 照会を明示的な保存先利用不能へ変換します。</remarks>
    /// <param name="service">読み込めないService。</param>
    /// <param name="pid">読み込めないPID。</param>
    /// <returns>この実装は値を返しません。</returns>
    /// <exception cref="InvalidOperationException">常に投げます。</exception>
    public OBDPIDDefinition? Definition(byte service, byte pid) => throw Unavailable();

    /// <summary>利用不能エラーを生成します。</summary>
    /// <remarks>責務: 共通のエラー値を作ります。</remarks>
    /// <returns>例外。</returns>
    private static InvalidOperationException Unavailable() => new("PID definition storage is unavailable");
}
