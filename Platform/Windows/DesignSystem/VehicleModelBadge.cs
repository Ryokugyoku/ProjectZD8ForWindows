using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProjectZD8ForWindows.Platform.Windows.Localization;
using Windows.UI;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 車種専用 PID の値であることを示す、車種コード入りの丸いバッジです（iOS 版の <c>VehicleModelBadge</c>）。
/// </summary>
public sealed partial class VehicleModelBadge : Grid
{
    /// <summary>車種コードと大きさを指定して作ります。</summary>
    /// <param name="modelCode">車種コード（例 ZD8）。</param>
    /// <param name="size">直径（px）。</param>
    public VehicleModelBadge(string modelCode, double size = 38)
    {
        Width = size;
        Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        var gradient = new LinearGradientBrush { StartPoint = new global::Windows.Foundation.Point(0, 0), EndPoint = new global::Windows.Foundation.Point(1, 1) };
        gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 88, 86, 214), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 10, 99, 214), Offset = 0.5 });
        gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 5, 199, 230), Offset = 1 });
        Children.Add(new Ellipse
        {
            Fill = gradient,
            Stroke = new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)),
            StrokeThickness = 1.5,
        });
        Children.Add(new TextBlock
        {
            Text = modelCode,
            FontSize = size * 0.24,
            FontWeight = FontWeights.Black,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.Clip,
        });
        Translation = new Vector3(0, 0, 8);
        AutomationProperties.SetName(this, LocalizedText.Format("vehicle_specific_pid.badge %@", modelCode));
        ToolTipService.SetToolTip(this, LocalizedText.Format("vehicle_specific_pid.badge %@", modelCode));
    }
}
