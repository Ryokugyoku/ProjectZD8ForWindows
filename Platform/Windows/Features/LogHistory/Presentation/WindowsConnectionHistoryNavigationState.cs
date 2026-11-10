using System.Collections.Generic;
using System.Linq;
using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.LogHistory.Presentation;

/// <summary>
/// 接続履歴の中の移動の積み重ねです（iOS 版の <c>MacOSConnectionHistoryNavigationState</c>）。
/// </summary>
/// <remarks>UI フレームワークに依存しない、画面の一時的な状態です。</remarks>
public sealed class WindowsConnectionHistoryNavigationState
{
    /// <summary>移動の積み重ね（最後が今の画面）です。</summary>
    private readonly List<WindowsConnectionHistoryRoute> _path = [];

    /// <summary>移動の積み重ねです。</summary>
    public IReadOnlyList<WindowsConnectionHistoryRoute> Path => _path;

    /// <summary>今の行き先です。一覧の最上位なら null。</summary>
    public WindowsConnectionHistoryRoute? Current => _path.LastOrDefault();

    /// <summary>行き先へ進みます。</summary>
    /// <remarks>責務: 行き先を積み重ねの最後に足します。</remarks>
    /// <param name="route">行き先。</param>
    public void Push(WindowsConnectionHistoryRoute route) => _path.Add(route);

    /// <summary>車両の一覧（最上位）へ戻ります。</summary>
    /// <remarks>責務: 積み重ねを空にします。</remarks>
    public void ReturnToVehicleArchive() => _path.Clear();

    /// <summary>セッション詳細から、車両のセッション一覧へ戻ります。</summary>
    /// <remarks>責務: 最後がセッション詳細のときだけ 1 つ戻ります。</remarks>
    public void ReturnToVehicleSessionList()
    {
        if (Current is WindowsConnectionHistoryRoute.SessionDetail)
        {
            _path.RemoveAt(_path.Count - 1);
        }
    }

    /// <summary>解析からセッション詳細へ戻ります。</summary>
    /// <remarks>責務: 最後が解析のときだけ 1 つ戻ります。</remarks>
    public void ReturnFromAnalysis()
    {
        if (Current is WindowsConnectionHistoryRoute.SessionAnalysis)
        {
            _path.RemoveAt(_path.Count - 1);
        }
    }

    /// <summary>表示中のセッションが消えたら、一覧へ戻ります。</summary>
    /// <remarks>責務: 最後の行き先のセッションがもうなければ、そのセッションの行き先を取り除きます。</remarks>
    /// <param name="availableSessionIDs">今あるセッション。</param>
    public void ReturnFromDeletedSession(IReadOnlySet<ConnectionSessionID> availableSessionIDs)
    {
        while (Current switch
        {
            WindowsConnectionHistoryRoute.SessionDetail detail => !availableSessionIDs.Contains(detail.SessionID),
            WindowsConnectionHistoryRoute.SessionAnalysis analysis => !availableSessionIDs.Contains(analysis.SessionID),
            _ => false,
        })
        {
            _path.RemoveAt(_path.Count - 1);
        }
    }
}
