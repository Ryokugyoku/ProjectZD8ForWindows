using ProjectZD8.Application.LogHistory.State;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Presentation;

/// <summary>接続履歴の中の行き先です（iOS 版の <c>MacOSConnectionHistoryRoute</c>）。</summary>
/// <remarks>
/// macOS では解析画面をセッション詳細の <c>navigationDestination(isPresented:)</c> で出していましたが、
/// Windows では同じ戻り方にするため、解析も行き先の 1 つとして持ちます。
/// </remarks>
public abstract record WindowsConnectionHistoryRoute
{
    /// <summary>車両ごとのセッション一覧です。</summary>
    /// <param name="GroupID">車両のまとまり。</param>
    public sealed record VehicleSessions(ConnectionHistoryVehicleGroupID GroupID) : WindowsConnectionHistoryRoute;

    /// <summary>セッションの詳細です。</summary>
    /// <param name="SessionID">セッション。</param>
    public sealed record SessionDetail(ConnectionSessionID SessionID) : WindowsConnectionHistoryRoute;

    /// <summary>セッションのログの解析です。</summary>
    /// <param name="SessionID">セッション。</param>
    public sealed record SessionAnalysis(ConnectionSessionID SessionID) : WindowsConnectionHistoryRoute;
}
