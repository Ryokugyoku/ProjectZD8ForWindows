namespace ProjectZD8.Application.Maintenance.Ports;

/// <summary>ユーザーが選択した写真を整備エビデンス用データへ読み込む能力です。</summary>
public interface IMaintenancePhotoImportPort
{
    /// <summary>指定URLの画像データを読み込みます。</summary>
    /// <remarks>責務: 1件の選択ファイルを整備写真へ保存可能なデータへ変換します。</remarks>
    /// <param name="url">Platformのファイル選択で得たURL。</param>
    /// <returns>読み込んだ画像データ。</returns>
    /// <exception cref="Exception">アクセスまたは読込に失敗した場合のエラー。</exception>
    Task<byte[]> ImportPhoto(Uri url);
}
