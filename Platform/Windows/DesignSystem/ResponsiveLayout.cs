using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// ウインドウの幅のクラスです（docs/UI_GUIDELINES.md の「幅クラス」）。
/// </summary>
/// <remarks>
/// ウインドウの幅（有効ピクセル）で決めます。最小のウインドウは 640 なので、Compact は「最小〜スナップした半分」の幅です。
/// 確かめるときの幅 640 / 900 / 1280 / 1920 は、それぞれ Compact / Medium / Expanded / Wide に入ります。
/// </remarks>
public enum WidthClass
{
    /// <summary>760 未満。ナビはハンバーガー（重ねて開く）。1 列。</summary>
    Compact,

    /// <summary>760 以上 1200 未満。ナビは記号だけの細いレール。1〜2 列。</summary>
    Medium,

    /// <summary>1200 以上 1600 未満。ナビは題つきのペイン。2〜3 列、一覧と詳細を左右に。</summary>
    Expanded,

    /// <summary>1600 以上。ナビは題つきのペイン。3〜4 列。中身は最大幅で中央に置く。</summary>
    Wide,
}

/// <summary>
/// ウインドウの幅クラスを 1 か所で判定し、変わったときだけ知らせます。
/// </summary>
/// <remarks>
/// MainWindow が根の要素を <see cref="Observe"/> に渡します。画面は個別に SizeChanged を見ず、
/// <see cref="Bind(FrameworkElement, Action{WidthClass})"/> で購読して、幅クラスが変わったときだけ配置を切り替えます
/// （木を作り直さず、Grid の行・列や余白・Visibility を差し替える）。UI スレッドからだけ使います。
/// </remarks>
public static class ResponsiveLayout
{
    /// <summary>Medium の下限です。</summary>
    public const double MediumMinWidth = 760;

    /// <summary>Expanded の下限です。</summary>
    public const double ExpandedMinWidth = 1200;

    /// <summary>Wide の下限です。</summary>
    public const double WideMinWidth = 1600;

    /// <summary>今の幅クラスです。</summary>
    public static WidthClass Current { get; private set; } = WidthClass.Expanded;

    /// <summary>今のウインドウの幅（有効ピクセル）です。</summary>
    public static double WindowWidth { get; private set; }

    /// <summary>幅クラスが変わったときに起きます（同じクラスの中の幅の変化では起きない）。</summary>
    public static event EventHandler<WidthClass>? Changed;

    /// <summary>
    /// 幅を幅クラスへ変換します。
    /// </summary>
    /// <remarks>責務: しきい値で幅を 4 つのクラスへ分けます。</remarks>
    /// <param name="width">ウインドウの幅（有効ピクセル）。</param>
    /// <returns>幅クラス。</returns>
    public static WidthClass Classify(double width) => width switch
    {
        < MediumMinWidth => WidthClass.Compact,
        < ExpandedMinWidth => WidthClass.Medium,
        < WideMinWidth => WidthClass.Expanded,
        _ => WidthClass.Wide,
    };

    /// <summary>
    /// ウインドウの根の要素の幅を見張り始めます（MainWindow が 1 回だけ呼ぶ）。
    /// </summary>
    /// <remarks>責務: 根の要素の幅から幅クラスを決め、変わったときだけ <see cref="Changed"/> を起こします。</remarks>
    /// <param name="root">ウインドウの根の要素。</param>
    public static void Observe(FrameworkElement root)
    {
        root.SizeChanged += (_, args) => Update(args.NewSize.Width);
        if (root.ActualWidth > 0)
        {
            Update(root.ActualWidth);
        }
    }

    /// <summary>
    /// 要素が表示されている間だけ、幅クラスの変化を受け取ります。
    /// </summary>
    /// <remarks>
    /// 責務: 表示されたときに今の幅クラスで 1 回呼び、以後は変わったときだけ呼びます。外されたら購読をやめるので、画面は解除を書かなくてよい。
    /// 同じクラスで 2 回呼ぶことはありません。
    /// </remarks>
    /// <param name="owner">購読の持ち主（画面や部品）。</param>
    /// <param name="apply">幅クラスに合わせて配置を切り替える処理。木を作り直さないこと。</param>
    public static void Bind(FrameworkElement owner, Action<WidthClass> apply)
    {
        WidthClass? applied = null;
        void OnChanged(object? sender, WidthClass widthClass)
        {
            if (applied != widthClass)
            {
                applied = widthClass;
                apply(widthClass);
            }
        }

        owner.Loaded += (_, _) =>
        {
            Changed -= OnChanged;
            Changed += OnChanged;
            OnChanged(null, Current);
        };
        owner.Unloaded += (_, _) => Changed -= OnChanged;
        if (owner.IsLoaded)
        {
            Changed += OnChanged;
        }

        // 表示される前に今の幅クラスで 1 回組んでおく（最初の描画で配置が跳ねないように）。
        OnChanged(null, Current);
    }

    /// <summary>
    /// XAML で組んだ画面の VisualState（"Compact" / "Medium" / "Expanded" / "Wide"）を、幅クラスに合わせて切り替えます。
    /// </summary>
    /// <remarks>責務: <see cref="Bind(FrameworkElement, Action{WidthClass})"/> で、幅クラスと同じ名前の状態へ移します。</remarks>
    /// <param name="control">VisualStateGroup を持つコントロール。</param>
    public static void BindVisualStates(Control control) =>
        Bind(control, widthClass => VisualStateManager.GoToState(control, widthClass.ToString(), Motion.IsEnabled));

    /// <summary>
    /// 幅クラスを更新します。
    /// </summary>
    /// <remarks>責務: 新しい幅から幅クラスを決め、変わったときだけ知らせます。</remarks>
    /// <param name="width">ウインドウの幅（有効ピクセル）。</param>
    private static void Update(double width)
    {
        WindowWidth = width;
        var next = Classify(width);
        if (_initialized && next == Current)
        {
            return;
        }

        _initialized = true;
        Current = next;
        Changed?.Invoke(null, next);
    }

    /// <summary>最初の幅を受け取ったかどうかです。</summary>
    private static bool _initialized;
}
