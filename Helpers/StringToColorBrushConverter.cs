using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace PCHealthDashboard.Helpers;

public class StringToColorBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush YellowBrush = new(MediaColor.FromRgb(253, 224, 71));
    private static readonly SolidColorBrush OrangeBrush = new(MediaColor.FromRgb(251, 146, 60));
    private static readonly SolidColorBrush RedBrush = new(MediaColor.FromRgb(248, 113, 113));
    private static readonly SolidColorBrush GreenBrush = new(MediaColor.FromRgb(74, 222, 128));

    static StringToColorBrushConverter()
    {
        YellowBrush.Freeze();
        OrangeBrush.Freeze();
        RedBrush.Freeze();
        GreenBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string colorName = value?.ToString()?.Trim() ?? "Orange";
        return colorName.ToLowerInvariant() switch
        {
            "yellow" or "#fde047" => YellowBrush,
            "orange" or "#fb923c" or "#f59e0b" => OrangeBrush,
            "red" or "#f87171" => RedBrush,
            "green" or "#4ade80" => GreenBrush,
            _ => TryParseBrush(colorName)
        };
    }

    private static SolidColorBrush TryParseBrush(string colorStr)
    {
        try
        {
            var parsed = (MediaColor)MediaColorConverter.ConvertFromString(colorStr);
            var brush = new SolidColorBrush(parsed);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return OrangeBrush;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
