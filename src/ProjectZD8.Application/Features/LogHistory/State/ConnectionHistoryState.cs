using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Policies;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LogHistory.State;

/// <summary>接続履歴の端末間同期表示段階です。</summary>
public enum ConnectionHistorySyncPhase
{
    /// <summary>同期要求前または変更なしです。</summary>
    Idle,
    /// <summary>CloudKitへ送受信しています。</summary>
    Syncing,
    /// <summary>直近のCloudKit同期を完了しました。</summary>
    Synchronized,
    /// <summary>直近のCloudKit同期が失敗し再試行を待っています。</summary>
    Failed,
}

/// <summary>自動判別できなかったセッション停止をユーザーへ確認する内容です。</summary>
public sealed record ConnectionSessionStopReviewPrompt
{
    /// <summary>確認対象の接続セッションIDです。</summary>
    public ConnectionSessionID SessionID { get; }
    /// <summary>アプリが観測した直接の終了理由です。</summary>
    public ConnectionSessionEndReason ObservedReason { get; }

    /// <summary>セッションと観測済み終了理由を確認内容として生成します。</summary>
    /// <remarks>責務: 1件の確認可能な接続セッションを停止確認表示に必要な値へ固定します。</remarks>
    /// <param name="sessionID">確認対象の接続セッションID。</param>
    /// <param name="observedReason">アプリが観測した直接の終了理由。</param>
    public ConnectionSessionStopReviewPrompt(ConnectionSessionID sessionID, ConnectionSessionEndReason observedReason)
    {
        SessionID = sessionID;
        ObservedReason = observedReason;
    }
}

/// <summary>iPhoneローカルRawログ除去前に表示する確認内容です。</summary>
public sealed record ConnectionSessionRawRemovalPrompt
{
    /// <summary>除去候補の接続セッションIDです。</summary>
    public ConnectionSessionID SessionID { get; }
    /// <summary>CloudKit保管証跡に基づく除去判断です。</summary>
    public ConnectionSessionLocalRemovalDecision Decision { get; }
    /// <summary>除去対象のRaw応答件数です。</summary>
    public long RecordCount { get; }
    /// <summary>除去対象のRaw Payload合計バイト数です。</summary>
    public long ByteCount { get; }

    /// <summary>セッションと安全判断を確認表示値として生成します。</summary>
    /// <remarks>責務: 1件のローカル除去候補を警告に必要な件数と容量へまとめます。</remarks>
    /// <param name="sessionID">除去候補の接続セッションID。</param>
    /// <param name="decision">CloudKit保管証跡に基づく除去判断。</param>
    /// <param name="recordCount">除去対象のRaw応答件数。</param>
    /// <param name="byteCount">除去対象のRaw Payload合計バイト数。</param>
    public ConnectionSessionRawRemovalPrompt(
        ConnectionSessionID sessionID,
        ConnectionSessionLocalRemovalDecision decision,
        long recordCount,
        long byteCount)
    {
        SessionID = sessionID;
        Decision = decision;
        RecordCount = recordCount;
        ByteCount = byteCount;
    }
}

/// <summary>macOSで全端末削除前に表示するセッション情報です。</summary>
public sealed record ConnectionSessionDeletionPrompt
{
    /// <summary>削除対象の接続セッションIDです。</summary>
    public ConnectionSessionID SessionID { get; }
    /// <summary>物理削除するRawログの収集件数です。</summary>
    public long RecordCount { get; }
    /// <summary>物理削除するRaw Payload合計バイト数です。</summary>
    public long ByteCount { get; }

    /// <summary>削除対象を警告表示に必要な値へまとめます。</summary>
    /// <remarks>責務: 1件の削除候補を全端末削除警告に必要な識別子とRaw集計へ変換します。</remarks>
    /// <param name="sessionID">削除対象の接続セッションID。</param>
    /// <param name="recordCount">物理削除するRawログの収集件数。</param>
    /// <param name="byteCount">物理削除するRaw Payload合計バイト数。</param>
    public ConnectionSessionDeletionPrompt(ConnectionSessionID sessionID, long recordCount, long byteCount)
    {
        SessionID = sessionID;
        RecordCount = recordCount;
        ByteCount = byteCount;
    }
}

/// <summary>終了済みセッション一覧へ適用する終了理由条件です。</summary>
public enum ConnectionHistoryEndReasonFilter
{
    /// <summary>すべての終了理由を表示します。</summary>
    All,
    /// <summary>ユーザー操作による正常終了だけを表示します。</summary>
    UserDisconnected,
    /// <summary>車両の応答不能による終了だけを表示します。</summary>
    VehicleNoResponse,
    /// <summary>接続喪失による終了だけを表示します。</summary>
    ConnectionLost,
    /// <summary>取得開始失敗による終了だけを表示します。</summary>
    AcquisitionFailed,
    /// <summary>新しい接続による置換終了だけを表示します。</summary>
    Superseded,
    /// <summary>サインアウトによる終了だけを表示します。</summary>
    AccountSignedOut,
    /// <summary>予期しない終了だけを表示します。</summary>
    UnexpectedTermination,
}

/// <summary><see cref="ConnectionHistoryEndReasonFilter"/> の raw value と全ケースを扱います。</summary>
public static class ConnectionHistoryEndReasonFilterRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<ConnectionHistoryEndReasonFilter> AllCases { get; } =
    [
        ConnectionHistoryEndReasonFilter.All,
        ConnectionHistoryEndReasonFilter.UserDisconnected,
        ConnectionHistoryEndReasonFilter.VehicleNoResponse,
        ConnectionHistoryEndReasonFilter.ConnectionLost,
        ConnectionHistoryEndReasonFilter.AcquisitionFailed,
        ConnectionHistoryEndReasonFilter.Superseded,
        ConnectionHistoryEndReasonFilter.AccountSignedOut,
        ConnectionHistoryEndReasonFilter.UnexpectedTermination,
    ];

    /// <summary>Swift と同じ raw value 文字列を返します。</summary>
    /// <remarks>責務: 1件の終了理由条件を永続化可能な文字列へ変換します。</remarks>
    /// <param name="value">変換する終了理由条件。</param>
    /// <returns>ケース名そのままの raw value。</returns>
    public static string ToRawValue(this ConnectionHistoryEndReasonFilter value) => value switch
    {
        ConnectionHistoryEndReasonFilter.All => "all",
        ConnectionHistoryEndReasonFilter.UserDisconnected => "userDisconnected",
        ConnectionHistoryEndReasonFilter.VehicleNoResponse => "vehicleNoResponse",
        ConnectionHistoryEndReasonFilter.ConnectionLost => "connectionLost",
        ConnectionHistoryEndReasonFilter.AcquisitionFailed => "acquisitionFailed",
        ConnectionHistoryEndReasonFilter.Superseded => "superseded",
        ConnectionHistoryEndReasonFilter.AccountSignedOut => "accountSignedOut",
        ConnectionHistoryEndReasonFilter.UnexpectedTermination => "unexpectedTermination",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value 文字列から終了理由条件を復元します。</summary>
    /// <remarks>責務: 1件の raw value 文字列を対応するケースへ変換します。</remarks>
    /// <param name="rawValue">復元する raw value。</param>
    /// <param name="value">復元できた終了理由条件。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out ConnectionHistoryEndReasonFilter value)
    {
        foreach (var candidate in AllCases)
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>終了理由が現在の条件へ一致するかを返します。</summary>
    /// <remarks>責務: 1件の終了理由を選択中の終了理由条件で判定します。</remarks>
    /// <param name="filter">選択中の終了理由条件。</param>
    /// <param name="reason">判定するセッション終了理由。</param>
    /// <returns>一覧へ含める場合は <see langword="true"/>。</returns>
    public static bool Matches(this ConnectionHistoryEndReasonFilter filter, ConnectionSessionEndReason? reason) => filter switch
    {
        ConnectionHistoryEndReasonFilter.All => true,
        ConnectionHistoryEndReasonFilter.UserDisconnected => reason == ConnectionSessionEndReason.UserDisconnected,
        ConnectionHistoryEndReasonFilter.VehicleNoResponse => reason == ConnectionSessionEndReason.VehicleNoResponse,
        ConnectionHistoryEndReasonFilter.ConnectionLost => reason == ConnectionSessionEndReason.ConnectionLost,
        ConnectionHistoryEndReasonFilter.AcquisitionFailed => reason == ConnectionSessionEndReason.AcquisitionFailed,
        ConnectionHistoryEndReasonFilter.Superseded => reason == ConnectionSessionEndReason.Superseded,
        ConnectionHistoryEndReasonFilter.AccountSignedOut => reason == ConnectionSessionEndReason.AccountSignedOut,
        ConnectionHistoryEndReasonFilter.UnexpectedTermination => reason == ConnectionSessionEndReason.UnexpectedTermination,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };
}

/// <summary>終了済みセッション一覧へ適用する並び順です。</summary>
public enum ConnectionHistorySortOrder
{
    /// <summary>開始日時が新しい順です。</summary>
    Newest,
    /// <summary>開始日時が古い順です。</summary>
    Oldest,
    /// <summary>走行時間が長い順です。</summary>
    LongestDuration,
    /// <summary>走行時間が短い順です。</summary>
    ShortestDuration,
}

/// <summary><see cref="ConnectionHistorySortOrder"/> の raw value と全ケースを扱います。</summary>
public static class ConnectionHistorySortOrderRawValue
{
    /// <summary>宣言順の全ケースです。</summary>
    public static IReadOnlyList<ConnectionHistorySortOrder> AllCases { get; } =
    [
        ConnectionHistorySortOrder.Newest,
        ConnectionHistorySortOrder.Oldest,
        ConnectionHistorySortOrder.LongestDuration,
        ConnectionHistorySortOrder.ShortestDuration,
    ];

    /// <summary>Swift と同じ raw value 文字列を返します。</summary>
    /// <remarks>責務: 1件の並び順を永続化可能な文字列へ変換します。</remarks>
    /// <param name="value">変換する並び順。</param>
    /// <returns>ケース名そのままの raw value。</returns>
    public static string ToRawValue(this ConnectionHistorySortOrder value) => value switch
    {
        ConnectionHistorySortOrder.Newest => "newest",
        ConnectionHistorySortOrder.Oldest => "oldest",
        ConnectionHistorySortOrder.LongestDuration => "longestDuration",
        ConnectionHistorySortOrder.ShortestDuration => "shortestDuration",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>raw value 文字列から並び順を復元します。</summary>
    /// <remarks>責務: 1件の raw value 文字列を対応するケースへ変換します。</remarks>
    /// <param name="rawValue">復元する raw value。</param>
    /// <param name="value">復元できた並び順。</param>
    /// <returns>対応するケースがあれば <see langword="true"/>。</returns>
    public static bool TryParse(string rawValue, out ConnectionHistorySortOrder value)
    {
        foreach (var candidate in AllCases)
        {
            if (string.Equals(candidate.ToRawValue(), rawValue, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }
}

/// <summary>車両別履歴グループの安定識別子です。</summary>
public abstract record ConnectionHistoryVehicleGroupID
{
    /// <summary>派生ケース以外からの継承を防ぎます。</summary>
    /// <remarks>責務: ケースを入れ子の sealed record だけに限定します。</remarks>
    private ConnectionHistoryVehicleGroupID()
    {
    }

    /// <summary>Garage登録車両へ関連付いた履歴です。</summary>
    /// <param name="VehicleID">関連付いた登録車両の識別子。</param>
    public sealed record Registered(VehicleID VehicleID) : ConnectionHistoryVehicleGroupID;

    /// <summary>車両関連付け前に終了した履歴です。</summary>
    public sealed record Unassigned : ConnectionHistoryVehicleGroupID;
}

/// <summary>終了済み接続履歴を1台の車両単位へ集約した表示状態です。</summary>
public sealed record ConnectionHistoryVehicleGroup
{
    /// <summary>車両グループの安定識別子です。</summary>
    public ConnectionHistoryVehicleGroupID Id { get; }
    /// <summary>最新セッション時点の車両表示情報です。</summary>
    public ConnectionSessionVehicle? Vehicle { get; }
    /// <summary>開始日時が新しい順の終了済みセッションです。</summary>
    public IReadOnlyList<ConnectionSession> Sessions { get; }

    /// <summary>グループ識別子、車両情報、セッションを固定して生成します。</summary>
    /// <remarks>責務: 1台分の終了済み履歴を表示用グループへまとめます。</remarks>
    /// <param name="id">車両グループの安定識別子。</param>
    /// <param name="vehicle">最新セッション時点の車両表示情報。</param>
    /// <param name="sessions">開始日時が新しい順の終了済みセッション。</param>
    public ConnectionHistoryVehicleGroup(
        ConnectionHistoryVehicleGroupID id,
        ConnectionSessionVehicle? vehicle,
        IReadOnlyList<ConnectionSession> sessions)
    {
        Id = id;
        Vehicle = vehicle;
        Sessions = sessions.ToArray();
    }

    /// <summary>グループ内のセッション件数です。</summary>
    public int SessionCount => Sessions.Count;
    /// <summary>グループ内で正常終了しなかったセッション件数です。</summary>
    public int InterruptedCount => Sessions.Count(session => session.Status == ConnectionSessionStatus.Interrupted);
    /// <summary>グループ内の記録済み総走行時間（秒）です。</summary>
    public double TotalDuration => Sessions.Aggregate(0.0, (total, session) => total + session.RecordedDuration());
    /// <summary>全セッションで差分を確定できた場合の記録済み総走行距離です。</summary>
    public double? TotalDistanceKilometers
    {
        get
        {
            var distances = Sessions
                .Select(session => session.RecordedDistanceKilometers)
                .Where(distance => distance is not null)
                .Select(distance => distance!.Value)
                .ToList();
            if (distances.Count != Sessions.Count)
            {
                return null;
            }
            return distances.Aggregate(0.0, (total, distance) => total + distance);
        }
    }
    /// <summary>グループ内で最後に接続を開始した日時です。</summary>
    public DateTimeOffset? LatestStartedAt => Sessions.Count > 0 ? Sessions[0].StartedAt : null;

    /// <summary>セッション列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じくセッション列を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(ConnectionHistoryVehicleGroup? other) =>
        other is not null
        && Id.Equals(other.Id)
        && Equals(Vehicle, other.Vehicle)
        && Sessions.SequenceEqual(other.Sessions);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: セッション列の要素を含めてハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Vehicle);
        foreach (var session in Sessions)
        {
            hash.Add(session);
        }
        return hash.ToHashCode();
    }
}

/// <summary>履歴取得の現在段階です。</summary>
/// <remarks>Swift の <c>ConnectionHistoryState.Phase</c> に対応します。C# では同名プロパティと衝突するため外へ出しています。</remarks>
public enum ConnectionHistoryStatePhase
{
    /// <summary>アカウント確定前または読込待ちです。</summary>
    Idle,
    /// <summary>永続化済み履歴を読み込んでいます。</summary>
    Loading,
    /// <summary>履歴の読込が完了しました。</summary>
    Loaded,
    /// <summary>履歴保存先を利用できません。</summary>
    Failed,
}

/// <summary>接続履歴画面へ公開する読込済み状態です。</summary>
/// <remarks>Swift の値型（struct）に対応します。変更は <c>with</c> 式で新しい値を作ります。</remarks>
public sealed record ConnectionHistoryState
{
    /// <summary>履歴取得の現在段階です。</summary>
    public ConnectionHistoryStatePhase Phase { get; init; } = ConnectionHistoryStatePhase.Idle;
    /// <summary>開始日時が新しい順の接続セッションです。</summary>
    public IReadOnlyList<ConnectionSession> Sessions { get; init; } = [];
    /// <summary>直近の読込失敗を示すローカライズキーです。</summary>
    public string? FailureKey { get; init; }
    /// <summary>絞り込み範囲の開始日です。</summary>
    public DateTimeOffset? FilterStartDate { get; init; }
    /// <summary>絞り込み範囲の終了日です。</summary>
    public DateTimeOffset? FilterEndDate { get; init; }
    /// <summary>選択中の終了理由条件です。</summary>
    public ConnectionHistoryEndReasonFilter EndReasonFilter { get; init; } = ConnectionHistoryEndReasonFilter.All;
    /// <summary>選択中の並び順です。</summary>
    public ConnectionHistorySortOrder SortOrder { get; init; } = ConnectionHistorySortOrder.Newest;
    /// <summary>CloudKitセッション同期の現在段階です。</summary>
    public ConnectionHistorySyncPhase SyncPhase { get; init; } = ConnectionHistorySyncPhase.Idle;
    /// <summary>セッション終了時のiCloud自動アップロードが有効かを示します。</summary>
    public bool AutomaticUploadEnabled { get; init; }
    /// <summary>iCloudへ個別アップロードしているセッションIDです。</summary>
    public ConnectionSessionID? UploadingSessionID { get; init; }
    /// <summary>個別アップロード中のCloudKit Asset保存進捗です。</summary>
    public double? UploadProgress { get; init; }
    /// <summary>直近のiCloud個別アップロードに失敗したセッションIDです。</summary>
    public ConnectionSessionID? UploadFailureSessionID { get; init; }
    /// <summary>ユーザー操作による停止かを確認する表示内容です。</summary>
    public ConnectionSessionStopReviewPrompt? StopReviewPrompt { get; init; }
    /// <summary>直近の停止確認保存失敗を示すローカライズキーです。</summary>
    public string? StopReviewFailureKey { get; init; }
    /// <summary>iPhoneローカルRawログ除去前の確認内容です。</summary>
    public ConnectionSessionRawRemovalPrompt? RawRemovalPrompt { get; init; }
    /// <summary>macOS全端末セッション削除前の確認内容です。</summary>
    public ConnectionSessionDeletionPrompt? SessionDeletionPrompt { get; init; }
    /// <summary>全端末セッション削除中の対象IDです。</summary>
    public ConnectionSessionID? DeletingSessionID { get; init; }
    /// <summary>直近の全端末削除失敗を示すローカライズキーです。</summary>
    public string? SessionDeletionFailureKey { get; init; }

    /// <summary>現在接続中のセッションです。</summary>
    public IReadOnlyList<ConnectionSession> ActiveSessions =>
        Sessions.Where(session => session.Status == ConnectionSessionStatus.Connected).ToList();
    /// <summary>終了日時が確定したセッションです。</summary>
    public IReadOnlyList<ConnectionSession> ClosedSessions =>
        Sessions.Where(session => session.Status != ConnectionSessionStatus.Connected).ToList();
    /// <summary>全履歴のうち正常に終了しなかったセッション件数です。</summary>
    public int InterruptedCount =>
        ClosedSessions.Count(session => session.Status == ConnectionSessionStatus.Interrupted);
    /// <summary>終了済みセッションの記録済み総走行時間（秒）です。</summary>
    public double TotalRecordedDuration =>
        ClosedSessions.Aggregate(0.0, (total, session) => total + session.RecordedDuration());
    /// <summary>絞り込み条件が1件以上有効かを示します。</summary>
    public bool HasActiveFilters =>
        FilterStartDate is not null || FilterEndDate is not null || EndReasonFilter != ConnectionHistoryEndReasonFilter.All;

    /// <summary>指定セッションを履歴画面から手動アップロードできるかを返します。</summary>
    /// <remarks>責務: 1件のセッション保管状態と自動アップロード設定を手動アップロード操作の表示可否へ変換します。</remarks>
    /// <param name="session">判定する終了済み接続セッション。</param>
    /// <returns>手動アップロード操作を表示する場合は <see langword="true"/>。</returns>
    public bool IsManualUploadAvailable(ConnectionSession session)
    {
        if (session.EndedAt is null
            || session.RawLogSummary.LocalState == ConnectionSessionLocalRawState.Removed
            || session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Uploaded)
        {
            return false;
        }
        return !AutomaticUploadEnabled || session.RawLogSummary.CloudState == ConnectionSessionCloudSyncState.Failed;
    }

    /// <summary>終了済み履歴を車両単位へ集約して返します。</summary>
    public IReadOnlyList<ConnectionHistoryVehicleGroup> VehicleGroups =>
        ClosedSessions
            .GroupBy(session => session.Vehicle is { } vehicle
                ? (ConnectionHistoryVehicleGroupID)new ConnectionHistoryVehicleGroupID.Registered(vehicle.Id)
                : new ConnectionHistoryVehicleGroupID.Unassigned())
            .Select(group =>
            {
                var sorted = group.OrderByDescending(session => session.StartedAt).ToList();
                return new ConnectionHistoryVehicleGroup(group.Key, sorted.FirstOrDefault()?.Vehicle, sorted);
            })
            .OrderByDescending(group => group.LatestStartedAt ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>指定車両グループへ複合条件と並び順を適用したセッションを返します。</summary>
    /// <remarks>責務: 1台分の終了済み履歴を日付範囲と終了理由で絞り込み、指定順へ整列します。</remarks>
    /// <param name="groupID">対象車両グループの識別子。</param>
    /// <returns>現在の全条件を同時に満たす終了済みセッション。</returns>
    public IReadOnlyList<ConnectionSession> FilteredSessions(ConnectionHistoryVehicleGroupID groupID)
    {
        var sessions = VehicleGroups.FirstOrDefault(group => group.Id.Equals(groupID))?.Sessions;
        if (sessions is null)
        {
            return [];
        }
        var startBound = FilterStartDate is { } start ? StartOfDay(start) : (DateTimeOffset?)null;
        var endBound = FilterEndDate is { } end ? StartOfNextDay(end) : (DateTimeOffset?)null;
        var filtered = sessions.Where(session =>
        {
            var isAfterStart = startBound is not { } lower || session.StartedAt >= lower;
            var isBeforeEnd = endBound is not { } upper || session.StartedAt < upper;
            return isAfterStart && isBeforeEnd && EndReasonFilter.Matches(session.EndReason);
        });
        return SortOrder switch
        {
            ConnectionHistorySortOrder.Newest => filtered.OrderByDescending(session => session.StartedAt).ToList(),
            ConnectionHistorySortOrder.Oldest => filtered.OrderBy(session => session.StartedAt).ToList(),
            ConnectionHistorySortOrder.LongestDuration => filtered.OrderByDescending(session => session.RecordedDuration()).ToList(),
            ConnectionHistorySortOrder.ShortestDuration => filtered.OrderBy(session => session.RecordedDuration()).ToList(),
            _ => throw new InvalidOperationException($"未知の並び順です: {SortOrder}"),
        };
    }

    /// <summary>セッション列を含む全項目が等しいかを返します。</summary>
    /// <remarks>責務: Swift の値型と同じくセッション列を要素ごとに比較します。</remarks>
    /// <param name="other">比較対象。</param>
    /// <returns>全項目が等しければ <see langword="true"/>。</returns>
    public bool Equals(ConnectionHistoryState? other) =>
        other is not null
        && Phase == other.Phase
        && Sessions.SequenceEqual(other.Sessions)
        && string.Equals(FailureKey, other.FailureKey, StringComparison.Ordinal)
        && FilterStartDate == other.FilterStartDate
        && FilterEndDate == other.FilterEndDate
        && EndReasonFilter == other.EndReasonFilter
        && SortOrder == other.SortOrder
        && SyncPhase == other.SyncPhase
        && AutomaticUploadEnabled == other.AutomaticUploadEnabled
        && UploadingSessionID == other.UploadingSessionID
        && UploadProgress == other.UploadProgress
        && UploadFailureSessionID == other.UploadFailureSessionID
        && Equals(StopReviewPrompt, other.StopReviewPrompt)
        && string.Equals(StopReviewFailureKey, other.StopReviewFailureKey, StringComparison.Ordinal)
        && Equals(RawRemovalPrompt, other.RawRemovalPrompt)
        && Equals(SessionDeletionPrompt, other.SessionDeletionPrompt)
        && DeletingSessionID == other.DeletingSessionID
        && string.Equals(SessionDeletionFailureKey, other.SessionDeletionFailureKey, StringComparison.Ordinal);

    /// <summary>等価性と整合するハッシュ値を返します。</summary>
    /// <remarks>責務: セッション列の要素を含めて主要項目からハッシュ値を合成します。</remarks>
    /// <returns>ハッシュ値。</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Phase);
        foreach (var session in Sessions)
        {
            hash.Add(session);
        }
        hash.Add(FailureKey, StringComparer.Ordinal);
        hash.Add(FilterStartDate);
        hash.Add(FilterEndDate);
        hash.Add(EndReasonFilter);
        hash.Add(SortOrder);
        hash.Add(SyncPhase);
        hash.Add(AutomaticUploadEnabled);
        hash.Add(UploadingSessionID);
        hash.Add(UploadProgress);
        hash.Add(UploadFailureSessionID);
        hash.Add(StopReviewPrompt);
        hash.Add(DeletingSessionID);
        return hash.ToHashCode();
    }

    /// <summary>現在のタイムゾーンで指定日時を含む日の開始時刻を返します。</summary>
    /// <remarks>責務: Swift の <c>Calendar.autoupdatingCurrent.startOfDay(for:)</c> と同じく端末の現地日付の0時を求めます。</remarks>
    /// <param name="date">基準日時。</param>
    /// <returns>現地日付の開始時刻。</returns>
    private static DateTimeOffset StartOfDay(DateTimeOffset date) =>
        LocalStartOfDay(TimeZoneInfo.ConvertTime(date, TimeZoneInfo.Local).Date);

    /// <summary>現在のタイムゾーンで指定日時の翌日の開始時刻を返します。</summary>
    /// <remarks>責務: Swift の開始日に1日を足す計算と同じく翌日の現地0時を求めます。</remarks>
    /// <param name="date">基準日時。</param>
    /// <returns>翌日の現地開始時刻。</returns>
    private static DateTimeOffset StartOfNextDay(DateTimeOffset date) =>
        LocalStartOfDay(TimeZoneInfo.ConvertTime(date, TimeZoneInfo.Local).Date.AddDays(1));

    /// <summary>現地日付の最初の有効な時刻を返します。</summary>
    /// <remarks>責務: 夏時間で0時が存在しない日も含め、1日の最初の現地時刻を時差付き日時へ変換します。</remarks>
    /// <param name="day">現地日付。</param>
    /// <returns>現地日付の開始時刻。</returns>
    private static DateTimeOffset LocalStartOfDay(DateTime day)
    {
        var zone = TimeZoneInfo.Local;
        var local = DateTime.SpecifyKind(day, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}

/// <summary>接続履歴の集計に使うセッション拡張です。</summary>
file static class ConnectionSessionRecordedDurationExtensions
{
    /// <summary>終了日時が確定している範囲の記録時間（秒）を返します。</summary>
    /// <remarks>責務: 1件のセッションの開始から終了までを0以上の秒数へ変換します。</remarks>
    /// <param name="session">対象セッション。</param>
    /// <returns>記録時間（秒）。</returns>
    public static double RecordedDuration(this ConnectionSession session) =>
        Math.Max(0, session.EndedAt is { } endedAt ? (endedAt - session.StartedAt).TotalSeconds : 0);
}
