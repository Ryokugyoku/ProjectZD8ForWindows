using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>デモ終端と実終端を対応するPID取得境界へ振り分けます。</summary>
public sealed class DemoAwareOBDPIDTelemetryAdapter : IOBDPIDTelemetryPort
{
    /// <summary>実OBD通信を行うPID取得境界です。</summary>
    private readonly IOBDPIDTelemetryPort live;

    /// <summary>物理通信なしで応答を返すPID取得境界です。</summary>
    private readonly IOBDPIDTelemetryPort demo;

    /// <summary>実通信境界とデモ境界を保持して生成します。</summary>
    /// <remarks>責務: 2種類のPID取得境界を終端識別子による振分けへ固定します。</remarks>
    /// <param name="live">実終端を処理するPID取得境界。</param>
    /// <param name="demo">デモ終端を処理するPID取得境界。</param>
    public DemoAwareOBDPIDTelemetryAdapter(IOBDPIDTelemetryPort live, IOBDPIDTelemetryPort demo)
    {
        this.live = live;
        this.demo = demo;
    }

    /// <summary>終端識別子に対応する要求単位のTransport観測境界を実行します。</summary>
    /// <remarks>責務: 1件のPID要求群をデモまたは実車のtyped観測処理へ振り分けます。</remarks>
    /// <param name="requests">読み取るService/PID要求。</param>
    /// <param name="endpoint">OBDアダプターの接続終端。</param>
    /// <returns>選択した境界が直接確認した要求単位の結果。</returns>
    /// <exception cref="Exception">選択した取得境界の要求送信前エラー。</exception>
    public Task<IReadOnlyList<OBDPIDRequestTransportObservation>> ReadObservations(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) =>
        DemoOBDAdapter.Matches(endpoint)
            ? demo.ReadObservations(requests, endpoint)
            : live.ReadObservations(requests, endpoint);

    /// <summary>終端識別子に対応するPID取得境界を実行します。</summary>
    /// <remarks>責務: 1件のPID読取要求群をデモまたは実車の取得処理へ振り分けます。</remarks>
    /// <param name="requests">読み取るService/PID要求。</param>
    /// <param name="endpoint">OBDアダプターの接続終端。</param>
    /// <returns>選択した境界が返した未加工応答バイト。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint) =>
        DemoOBDAdapter.Matches(endpoint) ? demo.Read(requests, endpoint) : live.Read(requests, endpoint);

    /// <summary>実終端だけを車種専用PID取得境界へ振り分けます。</summary>
    /// <remarks>責務: 1件の専用PID要求をデモ非対応または実OBD取得へ振り分けます。</remarks>
    /// <param name="definitions">型式適用済みの専用PID定義。</param>
    /// <param name="endpoint">OBDアダプターの接続終端。</param>
    /// <returns>実通信境界が返した未加工応答バイト。</returns>
    /// <exception cref="OBDPIDTelemetryException">デモ終端では <c>UnsupportedPID</c>。実終端では注入先のエラー。</exception>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> ReadVehicleSpecific(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint) =>
        DemoOBDAdapter.Matches(endpoint)
            ? Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
                new OBDPIDTelemetryException(OBDPIDTelemetryError.UnsupportedPID))
            : live.ReadVehicleSpecific(definitions, endpoint);

    /// <summary>デモと実通信の両PID取得セッションを終了します。</summary>
    /// <remarks>責務: 保持する2件のPID取得境界へ終了要求を伝播します。</remarks>
    /// <returns>終了処理の完了。</returns>
    public async Task EndSession()
    {
        await demo.EndSession();
        await live.EndSession();
    }
}
