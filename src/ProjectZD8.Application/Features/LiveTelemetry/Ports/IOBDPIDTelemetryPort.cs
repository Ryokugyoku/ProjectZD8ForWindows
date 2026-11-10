using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LiveTelemetry.Ports;

/// <summary>1回のOBD接続で指定PIDの未加工データを読み取る能力です。</summary>
/// <remarks>
/// Swift の protocol extension による既定実装は、C# の既定インターフェイスメソッドで表します。
/// 既定実装は <see cref="IOBDPIDTelemetryPort"/> 型を通して呼び出したときに使われます。
/// </remarks>
public interface IOBDPIDTelemetryPort
{
    /// <summary>複数要求を順番に実行し、送信後の結果を要求単位で返します。</summary>
    /// <remarks>
    /// 責務: PID要求群を推測を含まないTransport観測列へ変換します。
    /// 既定実装（typed観測未実装の境界向け）は、辞書型の既存実装 <see cref="Read"/> から正応答だけを要求単位へ変換し、
    /// 既存境界がpayloadを返した要求だけの入力順観測列を返します。
    /// </remarks>
    /// <param name="requests">読み取るService/PID要求。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <returns>実際に送信開始した要求ごとのTransport観測。</returns>
    /// <exception cref="Exception">要求送信前の定義、接続、または初期化失敗。既定実装では既存の一括読取が送出したエラー。</exception>
    async Task<IReadOnlyList<OBDPIDRequestTransportObservation>> ReadObservations(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        var responses = await Read(requests, endpoint);
        var observations = new List<OBDPIDRequestTransportObservation>();
        foreach (var request in requests)
        {
            if (responses.TryGetValue(request, out var payload))
            {
                observations.Add(new OBDPIDRequestTransportObservation(
                    request,
                    new OBDPIDRequestTransportOutcome.Responded(payload)));
            }
        }

        return observations;
    }

    /// <summary>複数の読取要求を1回の物理接続で実行します。</summary>
    /// <remarks>責務: 許可済みService/PID要求群を未加工応答バイトへ変換します。</remarks>
    /// <param name="requests">読み取るService/PID要求。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <returns>要求ごとの未加工データバイト。</returns>
    /// <exception cref="OBDPIDTelemetryException">非対応PID、接続、拒否応答、または形式不正の場合のエラー。</exception>
    Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint);

    /// <summary>車両型式と送信ヘッダーが確認済みの専用PIDを読み取ります。</summary>
    /// <remarks>
    /// 責務: 型式適用済みPID定義群を物理ECU指定の未加工応答へ変換します。
    /// 既定実装（専用PID通信を実装しない境界向け）は、任意実装の車種専用PID要求を安全な非対応結果へ変換し、
    /// 常に <see cref="OBDPIDTelemetryError.UnsupportedPID"/> を送出します。
    /// </remarks>
    /// <param name="definitions">型式と11bit送信ヘッダーを持つ専用PID定義。</param>
    /// <param name="endpoint">OBDアダプターの物理終端。</param>
    /// <returns>応答が確認できたService/PIDごとの未加工データバイト。</returns>
    /// <exception cref="OBDPIDTelemetryException">ヘッダー不正、非対応通信、拒否応答、または接続失敗の場合のエラー。既定実装では常に <see cref="OBDPIDTelemetryError.UnsupportedPID"/>。</exception>
    Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> ReadVehicleSpecific(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint) =>
        Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            new OBDPIDTelemetryException(OBDPIDTelemetryError.UnsupportedPID));

    /// <summary>現在の継続取得セッションを終了します。</summary>
    /// <remarks>
    /// 責務: PID取得境界が保持する接続資源を再利用不能な終了状態へ遷移させます。
    /// 既定実装（保持資源がない取得境界向け）は、終了要求を変更なしで完了します。
    /// </remarks>
    /// <returns>終了処理の完了。</returns>
    Task EndSession() => Task.CompletedTask;
}
