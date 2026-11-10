using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Data.Persistence;

/// <summary>
/// 接続セッションの保存一式を、組み立てが 1 つの保存先として渡すための型です。
/// </summary>
/// <remarks>
/// iOS 版の組み立ては Swift のプロトコル合成（<c>any A &amp; B &amp; …</c>）で表しています。C# には同じ書き方がないので、
/// 合成をこのインターフェースにまとめ、SQLite 実装と使えないときの実装の両方が実装します。メンバーは足しません。
/// </remarks>
public interface IConnectionSessionStorage :
    IConnectionSessionRepository,
    IConnectionSessionRawLogRepository,
    IConnectionSessionErasureRepository,
    IAccountConnectionSessionErasureRepository,
    IConnectionSessionAcquisitionRepository,
    IConnectionSessionAcquisitionBatchRepository,
    IConnectionSessionAcquisitionTerminationRepository
{
}
