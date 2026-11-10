using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.Authentication.Actions;
using ProjectZD8.Application.Authentication.Ports;
using ProjectZD8.Application.Authentication.UseCases;

namespace ProjectZD8.Application.Authentication.State;

/// <summary>認証入口の型付き操作をApplicationユースケースと表示状態へ変換します。</summary>
/// <remarks>
/// UI スレッド（単一スレッド）から呼ぶ前提です（Swift 版の <c>@MainActor</c>）。
/// Swift 版の <c>@Observable</c> に対応し、<see cref="State"/> の変更を <see cref="PropertyChanged"/> で通知します。
/// Swift の <c>Task { ... }</c> と違い、C# の非同期メソッドは最初の未完了の待機まで呼び出し中に同期で進みます。
/// 状態遷移の順番は Swift と同じです。実行中のタスクは「未完了のタスクを保持しているか」で判定します（Swift の <c>defer { task = nil }</c> に相当）。
/// </remarks>
public sealed class AuthenticationSessionModel : INotifyPropertyChanged
{
    /// <summary>ルート画面とログイン画面が描画する現在の認証状態です。</summary>
    private AuthenticationState _state;

    /// <summary>保存済みセッションをApple資格状態と照合するユースケースです。</summary>
    private readonly RestoreAuthenticationSessionUseCase _restoreSession;

    /// <summary>Apple認証結果を安全なセッションとして保存するユースケースです。</summary>
    private readonly SignInWithAppleUseCase _signInWithApple;

    /// <summary>認証済みアカウントのアプリ保存データとログイン識別子を削除するユースケースです。</summary>
    private readonly DeleteAccountUseCase _deleteAccount;

    /// <summary>他端末から受信した失効を端末内ログアウトへ反映するユースケースです。</summary>
    private readonly RemoteAccountLogoutUseCase _remoteAccountLogout;

    /// <summary>アカウント別セッション失効を端末間で監視する境界です。</summary>
    private readonly IAccountSessionRevocationPort _sessionRevocation;

    /// <summary>重複した起動時確認を防ぐ復元タスクです。</summary>
    private Task? _restorationTask;

    /// <summary>重複したApple認証要求を防ぐログインタスクです。</summary>
    private Task? _signInTask;

    /// <summary>重複したアカウント削除要求を防ぐ削除タスクです。</summary>
    private Task? _accountDeletionTask;

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>ルート画面とログイン画面が描画する現在の認証状態です。</summary>
    public AuthenticationState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    /// <summary>初期表示状態、認証ユースケース、端末間失効境界を注入します。</summary>
    /// <remarks>責務: 認証入口の状態遷移をApple認証と端末間セッション失効のApplication境界へ結び付けます。</remarks>
    /// <param name="state">認証入口の初期表示状態。</param>
    /// <param name="restoreSession">保存済みセッションを照合するユースケース。</param>
    /// <param name="signInWithApple">Apple認証とセッション保存を行うユースケース。</param>
    /// <param name="deleteAccount">アカウント保存データとログイン識別子を削除するユースケース。</param>
    /// <param name="remoteAccountLogout">他端末から失効されたアカウントを端末内からログアウトするユースケース。</param>
    /// <param name="sessionRevocation">アカウント別セッション失効の同期境界。</param>
    public AuthenticationSessionModel(
        AuthenticationState state,
        RestoreAuthenticationSessionUseCase restoreSession,
        SignInWithAppleUseCase signInWithApple,
        DeleteAccountUseCase deleteAccount,
        RemoteAccountLogoutUseCase remoteAccountLogout,
        IAccountSessionRevocationPort sessionRevocation)
    {
        _state = state;
        _restoreSession = restoreSession;
        _signInWithApple = signInWithApple;
        _deleteAccount = deleteAccount;
        _remoteAccountLogout = remoteAccountLogout;
        _sessionRevocation = sessionRevocation;

        if (state.Phase == AuthenticationPhase.SignedIn && state.Session?.UserIdentifier is { } userIdentifier)
        {
            sessionRevocation.RegisterCurrentSession(userIdentifier);
            StartObservingSessionRevocation(userIdentifier);
        }
    }

    /// <summary>認証入口から受け取った1件の操作を表示状態またはユースケースへ反映します。</summary>
    /// <remarks>責務: 1件の認証操作を対応するApplication状態遷移へ変換します。</remarks>
    /// <param name="action">ログイン画面またはルート画面から通知された型付き操作。</param>
    public void Send(AuthenticationAction action)
    {
        switch (action)
        {
            case AuthenticationAction.Appeared:
                BeginSessionRestorationIfNeeded();
                break;
            case AuthenticationAction.LoginTapped:
                PresentDisclaimerIfPossible();
                break;
            case AuthenticationAction.DisclaimerDismissed:
                State = State with { IsDisclaimerPresented = false };
                break;
            case AuthenticationAction.DisclaimerAccepted:
                BeginSignInIfPossible();
                break;
            case AuthenticationAction.RetryTapped:
                State = State with { Failure = null };
                PresentDisclaimerIfPossible();
                break;
            case AuthenticationAction.AccountDeletionRequested:
                PresentAccountDeletionWarningIfPossible();
                break;
            case AuthenticationAction.AccountDeletionWarningConfirmed:
                PresentAccountDeletionReviewIfPossible();
                break;
            case AuthenticationAction.AccountDeletionCancelled:
                CancelAccountDeletionIfPossible();
                break;
            case AuthenticationAction.AccountDeletionConfirmed:
                BeginAccountDeletionIfPossible();
                break;
        }
    }

    /// <summary>認証済みで削除処理中でない場合に最初の警告を提示します。</summary>
    /// <remarks>責務: 設定画面の削除要求を取り消し可能な警告段階へ遷移させます。</remarks>
    private void PresentAccountDeletionWarningIfPossible()
    {
        if (State.Phase != AuthenticationPhase.SignedIn || State.AccountDeletionPhase != AccountDeletionPhase.Idle)
        {
            return;
        }
        State = State with { AccountDeletionFailure = null, AccountDeletionPhase = AccountDeletionPhase.Warning };
    }

    /// <summary>最初の警告が表示中の場合だけ削除事項の確認段階へ進めます。</summary>
    /// <remarks>責務: 1段階目の警告同意を最終削除事項の表示状態へ変換します。</remarks>
    private void PresentAccountDeletionReviewIfPossible()
    {
        if (State.Phase != AuthenticationPhase.SignedIn || State.AccountDeletionPhase != AccountDeletionPhase.Warning)
        {
            return;
        }
        State = State with { AccountDeletionPhase = AccountDeletionPhase.Reviewing };
    }

    /// <summary>削除実行中でない確認状態を通常の認証済み状態へ戻します。</summary>
    /// <remarks>責務: 未実行のアカウント削除確認だけを安全に取り消します。</remarks>
    private void CancelAccountDeletionIfPossible()
    {
        if (State.AccountDeletionPhase == AccountDeletionPhase.Deleting)
        {
            return;
        }
        State = State with { AccountDeletionFailure = null, AccountDeletionPhase = AccountDeletionPhase.Idle };
    }

    /// <summary>最終確認済みの認証済みアカウントを1回だけ削除します。</summary>
    /// <remarks>責務: 最終削除操作をアカウント削除ユースケースとログイン画面への状態遷移へ結び付けます。</remarks>
    private void BeginAccountDeletionIfPossible()
    {
        if (State.Phase != AuthenticationPhase.SignedIn
            || (State.AccountDeletionPhase != AccountDeletionPhase.Reviewing && State.AccountDeletionPhase != AccountDeletionPhase.Failed)
            || State.Session?.UserIdentifier is not { } userIdentifier
            || IsRunning(_accountDeletionTask))
        {
            return;
        }

        State = State with { AccountDeletionFailure = null, AccountDeletionPhase = AccountDeletionPhase.Deleting };
        _accountDeletionTask = RunAccountDeletion(userIdentifier);
    }

    /// <summary>アカウント削除ユースケースを実行し、結果を表示状態へ反映します。</summary>
    /// <remarks>責務: 1回のアカウント削除結果をログイン画面または再試行可能な失敗状態へ変換します。</remarks>
    /// <param name="userIdentifier">削除対象のAppleユーザー識別子。</param>
    /// <returns>削除処理の完了を表すタスク。</returns>
    private async Task RunAccountDeletion(string userIdentifier)
    {
        try
        {
            await _deleteAccount.Execute(userIdentifier);
            _sessionRevocation.StopObserving();
            State = State with
            {
                AccountDeletionPhase = AccountDeletionPhase.Idle,
                AccountDeletionFailure = null,
                Session = null,
                Phase = AuthenticationPhase.SignedOut,
            };
        }
        catch (Exception)
        {
            State = State with
            {
                AccountDeletionFailure = AccountDeletionFailure.DeletionFailed,
                AccountDeletionPhase = AccountDeletionPhase.Failed,
            };
        }
    }

    /// <summary>初期確認中で未実行の場合に限り保存済みセッションの復元を開始します。</summary>
    /// <remarks>責務: 起動中に1回だけセッション復元ユースケースを実行します。</remarks>
    private void BeginSessionRestorationIfNeeded()
    {
        if (State.Phase != AuthenticationPhase.CheckingSession || IsRunning(_restorationTask))
        {
            return;
        }
        _restorationTask = RunSessionRestoration();
    }

    /// <summary>セッション復元ユースケースを実行し、結果を表示状態へ反映します。</summary>
    /// <remarks>責務: 1回の保存済みセッション照合結果を認証済みまたはログアウト状態へ変換します。</remarks>
    /// <returns>復元処理の完了を表すタスク。</returns>
    private async Task RunSessionRestoration()
    {
        try
        {
            var session = await _restoreSession.Execute();
            State = State with
            {
                Session = session,
                Phase = session is null ? AuthenticationPhase.SignedOut : AuthenticationPhase.SignedIn,
            };
            if (session?.UserIdentifier is { } userIdentifier)
            {
                StartObservingSessionRevocation(userIdentifier);
            }
        }
        catch (Exception)
        {
            State = State with
            {
                Session = null,
                Phase = AuthenticationPhase.SignedOut,
                Failure = AuthenticationFailure.SessionCheckFailed,
            };
        }
    }

    /// <summary>ログイン待機中の場合に限り免責事項を提示します。</summary>
    /// <remarks>責務: Apple認証前の明示同意画面をログイン可能状態だけで開きます。</remarks>
    private void PresentDisclaimerIfPossible()
    {
        if (State.Phase != AuthenticationPhase.SignedOut)
        {
            return;
        }
        State = State with { Failure = null, IsDisclaimerPresented = true };
    }

    /// <summary>免責事項が表示中でログイン可能な場合に限りApple認証を開始します。</summary>
    /// <remarks>責務: 明示同意済みの1回のAppleログイン要求だけを実行します。</remarks>
    private void BeginSignInIfPossible()
    {
        if (State.Phase != AuthenticationPhase.SignedOut
            || !State.IsDisclaimerPresented
            || IsRunning(_signInTask))
        {
            return;
        }

        State = State with
        {
            IsDisclaimerPresented = false,
            Failure = null,
            Phase = AuthenticationPhase.SigningIn,
        };
        _signInTask = RunSignIn();
    }

    /// <summary>Appleログインユースケースを実行し、結果を表示状態へ反映します。</summary>
    /// <remarks>責務: 1回のApple認証結果を認証済み状態または区別可能なログアウト状態へ変換します。</remarks>
    /// <returns>ログイン処理の完了を表すタスク。</returns>
    private async Task RunSignIn()
    {
        try
        {
            var session = await _signInWithApple.Execute();
            _sessionRevocation.RegisterCurrentSession(session.UserIdentifier);
            StartObservingSessionRevocation(session.UserIdentifier);
            State = State with { Session = session, Phase = AuthenticationPhase.SignedIn };
        }
        catch (AppleAccountAuthorizationException error) when (error.Reason == AppleAccountAuthorizationError.Cancelled)
        {
            State = State with { Session = null, Phase = AuthenticationPhase.SignedOut };
        }
        catch (AppleAccountAuthorizationException error) when (error.Reason == AppleAccountAuthorizationError.Unavailable)
        {
            State = State with
            {
                Session = null,
                Phase = AuthenticationPhase.SignedOut,
                Failure = AuthenticationFailure.ServiceUnavailable,
            };
        }
        catch (Exception)
        {
            State = State with
            {
                Session = null,
                Phase = AuthenticationPhase.SignedOut,
                Failure = AuthenticationFailure.SignInFailed,
            };
        }
    }

    /// <summary>指定アカウントの未受理セッション失効を監視します。</summary>
    /// <remarks>責務: 認証済みアカウント1件を端末間失効通知と現在端末のログアウト処理へ結び付けます。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てたユーザー識別子。</param>
    private void StartObservingSessionRevocation(string userIdentifier)
    {
        _sessionRevocation.StartObserving(userIdentifier, () => ApplyRemoteSessionRevocation(userIdentifier));
    }

    /// <summary>他端末から失効された現在アカウントを端末内からログアウトします。</summary>
    /// <remarks>責務: 1件の遠隔失効を端末内消去結果とログイン画面への状態遷移へ変換します。</remarks>
    /// <param name="userIdentifier">失効通知の対象となったAppleユーザー識別子。</param>
    private void ApplyRemoteSessionRevocation(string userIdentifier)
    {
        if (State.Phase != AuthenticationPhase.SignedIn
            || !string.Equals(State.Session?.UserIdentifier, userIdentifier, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _remoteAccountLogout.Execute(userIdentifier);
        }
        catch (Exception)
        {
            State = State with { Failure = AuthenticationFailure.RemoteLogoutPersistenceFailed };
        }
        _sessionRevocation.StopObserving();
        State = State with
        {
            AccountDeletionPhase = AccountDeletionPhase.Idle,
            AccountDeletionFailure = null,
            Session = null,
            Phase = AuthenticationPhase.SignedOut,
        };
    }

    /// <summary>保持しているタスクがまだ完了していないかを返します。</summary>
    /// <remarks>責務: Swift 版の「タスクを保持している（nil でない）」判定を、完了済みタスクを未保持とみなす判定へ変換します。</remarks>
    /// <param name="task">判定するタスク。</param>
    /// <returns>未完了のタスクを保持していれば <see langword="true"/>。</returns>
    private static bool IsRunning(Task? task) => task is { IsCompleted: false };

    /// <summary>値が変わった場合だけフィールドを更新して変更を通知します。</summary>
    /// <remarks>責務: 1件の観測対象プロパティの更新を変更通知へ変換します。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">変更したプロパティ名。</param>
    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
