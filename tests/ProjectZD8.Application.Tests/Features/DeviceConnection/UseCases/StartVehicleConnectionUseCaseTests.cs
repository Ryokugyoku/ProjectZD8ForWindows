using ProjectZD8.Application.DeviceConnection.UseCases;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Tests.DeviceConnection.UseCases;

/// <summary>HOME接続開始が車両確定前の識別だけへ展開されることを検証します。</summary>
public sealed class StartVehicleConnectionUseCaseTests
{
    /// <summary>接続セッションを先行開始せず同じOBD終端を車両識別へ通知します。</summary>
    /// <remarks>責務: HOME接続要求が車両識別通知だけを発行することを確認します。</remarks>
    [Fact]
    public void ExecuteStartsIdentification()
    {
        OBDConnectionEndpoint? receivedEndpoint = null;
        var endpoint = new OBDConnectionEndpoint(
            transport: OBDConnectionEndpointTransport.Serial,
            systemIdentifier: "projectzd8://test",
            displayName: "Test");
        var useCase = new StartVehicleConnectionUseCase(identifyVehicle: received => receivedEndpoint = received);

        useCase.Execute(endpoint);

        Assert.Equal(endpoint, receivedEndpoint);
    }
}
