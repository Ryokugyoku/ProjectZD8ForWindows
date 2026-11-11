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
/// 自前のテーマ辞書を <see cref="EffectiveTheme"/> で選んで引きます（ハイコントラストのときは HighContrast の辞書）。
/// 余白・角丸・高さ・動きの時間は <see cref="Space"/>・<see cref="Radius"/>・<see cref="Metrics"/>・<see cref="Motion"/> にあります。
/// </remarks>
public static class DesignTokens
{
    /// <summary>今の画面の実際のテーマです（シェルが外観の変更のたびに更新する）。</summary>
    public static ElementTheme EffectiveTheme { get; set; } = ElementTheme.Light;

    /// <summary>ハイコントラストが有効かどうかです（MainWindow がウインドウのテーマの設定から更新する）。</summary>
    public static bool IsHighContrast { get; set; }

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
    /// WinUI のシステムのブラシ（<c>TextFillColorSecondaryBrush</c> など）を引きます。
    /// </summary>
    /// <remarks>責務: アプリのリソースからテーマに合ったシステムのブラシを返します。コードで組む画面で XAML の ThemeResource の代わりに使います。</remarks>
    /// <param name="key">ブラシのキー。</param>
    /// <returns>ブラシ。</returns>
    public static Brush SystemBrush(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];

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
    /// 色の役割の細い境界線のブラシを返します。
    /// </summary>
    /// <remarks>責務: 役割を、カードやタイルの縁に使う控えめな線のブラシへ変換します。Neutral は標準のカードの線です。</remarks>
    /// <param name="tone">色の役割。</param>
    /// <returns>線のブラシ。</returns>
    public static Brush Stroke(Tone tone) => Brush(tone switch
    {
        Tone.Accent => "RevTorqueAccentStrokeBrush",
        Tone.Caution => "RevTorqueCautionSubtleBrush",
        Tone.Success => "RevTorqueSuccessSubtleBrush",
        Tone.Danger => "RevTorqueDangerSubtleBrush",
        Tone.Indigo => "RevTorqueIndigoSubtleBrush",
        _ => "RevTorqueCardStrokeBrush",
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
        var key = IsHighContrast ? "HighContrast" : EffectiveTheme == ElementTheme.Dark ? "Dark" : "Light";
        return (ResourceDictionary)theme.ThemeDictionaries[key];
    }
}

/// <summary>
/// 余白の段階です（4 の倍数）。画面のコードは直値の代わりにこれを使います。
/// </summary>
public static class Space
{
    /// <summary>2。記号と文字の間など、くっついて見せたいもの。</summary>
    public const double XXS = 2;

    /// <summary>4。数値と単位、題と副題。</summary>
    public const double XS = 4;

    /// <summary>8。カードの中の行どうし、ボタンの並び。</summary>
    public const double S = 8;

    /// <summary>12。カードどうし（狭い幅）、まとまりの中。</summary>
    public const double M = 12;

    /// <summary>16。カードどうし（標準）、カードの内側（狭い幅）。</summary>
    public const double L = 16;

    /// <summary>20。カードの内側（標準）。</summary>
    public const double XL = 20;

    /// <summary>24。節どうし、主役のカードの内側。</summary>
    public const double XXL = 24;

    /// <summary>32。大きな節どうし、広い幅のページの余白。</summary>
    public const double XXXL = 32;

    /// <summary>
    /// 幅クラスごとのページの左右の余白を返します。
    /// </summary>
    /// <remarks>責務: 幅クラスを、ページの外周の余白へ変換します（Compact 16 / Medium 24 / Expanded 32 / Wide 40）。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    /// <returns>左右の余白。</returns>
    public static double PageGutter(WidthClass widthClass) => widthClass switch
    {
        WidthClass.Compact => 16,
        WidthClass.Medium => 24,
        WidthClass.Expanded => 32,
        _ => 40,
    };

    /// <summary>
    /// 幅クラスごとの節どうしの間隔を返します。
    /// </summary>
    /// <remarks>責務: 幅クラスを、ページの中の節（カードの列）どうしの縦の間隔へ変換します。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    /// <returns>間隔。</returns>
    public static double Section(WidthClass widthClass) => widthClass switch
    {
        WidthClass.Compact => 16,
        WidthClass.Medium => 20,
        _ => 24,
    };

    /// <summary>
    /// 幅クラスごとのカードどうしの間隔を返します。
    /// </summary>
    /// <remarks>責務: 幅クラスを、格子に並べたカードどうしの間隔へ変換します。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    /// <returns>間隔。</returns>
    public static double Gap(WidthClass widthClass) => widthClass == WidthClass.Compact ? 12 : 16;

    /// <summary>
    /// 幅クラスごとのカードの内側の余白を返します。
    /// </summary>
    /// <remarks>責務: 幅クラスを、標準のカードの内側の余白へ変換します。</remarks>
    /// <param name="widthClass">幅クラス。</param>
    /// <returns>余白。</returns>
    public static double CardPadding(WidthClass widthClass) => widthClass == WidthClass.Compact ? 16 : 20;
}

/// <summary>
/// 角丸の段階です。
/// </summary>
public static class Radius
{
    /// <summary>4。小さな印・バッジ。</summary>
    public const double S = 4;

    /// <summary>6。ボタン・入力欄（コントロール全体の既定。RevTorqueTheme.xaml の ControlCornerRadius）。</summary>
    public const double Control = 6;

    /// <summary>8。行・静かな面・タイル。</summary>
    public const double M = 8;

    /// <summary>12。標準のカード。</summary>
    public const double L = 12;

    /// <summary>20。主役のカード（ヒーロー）。</summary>
    public const double XL = 20;

    /// <summary>カプセル（状態の印）。</summary>
    public const double Pill = 999;
}

/// <summary>
/// 高さ・幅・大きさの約束です。
/// </summary>
public static class Metrics
{
    /// <summary>タイトルバーの高さ（Windows の「高い」タイトルバー）。</summary>
    public const double TitleBarHeight = 48;

    /// <summary>標準のボタン・入力欄の高さ。</summary>
    public const double ControlHeight = 36;

    /// <summary>主の操作のボタンの高さ。</summary>
    public const double PrimaryControlHeight = 44;

    /// <summary>主の操作のボタンの最小の幅。</summary>
    public const double PrimaryControlMinWidth = 176;

    /// <summary>一覧の 1 行の最小の高さ（触れる大きさ）。</summary>
    public const double RowMinHeight = 56;

    /// <summary>本文の最大の幅（これ以上広い行を作らない）。</summary>
    public const double ReadingMaxWidth = 720;

    /// <summary>ページの中身の最大の幅（4K で最大化しても、これより広げず中央に置く）。</summary>
    public const double ContentMaxWidth = 1360;

    /// <summary>格子のタイル（数値 1 つ）の最小の幅。</summary>
    public const double TileMinWidth = 168;

    /// <summary>格子のカードの最小の幅。</summary>
    public const double CardMinWidth = 280;

    /// <summary>格子の広いカード（グラフ・一覧）の最小の幅。</summary>
    public const double WideCardMinWidth = 420;

    /// <summary>記号のタイルの大きさ（小・中・大・主役）。</summary>
    public const double IconTileSmall = 32;

    /// <summary>記号のタイルの大きさ（中）。</summary>
    public const double IconTileMedium = 40;

    /// <summary>記号のタイルの大きさ（大）。</summary>
    public const double IconTileLarge = 56;

    /// <summary>記号のタイルの大きさ（主役）。</summary>
    public const double IconTileHero = 80;
}

/// <summary>
/// 動きの時間です。動きは Composition（GPU）で動くもの（ThemeTransition・暗黙アニメーション）だけを使います。
/// </summary>
public static class Motion
{
    /// <summary>OS の「アニメーション効果」の設定を読むためのものです。</summary>
    private static readonly global::Windows.UI.ViewManagement.UISettings Settings = new();

    /// <summary>短い（ホバー・押下の色の変化）。</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);

    /// <summary>標準（ページの切り替え・要素の出入り）。</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(200);

    /// <summary>長い（配置の組み替え）。</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(320);

    /// <summary>OS でアニメーション効果が有効かどうかです（無効なら動かさない）。</summary>
    public static bool IsEnabled => Settings.AnimationsEnabled;
}
