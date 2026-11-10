using System.Globalization;
using ProjectZD8ForWindows.Platform.Windows.Localization;

namespace ProjectZD8ForWindows.Platform.Windows.Formatting;

/// <summary>
/// 距離を表示します（iOS 版の履歴画面の <c>distanceText</c>）。
/// </summary>
public static class DistanceText
{
    /// <summary>
    /// キロメートルを、1 km 未満はメートル、それ以上は小数 1 桁の km で表示します。
    /// </summary>
    /// <remarks>責務: 距離を表示文字列にし、値がなければ「取得できない」の文言にします。</remarks>
    /// <param name="kilometers">距離（km）。null なら取得できなかった。</param>
    /// <returns>表示文字列。</returns>
    public static string Format(double? kilometers)
    {
        if (kilometers is not { } value)
        {
            return LocalizedText.Get("history.distance.unsupported");
        }

        return value < 1
            ? (value * 1_000).ToString("#,0", CultureInfo.CurrentCulture) + " m"
            : value.ToString("#,0.0", CultureInfo.CurrentCulture) + " km";
    }
}
