using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.UseCases;

/// <summary>車両一括削除を安全に開始できない状態です。</summary>
/// <remarks>Swift の <c>DeleteVehicleWithSessionsUseCase.Error</c> に対応します。</remarks>
public enum DeleteVehicleWithSessionsUseCaseError
{
    /// <summary>対象車両に取得中の接続セッションがあります。</summary>
    ActiveSession,
}

/// <summary>車両一括削除を開始できないことを表す例外です。</summary>
public sealed class DeleteVehicleWithSessionsUseCaseException : Exception
{
    /// <summary>車両一括削除を安全に開始できない状態です。</summary>
    public DeleteVehicleWithSessionsUseCaseError Reason { get; }

    /// <summary>失敗理由を指定して例外を生成します。</summary>
    /// <remarks>責務: 1件の車両一括削除の失敗理由を送出可能な例外へ包みます。</remarks>
    /// <param name="reason">車両一括削除の失敗理由。</param>
    public DeleteVehicleWithSessionsUseCaseException(DeleteVehicleWithSessionsUseCaseError reason)
        : base($"{nameof(DeleteVehicleWithSessionsUseCaseError)}.{reason}")
    {
        Reason = reason;
    }
}

/// <summary>1台の登録車両とその接続セッションを全端末から削除します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class DeleteVehicleWithSessionsUseCase
{
    /// <summary>削除対象セッションを取得するローカル履歴境界です。</summary>
    private readonly IConnectionSessionRepository sessionRepository;

    /// <summary>現在端末のセッションを物理削除する境界です。</summary>
    private readonly IConnectionSessionErasureRepository localSessionErasureRepository;

    /// <summary>全端末へセッション削除を伝播するCloudKit境界です。</summary>
    private readonly IConnectionSessionTransferRepository sessionTransferRepository;

    /// <summary>車両プロフィールを削除する同期境界です。</summary>
    private readonly IVehicleRepository vehicleRepository;

    /// <summary>車両削除に必要な履歴、全端末転送、車両保存の各境界を固定します。</summary>
    /// <remarks>責務: 1件の車両一括削除処理をセッションと車両の永続化境界へ結び付けます。</remarks>
    /// <param name="sessionRepository">削除対象セッションを取得するローカル履歴境界。</param>
    /// <param name="localSessionErasureRepository">現在端末のセッション物理削除境界。</param>
    /// <param name="sessionTransferRepository">全端末へセッション削除を伝播するCloudKit境界。</param>
    /// <param name="vehicleRepository">最後に車両プロフィールを削除する同期境界。</param>
    public DeleteVehicleWithSessionsUseCase(
        IConnectionSessionRepository sessionRepository,
        IConnectionSessionErasureRepository localSessionErasureRepository,
        IConnectionSessionTransferRepository sessionTransferRepository,
        IVehicleRepository vehicleRepository)
    {
        this.sessionRepository = sessionRepository;
        this.localSessionErasureRepository = localSessionErasureRepository;
        this.sessionTransferRepository = sessionTransferRepository;
        this.vehicleRepository = vehicleRepository;
    }

    /// <summary>対象車両の全終了済みセッションを全端末削除してから車両プロフィールを削除します。</summary>
    /// <remarks>責務: 1件の車両IDを関連セッション削除済みの車両削除結果へ変換します。</remarks>
    /// <param name="vehicleID">削除する登録車両の安定識別子。</param>
    /// <param name="accountIdentifier">車両とセッションを所有するAppleアカウント識別子。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    /// <exception cref="DeleteVehicleWithSessionsUseCaseException">取得中セッションがある場合。</exception>
    /// <exception cref="Exception">履歴・CloudKit・ローカル・車両の削除に失敗した場合のエラー。</exception>
    public async Task Execute(VehicleID vehicleID, string accountIdentifier)
    {
        var sessions = sessionRepository.Sessions(accountIdentifier)
            .Where(session => session.Vehicle?.Id == vehicleID)
            .ToArray();
        if (!sessions.All(session => session.EndedAt is not null))
        {
            throw new DeleteVehicleWithSessionsUseCaseException(DeleteVehicleWithSessionsUseCaseError.ActiveSession);
        }

        foreach (var session in sessions)
        {
            await sessionTransferRepository.DeleteSession(session.Id, accountIdentifier);
            localSessionErasureRepository.DeleteSession(session.Id, accountIdentifier);
        }

        await vehicleRepository.DeleteVehicle(vehicleID, accountIdentifier);
    }
}
