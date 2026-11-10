using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>実車で確認したService 01応答形式の解析を検証します。</summary>
public sealed class ELM327PIDResponseParserTests
{
    /// <summary>SEARCHING行を除いて冷却水温バイトを取り出します。</summary>
    /// <remarks>責務: 実車観測形式 <c>41 05 85</c> からデータバイト1件を保持することを確認します。</remarks>
    [Fact]
    public void ParsesCoolantResponseAfterSearchingLine()
    {
        var bytes = new ELM327PIDResponseParser().Parse("SEARCHING...\r41 05 85\r", new OBDPIDRequest(0x01, 0x05));

        Assert.Equal([0x85], bytes);
    }

    /// <summary>回転数応答の2データバイトを保持します。</summary>
    /// <remarks>責務: 実車観測形式 <c>41 0C 00 00</c> をゼロ値の成功応答として解析することを確認します。</remarks>
    [Fact]
    public void ParsesZeroEngineSpeedResponse()
    {
        var bytes = new ELM327PIDResponseParser().Parse("41 0C 00 00\r", new OBDPIDRequest(0x01, 0x0C));

        Assert.Equal([0x00, 0x00], bytes);
    }

    /// <summary>異なるPIDの応答を求めたPIDの成功にしません。</summary>
    /// <remarks>責務: 要求と一致しない正応答が形式不正として拒否されることを確認します。</remarks>
    [Fact]
    public void RejectsResponseForDifferentPID()
    {
        var error = Assert.Throws<OBDPIDTelemetryException>(() =>
            new ELM327PIDResponseParser().Parse("41 0C 00 00\r", new OBDPIDRequest(0x01, 0x05)));

        Assert.Equal(OBDPIDTelemetryError.MalformedResponse, error.Reason);
    }

    /// <summary>既知の拒否文字列を含む応答を拒否応答にします。</summary>
    /// <remarks>責務: <c>NO DATA</c> が形式不正ではなくコマンド拒否として区別されることを確認します。</remarks>
    [Fact]
    public void RejectsNoDataResponse()
    {
        var error = Assert.Throws<OBDPIDTelemetryException>(() =>
            new ELM327PIDResponseParser().Parse("NO DATA\r", new OBDPIDRequest(0x01, 0x05)));

        Assert.Equal(OBDPIDTelemetryError.CommandRejected, error.Reason);
    }
}
