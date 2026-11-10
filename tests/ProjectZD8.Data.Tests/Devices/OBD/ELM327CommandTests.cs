using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>ELMコマンドの送信文字列と識別子を検証します。</summary>
public sealed class ELM327CommandTests
{
    /// <summary>許可済みコマンドの本文と要求識別子が iOS と同じであることを検証します。</summary>
    /// <remarks>責務: 送信バイトと未加工観測の識別子が変わらないことを確認します。</remarks>
    [Fact]
    public void CommandsMatchSwiftRawValuesAndCaseNames()
    {
        Assert.Equal(
            ["ATZ", "ATE0", "ATL0", "ATS1", "ATH0", "ATSP0", "ATI", "ATDP", "0902"],
            ELM327Command.AllCases.Select(command => command.RawValue));
        Assert.Equal(
            ["reset", "echoOff", "linefeedsOff", "spacesOn", "headersOff", "automaticProtocol", "adapterIdentity", "protocolDescription", "vehicleIdentificationNumber"],
            ELM327Command.AllCases.Select(command => command.RequestID));
        Assert.Equal("ATZ\r"u8.ToArray(), ELM327Command.Reset.EncodedData);
    }

    /// <summary>Service 01 以外と範囲外ヘッダーを生成しないことを検証します。</summary>
    /// <remarks>責務: 固定許可リストの境界値を確認します。</remarks>
    [Fact]
    public void FailableCommandsRejectOutOfRangeValues()
    {
        Assert.Equal("010C", ELM327CurrentDataCommand.Create(new OBDPIDRequest(0x01, 0x0C))?.RequestID);
        Assert.Null(ELM327CurrentDataCommand.Create(new OBDPIDRequest(0x02, 0x0C)));
        Assert.Equal("ATSH7FF", ELM327TransmitHeaderCommand.Create(0x7FF)?.RequestID);
        Assert.Equal("ATSH0E0", ELM327TransmitHeaderCommand.Create(0x0E0)?.RequestID);
        Assert.Null(ELM327TransmitHeaderCommand.Create(0x800));
    }
}
