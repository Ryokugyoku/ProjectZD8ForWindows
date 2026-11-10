using ProjectZD8.Application.VehicleManagement.State;

namespace ProjectZD8ForWindows.Platform.Windows.Features.VehicleManagement.Presentation;

/// <summary>PID の収集設定の一覧の絞り込みです（iOS 版の <c>MacOSPIDCollectionFilter</c>）。</summary>
public enum WindowsPIDCollectionFilter
{
    /// <summary>すべて。</summary>
    All,

    /// <summary>収集中だけ。</summary>
    Enabled,

    /// <summary>停止中だけ。</summary>
    Disabled,
}

/// <summary>絞り込みの表示用のキーと判定です。</summary>
public static class WindowsPIDCollectionFilterPresentation
{
    /// <summary>絞り込みの題のキーを返します。</summary>
    /// <remarks>責務: 絞り込みを xcstrings のキーへ変換します。</remarks>
    /// <param name="filter">絞り込み。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this WindowsPIDCollectionFilter filter) => filter switch
    {
        WindowsPIDCollectionFilter.All => "garage.pid_settings.filter.all",
        WindowsPIDCollectionFilter.Enabled => "garage.pid_settings.filter.enabled",
        _ => "garage.pid_settings.filter.disabled",
    };

    /// <summary>項目が絞り込みに合うかどうかを返します。</summary>
    /// <remarks>責務: 収集の有効・無効で項目を選びます。</remarks>
    /// <param name="filter">絞り込み。</param>
    /// <param name="item">PID の項目。</param>
    /// <returns>合うなら true。</returns>
    public static bool Matches(this WindowsPIDCollectionFilter filter, VehiclePIDSelectionItem item) => filter switch
    {
        WindowsPIDCollectionFilter.All => true,
        WindowsPIDCollectionFilter.Enabled => item.IsEnabled,
        _ => !item.IsEnabled,
    };
}
