using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Devices.OBD;

/// <summary>実走行に近い相関を持つ決定的なデモPID応答を生成します。</summary>
/// <remarks>
/// iOS 版は対応PIDビットマップを <c>StandardOBDPIDSeed.definitions</c> から直接作ります。
/// Windows 版では seed が永続化担当のフォルダーにあるため、組み立てが同じ定義一覧をコンストラクターで渡します。
/// UI スレッドから呼ぶ前提です。
/// </remarks>
public sealed class DemoOBDPIDTelemetryAdapter : IOBDPIDTelemetryPort
{
    /// <summary>数値化可能PIDの番号集合です。</summary>
    private readonly HashSet<byte> supportedPIDs;

    /// <summary>連続する観測値を進行させる更新番号です。</summary>
    private ulong tick;

    /// <summary>空のデモ走行状態を生成します。</summary>
    /// <remarks>責務: デモPID応答の更新番号を初期値へ設定し、対応PIDビットマップの元になる定義を固定します。</remarks>
    /// <param name="standardDefinitions">標準PID定義一覧（iOS の <c>StandardOBDPIDSeed.definitions</c> と同じもの）。</param>
    public DemoOBDPIDTelemetryAdapter(IReadOnlyList<OBDPIDDefinition> standardDefinitions)
    {
        supportedPIDs = standardDefinitions.Where(definition => definition.IsDecodable).Select(definition => definition.Pid).ToHashSet();
    }

    /// <summary>指定PID群へ物理通信なしの応答バイトを返します。</summary>
    /// <remarks>責務: 1回のデモ読取要求を相関した走行状態の未加工PIDバイトへ変換します。</remarks>
    /// <param name="requests">PID定義DBから選ばれたService/PID要求。</param>
    /// <param name="endpoint">デモOBD接続終端。</param>
    /// <returns>要求ごとの決定的な未加工応答バイト。</returns>
    /// <exception cref="OBDPIDTelemetryException">デモ以外の終端では <c>Unavailable</c>。</exception>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        if (!DemoOBDAdapter.Matches(endpoint))
        {
            return Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
                new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable));
        }

        unchecked
        {
            tick += 1;
        }

        var phase = (double)(tick % 160) / 160.0;
        var speed = ClampToByte((long)Math.Round(Math.Sin(phase * Math.PI) * 92, MidpointRounding.AwayFromZero));
        var rpm = Math.Max(760, speed * 38 + 820 + (int)(tick % 9) * 12);
        var coolant = (int)Math.Min(92UL, 72 + tick / 12);
        var values = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        foreach (var request in requests.Where(request => request.Service == 0x01))
        {
            if (ResponseBytes(request.Pid, speed, rpm, coolant) is { } bytes)
            {
                values[request] = bytes;
            }
            else
            {
                values.Remove(request);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(values);
    }

    /// <summary>デモ走行状態を指定PIDの正常域バイトへ符号化します。</summary>
    /// <remarks>責務: 1件の主要Service 01 PIDを決定的な合成応答バイトへ変換します。</remarks>
    /// <param name="pid">応答するService 01 PID番号。</param>
    /// <param name="speed">現在の合成車速。</param>
    /// <param name="rpm">現在の合成エンジン回転数。</param>
    /// <param name="coolant">現在の合成冷却水温。</param>
    /// <returns>登録済み主要PIDでは正常域の応答バイト、未登録PIDでは <c>null</c>。</returns>
    private byte[]? ResponseBytes(byte pid, byte speed, int rpm, int coolant)
    {
        if (pid % 0x20 == 0)
        {
            return SupportedPIDBitmap(pid);
        }

        return pid switch
        {
            0x04 => [ClampToByte(55 + speed)],
            0x05 => [ClampToByte(coolant + 40)],
            >= 0x06 and <= 0x09 => [128],
            0x0A => [100],
            0x0B => [42],
            0x0C => TwoBytes(rpm * 4),
            0x0D => [speed],
            0x0E => [148],
            0x0F => [70],
            0x10 => TwoBytes(1_200 + speed * 18),
            0x11 => [ClampToByte(42 + speed)],
            >= 0x14 and <= 0x1B => [140, 128],
            0x1F => TwoBytes((int)(tick % 3_600)),
            0x21 => [0, 0],
            0x22 => TwoBytes(3_797),
            0x23 => TwoBytes(400),
            >= 0x24 and <= 0x2B => [0x80, 0x00, 0x00, 0x00],
            0x2C => [77],
            0x2D => [128],
            0x2E => [38],
            0x2F => [166],
            0x30 => [2],
            0x31 => TwoBytes(120),
            0x32 => [0x80, 0x00],
            0x33 => [100],
            >= 0x34 and <= 0x3B => [0x80, 0x00, 0x00, 0x00],
            >= 0x3C and <= 0x3F => TwoBytes(4_900),
            0x42 => TwoBytes(14_200),
            0x43 => TwoBytes(64 + speed * 2),
            0x44 => [0x80, 0x00],
            0x45 or 0x47 or 0x48 => [ClampToByte(40 + speed)],
            0x46 => [65],
            >= 0x49 and <= 0x4C => [ClampToByte(35 + speed)],
            0x4D => [0, 0],
            0x4E => [0, 0],
            0x50 => [20],
            0x52 => [128],
            0x53 => [0x27, 0x10],
            0x54 => [0x7F, 0xFF],
            0x59 => [0x01, 0x90],
            0x5A or 0x5B => [128],
            0x5C => [120],
            0x5D => [0x6E, 0x00],
            0x5E => [0x00, 0xC8],
            0x61 or 0x62 => [125],
            0x63 => [0x01, 0x90],
            0x7C => [0x11, 0x30],
            0x8D => [128],
            0x8E => [125],
            0xA2 => [0x01, 0x00],
            0xA4 => [0x00, 0x00, 0x03, 0xE8],
            0xA5 => [0x00, 0x80],
            0xA6 => FourBytes(123_456 + tick / 10),
            _ => null,
        };
    }

    /// <summary>デモが数値化して返せるPIDをService 01対応ビットマップへ符号化します。</summary>
    /// <remarks>責務: 1件の0x20刻み範囲先頭を4バイトの対応PIDビットマップへ変換します。</remarks>
    /// <param name="baseValue">00、20、40以降の範囲先頭PID。</param>
    /// <returns>範囲内の数値化可能PIDと後続範囲有無を示す4バイト。</returns>
    private byte[] SupportedPIDBitmap(byte baseValue)
    {
        var bytes = new byte[4];
        for (var offset = 1; offset <= 32; offset++)
        {
            var value = baseValue + offset;
            var hasPID = value <= 0xFF && supportedPIDs.Contains((byte)value);
            var hasLaterRange = offset == 32 && supportedPIDs.Any(pid => pid > baseValue + 32);
            if (!hasPID && !hasLaterRange)
            {
                continue;
            }

            var zeroBased = offset - 1;
            bytes[zeroBased / 8] |= (byte)(0x80 >> (zeroBased % 8));
        }

        return bytes;
    }

    /// <summary>整数を0〜255へ制限して1バイトにします。</summary>
    /// <remarks>責務: Swift の <c>UInt8(clamping:)</c> と同じ変換をします。</remarks>
    /// <param name="value">変換する整数。</param>
    /// <returns>範囲内へ制限したバイト。</returns>
    private static byte ClampToByte(long value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>非負整数を上位バイト優先の2バイトへ符号化します。</summary>
    /// <remarks>責務: 1件のデモ用整数をUInt16範囲へ制限した未加工バイトへ変換します。</remarks>
    /// <param name="value">符号化する非負整数。</param>
    /// <returns>上位、下位の順に並ぶ2バイト。</returns>
    private static byte[] TwoBytes(int value)
    {
        var encoded = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        return [(byte)(encoded >> 8), (byte)(encoded & 0xFF)];
    }

    /// <summary>非負整数を上位バイト優先の4バイトへ符号化します。</summary>
    /// <remarks>責務: 1件のデモ用累積値をUInt32範囲へ制限した未加工バイトへ変換します。</remarks>
    /// <param name="value">符号化する非負整数。</param>
    /// <returns>上位から下位の順に並ぶ4バイト。</returns>
    private static byte[] FourBytes(ulong value)
    {
        var encoded = (uint)Math.Min(value, uint.MaxValue);
        return
        [
            (byte)(encoded >> 24),
            (byte)((encoded >> 16) & 0xFF),
            (byte)((encoded >> 8) & 0xFF),
            (byte)(encoded & 0xFF),
        ];
    }
}
