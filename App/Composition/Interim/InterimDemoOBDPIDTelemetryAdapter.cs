using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】iOS 版の <c>DemoOBDPIDTelemetryAdapter</c> を写した、もっともらしい PID 応答を返すデモ用の通信です。
/// </summary>
/// <remarks>
/// デモアダプター以外への要求は「利用できない」で失敗させます（実機の通信はデバイス担当の Data 層で作る）。
/// 値の作り方は iOS 版と同じです。UI スレッドから呼ばれる前提です。
/// </remarks>
public sealed class InterimDemoOBDPIDTelemetryAdapter : IOBDPIDTelemetryPort
{
    /// <summary>対応 PID のビットマップに載せる PID 番号です。</summary>
    private readonly IReadOnlySet<byte> _supportedPIDs = InterimStandardOBDPIDSeed.Definitions
        .Where(definition => definition.IsDecodable)
        .Select(definition => definition.Pid)
        .ToHashSet();

    /// <summary>読み取り回数（値を少しずつ動かすため）です。</summary>
    private ulong _tick;

    /// <summary>デモの PID 応答を返します。</summary>
    /// <remarks>責務: Service 01 の要求ごとに、走行を模した応答バイトを作ります。</remarks>
    /// <param name="requests">要求。</param>
    /// <param name="endpoint">接続先。</param>
    /// <returns>要求ごとの応答バイト。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> Read(
        IReadOnlyList<OBDPIDRequest> requests,
        OBDConnectionEndpoint endpoint)
    {
        if (!InterimDemoOBDAdapter.Matches(endpoint))
        {
            return Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
                new OBDPIDTelemetryException(OBDPIDTelemetryError.Unavailable));
        }

        _tick = unchecked(_tick + 1);
        var phase = (_tick % 160) / 160.0;
        var speed = (byte)Math.Clamp((int)Math.Round(Math.Sin(phase * Math.PI) * 92), 0, 255);
        var rpm = Math.Max(760, speed * 38 + 820 + (int)(_tick % 9) * 12);
        var coolant = (int)Math.Min(92, 72 + (long)(_tick / 12));
        var values = new Dictionary<OBDPIDRequest, IReadOnlyList<byte>>();
        foreach (var request in requests.Where(request => request.Service == 0x01))
        {
            if (ResponseBytes(request.Pid, speed, rpm, coolant) is { } bytes)
            {
                values[request] = bytes;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(values);
    }

    /// <summary>デモでは車種専用 PID を扱いません。</summary>
    /// <remarks>責務: iOS 版と同じく未対応の PID として失敗させます。</remarks>
    /// <param name="definitions">車種専用 PID の定義。</param>
    /// <param name="endpoint">接続先。</param>
    /// <returns>常に失敗するタスク。</returns>
    public Task<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>> ReadVehicleSpecific(
        IReadOnlyList<OBDPIDDefinition> definitions,
        OBDConnectionEndpoint endpoint) =>
        Task.FromException<IReadOnlyDictionary<OBDPIDRequest, IReadOnlyList<byte>>>(
            new OBDPIDTelemetryException(OBDPIDTelemetryError.UnsupportedPID));

    /// <summary>1 件の PID の応答バイトを作ります。</summary>
    /// <remarks>責務: PID 番号と走行状態から、iOS 版と同じ応答バイトを作ります。</remarks>
    /// <param name="pid">PID 番号。</param>
    /// <param name="speed">車速。</param>
    /// <param name="rpm">回転数。</param>
    /// <param name="coolant">冷却水温。</param>
    /// <returns>応答バイト。応答しない PID なら null。</returns>
    private byte[]? ResponseBytes(byte pid, byte speed, int rpm, int coolant)
    {
        if (pid % 0x20 == 0)
        {
            return SupportedPIDBitmap(pid);
        }

        return pid switch
        {
            0x04 => [Clamp(55 + speed)],
            0x05 => [Clamp(coolant + 40)],
            >= 0x06 and <= 0x09 => [128],
            0x0A => [100],
            0x0B => [42],
            0x0C => TwoBytes(rpm * 4),
            0x0D => [speed],
            0x0E => [148],
            0x0F => [70],
            0x10 => TwoBytes(1_200 + speed * 18),
            0x11 => [Clamp(42 + speed)],
            >= 0x14 and <= 0x1B => [140, 128],
            0x1F => TwoBytes((int)(_tick % 3_600)),
            0x21 => [0, 0],
            0x2C => [77],
            0x2F => [166],
            0x31 => TwoBytes(120),
            0x33 => [100],
            >= 0x3C and <= 0x3F => TwoBytes(4_900),
            0x42 => TwoBytes(14_200),
            0x43 => TwoBytes(64 + speed * 2),
            0x45 or 0x47 or 0x48 => [Clamp(40 + speed)],
            0x46 => [65],
            >= 0x49 and <= 0x4C => [Clamp(35 + speed)],
            0x5C => [120],
            0x5E => [0x00, 0xC8],
            0x61 or 0x62 => [125],
            0xA6 => FourBytes(123_456 + _tick / 10),
            _ => null,
        };
    }

    /// <summary>対応 PID のビットマップを作ります。</summary>
    /// <remarks>責務: 基準 PID の次の 32 件について、対応する PID のビットを立てます。</remarks>
    /// <param name="basePid">基準 PID（0x00、0x20 …）。</param>
    /// <returns>4 バイトのビットマップ。</returns>
    private byte[] SupportedPIDBitmap(byte basePid)
    {
        var bytes = new byte[4];
        for (var offset = 1; offset <= 32; offset++)
        {
            var value = basePid + offset;
            var hasPID = value <= 0xFF && _supportedPIDs.Contains((byte)value);
            var hasLaterRange = offset == 32 && _supportedPIDs.Any(pid => pid > basePid + 32);
            if (!hasPID && !hasLaterRange)
            {
                continue;
            }

            var zeroBased = offset - 1;
            bytes[zeroBased / 8] |= (byte)(0x80 >> (zeroBased % 8));
        }

        return bytes;
    }

    /// <summary>0〜255 に収めたバイトを返します。</summary>
    /// <remarks>責務: 整数を 1 バイトへ飽和変換します。</remarks>
    /// <param name="value">値。</param>
    /// <returns>バイト。</returns>
    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    /// <summary>2 バイトのビッグエンディアンを返します。</summary>
    /// <remarks>責務: 整数を 0〜65535 に収めて 2 バイトへ変換します。</remarks>
    /// <param name="value">値。</param>
    /// <returns>2 バイト。</returns>
    private static byte[] TwoBytes(int value)
    {
        var encoded = (ushort)Math.Clamp(value, 0, ushort.MaxValue);
        return [(byte)(encoded >> 8), (byte)(encoded & 0xFF)];
    }

    /// <summary>4 バイトのビッグエンディアンを返します。</summary>
    /// <remarks>責務: 整数を 32 ビットに収めて 4 バイトへ変換します。</remarks>
    /// <param name="value">値。</param>
    /// <returns>4 バイト。</returns>
    private static byte[] FourBytes(ulong value)
    {
        var encoded = (uint)Math.Min(value, uint.MaxValue);
        return [(byte)(encoded >> 24), (byte)((encoded >> 16) & 0xFF), (byte)((encoded >> 8) & 0xFF), (byte)(encoded & 0xFF)];
    }
}
