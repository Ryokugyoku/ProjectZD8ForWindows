using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.VehicleManagement.State;

/// <summary>車両管理ワークフローの現在段階です。</summary>
/// <remarks>Swift の <c>VehicleManagementState.Phase</c> に対応します。C# では型と同名のプロパティ <see cref="VehicleManagementState.Phase"/> を入れ子にできないため外へ出しています。</remarks>
public enum VehicleManagementStatePhase
{
    /// <summary>アカウント確定前または操作待ちです。</summary>
    Idle,

    /// <summary>同期先から一覧を読み込んでいます。</summary>
    Loading,

    /// <summary>OBDから識別情報を取得しています。</summary>
    Identifying,

    /// <summary>未登録VINと全取得情報の確認を待っています。</summary>
    ConfirmingIdentification,

    /// <summary>新規車両の入力を待っています。</summary>
    Registering,

    /// <summary>既存車両の編集を待っています。</summary>
    Editing,

    /// <summary>登録済み車両へ接続を引き渡せる状態です。</summary>
    ReadyToConnect,

    /// <summary>OBD識別が未提供または処理に失敗しました。</summary>
    Failed,
}

/// <summary>車両識別、登録、一覧編集の現在状態です。</summary>
public sealed record VehicleManagementState
{
    /// <summary>現在のワークフロー段階です。</summary>
    public VehicleManagementStatePhase Phase { get; init; } = VehicleManagementStatePhase.Idle;

    /// <summary>アカウントに属する登録車両です。</summary>
    public IReadOnlyList<VehicleProfile> Vehicles { get; init; } = [];

    /// <summary>確認中のOBD識別観測です。</summary>
    public VehicleIdentificationSnapshot? PendingIdentification { get; init; }

    /// <summary>編集中の車両プロフィールです。</summary>
    public VehicleProfile? EditingVehicle { get; init; }

    /// <summary>接続対象として確定した登録車両です。</summary>
    public VehicleProfile? ConnectionVehicle { get; init; }

    /// <summary>現在の識別または再試行で使用するOBD物理終端です。</summary>
    public OBDConnectionEndpoint? ConnectionEndpoint { get; init; }

    /// <summary>直近の失敗をユーザーへ説明するローカライズキーです。</summary>
    public string? FailureKey { get; init; }

    /// <summary>直近のOBD識別失敗で完了できなかった型付き段階です。</summary>
    public VehicleIdentificationErrorStage? IdentificationFailureStage { get; init; }

    /// <summary>CloudKitまたはキャッシュから最後に読込を完了したかを示します。</summary>
    public bool HasLoadedVehicles { get; init; }

    /// <summary>PID収集設定を表示している車両IDです。</summary>
    public VehicleID? PidSettingsVehicleID { get; init; }

    /// <summary>PID収集設定へ表示する対応PID一覧です。</summary>
    public IReadOnlyList<VehiclePIDSelectionItem> PidSelectionItems { get; init; } = [];

    /// <summary>登録車両ごとの接続・ログ集計です。</summary>
    public IReadOnlyDictionary<VehicleID, VehicleActivitySummary> ActivityByVehicleID { get; init; } =
        new Dictionary<VehicleID, VehicleActivitySummary>();

    /// <summary>対応登録済みの車種専用PID型式です。</summary>
    public IReadOnlyDictionary<VehicleID, string> SpecialPIDModelCodeByVehicleID { get; init; } =
        new Dictionary<VehicleID, string>();

    /// <summary>全プロパティを Swift の Equatable と同じ意味で比較します。</summary>
    /// <remarks>責務: 一覧は要素順、辞書はキーと値の集合で比較します。</remarks>
    /// <param name="other">比較相手。</param>
    /// <returns>等しい場合は <c>true</c>。</returns>
    public bool Equals(VehicleManagementState? other) =>
        other is not null
        && Phase == other.Phase
        && Vehicles.SequenceEqual(other.Vehicles)
        && Equals(PendingIdentification, other.PendingIdentification)
        && Equals(EditingVehicle, other.EditingVehicle)
        && Equals(ConnectionVehicle, other.ConnectionVehicle)
        && Equals(ConnectionEndpoint, other.ConnectionEndpoint)
        && string.Equals(FailureKey, other.FailureKey, StringComparison.Ordinal)
        && IdentificationFailureStage == other.IdentificationFailureStage
        && HasLoadedVehicles == other.HasLoadedVehicles
        && PidSettingsVehicleID == other.PidSettingsVehicleID
        && PidSelectionItems.SequenceEqual(other.PidSelectionItems)
        && DictionaryEquals(ActivityByVehicleID, other.ActivityByVehicleID)
        && DictionaryEquals(SpecialPIDModelCodeByVehicleID, other.SpecialPIDModelCodeByVehicleID);

    /// <summary>比較と矛盾しないハッシュ値を返します。</summary>
    /// <remarks>責務: 順序に依存しない要素数と単純な値からハッシュ値を作ります。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Phase, Vehicles.Count, FailureKey, HasLoadedVehicles, PidSelectionItems.Count, ActivityByVehicleID.Count);

    /// <summary>2つの辞書がキーと値の組として等しいかを返します。</summary>
    /// <remarks>責務: Swift の Dictionary の等価比較と同じ結果を返します。</remarks>
    /// <typeparam name="TKey">キーの型。</typeparam>
    /// <typeparam name="TValue">値の型。</typeparam>
    /// <param name="left">比較元。</param>
    /// <param name="right">比較相手。</param>
    /// <returns>等しい場合は <c>true</c>。</returns>
    private static bool DictionaryEquals<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> left,
        IReadOnlyDictionary<TKey, TValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !EqualityComparer<TValue>.Default.Equals(value, otherValue))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Garageへ表示する1台分の接続・ログ集計です。</summary>
public sealed record VehicleActivitySummary
{
    /// <summary>ログ未記録の初期集計を生成します。</summary>
    /// <remarks>責務: Garage表示用アクティビティを履歴なしの安全なゼロ値で初期化します。</remarks>
    /// <param name="sessionCount">記録済み接続セッション件数。</param>
    /// <param name="lastLoggedAt">最後にログを記録した日時。</param>
    /// <param name="totalRecordedDuration">全セッションの記録済み走行時間（秒）。</param>
    /// <param name="interruptedCount">正常終了しなかったセッション件数。</param>
    /// <param name="isConnected">現在進行中の接続セッションがあるか。</param>
    /// <param name="latestOdometerKilometers">Garageへ表示する最新の累積走行距離。</param>
    /// <param name="odometerModelCode">累積走行距離を車種専用PIDから取得した場合の型式。</param>
    public VehicleActivitySummary(
        int sessionCount = 0,
        DateTimeOffset? lastLoggedAt = null,
        double totalRecordedDuration = 0,
        int interruptedCount = 0,
        bool isConnected = false,
        double? latestOdometerKilometers = null,
        string? odometerModelCode = null)
    {
        SessionCount = sessionCount;
        LastLoggedAt = lastLoggedAt;
        TotalRecordedDuration = totalRecordedDuration;
        InterruptedCount = interruptedCount;
        IsConnected = isConnected;
        LatestOdometerKilometers = latestOdometerKilometers;
        OdometerModelCode = odometerModelCode;
    }

    /// <summary>記録済み接続セッション件数です。</summary>
    public int SessionCount { get; }

    /// <summary>最後にログを記録した日時です。</summary>
    public DateTimeOffset? LastLoggedAt { get; }

    /// <summary>全セッションの記録済み走行時間です。</summary>
    /// <remarks>Swift の <c>TimeInterval</c>（秒の <c>Double</c>）をそのまま秒数で保持します。</remarks>
    public double TotalRecordedDuration { get; }

    /// <summary>正常終了しなかったセッション件数です。</summary>
    public int InterruptedCount { get; }

    /// <summary>現在進行中の接続セッションがあるかを示します。</summary>
    public bool IsConnected { get; }

    /// <summary>Garageへ表示できる直近セッションの累積走行距離です。</summary>
    public double? LatestOdometerKilometers { get; }

    /// <summary>累積走行距離を車種専用PIDから取得した場合の型式です。</summary>
    public string? OdometerModelCode { get; }
}

/// <summary>車両設定画面へ表示する1件の対応PID収集選択です。</summary>
public sealed record VehiclePIDSelectionItem
{
    /// <summary>対応PID収集選択を生成します。</summary>
    /// <remarks>責務: 1件のService/PIDと表示情報を収集選択へまとめます。</remarks>
    /// <param name="id">Service/PID識別子。</param>
    /// <param name="nameKey">PIDテーブルに定義がある場合の名称キー。</param>
    /// <param name="isEnabled">継続収集の有効状態。</param>
    /// <param name="vehicleModelCode">車種専用PIDの場合に表示する型式。</param>
    public VehiclePIDSelectionItem(OBDPIDRequest id, string? nameKey, bool isEnabled, string? vehicleModelCode)
    {
        Id = id;
        NameKey = nameKey;
        IsEnabled = isEnabled;
        VehicleModelCode = vehicleModelCode;
    }

    /// <summary>Service/PID識別子です。</summary>
    public OBDPIDRequest Id { get; }

    /// <summary>PIDテーブルに定義がある場合の名称キーです。</summary>
    public string? NameKey { get; }

    /// <summary>継続収集の有効状態です。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>車種専用PIDの場合に表示する型式です。</summary>
    public string? VehicleModelCode { get; }
}
