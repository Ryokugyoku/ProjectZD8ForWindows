using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LogHistory.Actions;

/// <summary>接続履歴の読込範囲、絞り込み、更新、削除をLogHistoryへ通知します。</summary>
public abstract record ConnectionHistoryAction
{
    /// <summary>派生ケース以外からの継承を防ぎます。</summary>
    /// <remarks>責務: ケースを入れ子の sealed record だけに限定します。</remarks>
    private ConnectionHistoryAction()
    {
    }

    /// <summary>現在の認証アカウントへ読込範囲を切り替えます。</summary>
    /// <param name="Identifier">新しいAppleアカウント識別子。</param>
    public sealed record AccountIdentifierChanged(string? Identifier) : ConnectionHistoryAction;

    /// <summary>現在アカウントの履歴再読込を要求します。</summary>
    public sealed record RefreshRequested : ConnectionHistoryAction;

    /// <summary>現在端末で保存内容が変わった履歴だけを再読込します。</summary>
    public sealed record LocalDataChanged : ConnectionHistoryAction;

    /// <summary>セッション終了時のiCloud自動アップロード設定を一覧表示へ反映します。</summary>
    /// <param name="IsEnabled">自動アップロードが有効かどうか。</param>
    public sealed record AutomaticUploadChanged(bool IsEnabled) : ConnectionHistoryAction;

    /// <summary>指定した未送信セッションだけをiCloudへアップロードします。</summary>
    /// <param name="SessionID">アップロード対象の接続セッションID。</param>
    public sealed record SessionUploadRequested(ConnectionSessionID SessionID) : ConnectionHistoryAction;

    /// <summary>セッションアップロード失敗の通知を閉じます。</summary>
    public sealed record SessionUploadFailureDismissed : ConnectionHistoryAction;

    /// <summary>セッション一覧へ適用する開始日を変更します。</summary>
    /// <param name="Date">新しい開始日。</param>
    public sealed record FilterStartDateChanged(DateTimeOffset? Date) : ConnectionHistoryAction;

    /// <summary>セッション一覧へ適用する終了日を変更します。</summary>
    /// <param name="Date">新しい終了日。</param>
    public sealed record FilterEndDateChanged(DateTimeOffset? Date) : ConnectionHistoryAction;

    /// <summary>セッション一覧へ適用する終了理由を変更します。</summary>
    /// <param name="Filter">新しい終了理由条件。</param>
    public sealed record EndReasonFilterChanged(ConnectionHistoryEndReasonFilter Filter) : ConnectionHistoryAction;

    /// <summary>セッション一覧の並び順を変更します。</summary>
    /// <param name="Order">新しい並び順。</param>
    public sealed record SortOrderChanged(ConnectionHistorySortOrder Order) : ConnectionHistoryAction;

    /// <summary>セッション一覧の絞り込み条件を初期化します。</summary>
    public sealed record FiltersReset : ConnectionHistoryAction;

    /// <summary>応答停止または接続喪失をユーザー操作として確認する前の説明を要求します。</summary>
    /// <param name="SessionID">確認対象の接続セッションID。</param>
    public sealed record StopReviewRequested(ConnectionSessionID SessionID) : ConnectionHistoryAction;

    /// <summary>表示中の終了確認をユーザー操作による停止として確定します。</summary>
    public sealed record StopReviewConfirmed : ConnectionHistoryAction;

    /// <summary>表示中の終了確認を取り消します。</summary>
    public sealed record StopReviewCancelled : ConnectionHistoryAction;

    /// <summary>終了確認の保存失敗通知を閉じます。</summary>
    public sealed record StopReviewFailureDismissed : ConnectionHistoryAction;

    /// <summary>指定セッションをこのiPhoneから取り除く前の確認を要求します。</summary>
    /// <param name="SessionID">除去候補の接続セッションID。</param>
    public sealed record LocalRawRemovalRequested(ConnectionSessionID SessionID) : ConnectionHistoryAction;

    /// <summary>表示中の確認内容を承知してローカルRawログ除去を確定します。</summary>
    public sealed record LocalRawRemovalConfirmed : ConnectionHistoryAction;

    /// <summary>ローカルRawログ除去の確認を取り消します。</summary>
    public sealed record LocalRawRemovalCancelled : ConnectionHistoryAction;

    /// <summary>指定セッションを全端末から物理削除する前の確認を要求します。</summary>
    /// <param name="SessionID">削除候補の接続セッションID。</param>
    public sealed record SessionDeletionRequested(ConnectionSessionID SessionID) : ConnectionHistoryAction;

    /// <summary>表示中の警告を承知して全端末削除を確定します。</summary>
    public sealed record SessionDeletionConfirmed : ConnectionHistoryAction;

    /// <summary>全端末削除の確認を取り消します。</summary>
    public sealed record SessionDeletionCancelled : ConnectionHistoryAction;

    /// <summary>全端末削除失敗の通知を閉じます。</summary>
    public sealed record SessionDeletionFailureDismissed : ConnectionHistoryAction;
}
