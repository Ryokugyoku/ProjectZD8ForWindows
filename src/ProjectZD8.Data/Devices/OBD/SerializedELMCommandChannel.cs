using System.Text;
using ProjectZD8.Application.VehicleManagement.Ports;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>1つのELM/STN接続で同時に1件だけコマンドを実行します。</summary>
/// <remarks>
/// Swift の <c>actor</c> に対応します。呼び出し側（UI スレッド）が1件ずつ await する前提で、
/// 応答境界の状態だけを保持します。
/// </remarks>
public sealed class SerializedELMCommandChannel
{
    /// <summary>不正なUTF-8を置換せず失敗として検出する符号化です。</summary>
    private static readonly UTF8Encoding StrictUTF8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>コマンドの物理送受信先です。</summary>
    private readonly IOBDCommandTransport transport;

    /// <summary>応答境界を保持できているかどうかです。</summary>
    private bool isBoundaryEstablished = true;

    /// <summary>物理Transportを保持して直列チャネルを生成します。</summary>
    /// <remarks>責務: 1件のTransportを単一inflightコマンド境界へ固定します。</remarks>
    /// <param name="transport">開かれたOBDバイトストリーム。</param>
    public SerializedELMCommandChannel(IOBDCommandTransport transport)
    {
        this.transport = transport;
    }

    /// <summary>型付きコマンドを送ってプロンプト終端応答を返します。</summary>
    /// <remarks>責務: 1件の許可済みコマンドを完全応答まで占有して実行します。</remarks>
    /// <param name="command">許可済みELM/STNコマンド。</param>
    /// <returns>プロンプトを除く加工前応答文字列。</returns>
    /// <exception cref="VehicleIdentificationException">境界喪失、送受信失敗、不正文字列の場合の識別エラー。</exception>
    public async Task<string> Execute(IELM327CommandEncoding command)
    {
        if (!isBoundaryEstablished)
        {
            throw new VehicleIdentificationException(new VehicleIdentificationError.ConnectionFailed());
        }

        try
        {
            await transport.Write(command.EncodedData);
            var data = await transport.ReadUntilPrompt();
            string response;
            try
            {
                response = StrictUTF8.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                throw new VehicleIdentificationException(new VehicleIdentificationError.MalformedResponse());
            }

            return response.Replace(">", string.Empty, StringComparison.Ordinal);
        }
        catch
        {
            isBoundaryEstablished = false;
            throw;
        }
    }
}
