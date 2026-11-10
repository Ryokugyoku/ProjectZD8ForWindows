using System.Globalization;
using System.Text;
using ProjectZD8.Application.VehicleManagement.Ports;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>ELM/STNが整形したService 09 PID 02応答から車両識別子を復元します。</summary>
public sealed class ELM327VehicleIdentifierParser
{
    /// <summary>VINと非VINのOBD由来識別子を分離した解析結果です。</summary>
    /// <param name="Vin">17バイトの表示可能文字として取得できたVIN候補です。</param>
    /// <param name="ObdIdentifier">空白またはNULL埋めを除いた非VINのOBD由来識別子です。</param>
    public sealed record Result(string? Vin, string? ObdIdentifier);

    /// <summary>複数プロトコル形式を保った応答から車両識別子を取り出します。</summary>
    /// <remarks>責務: 1件のMode 09応答をVINまたは非VINのOBD由来識別子へ復元します。</remarks>
    /// <param name="response">プロンプトを除いた加工前ELM/STN応答。</param>
    /// <returns>VINと非VIN識別子を混同しない解析結果。</returns>
    /// <exception cref="VehicleIdentificationException">正応答、順序、またはASCII識別子を確認できない場合は <c>MalformedResponse</c>。</exception>
    public Result Parse(string response)
    {
        var lines = ELM327ResponseText.SplitLines(response)
            .Select(ELM327ResponseText.TrimWhitespaceAndNewlines)
            .Where(line => line.Length > 0 && !ELM327ResponseText.ContainsSearching(line))
            .ToArray();
        return ParseCANFormatted(lines)
            ?? ParseLegacyFrames(lines)
            ?? throw new VehicleIdentificationException(new VehicleIdentificationError.MalformedResponse());
    }

    /// <summary>CAN自動整形済みの連続行を解析します。</summary>
    /// <remarks>責務: CAN連続フレームの表示順序からMode 09ペイロードを連結します。</remarks>
    /// <param name="lines">空行を除いた応答行。</param>
    /// <returns>復元できた識別子。対象形式でなければ <c>null</c>。</returns>
    private static Result? ParseCANFormatted(IReadOnlyList<string> lines)
    {
        var ordered = new List<(int Sequence, byte[] Bytes)>();
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0
                || !int.TryParse(line.AsSpan(0, colon), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var sequence))
            {
                continue;
            }

            ordered.Add((sequence, Bytes(line[(colon + 1)..])));
        }

        var sorted = ordered.OrderBy(frame => frame.Sequence).ToArray();
        if (sorted.Length == 0 || sorted[0].Sequence != 0)
        {
            return null;
        }

        return DecodeIdentifier(sorted.SelectMany(frame => frame.Bytes).ToArray());
    }

    /// <summary>J1850等の行番号付きMode 09応答を解析します。</summary>
    /// <remarks>責務: 各行の49 02応答と順序バイトから識別子領域を並べ替えます。</remarks>
    /// <param name="lines">空行を除いた応答行。</param>
    /// <returns>復元できた識別子。対象形式でなければ <c>null</c>。</returns>
    private static Result? ParseLegacyFrames(IReadOnlyList<string> lines)
    {
        var frames = new List<(int Sequence, byte[] Bytes)>();
        foreach (var line in lines)
        {
            var values = Bytes(line);
            if (values.Length >= 4 && values[0] == 0x49 && values[1] == 0x02)
            {
                frames.Add((values[2], values[3..]));
            }
        }

        if (frames.Count > 0)
        {
            return DecodeIdentifierPayload(frames.OrderBy(frame => frame.Sequence).SelectMany(frame => frame.Bytes).ToArray());
        }

        return DecodeIdentifier(lines.SelectMany(Bytes).ToArray());
    }

    /// <summary>Service/PID/項目数を含む連結ペイロードから識別子領域を取り出します。</summary>
    /// <remarks>責務: 49 02 01正応答の直後17バイトだけを識別子候補へ渡します。</remarks>
    /// <param name="bytes">連結された応答データ。</param>
    /// <returns>正応答から復元できた識別子。</returns>
    private static Result? DecodeIdentifier(byte[] bytes)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index + 2 < bytes.Length && bytes[index] == 0x49 && bytes[index + 1] == 0x02 && bytes[index + 2] == 0x01)
            {
                return DecodeIdentifierPayload(bytes[(index + 3)..]);
            }
        }

        return null;
    }

    /// <summary>17バイトの識別子領域をVINまたは非VIN識別子に分類します。</summary>
    /// <remarks>責務: 文字埋めの有無だけでVIN候補と非VINのOBD由来識別子を分離します。</remarks>
    /// <param name="payload">Service/PID/項目数を除いたデータ。</param>
    /// <returns>ASCIIとして復元できた識別子。形式不正なら <c>null</c>。</returns>
    private static Result? DecodeIdentifierPayload(byte[] payload)
    {
        var field = payload.Take(17).ToArray();
        if (field.Length != 17)
        {
            return null;
        }

        if (field.All(IsPrintable))
        {
            return new Result(Encoding.ASCII.GetString(field), null);
        }

        var start = 0;
        while (start < field.Length && IsPadding(field[start]))
        {
            start++;
        }

        var end = field.Length;
        while (end > start && IsPadding(field[end - 1]))
        {
            end--;
        }

        var trimmed = field[start..end];
        if (trimmed.Length == 0 || !trimmed.All(IsPrintable))
        {
            return null;
        }

        return new Result(null, Encoding.ASCII.GetString(trimmed));
    }

    /// <summary>識別子として表示可能なASCII文字かを返します。</summary>
    /// <remarks>責務: 1バイトを 0x21〜0x7E の範囲と照合します。</remarks>
    /// <param name="value">判定するバイト。</param>
    /// <returns>表示可能な場合は <c>true</c>。</returns>
    private static bool IsPrintable(byte value) => value is >= 0x21 and <= 0x7E;

    /// <summary>識別子の埋め文字かを返します。</summary>
    /// <remarks>責務: 1バイトをNULLまたは空白と照合します。</remarks>
    /// <param name="value">判定するバイト。</param>
    /// <returns>埋め文字の場合は <c>true</c>。</returns>
    private static bool IsPadding(byte value) => value is 0x00 or 0x20;

    /// <summary>行内の16進数2桁トークンをバイトへ変換します。</summary>
    /// <remarks>責務: 1件のELM表示行から解釈可能な16進バイトだけを順序通り抽出します。</remarks>
    /// <param name="line">空白ありまたは連続16進形式の行。</param>
    /// <returns>抽出できたバイト列。</returns>
    private static byte[] Bytes(string line)
    {
        var hex = new string(line.Where(char.IsAsciiHexDigit).ToArray());
        if (hex.Length % 2 != 0)
        {
            return [];
        }

        var result = new List<byte>(hex.Length / 2);
        for (var index = 0; index < hex.Length; index += 2)
        {
            if (ELM327ResponseText.TryParseHexByte(hex.AsSpan(index, 2), out var value))
            {
                result.Add(value);
            }
        }

        return result.ToArray();
    }
}
