namespace ProjectZD8ForWindows.Platform.Windows.Features.Analysis.Presentation;

/// <summary>解析画面の表示の切り替えです（iOS 版の <c>MacOSSessionLogAnalysisMode</c>）。</summary>
public enum WindowsSessionLogAnalysisMode
{
    /// <summary>概要。</summary>
    Overview,

    /// <summary>推移（時系列のグラフ）。</summary>
    Trends,

    /// <summary>関係（散布図）。</summary>
    Relationships,
}

/// <summary>解析画面の表示の切り替えの表示用のキーです。</summary>
public static class WindowsSessionLogAnalysisModePresentation
{
    /// <summary>切り替えの題のキーを返します。</summary>
    /// <remarks>責務: 表示の切り替えを xcstrings のキーへ変換します。</remarks>
    /// <param name="mode">表示の切り替え。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this WindowsSessionLogAnalysisMode mode) => mode switch
    {
        WindowsSessionLogAnalysisMode.Overview => "analysis.mode.overview",
        WindowsSessionLogAnalysisMode.Trends => "analysis.mode.trends",
        _ => "analysis.mode.relationships",
    };
}
