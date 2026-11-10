using ProjectZD8.Application.VehicleManagement.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.VehicleManagement.UseCases;

/// <summary>車両削除が関連セッションを全端末から先に除去することを検証します。</summary>
public sealed class DeleteVehicleWithSessionsUseCaseTests
{
    /// <summary>対象車両の終了済みセッションだけをCloudKit、ローカル、車両の順で削除します。</summary>
    /// <remarks>責務: 車両一括削除が別車両を保持して必要な副作用順序を満たすことを確認します。</remarks>
    [Fact]
    public async Task ExecuteDeletesOnlyTargetVehicleSessionsBeforeVehicle()
    {
        var targetVehicleID = VehicleID.New();
        var otherVehicleID = VehicleID.New();
        var first = EndedSession(targetVehicleID);
        var second = EndedSession(targetVehicleID);
        var other = EndedSession(otherVehicleID);
        var dependencies = new VehicleDeletionDependenciesSpy([first, other, second]);
        var useCase = MakeUseCase(dependencies);

        await useCase.Execute(targetVehicleID, accountIdentifier: "account");

        Assert.Equal(
            [
                new VehicleDeletionEvent.Cloud(first.Id), new VehicleDeletionEvent.Local(first.Id),
                new VehicleDeletionEvent.Cloud(second.Id), new VehicleDeletionEvent.Local(second.Id),
                new VehicleDeletionEvent.Vehicle(targetVehicleID),
            ],
            dependencies.Events);
    }

    /// <summary>取得中セッションがある車両は一部データを消さずに削除を拒否します。</summary>
    /// <remarks>責務: 車両一括削除が取得中セッション検出時に全副作用を開始前停止することを確認します。</remarks>
    [Fact]
    public async Task ExecuteRejectsVehicleWithActiveSessionBeforeAnyDeletion()
    {
        var vehicleID = VehicleID.New();
        var active = new ConnectionSession(
            accountIdentifier: "account",
            vehicle: new ConnectionSessionVehicle(id: vehicleID, name: "BRZ", displayIdentifier: "test"));
        var dependencies = new VehicleDeletionDependenciesSpy([active]);
        var useCase = MakeUseCase(dependencies);

        var error = await Assert.ThrowsAsync<DeleteVehicleWithSessionsUseCaseException>(
            () => useCase.Execute(vehicleID, accountIdentifier: "account"));

        Assert.Equal(DeleteVehicleWithSessionsUseCaseError.ActiveSession, error.Reason);
        Assert.Empty(dependencies.Events);
    }

    /// <summary>指定車両を持つ終了済みテストセッションを生成します。</summary>
    /// <remarks>責務: 1件の車両IDを車両一括削除可能な終了済みセッションへ変換します。</remarks>
    /// <param name="vehicleID">セッションへ関連付ける車両ID。</param>
    /// <returns>ユーザー切断で終了したテストセッション。</returns>
    private static ConnectionSession EndedSession(VehicleID vehicleID) =>
        new ConnectionSession(
            accountIdentifier: "account",
            vehicle: new ConnectionSessionVehicle(id: vehicleID, name: "BRZ", displayIdentifier: "test")) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            EndReason = ConnectionSessionEndReason.UserDisconnected,
        };

    /// <summary>共通Spyを全永続化境界へ注入したテスト対象を生成します。</summary>
    /// <remarks>責務: 1件の副作用Spyを車両一括削除ユースケースの全境界へ結び付けます。</remarks>
    /// <param name="dependencies">セッションと車両の副作用を記録する共通Spy。</param>
    /// <returns>削除順序を観測できる車両一括削除ユースケース。</returns>
    private static DeleteVehicleWithSessionsUseCase MakeUseCase(VehicleDeletionDependenciesSpy dependencies) =>
        new(
            sessionRepository: dependencies,
            localSessionErasureRepository: dependencies,
            sessionTransferRepository: dependencies,
            vehicleRepository: dependencies);

    /// <summary>車両一括削除テストで観測する副作用です。</summary>
    private abstract record VehicleDeletionEvent
    {
        /// <summary>CloudKitへ公開したセッション削除です。</summary>
        /// <param name="SessionID">削除したセッションID。</param>
        public sealed record Cloud(ConnectionSessionID SessionID) : VehicleDeletionEvent;

        /// <summary>現在端末で物理削除したセッションです。</summary>
        /// <param name="SessionID">削除したセッションID。</param>
        public sealed record Local(ConnectionSessionID SessionID) : VehicleDeletionEvent;

        /// <summary>最後に削除した車両プロフィールです。</summary>
        /// <param name="VehicleID">削除した車両ID。</param>
        public sealed record Vehicle(VehicleID VehicleID) : VehicleDeletionEvent;
    }

    /// <summary>車両一括削除に必要な全永続化境界を記録可能に置き換えます。</summary>
    private sealed class VehicleDeletionDependenciesSpy :
        IConnectionSessionRepository,
        IConnectionSessionErasureRepository,
        IConnectionSessionTransferRepository,
        IVehicleRepository
    {
        /// <summary>履歴取得時に返す接続セッションです。</summary>
        private readonly IReadOnlyList<ConnectionSession> storedSessions;

        /// <summary>発生順に記録した削除副作用の保存先です。</summary>
        private readonly List<VehicleDeletionEvent> events = [];

        /// <summary>取得対象セッションを固定してSpyを生成します。</summary>
        /// <remarks>責務: セッション取得結果を車両削除副作用の記録先へ結び付けます。</remarks>
        /// <param name="sessions">履歴取得時に返す接続セッション。</param>
        public VehicleDeletionDependenciesSpy(IReadOnlyList<ConnectionSession> sessions)
        {
            storedSessions = sessions;
        }

        /// <summary>発生順に記録した削除副作用です。</summary>
        public IReadOnlyList<VehicleDeletionEvent> Events => events;

        /// <summary>テスト対象外のセッション保存を副作用なしで受理します。</summary>
        /// <remarks>責務: 1件のセッション保存要求をテスト用の無操作として完了します。</remarks>
        /// <param name="session">使用しない接続セッション。</param>
        public void Save(ConnectionSession session)
        {
        }

        /// <summary>固定済み接続セッションを返します。</summary>
        /// <remarks>責務: 1件のアカウント履歴要求へ注入済みセッション一覧を返します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>初期化時に固定した接続セッション一覧。</returns>
        public IReadOnlyList<ConnectionSession> Sessions(string accountIdentifier) => storedSessions;

        /// <summary>現在端末のセッション削除を記録します。</summary>
        /// <remarks>責務: 1件のローカル削除要求を発生順イベントへ変換します。</remarks>
        /// <param name="sessionID">削除対象の接続セッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        void IConnectionSessionErasureRepository.DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
        {
            events.Add(new VehicleDeletionEvent.Local(sessionID));
        }

        /// <summary>テスト対象外の転送要求へ固定Digestを返します。</summary>
        /// <remarks>責務: 1件の転送要求を外部通信なしで完了します。</remarks>
        /// <param name="package">使用しない転送Payload。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>固定Digest。</returns>
        public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) => Task.FromResult("digest");

        /// <summary>テスト対象外の転送取得へ空配列を返します。</summary>
        /// <remarks>責務: 1件の転送取得要求を空結果として完了します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空の検証済み転送一覧。</returns>
        public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>([]);

        /// <summary>テスト対象外の受領証公開を副作用なしで受理します。</summary>
        /// <remarks>責務: 1件の受領証公開要求を外部通信なしで完了します。</remarks>
        /// <param name="receipt">使用しない受領証。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
            Task.CompletedTask;

        /// <summary>テスト対象外の受領証取得へ空配列を返します。</summary>
        /// <remarks>責務: 1件の受領証取得要求を空結果として完了します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空の受領証一覧。</returns>
        public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(
            string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([]);

        /// <summary>テスト対象外の削除マーカー取得へ空集合を返します。</summary>
        /// <remarks>責務: 1件の削除マーカー取得要求を空結果として完了します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空のセッションID集合。</returns>
        public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
            Task.FromResult<IReadOnlySet<ConnectionSessionID>>(new HashSet<ConnectionSessionID>());

        /// <summary>CloudKitセッション削除を記録します。</summary>
        /// <remarks>責務: 1件の全端末削除要求を発生順イベントへ変換します。</remarks>
        /// <param name="sessionID">削除対象の接続セッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        Task IConnectionSessionTransferRepository.DeleteSession(ConnectionSessionID sessionID, string accountIdentifier)
        {
            events.Add(new VehicleDeletionEvent.Cloud(sessionID));
            return Task.CompletedTask;
        }

        /// <summary>テスト対象外のアカウント全削除を副作用なしで受理します。</summary>
        /// <remarks>責務: 1件のアカウント全削除要求を外部通信なしで完了します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteAll(string accountIdentifier) => Task.CompletedTask;

        /// <summary>テスト対象外の車両一覧取得へ空配列を返します。</summary>
        /// <remarks>責務: 1件の車両一覧要求を空結果として完了します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空の車両一覧。</returns>
        public Task<IReadOnlyList<VehicleProfile>> LoadVehicles(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<VehicleProfile>>([]);

        /// <summary>テスト対象外の車両保存を副作用なしで受理します。</summary>
        /// <remarks>責務: 1件の車両保存要求を外部通信なしで完了します。</remarks>
        /// <param name="vehicle">使用しない車両プロフィール。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task SaveVehicle(VehicleProfile vehicle, string accountIdentifier) => Task.CompletedTask;

        /// <summary>車両プロフィール削除を記録します。</summary>
        /// <remarks>責務: 1件の車両削除要求を発生順イベントへ変換します。</remarks>
        /// <param name="id">削除対象の車両ID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteVehicle(VehicleID id, string accountIdentifier)
        {
            events.Add(new VehicleDeletionEvent.Vehicle(id));
            return Task.CompletedTask;
        }
    }
}
