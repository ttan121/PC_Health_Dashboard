using System;
using System.Globalization;
using System.Windows.Data;

namespace PCHealthDashboard.Helpers;

public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == parameter?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return parameter?.ToString() ?? string.Empty;
        }
        return System.Windows.Data.Binding.DoNothing;
    }
}
