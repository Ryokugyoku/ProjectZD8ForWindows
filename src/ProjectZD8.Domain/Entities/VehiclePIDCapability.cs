using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>1台の車両で応答対応が確認されたService 01 PIDの収集設定です。</summary>
public sealed record VehiclePIDCapability
{
    /// <summary>対応PIDレコードの安定複合識別子です。</summary>
    /// <param name="VehicleID">アプリ内の車両識別子です。</param>
    /// <param name="Request">OBD要求のService/PID識別子です。</param>
    public readonly record struct ID(VehicleID VehicleID, OBDPIDRequest Request);

    /// <summary>対応PIDレコードの安定複合識別子です。</summary>
    public ID Id { get; }
    /// <summary>継続収集対象としてユーザーが選択しているかを示します。</summary>
    public bool IsCollectionEnabled { get; init; }
    /// <summary>対応ビットマップを最後に確認した日時です。</summary>
    public DateTimeOffset DiscoveredAt { get; }

    /// <summary>車両別の対応PIDと収集選択を生成します。</summary>
    /// <remarks>責務: 1台の車両と1件の対応PIDを収集選択および確認日時へ固定します。</remarks>
    /// <param name="vehicleID">アプリ内の車両識別子。</param>
    /// <param name="request">対応が確認されたService/PID。</param>
    /// <param name="isCollectionEnabled">継続収集対象かどうか。</param>
    /// <param name="discoveredAt">対応確認日時。</param>
    public VehiclePIDCapability(VehicleID vehicleID, OBDPIDRequest request, bool isCollectionEnabled, DateTimeOffset discoveredAt)
    {
        Id = new ID(vehicleID, request);
        IsCollectionEnabled = isCollectionEnabled;
        DiscoveredAt = discoveredAt;
    }
}
