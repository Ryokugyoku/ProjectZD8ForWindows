using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>ELM/STNの単一Service 01応答からPIDデータバイトを抽出します。</summary>
public sealed class ELM327PIDResponseParser
{
    /// <summary>正応答として扱わない既知の拒否文字列です。</summary>
    private static readonly string[] Rejected = ["?", "NO DATA", "UNABLE TO CONNECT", "BUS ERROR", "CAN ERROR", "STOPPED"];

    /// <summary>指定Service/PIDの正応答からデータバイトを取り出します。</summary>
    /// <remarks>責務: 1件のELM応答を要求と一致する正応答ペイロードへ変換します。</remarks>
    /// <param name="response">プロンプトを除いた加工前応答。</param>
    /// <param name="request">期待するService/PID。</param>
    /// <returns>ServiceとPIDを除いたデータバイト。</returns>
    /// <exception cref="OBDPIDTelemetryException">拒否応答、正応答不在、または空ペイロードの場合。</exception>
    public IReadOnlyList<byte> Parse(string response, OBDPIDRequest request)
    {
        var normalized = response.ToUpperInvariant();
        if (Rejected.Any(value => normalized.Contains(value, StringComparison.Ordinal)))
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.CommandRejected);
        }

        var bytes = ELM327ResponseText.SplitLines(response)
            .Where(line => !ELM327ResponseText.ContainsSearching(line))
            .SelectMany(HexBytes)
            .ToArray();
        var responseService = (byte)(request.Service | 0x40);
        var start = -1;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index + 1 < bytes.Length && bytes[index] == responseService && bytes[index + 1] == request.Pid)
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.MalformedResponse);
        }

        var payload = bytes[(start + 2)..];
        if (payload.Length == 0)
        {
            throw new OBDPIDTelemetryException(OBDPIDTelemetryError.MalformedResponse);
        }

        return payload;
    }

    /// <summary>1行の空白区切り16進トークンをバイトへ変換します。</summary>
    /// <remarks>責務: ELM応答行の2桁16進トークンだけを順序通り取り出します。</remarks>
    /// <param name="line">解析する1行。</param>
    /// <returns>解析できたバイト列。</returns>
    private static IEnumerable<byte> HexBytes(string line)
    {
        foreach (var token in ELM327ResponseText.WhitespaceSeparatedTokens(line))
        {
            if (token.Length == 2 && ELM327ResponseText.TryParseHexByte(token, out var value))
            {
                yield return value;
            }
        }
    }
}
