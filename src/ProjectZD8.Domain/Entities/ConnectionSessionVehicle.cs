using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Domain.Entities;

/// <summary>接続時点の車両表示情報を履歴へ保持します。</summary>
public sealed record ConnectionSessionVehicle
{
    /// <summary>登録車両の安定識別子です。</summary>
    public VehicleID Id { get; }
    /// <summary>接続時点でのユーザー編集可能な車両名称です。</summary>
    public string Name { get; }
    /// <summary>接続時点でのVINまたはOBD由来代表識別値です。</summary>
    public string DisplayIdentifier { get; }

    /// <summary>登録車両から履歴用の不変スナップショットを生成します。</summary>
    /// <remarks>責務: 1台の登録車両を接続履歴に必要な表示情報へ縮約します。</remarks>
    /// <param name="profile">接続対象として確定した登録車両。</param>
    public ConnectionSessionVehicle(VehicleProfile profile)
        : this(profile.Id, profile.Name, profile.DisplayIdentifier)
    {
    }

    /// <summary>永続化済みの表示情報からスナップショットを復元します。</summary>
    /// <remarks>責務: 保存済みの車両IDと表示値を1件の履歴用車両情報へまとめます。</remarks>
    /// <param name="id">登録車両の安定識別子。</param>
    /// <param name="name">接続時点の車両名称。</param>
    /// <param name="displayIdentifier">接続時点の車両代表識別値。</param>
    public ConnectionSessionVehicle(VehicleID id, string name, string displayIdentifier)
    {
        Id = id;
        Name = name;
        DisplayIdentifier = displayIdentifier;
    }
}
