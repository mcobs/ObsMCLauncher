using System;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class NavWidthConverter : IValueConverter
{
    public static readonly NavWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var collapsed = value is bool b && b;
        return collapsed ? new GridLength(72) : new GridLength(200);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
