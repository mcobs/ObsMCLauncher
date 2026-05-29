using System;
using Avalonia.Data.Converters;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Desktop.Converters;

public class NotificationPositionToVisibilityConverter : IValueConverter
{
    public static readonly NotificationPositionToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is NotificationPosition position)
            return position == NotificationPosition.BottomRight;

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}