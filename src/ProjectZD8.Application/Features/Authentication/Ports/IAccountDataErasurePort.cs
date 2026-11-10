namespace ProjectZD8.Application.Authentication.Ports;

/// <summary>認証済みアカウントに属するアプリ保存データの消去を抽象化します。</summary>
public interface IAccountDataErasurePort
{
    /// <summary>指定アカウントの運転データ、同期設定、端末設定を消去します。</summary>
    /// <remarks>責務: 1件のAppleユーザー識別子に対応するアプリ保存データを削除済み状態へします。</remarks>
    /// <param name="userIdentifier">Appleがこのアプリへ割り当てた空でないユーザー識別子。</param>
    /// <exception cref="Exception">ローカル運転データを完全に削除できない場合の保存先エラー。</exception>
    void EraseAllData(string userIdentifier);
}
