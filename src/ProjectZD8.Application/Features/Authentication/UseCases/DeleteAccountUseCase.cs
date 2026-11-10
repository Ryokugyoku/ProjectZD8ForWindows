using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Domain.Repositories;

namespace ProjectZD8.Application.Authentication.UseCases;

/// <summary>アカウント保存データとログイン識別子を順序付けて削除します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です（Swift 版の <c>@MainActor</c>）。</remarks>
public sealed class DeleteAccountUseCase
{
    /// <summary>他端末へ新しいセッション失効世代を発行する境界です。</summary>
    private readonly IAccountSessionRevocationPort _sessionRevocation;

    /// <summary>CloudKit上のセッション概要、Raw Asset、Mac受領証、および削除マーカーを削除する境界です。</summary>
    private readonly IConnectionSessionTransferRepository _sessionTransfers;

    /// <summary>CloudKitと端末キャッシュの車両カタログを削除する境界です。</summary>
    private readonly IAccountVehicleDataErasurePort _vehicleDataEraser;

    /// <summary>アカウントに属する同期設定と端末設定を消去する境界です。</summary>
    private readonly IAccountDataErasurePort _dataEraser;

    /// <summary>最後に削除する保存済みログイン識別子の境界です。</summary>
    private readonly IAuthenticationSessionStorePort _sessionStore;

    /// <summary>失効同期、データ消去、認証セッション保存の各境界を注入します。</summary>
    /// <remarks>責務: アカウント削除順序を失効発行、CloudKit運転データ消去、ローカル消去、資格情報削除へ固定します。</remarks>
    /// <param name="sessionRevocation">他端末へセッション失効を発行する境界。</param>
    /// <param name="sessionTransfers">CloudKit上の運転データ削除境界。</param>
    /// <param name="vehicleDataEraser">CloudKitと端末キャッシュの車両カタログ削除境界。</param>
    /// <param name="dataEraser">アカウントに属するアプリ保存データの消去境界。</param>
    /// <param name="sessionStore">保存済みAppleユーザー識別子の削除境界。</param>
    public DeleteAccountUseCase(
        IAccountSessionRevocationPort sessionRevocation,
        IConnectionSessionTransferRepository sessionTransfers,
        IAccountVehicleDataErasurePort vehicleDataEraser,
        IAccountDataErasurePort dataEraser,
        IAuthenticationSessionStorePort sessionStore)
    {
        _sessionRevocation = sessionRevocation;
        _sessionTransfers = sessionTransfers;
        _vehicleDataEraser = vehicleDataEraser;
        _dataEraser = dataEraser;
        _sessionStore = sessionStore;
    }

    /// <summary>指定アカウントを他端末へ失効通知してからCloudKit全データ、ローカルデータ、ログイン識別子を削除します。</summary>
    /// <remarks>責務: 1件の認証済みアカウントを再試行可能な順序で端末から削除します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てた空でないユーザー識別子。</param>
    /// <returns>削除の完了を表すタスク。</returns>
    /// <exception cref="AuthenticationSessionStoreException">ユーザー識別子が空の場合（<c>Unavailable</c>）。</exception>
    /// <exception cref="Exception">CloudKit、ローカル保存、またはログイン識別子の削除を完了できない場合のエラー。</exception>
    public async Task Execute(string userIdentifier)
    {
        if (userIdentifier.Length == 0)
        {
            throw new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);
        }
        _sessionRevocation.PublishRevocation(userIdentifier);
        await _sessionTransfers.DeleteAll(userIdentifier);
        await _vehicleDataEraser.DeleteAllVehicleData(userIdentifier);
        _dataEraser.EraseAllData(userIdentifier);
        _sessionStore.RemoveUserIdentifier();
    }
}
