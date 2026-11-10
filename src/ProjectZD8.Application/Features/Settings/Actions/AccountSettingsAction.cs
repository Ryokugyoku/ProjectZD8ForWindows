using ProjectZD8.Application.Settings.State;

namespace ProjectZD8.Application.Settings.Actions;

/// <summary>アカウント設定モデルへ通知するスコープおよび選択操作です。</summary>
public abstract record AccountSettingsAction
{
    /// <summary>外部からの派生を防ぎます。</summary>
    /// <remarks>責務: 操作の種類を下のケースに限定します。</remarks>
    private AccountSettingsAction()
    {
    }

    /// <summary>現在認証済みのアプリ固有Appleユーザー識別子を通知します。</summary>
    /// <param name="Identifier">Appleユーザー識別子。未認証の場合は <c>null</c>。</param>
    public sealed record AccountIdentifierChanged(string? Identifier) : AccountSettingsAction;

    /// <summary>表示言語の選択変更を通知します。</summary>
    /// <param name="Language">選択された表示言語。</param>
    public sealed record LanguageSelected(SettingsLanguage Language) : AccountSettingsAction;

    /// <summary>外観モードの選択変更を通知します。</summary>
    /// <param name="Appearance">選択された外観。</param>
    public sealed record AppearanceSelected(SettingsAppearance Appearance) : AccountSettingsAction;

    /// <summary>セッション終了時のiCloud自動送信設定変更を通知します。</summary>
    /// <param name="IsEnabled">自動送信する場合は <c>true</c>。</param>
    public sealed record AutomaticSessionUploadChanged(bool IsEnabled) : AccountSettingsAction;
}
