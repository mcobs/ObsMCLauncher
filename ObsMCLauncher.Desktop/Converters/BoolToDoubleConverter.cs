using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var parts = parameter?.ToString()?.Split(',');
            var trueVal = parts is { Length: >= 1 } && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : 1.0;
            var falseVal = parts is { Length: >= 2 } && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0.0;
            return b ? trueVal : falseVal;
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
