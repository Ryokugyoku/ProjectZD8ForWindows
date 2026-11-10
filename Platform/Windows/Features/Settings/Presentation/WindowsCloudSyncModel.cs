using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace ProjectZD8ForWindows.Platform.Windows.Features.Settings.Presentation;

/// <summary>
/// iCloud（CloudKit Web Services）を使えるかどうかの表示上の区分です。
/// </summary>
public enum WindowsCloudSyncAvailability
{
    /// <summary>Sign in with Apple が Coming Soon（機能フラグ AppleSignIn:Enabled が無効か、設定が足りない）で、ローカルモードで動いている。</summary>
    NotConfigured,

    /// <summary>設定はあるが、iCloud にサインインしていない。</summary>
    SignedOut,

    /// <summary>iCloud にサインイン済みで同期できる。</summary>
    Available,
}

/// <summary>
/// Windows 版だけにある「iCloud へのサインイン」（CloudKit の Web 認証）の表示状態と操作を持ちます。
/// </summary>
/// <remarks>
/// iOS 版は端末の iCloud をそのまま使うので、この画面はありません。
/// サインインの手続きそのものは組み立てが渡す処理（Data 層の CloudKitWebAuthenticator）が行い、
/// このモデルは進行中・失敗の表示状態だけを持ちます。UI スレッド（単一スレッド）から呼ぶ前提です。
/// </remarks>
public sealed class WindowsCloudSyncModel : INotifyPropertyChanged
{
    /// <summary>今の可用性を返す処理です。</summary>
    private readonly Func<WindowsCloudSyncAvailability> _availability;

    /// <summary>ブラウザーで iCloud にサインインする処理です。</summary>
    private readonly Func<Task> _signIn;

    /// <summary>iCloud からサインアウトする処理です。</summary>
    private readonly Action _signOut;

    /// <summary>サインインの進行中なら true。</summary>
    private bool _isSigningIn;

    /// <summary>直前のサインインに失敗したなら true。</summary>
    private bool _didFail;

    /// <summary>
    /// 可用性の取得とサインイン・サインアウトの処理を受け取ります。
    /// </summary>
    /// <remarks>責務: 組み立てが渡す 3 つの処理を保持します。</remarks>
    /// <param name="availability">今の可用性を返す処理。</param>
    /// <param name="signIn">ブラウザーで iCloud にサインインする処理（失敗なら例外）。</param>
    /// <param name="signOut">iCloud からサインアウトする処理。</param>
    public WindowsCloudSyncModel(Func<WindowsCloudSyncAvailability> availability, Func<Task> signIn, Action signOut)
    {
        _availability = availability;
        _signIn = signIn;
        _signOut = signOut;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>今の可用性です。</summary>
    public WindowsCloudSyncAvailability Availability => _availability();

    /// <summary>サインインの進行中なら true。</summary>
    public bool IsSigningIn
    {
        get => _isSigningIn;
        private set => SetProperty(ref _isSigningIn, value);
    }

    /// <summary>直前のサインインに失敗したなら true。</summary>
    public bool DidFail
    {
        get => _didFail;
        private set => SetProperty(ref _didFail, value);
    }

    /// <summary>
    /// iCloud へのサインインを始めます。
    /// </summary>
    /// <remarks>責務: 進行中の表示にしてサインインの処理を待ち、結果を表示状態へ反映します。</remarks>
    /// <returns>サインインが終わるまでのタスク。</returns>
    public async Task SignIn()
    {
        if (IsSigningIn || Availability == WindowsCloudSyncAvailability.NotConfigured)
        {
            return;
        }

        DidFail = false;
        IsSigningIn = true;
        try
        {
            await _signIn();
        }
        catch (Exception)
        {
            DidFail = true;
        }
        finally
        {
            IsSigningIn = false;
            OnPropertyChanged(nameof(Availability));
        }
    }

    /// <summary>
    /// iCloud からサインアウトします。
    /// </summary>
    /// <remarks>責務: サインアウトの処理を呼び、可用性の表示を更新します。</remarks>
    public void SignOut()
    {
        _signOut();
        DidFail = false;
        OnPropertyChanged(nameof(Availability));
    }

    /// <summary>
    /// 可用性が外で変わったことを知らせます（トークンの失効など）。
    /// </summary>
    /// <remarks>責務: 可用性の変更通知を出します。</remarks>
    public void AvailabilityDidChange() => OnPropertyChanged(nameof(Availability));

    /// <summary>
    /// 値が変わったときだけ書き換えて通知します。
    /// </summary>
    /// <remarks>責務: 1 つのプロパティの値の比較・代入・通知を行います。</remarks>
    /// <param name="field">書き換えるフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">プロパティ名。</param>
    private void SetProperty(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// 変更を通知します。
    /// </summary>
    /// <remarks>責務: PropertyChanged を 1 回出します。</remarks>
    /// <param name="propertyName">プロパティ名。</param>
    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
