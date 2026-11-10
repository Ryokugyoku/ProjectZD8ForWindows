using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Tests.ValueObjects;

/// <summary>取得manifestの順序付きPID集合契約を検証します。</summary>
public sealed class OrderedAcquisitionPIDSetTests
{
    /// <summary>入力されたService/PID順を保持します。</summary>
    /// <remarks>責務: 順序付きPID集合が要素を並べ替えないことを確認します。</remarks>
    [Fact]
    public void PreservesRequestedPIDOrder()
    {
        OBDPIDRequest[] requests = [new(Service: 1, Pid: 12), new(Service: 1, Pid: 5)];

        Assert.Equal(requests, new OrderedAcquisitionPIDSet(requests: requests).Requests);
    }

    /// <summary>同じService/PIDの重複を拒否します。</summary>
    /// <remarks>責務: ordered PID setが重複を黙って除去しないことを確認します。</remarks>
    [Fact]
    public void RejectsDuplicatePID()
    {
        var request = new OBDPIDRequest(Service: 1, Pid: 12);

        var error = Assert.Throws<OrderedAcquisitionPIDSetException>(
            () => new OrderedAcquisitionPIDSet(requests: [request, request]));
        Assert.Equal(OrderedAcquisitionPIDSetError.DuplicateRequest, error.Reason);
    }

    /// <summary>対応状態と収集選択を別軸で保持します。</summary>
    /// <remarks>責務: supported PIDでも収集無効を表現できることを確認します。</remarks>
    [Fact]
    public void SeparatesCapabilitySupportFromCollectionSelection()
    {
        var snapshot = new AcquisitionPIDDefinitionSnapshot(
            request: new OBDPIDRequest(Service: 1, Pid: 12),
            capabilitySupport: AcquisitionPIDCapabilitySupport.Supported,
            isCollectionEnabled: false,
            definitionRevision: 1,
            requiredByteCount: 2,
            definitionIdentity: new AcquisitionPIDDefinitionIdentity(canonicalizationVersion: 1, expression: "(A * 256 + B) / 4"),
            unit: "rpm",
            validityRange: AcquisitionPIDValidityRange.Inclusive(minimum: 0, maximum: 16_383.75));

        Assert.Equal(AcquisitionPIDCapabilitySupport.Supported, snapshot.CapabilitySupport);
        Assert.NotNull(snapshot.IsCollectionEnabled);
        Assert.False(snapshot.IsCollectionEnabled.Value);
    }
}
