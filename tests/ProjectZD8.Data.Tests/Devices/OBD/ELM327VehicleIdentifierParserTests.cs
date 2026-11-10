using ProjectZD8.Application.LiveTelemetry.Ports;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>Service 09 PID 02のVINと非VIN識別子形式を検証します。</summary>
public sealed class ELM327VehicleIdentifierParserTests
{
    /// <summary>CAN自動整形式からVINを復元します。</summary>
    /// <remarks>責務: 0から2の連続フレームを17文字VINへ連結できることを確認します。</remarks>
    [Fact]
    public void ParsesCANFormattedVIN()
    {
        const string response = "014\r\n0: 49 02 01 31 44 34\r\n1: 47 50 30 30 52 35 35\r\n2: 42 31 32 33 34 35 36\r";

        Assert.Equal(new ELM327VehicleIdentifierParser.Result("1D4GP00R55B123456", null), new ELM327VehicleIdentifierParser().Parse(response));
    }

    /// <summary>実車で観測した空白埋め識別子をVINと分離します。</summary>
    /// <remarks>責務: 7バイトの先頭空白を除いた10文字を非VINのOBD由来識別子として保持することを確認します。</remarks>
    [Fact]
    public void ParsesSpacePaddedOBDIdentifierWithoutCallingItVIN()
    {
        const string response = "014\r\n0: 49 02 01 20 20 20\r\n1: 20 20 20 20 5A 44 38\r\n2: 31 32 33 34 35 36 37\r";

        Assert.Equal(new ELM327VehicleIdentifierParser.Result(null, "ZD81234567"), new ELM327VehicleIdentifierParser().Parse(response));
    }

    /// <summary>J1850の行番号形式からVINを復元します。</summary>
    /// <remarks>責務: 49 02行の順序バイトと先頭埋め値を除いてVINを連結できることを確認します。</remarks>
    [Fact]
    public void ParsesLegacyVINFrames()
    {
        const string response =
            "49 02 01 31 44 34 47\r\n49 02 02 50 30 30 52\r\n49 02 03 35 35 42 31\r\n49 02 04 32 33 34 35\r\n49 02 05 36\r";

        Assert.Equal(new ELM327VehicleIdentifierParser.Result("1D4GP00R55B123456", null), new ELM327VehicleIdentifierParser().Parse(response));
    }

    /// <summary>17バイト未満の応答を識別子として扱いません。</summary>
    /// <remarks>責務: 切断されたMode 09応答が明示的な形式不正になることを確認します。</remarks>
    [Fact]
    public void RejectsTruncatedIdentifier()
    {
        var error = Assert.Throws<VehicleIdentificationException>(() => new ELM327VehicleIdentifierParser().Parse("49 02 01 31 32 33"));

        Assert.IsType<VehicleIdentificationError.MalformedResponse>(error.Reason);
    }
}
