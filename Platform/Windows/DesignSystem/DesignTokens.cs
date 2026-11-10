using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// RevTorqueTheme.xaml の色と文字の約束を、コードで組む画面から引くための入口です。
/// </summary>
/// <remarks>
/// アプリ全体の外観（ライト・ダーク）を設定で上書きしても正しい色を引けるよう、
/// 自前のテーマ辞書を <see cref="EffectiveTheme"/> で選んで引きます。WinUI のシステムの色はスタイル経由で使います。
/// </remarks>
public static class DesignTokens
{
    /// <summary>今の画面の実際のテーマです（シェルが外観の変更のたびに更新する）。</summary>
    public static ElementTheme EffectiveTheme { get; set; } = ElementTheme.Light;

    /// <summary>
    /// スタイルを引きます。
    /// </summary>
    /// <remarks>責務: アプリのリソースからスタイルを 1 件返します。</remarks>
    /// <param name="key">スタイルのキー。</param>
    /// <returns>スタイル。</returns>
    public static Style Style(string key) => (Style)Microsoft.UI.Xaml.Application.Current.Resources[key];

    /// <summary>
    /// 自前のテーマ辞書から、今のテーマのブラシを引きます。
    /// </summary>
    /// <remarks>責務: RevTorqueTheme.xaml のテーマ別のブラシを、実際のテーマに合わせて返します。</remarks>
    /// <param name="key">ブラシのキー。</param>
    /// <returns>ブラシ。</returns>
    public static Brush Brush(string key) => (Brush)ThemeDictionary()[key];

    /// <summary>
    /// 色の役割の前景ブラシを返します。
    /// </summary>
    /// <remarks>責務: 役割を前景のブラシへ変換します。</remarks>
    /// <param name="tone">色の役割。</param>
    /// <returns>前景のブラシ。</returns>
    public static Brush Foreground(Tone tone) => Brush(tone switch
    {
        Tone.Accent => "RevTorqueAccentBrush",
        Tone.Caution => "RevTorqueCautionBrush",
        Tone.Success => "RevTorqueSuccessBrush",
        Tone.Danger => "RevTorqueDangerBrush",
        Tone.Indigo => "RevTorqueIndigoBrush",
        _ => "RevTorqueNeutralBrush",
    });

    /// <summary>
    /// 色の役割の薄い背景ブラシを返します。
    /// </summary>
    /// <remarks>責務: 役割を低い不透明度の背景のブラシへ変換します。</remarks>
    /// <param name="tone">色の役割。</param>
    /// <returns>背景のブラシ。</returns>
    public static Brush SubtleBackground(Tone tone) => Brush(tone switch
    {
        Tone.Accent => "RevTorqueAccentSubtleBrush",
        Tone.Caution => "RevTorqueCautionSubtleBrush",
        Tone.Success => "RevTorqueSuccessSubtleBrush",
        Tone.Danger => "RevTorqueDangerSubtleBrush",
        Tone.Indigo => "RevTorqueIndigoSubtleBrush",
        _ => "RevTorqueQuietFillBrush",
    });

    /// <summary>
    /// 今のテーマの自前のテーマ辞書を返します。
    /// </summary>
    /// <remarks>責務: アプリのリソースから RevTorqueTheme.xaml を探し、テーマ別の辞書を選びます。</remarks>
    /// <returns>テーマ別の辞書。</returns>
    private static ResourceDictionary ThemeDictionary()
    {
        var theme = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries
            .First(dictionary => dictionary.Source?.OriginalString.EndsWith("RevTorqueTheme.xaml", StringComparison.Ordinal) == true);
        var key = EffectiveTheme == ElementTheme.Dark ? "Dark" : "Light";
        return (ResourceDictionary)theme.ThemeDictionaries[key];
    }
}
