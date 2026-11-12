using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 数値（と単位）と、その下の短い説明の組です。数字は等幅で、値は <see cref="SetValue"/> で差し替えます（作り直さない）。
/// </summary>
/// <remarks>
/// 子の並びは前と同じです（Children[0] が数値の TextBlock、Children[1] が説明）。単位は数値の TextBlock の中の小さな Run です。
/// </remarks>
public sealed partial class MetricBlock : StackPanel
{
    /// <summary>数値の文字です。</summary>
    private readonly TextBlock _value;

    /// <summary>単位を使うときの、数値の Run です。</summary>
    private readonly Run? _valueRun;

    /// <summary>単位を使うときの、単位の Run です。</summary>
    private readonly Run? _unitRun;

    /// <summary>説明の文字です。</summary>
    private readonly TextBlock _caption;

    /// <summary>数値と説明を指定して作ります。</summary>
    /// <param name="value">数値の文字列。</param>
    /// <param name="caption">説明。</param>
    /// <param name="alignment">左右の寄せ方。</param>
    /// <param name="valueForeground">数値の色。null なら標準。</param>
    /// <param name="valueStyleKey">数値の文字スタイル（<see cref="TextStyles"/> の Metric*）。</param>
    public MetricBlock(
        string value,
        string caption,
        HorizontalAlignment alignment = HorizontalAlignment.Left,
        Brush? valueForeground = null,
        string valueStyleKey = TextStyles.MetricValue)
        : this(value, null, caption, alignment, valueForeground, valueStyleKey)
    {
    }

    /// <summary>数値・単位・説明を指定して作ります。</summary>
    /// <param name="value">数値の文字列。</param>
    /// <param name="unit">単位（例 rpm）。null なら省く。</param>
    /// <param name="caption">説明。</param>
    /// <param name="alignment">左右の寄せ方。</param>
    /// <param name="valueForeground">数値の色。null なら標準。</param>
    /// <param name="valueStyleKey">数値の文字スタイル。</param>
    public MetricBlock(
        string value,
        string? unit,
        string caption,
        HorizontalAlignment alignment = HorizontalAlignment.Left,
        Brush? valueForeground = null,
        string valueStyleKey = TextStyles.MetricValue)
    {
        Spacing = Space.XXS;
        MinWidth = 76;
        HorizontalAlignment = alignment;
        _value = Typography.Metric(value, valueStyleKey, valueForeground);
        _value.HorizontalAlignment = alignment;
        if (unit is not null)
        {
            _valueRun = new Run { Text = value };
            _unitRun = new Run
            {
                Text = " " + unit,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DesignTokens.SystemBrush("TextFillColorSecondaryBrush"),
            };
            _value.Inlines.Clear();
            _value.Inlines.Add(_valueRun);
            _value.Inlines.Add(_unitRun);
        }

        _caption = Typography.Text(caption, TextStyles.SecondaryCaption);
        _caption.HorizontalAlignment = alignment;
        _caption.HorizontalTextAlignment = alignment == HorizontalAlignment.Right ? TextAlignment.Right : TextAlignment.Left;
        Children.Add(_value);
        Children.Add(_caption);
    }

    /// <summary>
    /// 数値を差し替えます。
    /// </summary>
    /// <remarks>責務: 数値（と単位）の文字だけを、違うときに変えます。</remarks>
    /// <param name="value">数値の文字列。</param>
    /// <param name="unit">単位。null なら変えない（単位を付けて作ったときだけ効く）。</param>
    public void SetValue(string value, string? unit = null)
    {
        if (_valueRun is null || _unitRun is null)
        {
            Retained.SetText(_value, value);
            return;
        }

        if (_valueRun.Text != value)
        {
            _valueRun.Text = value;
        }

        if (unit is not null && _unitRun.Text != " " + unit)
        {
            _unitRun.Text = " " + unit;
        }
    }

    /// <summary>
    /// 説明を差し替えます。
    /// </summary>
    /// <remarks>責務: 説明の文字だけを、違うときに変えます。</remarks>
    /// <param name="caption">説明。</param>
    public void SetCaption(string caption) => Retained.SetText(_caption, caption);
}
