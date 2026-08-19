using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PCHealthDashboard.Models;

namespace PCHealthDashboard.Helpers;

public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isBg = parameter?.ToString() == "bg";
        if (value is AlertSeverity severity)
        {
            return severity switch
            {
                AlertSeverity.Critical => isBg ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 21, 24)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)), // Red
                AlertSeverity.Warning => isBg ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 34, 16)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36)), // Yellow
                AlertSeverity.Info => isBg ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 40, 27)) : new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)), // Green
                _ => System.Windows.Media.Brushes.Gray
            };
        }
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
