using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

public class NotificationPositionToBorderConverter : IValueConverter
{
    public static readonly NotificationPositionToBorderConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not NotificationPosition current)
            return Brushes.Transparent;

        var target = parameter?.ToString();
        var isSelected = target switch
        {
            "Center" => current == NotificationPosition.Center,
            "BottomRight" => current == NotificationPosition.BottomRight,
            _ => false
        };

        return isSelected
            ? new SolidColorBrush(Color.Parse("#4A90D9"))
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}