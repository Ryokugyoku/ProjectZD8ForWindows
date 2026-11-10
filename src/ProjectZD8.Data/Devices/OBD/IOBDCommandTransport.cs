namespace ProjectZD8.Data.Devices.OBD;

/// <summary>ELM/STN系アダプターとのバイトストリーム通信能力です。</summary>
/// <remarks>
/// iOS 版の <c>OBDCommandTransport</c> に対応します。失敗は
/// <see cref="ProjectZD8.Application.VehicleManagement.Ports.VehicleIdentificationException"/> で通知します。
/// </remarks>
public interface IOBDCommandTransport
{
    /// <summary>物理終端を開きます。</summary>
    /// <remarks>責務: 1件のOBDアダプター物理終端を読書き可能状態へ遷移させます。</remarks>
    /// <exception cref="ProjectZD8.Application.VehicleManagement.Ports.VehicleIdentificationException">終端を開けない場合は型付き識別エラー。</exception>
    Task Open();

    /// <summary>1件の復帰文字終端コマンドを書き込みます。</summary>
    /// <remarks>責務: 1件の完全なホスト対アダプターコマンドをバイトストリームへ送ります。</remarks>
    /// <param name="data">復帰文字を含む完全なASCIIコマンド。</param>
    /// <exception cref="ProjectZD8.Application.VehicleManagement.Ports.VehicleIdentificationException">全バイトを書き込めない場合の通信エラー。</exception>
    Task Write(byte[] data);

    /// <summary>ELM/STNプロンプトまでの応答を読み取ります。</summary>
    /// <remarks>責務: 現在要求の応答を次の <c>&gt;</c> プロンプトまで境界を保って返します。</remarks>
    /// <returns>プロンプトを含む加工前応答バイト。</returns>
    /// <exception cref="ProjectZD8.Application.VehicleManagement.Ports.VehicleIdentificationException">期限切れ、切断、読取失敗の場合の通信エラー。</exception>
    Task<byte[]> ReadUntilPrompt();

    /// <summary>物理終端を閉じます。</summary>
    /// <remarks>責務: 現在のアダプターバイトストリームを再利用せず終了します。</remarks>
    Task Close();
}
