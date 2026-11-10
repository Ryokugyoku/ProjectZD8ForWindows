using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.LogHistory.State;

/// <summary>接続履歴の終了理由、終了理由条件、並び順を固定ローカライズキーへ変換します。</summary>
/// <remarks>Swift の <c>historyLocalizationKey</c> 拡張に対応します。</remarks>
public static class ConnectionHistoryLocalizationKey
{
    /// <summary>String Catalogへ登録済みの終了理由キーを返します。</summary>
    /// <remarks>責務: 1件の終了理由を接続履歴の固定ローカライズキーへ変換します。</remarks>
    /// <param name="reason">変換する終了理由。</param>
    /// <returns>String Catalogへ登録済みの終了理由キー。</returns>
    public static string HistoryLocalizationKey(this ConnectionSessionEndReason reason) => reason switch
    {
        ConnectionSessionEndReason.UserDisconnected => "history.reason.userDisconnected",
        ConnectionSessionEndReason.VehicleNoResponse => "history.reason.vehicleNoResponse",
        ConnectionSessionEndReason.ConnectionLost => "history.reason.connectionLost",
        ConnectionSessionEndReason.AcquisitionFailed => "history.reason.acquisitionFailed",
        ConnectionSessionEndReason.Superseded => "history.reason.superseded",
        ConnectionSessionEndReason.AccountSignedOut => "history.reason.accountSignedOut",
        ConnectionSessionEndReason.UnexpectedTermination => "history.reason.unexpectedTermination",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>String Catalogへ登録済みの終了理由条件キーを返します。</summary>
    /// <remarks>責務: 1件の終了理由条件を接続履歴の固定ローカライズキーへ変換します。</remarks>
    /// <param name="filter">変換する終了理由条件。</param>
    /// <returns>String Catalogへ登録済みの終了理由条件キー。</returns>
    public static string HistoryLocalizationKey(this ConnectionHistoryEndReasonFilter filter) => filter switch
    {
        ConnectionHistoryEndReasonFilter.All => "history.reason.all",
        ConnectionHistoryEndReasonFilter.UserDisconnected => "history.reason.userDisconnected",
        ConnectionHistoryEndReasonFilter.VehicleNoResponse => "history.reason.vehicleNoResponse",
        ConnectionHistoryEndReasonFilter.ConnectionLost => "history.reason.connectionLost",
        ConnectionHistoryEndReasonFilter.AcquisitionFailed => "history.reason.acquisitionFailed",
        ConnectionHistoryEndReasonFilter.Superseded => "history.reason.superseded",
        ConnectionHistoryEndReasonFilter.AccountSignedOut => "history.reason.accountSignedOut",
        ConnectionHistoryEndReasonFilter.UnexpectedTermination => "history.reason.unexpectedTermination",
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    /// <summary>String Catalogへ登録済みの並び順キーを返します。</summary>
    /// <remarks>責務: 1件の並び順を接続履歴の固定ローカライズキーへ変換します。</remarks>
    /// <param name="order">変換する並び順。</param>
    /// <returns>String Catalogへ登録済みの並び順キー。</returns>
    public static string HistoryLocalizationKey(this ConnectionHistorySortOrder order) => order switch
    {
        ConnectionHistorySortOrder.Newest => "history.sort.newest",
        ConnectionHistorySortOrder.Oldest => "history.sort.oldest",
        ConnectionHistorySortOrder.LongestDuration => "history.sort.longestDuration",
        ConnectionHistorySortOrder.ShortestDuration => "history.sort.shortestDuration",
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };
}
