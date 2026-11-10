using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8.Application.Logging.Actions;

/// <summary>接続開始から終了までをLoggingへ通知する操作です。</summary>
public abstract record ConnectionSessionLifecycleAction
{
    /// <summary>派生ケースだけが生成できるようにします。</summary>
    /// <remarks>責務: 操作の種類を下の入れ子ケースに限定します。</remarks>
    private ConnectionSessionLifecycleAction()
    {
    }

    /// <summary>現在の認証アカウントへ保存範囲を切り替えます。</summary>
    /// <param name="Identifier">新しいアカウント識別子。</param>
    public sealed record AccountIdentifierChanged(string? Identifier) : ConnectionSessionLifecycleAction;

    /// <summary>セッション終了時のiCloud自動アップロード設定を反映します。</summary>
    /// <param name="IsEnabled">自動アップロードを有効にするか。</param>
    public sealed record AutomaticUploadChanged(bool IsEnabled) : ConnectionSessionLifecycleAction;

    /// <summary>HOME接続開始を新規セッションとして記録します。</summary>
    public sealed record StartRequested : ConnectionSessionLifecycleAction;

    /// <summary>接続対象として確定した登録車両を現在セッションへ関連付けます。</summary>
    /// <param name="Vehicle">接続対象として確定した登録車両。</param>
    public sealed record VehicleResolved(VehicleProfile Vehicle) : ConnectionSessionLifecycleAction;

    /// <summary>優先順位を判定できる取得元付き累積距離を現在セッションへ記録します。</summary>
    /// <param name="Observation">取得元付き累積距離。</param>
    public sealed record DistanceObserved(ConnectionSessionDistanceObservation Observation) : ConnectionSessionLifecycleAction;

    /// <summary>現在セッションを指定原因で終了します。</summary>
    /// <param name="Reason">セッションが終了した直接原因。</param>
    public sealed record EndRequested(ConnectionSessionEndReason Reason) : ConnectionSessionLifecycleAction;
}
