using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 狭いときの 2 つの面の見せ方です。
/// </summary>
public enum SplitCollapseMode
{
    /// <summary>上に主、下に副を積む（例：ホームの主役のカードと操作、車両の編集の写真と入力）。</summary>
    Stack,

    /// <summary>どちらか 1 つだけを出す（一覧と詳細。<see cref="AdaptiveSplit.ShowsSecondary"/> で切り替える）。</summary>
    Single,
}

/// <summary>
/// 主（左・広い）と副（右・決まった幅）の 2 つの面を、広いときは左右に、狭いときは積むか片方だけにする格子です。
/// </summary>
/// <remarks>
/// 幅クラスが <see cref="CollapseBelow"/> より小さいと畳みます。面は作り直さず、Grid の行・列と Visibility だけを差し替えます。
/// 一覧と詳細（マスター・詳細）なら、<see cref="Primary"/> に一覧、<see cref="Secondary"/> に詳細を入れ、
/// <see cref="SecondaryFirst"/> を true にすると左が副（一覧）になります。
/// 設定（左右・幅・畳み方）は入れた時点で並べ直すので、オブジェクト初期化子の順番に左右されません。
/// </remarks>
public sealed partial class AdaptiveSplit : Grid
{
    /// <summary>主の面です。</summary>
    private FrameworkElement? _primary;

    /// <summary>副の面です。</summary>
    private FrameworkElement? _secondary;

    /// <summary>今の幅クラスです。</summary>
    private WidthClass _widthClass = ResponsiveLayout.Current;

    /// <summary>副を出すかどうか（<see cref="SplitCollapseMode.Single"/> で畳んだとき）です。</summary>
    private bool _showsSecondary;

    /// <summary>副の面の幅です。</summary>
    private double _secondaryWidth = 360;

    /// <summary>副を左に置くかどうかです。</summary>
    private bool _secondaryFirst;

    /// <summary>畳む境の幅クラスです。</summary>
    private WidthClass _collapseBelow = WidthClass.Expanded;

    /// <summary>畳んだときの見せ方です。</summary>
    private SplitCollapseMode _collapseMode = SplitCollapseMode.Stack;

    /// <summary>空の格子を作ります。</summary>
    public AdaptiveSplit()
    {
        ResponsiveLayout.Bind(this, widthClass =>
        {
            _widthClass = widthClass;
            Apply();
        });
    }

    /// <summary>主の面です（広い側）。</summary>
    public FrameworkElement? Primary
    {
        get => _primary;
        set => Replace(ref _primary, value);
    }

    /// <summary>副の面です（決まった幅の側）。</summary>
    public FrameworkElement? Secondary
    {
        get => _secondary;
        set => Replace(ref _secondary, value);
    }

    /// <summary>副の面の幅（px）です。既定 360。</summary>
    public double SecondaryWidth
    {
        get => _secondaryWidth;
        set
        {
            _secondaryWidth = value;
            Apply();
        }
    }

    /// <summary>副を左に置くなら true（一覧を左に置くマスター・詳細）。</summary>
    public bool SecondaryFirst
    {
        get => _secondaryFirst;
        set
        {
            _secondaryFirst = value;
            Apply();
        }
    }

    /// <summary>この幅クラスより小さいと畳みます。既定は Expanded（Compact と Medium で畳む）。</summary>
    public WidthClass CollapseBelow
    {
        get => _collapseBelow;
        set
        {
            _collapseBelow = value;
            Apply();
        }
    }

    /// <summary>畳んだときの見せ方です。</summary>
    public SplitCollapseMode CollapseMode
    {
        get => _collapseMode;
        set
        {
            _collapseMode = value;
            Apply();
        }
    }

    /// <summary>畳んで片方だけを出すとき、副を出すかどうかです（一覧と詳細なら、詳細を開いたら true）。</summary>
    public bool ShowsSecondary
    {
        get => _showsSecondary;
        set
        {
            _showsSecondary = value;
            Apply();
        }
    }

    /// <summary>今、畳んでいるかどうかです。</summary>
    public bool IsCollapsed => _widthClass < CollapseBelow;

    /// <summary>
    /// 面の並べ方を、今の幅クラスと設定に合わせ直します。
    /// </summary>
    /// <remarks>責務: 行・列の定義と、各面の行・列・Visibility を差し替えます。設定を変えたあとに呼びます。</remarks>
    public void Apply()
    {
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        var gap = Space.Section(_widthClass);
        if (!IsCollapsed)
        {
            ColumnSpacing = gap;
            RowSpacing = 0;
            var primaryColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            var secondaryColumn = new ColumnDefinition { Width = new GridLength(SecondaryWidth) };
            ColumnDefinitions.Add(SecondaryFirst ? secondaryColumn : primaryColumn);
            ColumnDefinitions.Add(SecondaryFirst ? primaryColumn : secondaryColumn);
            Place(_primary, 0, SecondaryFirst ? 1 : 0, true);
            Place(_secondary, 0, SecondaryFirst ? 0 : 1, true);
            return;
        }

        ColumnSpacing = 0;
        if (CollapseMode == SplitCollapseMode.Stack)
        {
            RowSpacing = gap;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_primary, SecondaryFirst ? 1 : 0, 0, true);
            Place(_secondary, SecondaryFirst ? 0 : 1, 0, true);
            return;
        }

        RowSpacing = 0;
        Place(_primary, 0, 0, !_showsSecondary);
        Place(_secondary, 0, 0, _showsSecondary);
    }

    /// <summary>面を 1 つ置きます。</summary>
    /// <param name="element">面。</param>
    /// <param name="row">行。</param>
    /// <param name="column">列。</param>
    /// <param name="visible">出すなら true。</param>
    private static void Place(FrameworkElement? element, int row, int column, bool visible)
    {
        if (element is null)
        {
            return;
        }

        SetRow(element, row);
        SetColumn(element, column);
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>面を差し替えます。</summary>
    /// <param name="slot">差し替える場所。</param>
    /// <param name="value">新しい面。</param>
    private void Replace(ref FrameworkElement? slot, FrameworkElement? value)
    {
        if (ReferenceEquals(slot, value))
        {
            return;
        }

        if (slot is not null)
        {
            Children.Remove(slot);
        }

        slot = value;
        if (value is not null)
        {
            Children.Add(value);
        }

        Apply();
    }
}
