using System;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class GreaterThanZeroToIsVisibleConverter : IValueConverter
{
    public static readonly GreaterThanZeroToIsVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is int i) return i > 0;
        if (value is long l) return l > 0;
        if (value is double d) return d > 0.00001;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
