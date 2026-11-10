using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Authentication.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Authentication.UseCases;

/// <summary>Apple認証セッションの復元と保存ユースケースを検証します。</summary>
public sealed class AuthenticationUseCaseTests
{
    /// <summary>未保存の場合にApple資格照会を行わずログアウト状態になることを検証します。</summary>
    /// <remarks>責務: 空のセッション保存領域が未認証結果へ変換されることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task RestoreWithoutStoredIdentifierReturnsSignedOut()
    {
        var authorization = new AuthenticationAuthorizationPortFake();
        var store = new AuthenticationSessionStorePortFake();
        var useCase = new RestoreAuthenticationSessionUseCase(
            authorizationPort: authorization,
            sessionStore: store);

        var session = await useCase.Execute();

        Assert.Null(session);
        Assert.Equal(0, authorization.CredentialStateRequestCount);
    }

    /// <summary>Appleが許可中と返した保存済みセッションを復元できることを検証します。</summary>
    /// <remarks>責務: 保存済み識別子のauthorized資格状態が認証済み結果になることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task RestoreAuthorizedIdentifierReturnsSignedIn()
    {
        var authorization = new AuthenticationAuthorizationPortFake { CredentialStateResult = AppleAccountCredentialState.Authorized };
        var store = new AuthenticationSessionStorePortFake("apple-user");
        var useCase = new RestoreAuthenticationSessionUseCase(
            authorizationPort: authorization,
            sessionStore: store);

        var session = await useCase.Execute();

        Assert.Equal("apple-user", session?.UserIdentifier);
        Assert.Equal("apple-user", authorization.RequestedUserIdentifier);
        Assert.False(store.DidRemoveIdentifier);
    }

    /// <summary>取消済みの保存識別子を削除してログアウト状態にすることを検証します。</summary>
    /// <remarks>責務: revoked資格状態が安全な保存領域の無効セッション除去を要求することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task RestoreRevokedIdentifierRemovesStoredSession()
    {
        var authorization = new AuthenticationAuthorizationPortFake { CredentialStateResult = AppleAccountCredentialState.Revoked };
        var store = new AuthenticationSessionStorePortFake("revoked-user");
        var useCase = new RestoreAuthenticationSessionUseCase(
            authorizationPort: authorization,
            sessionStore: store);

        var session = await useCase.Execute();

        Assert.Null(session);
        Assert.True(store.DidRemoveIdentifier);
        Assert.Null(store.Identifier);
    }

    /// <summary>Apple認証成功時にユーザー識別子を安全な保存境界へ渡すことを検証します。</summary>
    /// <remarks>責務: Apple認証結果が次回起動用セッションとして保存されることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task SignInStoresAuthorizedUserIdentifier()
    {
        var authorization = new AuthenticationAuthorizationPortFake { AuthorizationSession = new AppleAccountSession("signed-user") };
        var store = new AuthenticationSessionStorePortFake();
        var useCase = new SignInWithAppleUseCase(
            authorizationPort: authorization,
            sessionStore: store);

        var session = await useCase.Execute();

        Assert.Equal("signed-user", session.UserIdentifier);
        Assert.Equal("signed-user", store.Identifier);
    }

    /// <summary>空のAppleユーザー識別子を認証済みとして保存しないことを検証します。</summary>
    /// <remarks>責務: 不完全なApple認証結果がセッション確定前に拒否されることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task SignInRejectsEmptyUserIdentifier()
    {
        var authorization = new AuthenticationAuthorizationPortFake { AuthorizationSession = new AppleAccountSession("") };
        var store = new AuthenticationSessionStorePortFake();
        var useCase = new SignInWithAppleUseCase(
            authorizationPort: authorization,
            sessionStore: store);

        var error = await Assert.ThrowsAsync<AppleAccountAuthorizationException>(() => useCase.Execute());

        Assert.Equal(AppleAccountAuthorizationError.Failed, error.Reason);
        Assert.Null(store.Identifier);
    }

    /// <summary>アカウント削除が他端末失効をローカル消去より先に発行することを検証します。</summary>
    /// <remarks>責務: 削除処理の副作用順序が失効発行、データ消去、資格情報削除であることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task DeleteAccountPublishesRevocationBeforeLocalErasure()
    {
        var recorder = new AuthenticationDeletionOrderRecorder();
        var useCase = new DeleteAccountUseCase(
            sessionRevocation: new AuthenticationDeletionRevocationPortFake(recorder),
            sessionTransfers: new AuthenticationDeletionTransferRepositoryFake(recorder),
            vehicleDataEraser: new AuthenticationDeletionVehicleDataEraserFake(recorder),
            dataEraser: new AuthenticationDeletionDataEraserFake(recorder),
            sessionStore: new AuthenticationDeletionSessionStoreFake(recorder));

        await useCase.Execute("delete-user");

        Assert.Equal(
            new[]
            {
                "publish:delete-user",
                "delete-cloud-driving-data:delete-user",
                "delete-vehicle-data:delete-user",
                "erase:delete-user",
                "remove-session",
            },
            recorder.Events);
    }

    /// <summary>CloudKit運転データを削除できない場合はローカルデータと資格情報を保持します。</summary>
    /// <remarks>責務: CloudKit全削除失敗が端末内削除を開始せずアカウント削除を再試行可能に保つことを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task DeleteAccountRetainsLocalDataWhenCloudDeletionFails()
    {
        var recorder = new AuthenticationDeletionOrderRecorder();
        var useCase = new DeleteAccountUseCase(
            sessionRevocation: new AuthenticationDeletionRevocationPortFake(recorder),
            sessionTransfers: new AuthenticationDeletionTransferRepositoryFake(
                recorder,
                deleteAllError: new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable)),
            vehicleDataEraser: new AuthenticationDeletionVehicleDataEraserFake(recorder),
            dataEraser: new AuthenticationDeletionDataEraserFake(recorder),
            sessionStore: new AuthenticationDeletionSessionStoreFake(recorder));

        await Assert.ThrowsAnyAsync<Exception>(() => useCase.Execute("delete-user"));

        Assert.Equal(
            new[]
            {
                "publish:delete-user",
                "delete-cloud-driving-data:delete-user",
            },
            recorder.Events);
    }

    /// <summary>認証ユースケーステストでApple認証結果を制御します。</summary>
    private sealed class AuthenticationAuthorizationPortFake : IAppleAccountAuthorizationPort
    {
        /// <summary><see cref="Authorize"/> が返すセッションです。</summary>
        public AppleAccountSession AuthorizationSession { get; set; } = new("user");

        /// <summary>資格状態照会が返す状態です。</summary>
        public AppleAccountCredentialState CredentialStateResult { get; set; } = AppleAccountCredentialState.NotFound;

        /// <summary>資格状態を照会されたユーザー識別子です。</summary>
        public string? RequestedUserIdentifier { get; private set; }

        /// <summary>資格状態照会の実行回数です。</summary>
        public int CredentialStateRequestCount { get; private set; }

        /// <summary>設定されたAppleセッションを返します。</summary>
        /// <remarks>責務: テスト用Apple認証要求へ決定的なセッションを返します。</remarks>
        /// <returns>テストが設定したAppleアカウントセッション。</returns>
        public Task<AppleAccountSession> Authorize() => Task.FromResult(AuthorizationSession);

        /// <summary>設定されたApple資格状態を返します。</summary>
        /// <remarks>責務: 資格照会入力を記録して決定的な資格状態を返します。</remarks>
        /// <param name="userIdentifier">ユースケースが照会したAppleユーザー識別子。</param>
        /// <returns>テストが設定したApple資格状態。</returns>
        public Task<AppleAccountCredentialState> CredentialState(string userIdentifier)
        {
            RequestedUserIdentifier = userIdentifier;
            CredentialStateRequestCount += 1;
            return Task.FromResult(CredentialStateResult);
        }
    }

    /// <summary>認証ユースケーステストで安全なセッション保存を記録します。</summary>
    private sealed class AuthenticationSessionStorePortFake : IAuthenticationSessionStorePort
    {
        /// <summary>現在保存されているテスト用Appleユーザー識別子です。</summary>
        public string? Identifier { get; set; }

        /// <summary>セッション削除が要求されたかどうかです。</summary>
        public bool DidRemoveIdentifier { get; private set; }

        /// <summary>初期保存識別子を指定してFakeを生成します。</summary>
        /// <remarks>責務: テスト用セッション保存境界を指定済み初期値へ設定します。</remarks>
        /// <param name="identifier">初期状態で保存済みとするAppleユーザー識別子。</param>
        public AuthenticationSessionStorePortFake(string? identifier = null) => Identifier = identifier;

        /// <summary>現在のテスト用識別子を返します。</summary>
        /// <remarks>責務: テスト用保存領域から現在の認証セッション識別子を返します。</remarks>
        /// <returns>現在保存されているテスト用識別子。</returns>
        public string? LoadUserIdentifier() => Identifier;

        /// <summary>テスト用識別子を置き換えます。</summary>
        /// <remarks>責務: ユースケースが保存した認証セッション識別子を記録します。</remarks>
        /// <param name="userIdentifier">保存を要求されたAppleユーザー識別子。</param>
        public void SaveUserIdentifier(string userIdentifier) => Identifier = userIdentifier;

        /// <summary>テスト用識別子を削除します。</summary>
        /// <remarks>責務: セッション削除要求を記録して保存済み識別子を除去します。</remarks>
        public void RemoveUserIdentifier()
        {
            DidRemoveIdentifier = true;
            Identifier = null;
        }
    }

    /// <summary>アカウント削除ユースケースの副作用順序を記録します。</summary>
    private sealed class AuthenticationDeletionOrderRecorder
    {
        /// <summary>発生順に保存したテスト用副作用名です。</summary>
        public List<string> Events { get; } = [];
    }

    /// <summary>アカウント削除テストで失効発行を記録します。</summary>
    /// <param name="recorder">副作用名を発生順に保持する記録先。</param>
    private sealed class AuthenticationDeletionRevocationPortFake(AuthenticationDeletionOrderRecorder recorder) : IAccountSessionRevocationPort
    {
        /// <summary>このテストでは使用しないセッション基準登録を無視します。</summary>
        /// <remarks>責務: 削除順序テストに不要な基準登録境界を副作用なしで満たします。</remarks>
        /// <param name="userIdentifier">使用しないユーザー識別子。</param>
        public void RegisterCurrentSession(string userIdentifier) { }

        /// <summary>失効発行を共有履歴へ追加します。</summary>
        /// <remarks>責務: 1件の失効発行を順序検証可能なイベントへ変換します。</remarks>
        /// <param name="userIdentifier">失効対象のユーザー識別子。</param>
        public void PublishRevocation(string userIdentifier) => recorder.Events.Add($"publish:{userIdentifier}");

        /// <summary>このテストでは使用しない失効監視を無視します。</summary>
        /// <remarks>責務: 削除順序テストに不要な監視境界を副作用なしで満たします。</remarks>
        /// <param name="userIdentifier">使用しないユーザー識別子。</param>
        /// <param name="receive">使用しない失効受信処理。</param>
        public void StartObserving(string userIdentifier, Action receive) { }

        /// <summary>このテストでは使用しない監視終了を無視します。</summary>
        /// <remarks>責務: 削除順序テストに不要な監視終了境界を副作用なしで満たします。</remarks>
        public void StopObserving() { }
    }

    /// <summary>アカウント削除テストでデータ消去を記録します。</summary>
    /// <param name="recorder">副作用名を発生順に保持する記録先。</param>
    private sealed class AuthenticationDeletionDataEraserFake(AuthenticationDeletionOrderRecorder recorder) : IAccountDataErasurePort
    {
        /// <summary>データ消去を共有履歴へ追加します。</summary>
        /// <remarks>責務: 1件のデータ消去を順序検証可能なイベントへ変換します。</remarks>
        /// <param name="userIdentifier">消去対象のユーザー識別子。</param>
        public void EraseAllData(string userIdentifier) => recorder.Events.Add($"erase:{userIdentifier}");
    }

    /// <summary>アカウント削除テストでCloudKit運転データ削除を記録します。</summary>
    /// <param name="recorder">副作用名を発生順に保持する記録先。</param>
    /// <param name="deleteAllError">CloudKit全削除要求を失敗させる場合のエラー。</param>
    private sealed class AuthenticationDeletionTransferRepositoryFake(
        AuthenticationDeletionOrderRecorder recorder,
        Exception? deleteAllError = null) : IConnectionSessionTransferRepository
    {
        /// <summary>このテストでは転送送信を使用しません。</summary>
        /// <remarks>責務: テスト対象外の送信要求を固定Digestで満たします。</remarks>
        /// <param name="package">使用しない転送Payload。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>固定Digest。</returns>
        public Task<string> Upload(ConnectionSessionTransferPackage package, string accountIdentifier) => Task.FromResult("digest");

        /// <summary>このテストでは転送を返しません。</summary>
        /// <remarks>責務: テスト対象外の転送取得要求へ空配列を返します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空配列。</returns>
        public Task<IReadOnlyList<VerifiedConnectionSessionTransfer>> DownloadTransfers(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<VerifiedConnectionSessionTransfer>>([]);

        /// <summary>このテストでは受領証公開を使用しません。</summary>
        /// <remarks>責務: テスト対象外の受領証公開要求を副作用なしで満たします。</remarks>
        /// <param name="receipt">使用しないMac受領証。</param>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task PublishMacReceipt(ConnectionSessionMacImportReceipt receipt, ConnectionSessionID sessionID, string accountIdentifier) =>
            Task.CompletedTask;

        /// <summary>このテストでは受領証を返しません。</summary>
        /// <remarks>責務: テスト対象外の受領証取得要求へ空配列を返します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空配列。</returns>
        public Task<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>> DownloadMacReceipts(string accountIdentifier) =>
            Task.FromResult<IReadOnlyList<(ConnectionSessionID SessionID, ConnectionSessionMacImportReceipt Receipt)>>([]);

        /// <summary>このテストでは削除マーカーを返しません。</summary>
        /// <remarks>責務: テスト対象外の削除マーカー取得へ空集合を返します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空集合。</returns>
        public Task<IReadOnlySet<ConnectionSessionID>> DeletedSessionIDs(string accountIdentifier) =>
            Task.FromResult<IReadOnlySet<ConnectionSessionID>>(new HashSet<ConnectionSessionID>());

        /// <summary>このテストではセッション単位削除を使用しません。</summary>
        /// <remarks>責務: テスト対象外のセッション削除要求を副作用なしで満たします。</remarks>
        /// <param name="sessionID">使用しないセッションID。</param>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteSession(ConnectionSessionID sessionID, string accountIdentifier) => Task.CompletedTask;

        /// <summary>CloudKit運転データ削除を共有履歴へ追加します。</summary>
        /// <remarks>責務: 1件のCloudKit全削除を順序検証可能なイベントへ変換します。</remarks>
        /// <param name="accountIdentifier">削除対象アカウント識別子。</param>
        /// <returns>完了済みタスク、またはテスト指定エラーで失敗したタスク。</returns>
        public Task DeleteAll(string accountIdentifier)
        {
            recorder.Events.Add($"delete-cloud-driving-data:{accountIdentifier}");
            return deleteAllError is null ? Task.CompletedTask : Task.FromException(deleteAllError);
        }
    }

    /// <summary>アカウント削除テストで車両カタログ削除を記録します。</summary>
    /// <param name="recorder">副作用名を発生順に保持する記録先。</param>
    private sealed class AuthenticationDeletionVehicleDataEraserFake(AuthenticationDeletionOrderRecorder recorder) : IAccountVehicleDataErasurePort
    {
        /// <summary>全保存先の車両カタログ削除を共有履歴へ追加します。</summary>
        /// <remarks>責務: 1件の車両データ全削除を順序検証可能なイベントへ変換します。</remarks>
        /// <param name="accountIdentifier">削除対象アカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteAllVehicleData(string accountIdentifier)
        {
            recorder.Events.Add($"delete-vehicle-data:{accountIdentifier}");
            return Task.CompletedTask;
        }

        /// <summary>このテストでは端末キャッシュに車両を保持しません。</summary>
        /// <remarks>責務: テスト対象外のローカル車両ID照会へ空配列を返します。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        /// <returns>空配列。</returns>
        public IReadOnlyList<VehicleID> LocalVehicleIDs(string accountIdentifier) => [];

        /// <summary>このテストでは端末キャッシュ単独削除を使用しません。</summary>
        /// <remarks>責務: テスト対象外の車両キャッシュ削除要求を副作用なしで満たします。</remarks>
        /// <param name="accountIdentifier">使用しないアカウント識別子。</param>
        public void RemoveLocalVehicleCache(string accountIdentifier) { }
    }

    /// <summary>アカウント削除テストで資格情報削除を記録します。</summary>
    /// <param name="recorder">副作用名を発生順に保持する記録先。</param>
    private sealed class AuthenticationDeletionSessionStoreFake(AuthenticationDeletionOrderRecorder recorder) : IAuthenticationSessionStorePort
    {
        /// <summary>このテストでは使用しない保存済み識別子を返します。</summary>
        /// <remarks>責務: 削除順序テストに不要な読込境界を未保存結果として満たします。</remarks>
        /// <returns>常に <see langword="null"/>。</returns>
        public string? LoadUserIdentifier() => null;

        /// <summary>このテストでは使用しない識別子保存を無視します。</summary>
        /// <remarks>責務: 削除順序テストに不要な保存境界を副作用なしで満たします。</remarks>
        /// <param name="userIdentifier">使用しないユーザー識別子。</param>
        public void SaveUserIdentifier(string userIdentifier) { }

        /// <summary>資格情報削除を共有履歴へ追加します。</summary>
        /// <remarks>責務: 1件の資格情報削除を順序検証可能なイベントへ変換します。</remarks>
        public void RemoveUserIdentifier() => recorder.Events.Add("remove-session");
    }
}
