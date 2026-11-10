using ProjectZD8.Domain.Entities;

namespace ProjectZD8.Application.Analysis.Actions;

/// <summary>セッションRawログ解析の読込範囲をAnalysisへ通知します。</summary>
public abstract record SessionLogAnalysisAction
{
    /// <summary>派生ケース以外からの継承を防ぎます。</summary>
    /// <remarks>責務: 解析操作のケースを下の入れ子レコードだけへ限定します。</remarks>
    private SessionLogAnalysisAction()
    {
    }

    /// <summary>指定セッションの保存済みRawログ解析を要求します。</summary>
    /// <param name="Session">解析する保存済みセッション概要。</param>
    public sealed record SessionSelected(ConnectionSession Session) : SessionLogAnalysisAction;

    /// <summary>確認済みのiCloud Rawログ取得と解析を要求します。</summary>
    public sealed record DownloadConfirmed : SessionLogAnalysisAction;

    /// <summary>iCloud Rawログ取得の確認を取り消します。</summary>
    public sealed record DownloadCancelled : SessionLogAnalysisAction;

    /// <summary>現在表示中の解析結果を閉じます。</summary>
    public sealed record Dismissed : SessionLogAnalysisAction;
}
