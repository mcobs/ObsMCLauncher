using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Transformation;

namespace ObsMCLauncher.Desktop.Converters;

public sealed class BoolToTransformConverter : IValueConverter
{
    public static readonly BoolToTransformConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var parts = parameter?.ToString()?.Split('|');
            var trueVal = parts is { Length: >= 1 } ? parts[0].Trim() : "scale(1)";
            var falseVal = parts is { Length: >= 2 } ? parts[1].Trim() : "scale(0.8)";
            return TransformOperations.Parse(b ? trueVal : falseVal);
        }
        return TransformOperations.Parse("scale(0.8)");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
