using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PCHealthDashboard.Helpers;

public class StringToColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string colorName = value?.ToString() ?? "Green";
        return colorName.ToLower() switch
        {
            "yellow" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(253, 224, 71)), // Semantic Warning
            "orange" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 146, 60)), // Orange
            "red" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113)), // Semantic Critical
            "green" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)), // Semantic Healthy
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)) // Default Green
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
