using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString() ?? string.Empty;
        var right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 仅用于 OneWay 绑定
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
