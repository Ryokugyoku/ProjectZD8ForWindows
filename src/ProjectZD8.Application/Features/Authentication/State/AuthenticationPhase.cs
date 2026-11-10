namespace ProjectZD8.Application.Authentication.State;

/// <summary>アプリの認証入口が取り得る進行段階です。</summary>
public enum AuthenticationPhase
{
    /// <summary>保存済みセッションの資格状態を確認しています。</summary>
    CheckingSession,

    /// <summary>Appleアカウントでのログイン操作を待っています。</summary>
    SignedOut,

    /// <summary>Appleの認証結果を待っています。</summary>
    SigningIn,

    /// <summary>有効なAppleアカウントセッションが確認されています。</summary>
    SignedIn,
}
