using System.Collections.Generic;
using ProjectZD8.Application.VehicleManagement.State;
using ProjectZD8ForWindows.Platform.Windows.DesignSystem;

namespace ProjectZD8ForWindows.Platform.Windows.AppShell;

/// <summary>サイドバー（NavigationView）の行き先です（iOS 版の <c>MacOSSidebarDestination</c>）。</summary>
public enum WindowsSidebarDestination
{
    /// <summary>ホーム。</summary>
    Home,

    /// <summary>ライブログ。</summary>
    LiveLog,

    /// <summary>接続履歴。</summary>
    History,

    /// <summary>整備記録。</summary>
    Maintenance,

    /// <summary>ガレージ。</summary>
    Garage,

    /// <summary>設定。</summary>
    Settings,
}

/// <summary>行き先の表示用のキーと記号です。</summary>
public static class WindowsSidebarDestinationPresentation
{
    /// <summary>表示順の行き先です。</summary>
    public static IReadOnlyList<WindowsSidebarDestination> AllCases { get; } =
    [
        WindowsSidebarDestination.Home,
        WindowsSidebarDestination.LiveLog,
        WindowsSidebarDestination.History,
        WindowsSidebarDestination.Maintenance,
        WindowsSidebarDestination.Garage,
        WindowsSidebarDestination.Settings,
    ];

    /// <summary>行き先の題のキーを返します。</summary>
    /// <remarks>責務: 行き先を xcstrings のキーへ変換します。</remarks>
    /// <param name="destination">行き先。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this WindowsSidebarDestination destination) => destination switch
    {
        WindowsSidebarDestination.Home => "sidebar.home",
        WindowsSidebarDestination.LiveLog => "sidebar.live_log",
        WindowsSidebarDestination.History => "sidebar.connection_history",
        WindowsSidebarDestination.Maintenance => "sidebar.maintenance",
        WindowsSidebarDestination.Garage => "sidebar.garage",
        _ => "sidebar.settings",
    };

    /// <summary>画面の下のタブ帯（Compact）に出す、短い名前のキーを返します。</summary>
    /// <remarks>責務: 長い名前の行き先だけ短いキーへ変換します（狭い幅で文字が切れないように）。ほかは <see cref="TitleKey"/> と同じです。</remarks>
    /// <param name="destination">行き先。</param>
    /// <returns>キー。</returns>
    public static string TabTitleKey(this WindowsSidebarDestination destination) => destination switch
    {
        WindowsSidebarDestination.LiveLog => "windows.nav.tab.live_log",
        WindowsSidebarDestination.History => "windows.nav.tab.connection_history",
        _ => destination.TitleKey(),
    };

    /// <summary>行き先の補足のキーを返します。</summary>
    /// <remarks>責務: 行き先を補足の xcstrings のキーへ変換します。</remarks>
    /// <param name="destination">行き先。</param>
    /// <returns>キー。</returns>
    public static string SubtitleKey(this WindowsSidebarDestination destination) => destination.TitleKey() + ".subtitle";

    /// <summary>行き先の記号を返します。</summary>
    /// <remarks>責務: 行き先を記号の文字へ変換します。</remarks>
    /// <param name="destination">行き先。</param>
    /// <returns>記号の文字。</returns>
    public static string Glyph(this WindowsSidebarDestination destination) => destination switch
    {
        WindowsSidebarDestination.Home => FluentSymbol.Home,
        WindowsSidebarDestination.LiveLog => FluentSymbol.Pulse,
        WindowsSidebarDestination.History => FluentSymbol.History,
        WindowsSidebarDestination.Maintenance => FluentSymbol.Repair,
        WindowsSidebarDestination.Garage => FluentSymbol.Car,
        _ => FluentSymbol.Sliders,
    };

    /// <summary>
    /// 車両の接続処理の段階から、移るべき行き先を返します（iOS 版の <c>destination(forVehicleManagementPhase:)</c>）。
    /// </summary>
    /// <remarks>責務: 確認・登録・失敗の段階をガレージへ、それ以外を移動なしへ変換します。</remarks>
    /// <param name="phase">車両管理の段階。</param>
    /// <returns>行き先。移らないなら null。</returns>
    public static WindowsSidebarDestination? Destination(VehicleManagementStatePhase phase) => phase switch
    {
        VehicleManagementStatePhase.ConfirmingIdentification
            or VehicleManagementStatePhase.Registering
            or VehicleManagementStatePhase.Failed => WindowsSidebarDestination.Garage,
        _ => null,
    };
}
