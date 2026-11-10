using ProjectZD8.Application.Authentication.Ports;

namespace ProjectZD8.Application.Authentication.UseCases;

/// <summary>他端末から失効されたアカウントのローカル保存内容を消去します。</summary>
/// <remarks>UI スレッド（単一スレッド）から呼ぶ前提です（Swift 版の <c>@MainActor</c>）。</remarks>
public sealed class RemoteAccountLogoutUseCase
{
    /// <summary>アカウントに属するローカル設定を消去する境界です。</summary>
    private readonly IAccountDataErasurePort _dataEraser;

    /// <summary>端末内のログイン識別子を削除する安全な保存境界です。</summary>
    private readonly IAuthenticationSessionStorePort _sessionStore;

    /// <summary>データ消去境界と認証セッション保存境界を注入します。</summary>
    /// <remarks>責務: 他端末から受信した失効をローカルデータと認証識別子の削除へ結び付けます。</remarks>
    /// <param name="dataEraser">アカウントに属する端末内データの消去境界。</param>
    /// <param name="sessionStore">保存済みAppleユーザー識別子の削除境界。</param>
    public RemoteAccountLogoutUseCase(
        IAccountDataErasurePort dataEraser,
        IAuthenticationSessionStorePort sessionStore)
    {
        _dataEraser = dataEraser;
        _sessionStore = sessionStore;
    }

    /// <summary>指定アカウントの端末内データを消去してログイン識別子を削除します。</summary>
    /// <remarks>責務: 1件の遠隔失効を現在端末の再起動後も維持されるログアウトへ変換します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てたユーザー識別子。</param>
    /// <exception cref="AuthenticationSessionStoreException">ログイン識別子を安全な保存領域から削除できない場合。</exception>
    public void Execute(string userIdentifier)
    {
        _dataEraser.EraseAllData(userIdentifier);
        _sessionStore.RemoveUserIdentifier();
    }
}
