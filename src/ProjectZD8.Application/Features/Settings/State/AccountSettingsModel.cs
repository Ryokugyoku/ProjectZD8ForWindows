using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProjectZD8.Application.Settings.Actions;
using ProjectZD8.Application.Settings.UseCases;

namespace ProjectZD8.Application.Settings.State;

/// <summary>アカウント設定操作を保存、復元、端末間同期へ変換します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です。</remarks>
public sealed class AccountSettingsModel : INotifyPropertyChanged
{
    /// <summary>アカウント設定の保存、復元、同期監視を行うユースケースです。</summary>
    private readonly AccountSettingsUseCase useCase;

    /// <summary>現在設定を読み書きするAppleユーザー識別子です。</summary>
    private string? accountIdentifier;

    /// <summary>現在の設定の保存先です。</summary>
    private AccountSettings settings;

    /// <summary>初期設定とユースケースを注入してモデルを生成します。</summary>
    /// <remarks>責務: アカウント設定の状態遷移を保存と同期のユースケースへ結び付けます。</remarks>
    /// <param name="settings">アカウント確定前に表示する初期設定。</param>
    /// <param name="useCase">設定の保存、復元、同期監視を行うユースケース。</param>
    public AccountSettingsModel(AccountSettings settings, AccountSettingsUseCase useCase)
    {
        this.settings = settings;
        this.useCase = useCase;
    }

    /// <summary>観測対象プロパティが変わったときに通知します。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>ルート画面と設定画面が描画する現在の設定です。</summary>
    public AccountSettings Settings
    {
        get => settings;
        set => SetProperty(ref settings, value);
    }

    /// <summary>受け取った設定操作をアカウント状態または保存済み設定へ反映します。</summary>
    /// <remarks>責務: 1件のアカウント設定操作を対応する状態遷移と保存要求へ変換します。</remarks>
    /// <param name="action">App層または設定画面から通知された型付き操作。</param>
    public void Send(AccountSettingsAction action)
    {
        switch (action)
        {
            case AccountSettingsAction.AccountIdentifierChanged changed:
                ActivateAccount(changed.Identifier);
                break;
            case AccountSettingsAction.LanguageSelected selected:
                Settings = Settings with { Language = selected.Language };
                SaveIfAccountIsActive();
                break;
            case AccountSettingsAction.AppearanceSelected selected:
                Settings = Settings with { Appearance = selected.Appearance };
                SaveIfAccountIsActive();
                break;
            case AccountSettingsAction.AutomaticSessionUploadChanged changed:
                Settings = Settings with { AutomaticSessionUploadEnabled = changed.IsEnabled };
                SaveIfAccountIsActive();
                break;
        }
    }

    /// <summary>指定されたアカウントへ保存・同期スコープを切り替えます。</summary>
    /// <remarks>責務: 以前の監視を終了して現在のアカウント設定だけを復元および購読します。</remarks>
    /// <param name="identifier">新しいAppleユーザー識別子。未認証の場合は <c>null</c>。</param>
    private void ActivateAccount(string? identifier)
    {
        if (string.Equals(identifier, accountIdentifier, StringComparison.Ordinal))
        {
            return;
        }

        useCase.StopObserving();
        accountIdentifier = identifier;

        if (string.IsNullOrEmpty(identifier))
        {
            Settings = new AccountSettings();
            return;
        }

        Settings = useCase.Load(identifier);
        useCase.StartObserving(identifier, synchronizedSettings => Settings = synchronizedSettings);
    }

    /// <summary>認証済みアカウントが存在する場合だけ現在設定を保存します。</summary>
    /// <remarks>責務: 現在の表示設定とセッション自動送信設定を有効な1件のアカウントスコープへ保存します。</remarks>
    private void SaveIfAccountIsActive()
    {
        if (accountIdentifier is null)
        {
            return;
        }

        useCase.Save(Settings, accountIdentifier);
    }

    /// <summary>値が変わったときだけ保存して変更を通知します。</summary>
    /// <remarks>責務: 観測対象プロパティの変更通知を1か所にまとめます。</remarks>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">保存先のフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">プロパティ名。</param>
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
