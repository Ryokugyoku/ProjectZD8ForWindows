using ProjectZD8.Application.Authentication.State;

namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>Appleアカウントの認証と資格状態確認を抽象化します。</summary>
public interface IAppleAccountAuthorizationPort
{
    /// <summary>Appleのシステム認証を実行してアプリ固有セッションを返します。</summary>
    /// <remarks>責務: 1回のAppleアカウント認証要求をシステム認証境界へ渡します。</remarks>
    /// <returns>Appleが発行したアプリ固有ユーザー識別子を持つセッション。</returns>
    /// <exception cref="AppleAccountAuthorizationException">キャンセルまたは認証を完了できない場合。</exception>
    Task<AppleAccountSession> Authorize();

    /// <summary>保存済みAppleユーザー識別子の現在の資格状態を取得します。</summary>
    /// <remarks>責務: 1件のAppleユーザー識別子を現在のシステム資格状態へ照合します。</remarks>
    /// <param name="userIdentifier">以前のApple認証で保存したアプリ固有ユーザー識別子。</param>
    /// <returns>Appleが返した現在の資格状態。</returns>
    /// <exception cref="AppleAccountAuthorizationException">資格状態を取得できない場合。</exception>
    Task<AppleAccountCredentialState> CredentialState(string userIdentifier);
}
