namespace ProjectZD8.Application.VehicleManagement.Ports;

/// <summary>Platformが選択した画像URLを同期可能なプロフィールデータへ変換する能力です。</summary>
public interface IVehiclePhotoImportPort
{
    /// <summary>指定URLの画像をプロフィール用データとして読み込みます。</summary>
    /// <remarks>責務: ユーザーが選択した画像1件を永続化可能なデータへ変換します。</remarks>
    /// <param name="url">Platformのファイル選択結果URL。</param>
    /// <returns>読み込みと容量検証を完了した画像データ。</returns>
    Task<byte[]> ImportPhoto(Uri url);
}
