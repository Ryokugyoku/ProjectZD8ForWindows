using ProjectZD8.Application.Authentication.Actions;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.State;
using ProjectZD8.Application.Authentication.UseCases;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Repositories;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.Authentication.State;

/// <summary>認証入口モデルの明示同意と非同期状態遷移を検証します。</summary>
public sealed class AuthenticationSessionModelTests
{
    /// <summary>初期表示が保存済みセッションを確認してログアウト状態へ遷移することを検証します。</summary>
    /// <remarks>責務: appeared操作がセッション復元ユースケースを一度だけ実行することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task AppearedRestoresMissingSessionAsSignedOut()
    {
        var dependencies = MakeDependencies();
        var model = dependencies.Model;

        model.Send(AuthenticationAction.Appeared);
        await WaitForTasks();

        Assert.Equal(AuthenticationPhase.SignedOut, model.State.Phase);
        Assert.Null(model.State.Session);
        Assert.Equal(0, dependencies.Authorization.CredentialStateRequestCount);
    }

    /// <summary>ログイン操作だけではApple認証を開始せず免責事項を提示することを検証します。</summary>
    /// <remarks>責務: loginTapped操作がApple認証前の明示同意状態だけを開くことを確認します。</remarks>
    [Fact]
    public void LoginTapPresentsDisclaimerBeforeAuthorization()
    {
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedOut));

        dependencies.Model.Send(AuthenticationAction.LoginTapped);

        Assert.True(dependencies.Model.State.IsDisclaimerPresented);
        Assert.Equal(0, dependencies.Authorization.AuthorizationRequestCount);
    }

    /// <summary>免責事項が未表示の同意操作でApple認証を開始しないことを検証します。</summary>
    /// <remarks>責務: 明示同意画面を経ていない認証要求がApplication状態で拒否されることを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task AcceptanceWithoutPresentedDisclaimerDoesNotAuthorize()
    {
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedOut));

        dependencies.Model.Send(AuthenticationAction.DisclaimerAccepted);
        await WaitForTasks();

        Assert.Equal(0, dependencies.Authorization.AuthorizationRequestCount);
        Assert.Equal(AuthenticationPhase.SignedOut, dependencies.Model.State.Phase);
    }

    /// <summary>免責同意後のApple認証成功が認証済み状態になることを検証します。</summary>
    /// <remarks>責務: 明示同意済みApple認証結果がルートアクセス許可へ遷移することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task AcceptedDisclaimerCompletesAppleSignIn()
    {
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedOut));
        dependencies.Authorization.AuthorizationSession = new AppleAccountSession("accepted-user");

        dependencies.Model.Send(AuthenticationAction.LoginTapped);
        dependencies.Model.Send(AuthenticationAction.DisclaimerAccepted);
        await WaitForTasks();

        Assert.Equal(AuthenticationPhase.SignedIn, dependencies.Model.State.Phase);
        Assert.Equal("accepted-user", dependencies.Model.State.Session?.UserIdentifier);
        Assert.Equal("accepted-user", dependencies.Store.Identifier);
        Assert.Equal(new[] { "accepted-user" }, dependencies.Revocation.RegisteredIdentifiers);
        Assert.Equal("accepted-user", dependencies.Revocation.ObservedIdentifier);
    }

    /// <summary>Apple認証キャンセルが失敗表示なしでログイン待機へ戻ることを検証します。</summary>
    /// <remarks>責務: ユーザーキャンセルを再試行可能な通常のログアウト状態へ変換します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task CancelledAppleSignInReturnsToSignedOutWithoutFailure()
    {
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedOut));
        dependencies.Authorization.AuthorizationError = new AppleAccountAuthorizationException(AppleAccountAuthorizationError.Cancelled);

        dependencies.Model.Send(AuthenticationAction.LoginTapped);
        dependencies.Model.Send(AuthenticationAction.DisclaimerAccepted);
        await WaitForTasks();

        Assert.Equal(AuthenticationPhase.SignedOut, dependencies.Model.State.Phase);
        Assert.Null(dependencies.Model.State.Failure);
    }

    /// <summary>アカウント削除が警告と削除事項の確認を経るまで実行されないことを検証します。</summary>
    /// <remarks>責務: 2段階確認より前の操作が保存データ消去を開始しないことを確認します。</remarks>
    [Fact]
    public void AccountDeletionRequiresWarningAndReviewBeforeErasure()
    {
        var session = new AppleAccountSession("delete-user");
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedIn, session: session));

        dependencies.Model.Send(AuthenticationAction.AccountDeletionRequested);
        Assert.Equal(AccountDeletionPhase.Warning, dependencies.Model.State.AccountDeletionPhase);
        Assert.Empty(dependencies.DataEraser.ErasedIdentifiers);

        dependencies.Model.Send(AuthenticationAction.AccountDeletionWarningConfirmed);
        Assert.Equal(AccountDeletionPhase.Reviewing, dependencies.Model.State.AccountDeletionPhase);
        Assert.Empty(dependencies.DataEraser.ErasedIdentifiers);
    }

    /// <summary>最終確認後に全データとログイン識別子を削除してログアウトすることを検証します。</summary>
    /// <remarks>責務: 最終削除操作がデータ消去とログイン画面への状態遷移を完了することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task ConfirmedAccountDeletionErasesDataAndSignsOut()
    {
        var session = new AppleAccountSession("delete-user");
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedIn, session: session));
        dependencies.Store.Identifier = session.UserIdentifier;

        dependencies.Model.Send(AuthenticationAction.AccountDeletionRequested);
        dependencies.Model.Send(AuthenticationAction.AccountDeletionWarningConfirmed);
        dependencies.Model.Send(AuthenticationAction.AccountDeletionConfirmed);
        await WaitForTasks();

        Assert.Equal(new[] { "delete-user" }, dependencies.DataEraser.ErasedIdentifiers);
        Assert.Equal(new[] { "delete-user" }, dependencies.Transfers.DeletedAccountIdentifiers);
        Assert.Equal(new[] { "delete-user" }, dependencies.Revocation.PublishedIdentifiers);
        Assert.Null(dependencies.Store.Identifier);
        Assert.Null(dependencies.Model.State.Session);
        Assert.Equal(AuthenticationPhase.SignedOut, dependencies.Model.State.Phase);
        Assert.Equal(AccountDeletionPhase.Idle, dependencies.Model.State.AccountDeletionPhase);
    }

    /// <summary>ログイン識別子の削除失敗時に認証状態を保持して再試行可能にすることを検証します。</summary>
    /// <remarks>責務: 部分失敗をログアウト成功へ変換せず削除確認画面へ保持することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task CredentialRemovalFailureKeepsSignedInRetryState()
    {
        var session = new AppleAccountSession("retry-user");
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedIn, session: session));
        dependencies.Store.Identifier = session.UserIdentifier;
        dependencies.Store.RemoveError = new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);

        dependencies.Model.Send(AuthenticationAction.AccountDeletionRequested);
        dependencies.Model.Send(AuthenticationAction.AccountDeletionWarningConfirmed);
        dependencies.Model.Send(AuthenticationAction.AccountDeletionConfirmed);
        await WaitForTasks();

        Assert.Equal(new[] { "retry-user" }, dependencies.DataEraser.ErasedIdentifiers);
        Assert.Equal(AuthenticationPhase.SignedIn, dependencies.Model.State.Phase);
        Assert.Equal(session, dependencies.Model.State.Session);
        Assert.Equal(AccountDeletionPhase.Failed, dependencies.Model.State.AccountDeletionPhase);
        Assert.Equal(AccountDeletionFailure.DeletionFailed, dependencies.Model.State.AccountDeletionFailure);
    }

    /// <summary>復元した認証セッションが新しい失効世代を受理せず監視を開始することを検証します。</summary>
    /// <remarks>責務: 保存済みセッション復元を他端末失効の継続監視へ接続することを確認します。</remarks>
    /// <returns>テストの完了を表すタスク。</returns>
    [Fact]
    public async Task RestoredSessionStartsRevocationObservationWithoutRegisteringCurrentMarker()
    {
        var dependencies = MakeDependencies();
        dependencies.Store.Identifier = "restored-user";
        dependencies.Authorization.CredentialStateResult = AppleAccountCredentialState.Authorized;

        dependencies.Model.Send(AuthenticationAction.Appeared);
        await WaitForTasks();

        Assert.Equal(AuthenticationPhase.SignedIn, dependencies.Model.State.Phase);
        Assert.Equal("restored-user", dependencies.Revocation.ObservedIdentifier);
        Assert.Empty(dependencies.Revocation.RegisteredIdentifiers);
    }

    /// <summary>他端末の失効通知が現在端末のデータとセッションを削除してログアウトすることを検証します。</summary>
    /// <remarks>責務: 遠隔失効コールバックをローカル消去とログイン画面状態へ変換することを確認します。</remarks>
    [Fact]
    public void RemoteRevocationErasesLocalDataAndSignsOut()
    {
        var session = new AppleAccountSession("remote-user");
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedIn, session: session));
        dependencies.Store.Identifier = session.UserIdentifier;

        dependencies.Revocation.DeliverRevocation();

        Assert.Equal(new[] { "remote-user" }, dependencies.DataEraser.ErasedIdentifiers);
        Assert.Empty(dependencies.Revocation.PublishedIdentifiers);
        Assert.Null(dependencies.Store.Identifier);
        Assert.Null(dependencies.Model.State.Session);
        Assert.Equal(AuthenticationPhase.SignedOut, dependencies.Model.State.Phase);
        Assert.Equal(1, dependencies.Revocation.StopCount);
    }

    /// <summary>他端末ログアウトのKeychain削除失敗を案内しつつ現在表示をログイン画面へ戻すことを検証します。</summary>
    /// <remarks>責務: 遠隔失効の端末内永続化失敗を成功扱いせず区別可能なログイン画面状態へ保持します。</remarks>
    [Fact]
    public void RemoteRevocationPersistenceFailureSignsOutWithFailureNotice()
    {
        var session = new AppleAccountSession("remote-retry-user");
        var dependencies = MakeDependencies(new AuthenticationState(phase: AuthenticationPhase.SignedIn, session: session));
        dependencies.Store.Identifier = session.UserIdentifier;
        dependencies.Store.RemoveError = new AuthenticationSessionStoreException(AuthenticationSessionStoreError.Unavailable);

        dependencies.Revocation.DeliverRevocation();

        Assert.Equal(new[] { "remote-retry-user" }, dependencies.DataEraser.ErasedIdentifiers);
        Assert.Equal("remote-retry-user", dependencies.Store.Identifier);
        Assert.Equal(AuthenticationPhase.SignedOut, dependencies.Model.State.Phase);
        Assert.Equal(AuthenticationFailure.RemoteLogoutPersistenceFailed, dependencies.Model.State.Failure);
    }

    /// <summary>テスト用依存関係と認証モデルをまとめて生成します。</summary>
    /// <remarks>責務: 1件の認証モデルテストへ共有Fakeを同一構成で注入します。</remarks>
    /// <param name="state">モデルへ与える初期認証状態。未指定時はセッション確認中の状態を生成します。</param>
    /// <returns>認証モデルと各Fakeの組。</returns>
    private static AuthenticationModelDependencies MakeDependencies(AuthenticationState? state = null)
    {
        var authorization = new AuthenticationModelAuthorizationPortFake();
        var store = new AuthenticationModelSessionStorePortFake();
        var dataEraser = new AuthenticationModelAccountDataEraserFake();
        var transfers = new AuthenticationModelTransferRepositoryFake();
        var vehicleDataEraser = new AuthenticationModelVehicleDataEraserFake();
        var revocation = new AuthenticationModelSessionRevocationPortFake();
        var model = new AuthenticationSessionModel(
            state: state ?? new AuthenticationState(),
            restoreSession: new RestoreAuthenticationSessionUseCase(
                authorizationPort: authorization,
                sessionStore: store),
            signInWithApple: new SignInWithAppleUseCase(
                authorizationPort: authorization,
                sessionStore: store),
            deleteAccount: new DeleteAccountUseCase(
                sessionRevocation: revocation,
                sessionTransfers: transfers,
                vehicleDataEraser: vehicleDataEraser,
                dataEraser: dataEraser,
                sessionStore: store),
            remoteAccountLogout: new RemoteAccountLogoutUseCase(
                dataEraser: dataEraser,
                sessionStore: store),
            sessionRevocation: revocation);
        return new AuthenticationModelDependencies(model, authorization, store, dataEraser, transfers, revocation);
    }

    /// <summary>モデルが開始した非同期処理へ実行機会を与えます。</summary>
    /// <remarks>責務: 即時Fakeを待機する認証モデルタスクをテスト検証前に完了させます。</remarks>
    /// <returns>待機の完了を表すタスク。</returns>
    private static async Task WaitForTasks()
    {
        for (var index = 0; index < 8; index++)
        {
            await Task.Yield();
        }
    }

    /// <summary>認証モデルテストで共有するモデルとFakeの組です。</summary>
    /// <param name="Model">検証対象の認証セッションモデルです。</param>
    /// <param name="Authorization">Apple認証要求を記録するFakeです。</param>
    /// <param name="Store">セッション保存結果を記録するFakeです。</param>
    /// <param name="DataEraser">削除対象識別子を記録するアカウントデータ消去Fakeです。</param>
    /// <param name="Transfers">CloudKit運転データ削除を記録するFakeです。</param>
    /// <param name="Revocation">失効登録、発行、監視を記録するFakeです。</param>
    private sealed record AuthenticationModelDependencies(
        AuthenticationSessionModel Model,
        AuthenticationModelAuthorizationPortFake Authorization,
        AuthenticationModelSessionStorePortFake Store,
        AuthenticationModelAccountDataEraserFake DataEraser,
        AuthenticationModelTransferRepositoryFake Transfers,
        AuthenticationModelSessionRevocationPortFake Revocation);

    /// <summary>認証モデルテストでApple認証結果を制御します。</summary>
    private sealed class AuthenticationModelAuthorizationPortFake : IAppleAccountAuthorizationPort
    {
        /// <summary>Apple認証要求が成功時に返すセッションです。</summary>
        public AppleAccountSession AuthorizationSession { get; set; } = new("model-user");

        /// <summary>Apple認証要求が失敗として返すエラーです。<see langword="null"/> なら成功します。</summary>
        public Exception? AuthorizationError { get; set; }

        /// <summary>Apple資格状態照会が返す状態です。</summary>
        public AppleAccountCredentialState CredentialStateResult { get; set; } = AppleAccountCredentialState.NotFound;

        /// <summary>Apple認証要求の実行回数です。</summary>
        public int AuthorizationRequestCount { get; private set; }

        /// <summary>Apple資格状態照会の実行回数です。</summary>
        public int CredentialStateRequestCount { get; private set; }

        /// <summary>設定されたApple認証結果を返します。</summary>
        /// <remarks>責務: Apple認証要求を記録してテスト指定結果を返します。</remarks>
        /// <returns>テストが設定した認証セッション、またはテストが設定した認証失敗。</returns>
        public Task<AppleAccountSession> Authorize()
        {
            AuthorizationRequestCount += 1;
            return AuthorizationError is null
                ? Task.FromResult(AuthorizationSession)
                : Task.FromException<AppleAccountSession>(AuthorizationError);
        }

        /// <summary>設定されたApple資格状態を返します。</summary>
        /// <remarks>責務: 資格状態照会を記録してテスト指定状態を返します。</remarks>
        /// <param name="userIdentifier">モデルが照会したAppleユーザー識別子。</param>
        /// <returns>テストが設定したApple資格状態。</returns>
        public Task<AppleAccountCredentialState> CredentialState(string userIdentifier)
        {
            CredentialStateRequestCount += 1;
            return Task.FromResult(CredentialStateResult);
        }
    }

    /// <summary>認証モデルテストでセッション保存結果を記録します。</summary>
    private sealed class AuthenticationModelSessionStorePortFake : IAuthenticationSessionStorePort
    {
        /// <summary>現在保存されているテスト用Appleユーザー識別子です。</summary>
        public string? Identifier { get; set; }

        /// <summary>セッション削除時に送出するテスト指定エラーです。</summary>
        public Exception? RemoveError { get; set; }

        /// <summary>現在のテスト用識別子を返します。</summary>
        /// <remarks>責務: モデルの復元ユースケースへ保存済み識別子を返します。</remarks>
        /// <returns>現在保存されているテスト用識別子。</returns>
        public string? LoadUserIdentifier() => Identifier;

        /// <summary>テスト用識別子を置き換えます。</summary>
        /// <remarks>責務: モデルのログインユースケースが保存した識別子を記録します。</remarks>
        /// <param name="userIdentifier">保存を要求されたAppleユーザー識別子。</param>
        public void SaveUserIdentifier(string userIdentifier) => Identifier = userIdentifier;

        /// <summary>テスト用識別子を削除します。</summary>
        /// <remarks>責務: モデルの復元ユースケースが無効化した識別子を除去します。</remarks>
        public void RemoveUserIdentifier()
        {
            if (RemoveError is not null)
            {
                throw RemoveError;
            }
            Identifier = null;
        }
    }

    /// <summary>認証モデルテストでアカウントデータ消去要求を記録します。</summary>
    private sealed class AuthenticationModelAccountDataEraserFake : IAccountDataErasurePort
    {
        /// <summary>消去を要求されたAppleユーザー識別子です。</summary>
        public List<string> ErasedIdentifiers { get; } = [];

        /// <summary>指定識別子を消去要求履歴へ追加します。</summary>
        /// <remarks>責務: 1件のデータ消去要求をテストから観測可能な履歴へ記録します。</remarks>
        /// <param name="userIdentifier">消去を要求されたAppleユーザー識別子。</param>
        public void EraseAllData(string userIdentifier) => ErasedIdentifiers.Add(userIdentifier);
    }

    /// <summary>認証モデルテストでCloudKit運転データ操作を記録します。</summary>
    private sealed class AuthenticationModelTransferRepositoryFake : IConnectionSessionTransferRepository
    {
        /// <summary>全削除を要求されたアカウント識別子です。</summary>
        public List<string> DeletedAccountIdentifiers { get; } = [];

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

        /// <summary>CloudKit運転データ全削除を記録します。</summary>
        /// <remarks>責務: 1件のアカウント識別子を削除要求履歴へ追加します。</remarks>
        /// <param name="accountIdentifier">削除対象アカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteAll(string accountIdentifier)
        {
            DeletedAccountIdentifiers.Add(accountIdentifier);
            return Task.CompletedTask;
        }
    }

    /// <summary>認証モデルテストで車両データ削除境界を再現します。</summary>
    private sealed class AuthenticationModelVehicleDataEraserFake : IAccountVehicleDataErasurePort
    {
        /// <summary>全削除されたアカウント識別子です。</summary>
        public List<string> DeletedAccountIdentifiers { get; } = [];

        /// <summary>車両カタログ全削除を記録します。</summary>
        /// <remarks>責務: 1件のアカウント識別子を車両データ削除履歴へ追加します。</remarks>
        /// <param name="accountIdentifier">削除対象アカウント識別子。</param>
        /// <returns>完了済みタスク。</returns>
        public Task DeleteAllVehicleData(string accountIdentifier)
        {
            DeletedAccountIdentifiers.Add(accountIdentifier);
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

    /// <summary>認証モデルテストで端末間セッション失効操作を記録します。</summary>
    private sealed class AuthenticationModelSessionRevocationPortFake : IAccountSessionRevocationPort
    {
        /// <summary>現在世代の受理を要求されたユーザー識別子です。</summary>
        public List<string> RegisteredIdentifiers { get; } = [];

        /// <summary>失効発行を要求されたユーザー識別子です。</summary>
        public List<string> PublishedIdentifiers { get; } = [];

        /// <summary>現在監視中のユーザー識別子です。</summary>
        public string? ObservedIdentifier { get; private set; }

        /// <summary>監視終了を要求された回数です。</summary>
        public int StopCount { get; private set; }

        /// <summary>テストから実行する現在の失効通知処理です。</summary>
        private Action? _receiveRevocation;

        /// <summary>現在世代の受理要求を記録します。</summary>
        /// <remarks>責務: 1件のセッション基準登録をテストから検証可能な履歴へ追加します。</remarks>
        /// <param name="userIdentifier">登録を要求されたユーザー識別子。</param>
        public void RegisterCurrentSession(string userIdentifier) => RegisteredIdentifiers.Add(userIdentifier);

        /// <summary>新しい失効世代の発行要求を記録します。</summary>
        /// <remarks>責務: 1件の失効発行をテストから検証可能な履歴へ追加します。</remarks>
        /// <param name="userIdentifier">発行を要求されたユーザー識別子。</param>
        public void PublishRevocation(string userIdentifier) => PublishedIdentifiers.Add(userIdentifier);

        /// <summary>失効監視対象とコールバックを保持します。</summary>
        /// <remarks>責務: 1件の失効監視要求をテストから起動可能なコールバックへ保存します。</remarks>
        /// <param name="userIdentifier">監視を要求されたユーザー識別子。</param>
        /// <param name="receive">テストが失効を模擬するときに実行する処理。</param>
        public void StartObserving(string userIdentifier, Action receive)
        {
            ObservedIdentifier = userIdentifier;
            _receiveRevocation = receive;
        }

        /// <summary>失効監視の終了要求を記録します。</summary>
        /// <remarks>責務: 現在のFake監視状態を未購読へ戻して終了回数を加算します。</remarks>
        public void StopObserving()
        {
            StopCount += 1;
            ObservedIdentifier = null;
            _receiveRevocation = null;
        }

        /// <summary>保存済み失効コールバックをテストから実行します。</summary>
        /// <remarks>責務: 他端末からの失効受信を1回のFakeコールバック実行として再現します。</remarks>
        public void DeliverRevocation() => _receiveRevocation?.Invoke();
    }
}
