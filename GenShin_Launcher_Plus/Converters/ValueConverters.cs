using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GenShin_Launcher_Plus.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool invert = parameter is string s && s == "Invert";
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

public class ProgressToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = 0;
        if (value is double d) percent = d;
        else if (value is int i) percent = i;

        double maxWidth = 280;
        if (parameter is string s && double.TryParse(s, out var w))
            maxWidth = w;

        return Math.Max(0, Math.Min(maxWidth, percent / 100.0 * maxWidth));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
