namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>アカウント別セッション失効の端末間同期を抽象化します。</summary>
public interface IAccountSessionRevocationPort
{
    /// <summary>新規ログイン時点の失効世代を現在セッションの基準として保存します。</summary>
    /// <remarks>責務: 新規認証セッションが受理した失効世代を端末内基準へ記録します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てたユーザー識別子。</param>
    void RegisterCurrentSession(string userIdentifier);

    /// <summary>現在アカウントの新しい失効世代を端末間同期へ発行します。</summary>
    /// <remarks>責務: 1件のアカウント削除を他端末が検出可能な新規失効世代へ変換します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てたユーザー識別子。</param>
    void PublishRevocation(string userIdentifier);

    /// <summary>指定アカウントについて現在基準より新しい失効世代を監視します。</summary>
    /// <remarks>責務: 1件のアカウントに届いた未受理の失効世代だけをコールバックへ通知します。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てたユーザー識別子。</param>
    /// <param name="receive">他端末の失効を検出したときに実行する処理。</param>
    void StartObserving(string userIdentifier, Action receive);

    /// <summary>現在のセッション失効監視を終了します。</summary>
    /// <remarks>責務: 登録済みの失効通知購読とコールバックを破棄します。</remarks>
    void StopObserving();
}
